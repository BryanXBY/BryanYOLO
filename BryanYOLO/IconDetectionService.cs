using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BryanYOLO.Models;
using BryanYOLO.ViewModels;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Annotation = BryanYOLO.Models.Annotation;
using Image = SixLabors.ImageSharp.Image;
using Rectangle = System.Drawing.Rectangle;
using Point = System.Drawing.Point;


namespace BryanYOLO.Services
{
    public class IconDetectionService : IDisposable
    {
        private readonly AnnotationService _annotationService;
        private InferenceSession _session;
        private readonly object _sessionLock = new object();
        private int _outputClassCount = 1;
        private string _outputLayerName = "output0";
        private bool _isYoloV11Format = false; // YOLOv11专用格式
        private long[] _outputShape = null; // 保存输出形状
        private int _numAnchors = 8400; // YOLOv11默认anchor数
        private int _userSpecifiedClassCount = -1; // 用户指定的类别数
        private LabelAdjustmentConfig _config; // 保存配置
        private YoloVersion _modelVersion = YoloVersion.V5; // 模型版本

        public enum LabelPosition
        {
            Above,
            Below,
            Left,
            Right
        }

        public enum AdjustmentMode
        {
            Cover,      // 覆盖模式：调整框体包含图标
            Avoid       // 避开模式：调整框体避开图标
        }

        public enum DetectionMethod
        {
            Traditional,    // 传统模板匹配（CPU）
            OnnxModel      // ONNX模型检测（GPU加速）
        }

        public class IconDetectionResult
        {
            public Rectangle Bounds { get; set; }
            public double Confidence { get; set; }
            public Point Center { get; set; }
            public int ClassId { get; set; }  // 用于ONNX模型检测
        }

        public class LabelAdjustmentConfig
        {
            // 检测方法
            public DetectionMethod Method { get; set; } = DetectionMethod.OnnxModel;

            // ONNX模型相关
            public string OnnxModelPath { get; set; }
            public int ModelInputSize { get; set; } = 640;
            public int IconClassId { get; set; }  // ONNX模型中图标的类别ID
            public float ConfidenceThreshold { get; set; } = 0.5f;
            public int ModelClassCount { get; set; } = -1; // 用户指定的模型类别数
            public YoloVersion ModelVersion { get; set; } = YoloVersion.V5; // 模型版本

            // 传统方法相关（保留兼容性）
            public string IconImagePath { get; set; }
            public string CurrentImagePath { get; set; }
            public Rectangle SelectionRect { get; set; }

            // 调整参数
            public int TargetLabelId { get; set; }
            public LabelPosition Position { get; set; }
            public AdjustmentMode Mode { get; set; }
            public bool ChangeLabel { get; set; }
            public int NewLabelId { get; set; }
            public double MatchThreshold { get; set; } = 0.7;
            public bool ProcessCurrentImageOnly { get; set; } = false;
        }

        public class PreviewResult
        {
            public List<IconDetectionResult> Detections { get; set; }
            public int TotalDetected { get; set; }
            public int FilteredByClass { get; set; }
            public int FilteredByConfidence { get; set; }
            public string Message { get; set; }
            public bool Success { get; set; }
        }

        public IconDetectionService()
        {
            _annotationService = new AnnotationService();
        }

        // 设置用户指定的类别数
        public void SetModelClassCount(int classCount)
        {
            _userSpecifiedClassCount = classCount;
        }

        // 新增：预览单张图片的检测结果
        public async Task<PreviewResult> PreviewDetection(
            string imagePath,
            LabelAdjustmentConfig config,
            Action<string> statusCallback)
        {
            return await Task.Run(() =>
            {
                var result = new PreviewResult
                {
                    Detections = new List<IconDetectionResult>(),
                    Success = false
                };

                try
                {
                    _config = config; // 保存配置
                    _modelVersion = config.ModelVersion;

                    // 设置用户指定的类别数
                    if (config.ModelClassCount > 0)
                    {
                        _userSpecifiedClassCount = config.ModelClassCount;
                    }

                    // 初始化ONNX会话
                    if (config.Method == DetectionMethod.OnnxModel)
                    {
                        InitializeOnnxSession(config.OnnxModelPath, statusCallback);
                        statusCallback?.Invoke($"模型加载完成，版本: {_modelVersion}，类别数: {_outputClassCount}");
                    }

                    // 检测图标
                    var detections = DetectIconsWithOnnx(imagePath, config);

                    // 统计信息
                    result.TotalDetected = detections.Count;
                    result.Detections = detections;
                    result.FilteredByClass = detections.Count(d => d.ClassId == config.IconClassId);
                    result.Success = true;

                    if (detections.Count == 0)
                    {
                        result.Message = "未检测到任何目标";
                    }
                    else if (result.FilteredByClass == 0)
                    {
                        var detectedClasses = detections.Select(d => d.ClassId).Distinct().OrderBy(id => id);
                        result.Message = $"检测到 {detections.Count} 个目标，但没有类别ID为 {config.IconClassId} 的图标\n" +
                                       $"检测到的类别: {string.Join(", ", detectedClasses)}";
                    }
                    else
                    {
                        result.Message = $"成功检测到 {result.FilteredByClass} 个图标（类别ID: {config.IconClassId}）";
                    }
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = $"检测失败: {ex.Message}";
                    statusCallback?.Invoke($"错误: {ex.Message}");
                }
                finally
                {
                    // 清理会话
                    lock (_sessionLock)
                    {
                        _session?.Dispose();
                        _session = null;
                    }
                }

                return result;
            });
        }

        public async Task<int> ProcessBatchAdjustment(
            List<ImageItem> images,
            LabelAdjustmentConfig config,
            Action<double> progressCallback,
            Action<string> statusCallback)
        {
            return await Task.Run(() =>
            {
                int modifiedCount = 0;
                int processedCount = 0;

                try
                {
                    _config = config; // 保存配置
                    _modelVersion = config.ModelVersion;

                    // 设置用户指定的类别数
                    if (config.ModelClassCount > 0)
                    {
                        _userSpecifiedClassCount = config.ModelClassCount;
                    }

                    // 如果使用ONNX模型，初始化推理会话
                    if (config.Method == DetectionMethod.OnnxModel)
                    {
                        InitializeOnnxSession(config.OnnxModelPath, statusCallback);
                        statusCallback?.Invoke($"已加载ONNX模型（{_modelVersion}），检测类别ID: {config.IconClassId}，类别数: {_outputClassCount}");
                    }
                    else
                    {
                        statusCallback?.Invoke("使用传统模板匹配方法");
                    }

                    // 决定要处理的图片列表
                    var imagesToProcess = config.ProcessCurrentImageOnly && !string.IsNullOrEmpty(config.CurrentImagePath)
                        ? images.Where(img => img.ImagePath == config.CurrentImagePath).ToList()
                        : images;

                    foreach (var imageItem in imagesToProcess)
                    {
                        try
                        {
                            statusCallback?.Invoke($"正在处理：{imageItem.FileName} ({processedCount + 1}/{imagesToProcess.Count})");

                            bool modified = false;
                            if (config.Method == DetectionMethod.OnnxModel)
                            {
                                modified = ProcessSingleImageWithOnnx(imageItem, config, statusCallback);
                            }
                            else
                            {
                                modified = ProcessSingleImageTraditional(imageItem, config, statusCallback);
                            }

                            if (modified)
                            {
                                modifiedCount++;
                            }

                            processedCount++;
                            progressCallback?.Invoke((double)processedCount / imagesToProcess.Count);
                        }
                        catch (Exception ex)
                        {
                            statusCallback?.Invoke($"处理 {imageItem.FileName} 时出错：{ex.Message}");
                        }
                    }

                    statusCallback?.Invoke($"处理完成，共修改了 {modifiedCount} 张图片的标注");
                }
                finally
                {
                    // 清理ONNX会话
                    if (config.Method == DetectionMethod.OnnxModel)
                    {
                        lock (_sessionLock)
                        {
                            _session?.Dispose();
                            _session = null;
                        }
                    }
                }

                return modifiedCount;
            });
        }

        private void InitializeOnnxSession(string modelPath, Action<string> statusCallback)
        {
            statusCallback?.Invoke($"正在初始化ONNX推理引擎（{_modelVersion}）...");

            var sessionOptions = new SessionOptions();
            try
            {
                sessionOptions.AppendExecutionProvider_DML(0); // GPU加速
                statusCallback?.Invoke("使用GPU加速 (DirectML)");
            }
            catch
            {
                sessionOptions.AppendExecutionProvider_CPU(0);
                statusCallback?.Invoke("使用CPU推理");
            }
            sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

            lock (_sessionLock)
            {
                _session?.Dispose();
                _session = new InferenceSession(modelPath, sessionOptions);

                // 获取输出信息
                var outputMeta = _session.OutputMetadata;
                _outputLayerName = outputMeta.Keys.FirstOrDefault() ?? "output0";

                var outputNodeMeta = outputMeta[_outputLayerName];
                var dimensions = outputNodeMeta.Dimensions;
                if (dimensions != null)
                {
                    _outputShape = dimensions.Select(d => (long)d).ToArray();
                }

                if (_outputShape != null && _outputShape.Length >= 2)
                {
                    // 根据模型版本处理
                    switch (_modelVersion)
                    {
                        case YoloVersion.V11:
                            ProcessYoloV11Format(statusCallback);
                            break;

                        case YoloVersion.V10:
                            ProcessYoloV10Format(statusCallback);
                            break;

                        case YoloVersion.V8:
                            ProcessYoloV8Format(statusCallback);
                            break;

                        case YoloVersion.V5:
                        default:
                            ProcessYoloV5Format(statusCallback);
                            break;
                    }
                }

                // 调试输出
                string shapeStr = _outputShape != null ? string.Join(", ", _outputShape.Select(x => x.ToString())) : "unknown";
                statusCallback?.Invoke($"模型输出形状: [{shapeStr}]");
                statusCallback?.Invoke($"输出层名称: {_outputLayerName}");
                statusCallback?.Invoke($"模型版本: {_modelVersion}");
                statusCallback?.Invoke($"最终使用的类别数: {_outputClassCount}");
            }
        }

        private void ProcessYoloV11Format(Action<string> statusCallback)
        {
            if (_outputShape.Length == 3 && (_outputShape[2] == 8400 || _outputShape[2] == 6300 || _outputShape[2] == 1600))
            {
                _isYoloV11Format = true;
                _numAnchors = (int)_outputShape[2];

                if (_userSpecifiedClassCount > 0)
                {
                    _outputClassCount = _userSpecifiedClassCount;
                }
                else
                {
                    long totalChannels = _outputShape[1];
                    _outputClassCount = (int)(totalChannels - 4);
                    if (_outputClassCount <= 0)
                    {
                        _outputClassCount = 1;
                    }
                }

                statusCallback?.Invoke($"YOLOv11模型: {_outputClassCount} 个类别, {_numAnchors} 个anchors");
            }
        }

        private void ProcessYoloV10Format(Action<string> statusCallback)
        {
            if (_outputShape.Length == 3)
            {
                var lastDim = _outputShape[2];

                if (_userSpecifiedClassCount > 0)
                {
                    _outputClassCount = _userSpecifiedClassCount;
                }
                else if (lastDim == 6)
                {
                    // 端到端格式
                    _outputClassCount = 1; // 通常端到端模型不需要单独的类别数
                }
                else if (lastDim > 5)
                {
                    _outputClassCount = (int)(lastDim - 5);
                }
                else
                {
                    _outputClassCount = 1;
                }

                statusCallback?.Invoke($"YOLOv10模型: {_outputClassCount} 个类别");
            }
        }

        private void ProcessYoloV8Format(Action<string> statusCallback)
        {
            ProcessYoloV5Format(statusCallback); // YOLOv8格式类似v5
            statusCallback?.Invoke($"YOLOv8模型: {_outputClassCount} 个类别");
        }

        private void ProcessYoloV5Format(Action<string> statusCallback)
        {
            if (_outputShape.Length == 3)
            {
                var lastDim = _outputShape[2];

                if (_userSpecifiedClassCount > 0)
                {
                    _outputClassCount = _userSpecifiedClassCount;
                }
                else if (lastDim > 5)
                {
                    _outputClassCount = (int)(lastDim - 5);
                }
                else
                {
                    _outputClassCount = 1;
                }

                statusCallback?.Invoke($"YOLOv5模型: {_outputClassCount} 个类别");
            }
        }

        private bool ProcessSingleImageWithOnnx(
            ImageItem imageItem,
            LabelAdjustmentConfig config,
            Action<string> statusCallback)
        {
            // 使用ONNX模型检测图标
            var iconLocations = DetectIconsWithOnnx(imageItem.ImagePath, config);

            if (iconLocations.Count == 0)
            {
                statusCallback?.Invoke($"  未在 {imageItem.FileName} 中找到指定的图标");
                return false;
            }

            statusCallback?.Invoke($"  在 {imageItem.FileName} 中找到 {iconLocations.Count} 个图标");

            // 加载现有标注
            var annotations = _annotationService.LoadAnnotations(imageItem.AnnotationPath);
            if (annotations.Count == 0)
            {
                statusCallback?.Invoke($"  {imageItem.FileName} 中没有标注");
                return false;
            }

            // 筛选目标标签的标注
            var targetAnnotations = annotations.Where(a => a.ClassId == config.TargetLabelId).ToList();
            if (targetAnnotations.Count == 0)
            {
                statusCallback?.Invoke($"  {imageItem.FileName} 中没有标签ID为 {config.TargetLabelId} 的标注");
                return false;
            }

            // 获取图片尺寸
            int imageWidth, imageHeight;
            using (var img = Image.Load(imageItem.ImagePath))
            {
                imageWidth = img.Width;
                imageHeight = img.Height;
            }

            bool hasChanges = false;
            int adjustedCount = 0;

            // 对每个图标位置进行处理
            foreach (var iconLocation in iconLocations)
            {
                var nearestAnnotation = FindNearestAnnotation(
                    targetAnnotations,
                    iconLocation,
                    config.Position,
                    imageWidth,
                    imageHeight);

                if (nearestAnnotation != null)
                {
                    bool adjusted = AdjustAnnotationBounds(
                        nearestAnnotation,
                        iconLocation,
                        config,
                        imageWidth,
                        imageHeight);

                    if (adjusted)
                    {
                        adjustedCount++;
                        hasChanges = true;

                        if (config.ChangeLabel)
                        {
                            nearestAnnotation.ClassId = config.NewLabelId;
                        }
                    }
                }
            }

            // 保存修改后的标注
            if (hasChanges)
            {
                _annotationService.SaveAnnotations(imageItem.AnnotationPath, annotations);
                imageItem.UpdateStatus();
                statusCallback?.Invoke($"  已调整 {imageItem.FileName} 中的 {adjustedCount} 个标注");
                return true;
            }

            return false;
        }

        private List<IconDetectionResult> DetectIconsWithOnnx(string imagePath, LabelAdjustmentConfig config)
        {
            var results = new List<IconDetectionResult>();

            // 预处理图片
            var (inputTensor, scaleX, scaleY, padX, padY, origWidth, origHeight) =
                PreprocessImage(imagePath, config.ModelInputSize);

            // 运行推理
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("images", inputTensor)
            };

            List<IconDetectionResult> detections;
            lock (_sessionLock)
            {
                using var outputs = _session.Run(inputs);

                // 根据模型版本选择后处理方法
                detections = _modelVersion switch
                {
                    YoloVersion.V11 => PostprocessYoloV11Results(
                        outputs, config.ModelInputSize, config.ConfidenceThreshold,
                        scaleX, scaleY, padX, padY, origWidth, origHeight, config.IconClassId),
                    YoloVersion.V10 => PostprocessYoloV10Results(
                        outputs, config.ModelInputSize, config.ConfidenceThreshold,
                        scaleX, scaleY, padX, padY, origWidth, origHeight, config.IconClassId),
                    YoloVersion.V8 => PostprocessYoloV8Results(
                        outputs, config.ModelInputSize, config.ConfidenceThreshold,
                        scaleX, scaleY, padX, padY, origWidth, origHeight, config.IconClassId),
                    _ => PostprocessYoloV5Results(
                        outputs, config.ModelInputSize, config.ConfidenceThreshold,
                        scaleX, scaleY, padX, padY, origWidth, origHeight, config.IconClassId)
                };
            }

            // 应用NMS
            return ApplyNMS(detections, 0.45f);
        }

        private (DenseTensor<float> tensor, float scaleX, float scaleY, float padX, float padY, int origWidth, int origHeight)
            PreprocessImage(string imagePath, int inputSize)
        {
            using var image = Image.Load<Rgb24>(imagePath);

            var origWidth = image.Width;
            var origHeight = image.Height;

            float scale = Math.Min((float)inputSize / origWidth, (float)inputSize / origHeight);
            int newWidth = (int)(origWidth * scale);
            int newHeight = (int)(origHeight * scale);

            int padX = (inputSize - newWidth) / 2;
            int padY = (inputSize - newHeight) / 2;

            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(inputSize, inputSize),
                Mode = ResizeMode.Pad,
                PadColor = SixLabors.ImageSharp.Color.Gray,
                Position = AnchorPositionMode.Center
            }));

            var tensor = new DenseTensor<float>(new[] { 1, 3, inputSize, inputSize });

            for (int y = 0; y < inputSize; y++)
            {
                for (int x = 0; x < inputSize; x++)
                {
                    var pixel = image[x, y];
                    tensor[0, 0, y, x] = pixel.R / 255.0f;
                    tensor[0, 1, y, x] = pixel.G / 255.0f;
                    tensor[0, 2, y, x] = pixel.B / 255.0f;
                }
            }

            return (tensor, scale, scale, padX, padY, origWidth, origHeight);
        }

        // YOLOv11格式处理
        private List<IconDetectionResult> PostprocessYoloV11Results(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
            int inputSize,
            float confidenceThreshold,
            float scaleX, float scaleY, float padX, float padY,
            int origWidth, int origHeight,
            int targetClassId)
        {
            var detections = new List<IconDetectionResult>();

            var outputTensor = results.First(r => r.Name == _outputLayerName);
            var output = outputTensor.AsEnumerable<float>().ToArray();

            for (int i = 0; i < _numAnchors; i++)
            {
                float cx = output[0 * _numAnchors + i];
                float cy = output[1 * _numAnchors + i];
                float w = output[2 * _numAnchors + i];
                float h = output[3 * _numAnchors + i];

                float maxScore = 0;
                int bestClass = -1;

                for (int c = 0; c < _outputClassCount; c++)
                {
                    float score = output[(4 + c) * _numAnchors + i];
                    if (score > maxScore)
                    {
                        maxScore = score;
                        bestClass = c;
                    }
                }

                if (maxScore < confidenceThreshold) continue;
                if (bestClass != targetClassId) continue;

                cx = (cx - padX) / scaleX;
                cy = (cy - padY) / scaleY;
                w = w / scaleX;
                h = h / scaleY;

                int x1 = (int)(cx - w / 2);
                int y1 = (int)(cy - h / 2);
                int x2 = (int)(cx + w / 2);
                int y2 = (int)(cy + h / 2);

                x1 = Math.Max(0, Math.Min(origWidth - 1, x1));
                y1 = Math.Max(0, Math.Min(origHeight - 1, y1));
                x2 = Math.Max(0, Math.Min(origWidth, x2));
                y2 = Math.Max(0, Math.Min(origHeight, y2));

                int width = x2 - x1;
                int height = y2 - y1;

                if (width > 0 && height > 0)
                {
                    detections.Add(new IconDetectionResult
                    {
                        Bounds = new Rectangle(x1, y1, width, height),
                        Confidence = maxScore,
                        Center = new Point(x1 + width / 2, y1 + height / 2),
                        ClassId = bestClass
                    });
                }
            }

            return detections;
        }

        // YOLOv10格式处理
        private List<IconDetectionResult> PostprocessYoloV10Results(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
            int inputSize,
            float confidenceThreshold,
            float scaleX, float scaleY, float padX, float padY,
            int origWidth, int origHeight,
            int targetClassId)
        {
            var detections = new List<IconDetectionResult>();
            var outputTensor = results.First(r => r.Name == _outputLayerName);
            var output = outputTensor.AsEnumerable<float>().ToArray();

            // YOLOv10可能使用端到端的格式
            if (_outputShape.Length == 3 && _outputShape[2] == 6)
            {
                int numDetections = (int)_outputShape[1];

                for (int i = 0; i < numDetections; i++)
                {
                    int offset = i * 6;

                    float confidence = output[offset + 4];
                    if (confidence < confidenceThreshold) continue;

                    int classId = (int)output[offset + 5];
                    if (classId != targetClassId) continue;

                    float x1 = output[offset + 0];
                    float y1 = output[offset + 1];
                    float x2 = output[offset + 2];
                    float y2 = output[offset + 3];

                    float cx = (x1 + x2) / 2;
                    float cy = (y1 + y2) / 2;
                    float w = x2 - x1;
                    float h = y2 - y1;

                    cx = (cx - padX) / scaleX;
                    cy = (cy - padY) / scaleY;
                    w = w / scaleX;
                    h = h / scaleY;

                    int ix1 = (int)(cx - w / 2);
                    int iy1 = (int)(cy - h / 2);
                    int width = (int)w;
                    int height = (int)h;

                    ix1 = Math.Max(0, ix1);
                    iy1 = Math.Max(0, iy1);
                    width = Math.Min(width, origWidth - ix1);
                    height = Math.Min(height, origHeight - iy1);

                    if (width > 0 && height > 0)
                    {
                        detections.Add(new IconDetectionResult
                        {
                            Bounds = new Rectangle(ix1, iy1, width, height),
                            Confidence = confidence,
                            Center = new Point(ix1 + width / 2, iy1 + height / 2),
                            ClassId = classId
                        });
                    }
                }
            }
            else
            {
                // 如果不是端到端格式，使用类似YOLOv5的处理
                return PostprocessYoloV5Results(results, inputSize, confidenceThreshold,
                    scaleX, scaleY, padX, padY, origWidth, origHeight, targetClassId);
            }

            return detections;
        }

        // YOLOv8格式处理
        private List<IconDetectionResult> PostprocessYoloV8Results(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
            int inputSize,
            float confidenceThreshold,
            float scaleX, float scaleY, float padX, float padY,
            int origWidth, int origHeight,
            int targetClassId)
        {
            // YOLOv8格式类似v5
            return PostprocessYoloV5Results(results, inputSize, confidenceThreshold,
                scaleX, scaleY, padX, padY, origWidth, origHeight, targetClassId);
        }

        // YOLOv5格式处理（保留兼容性）
        private List<IconDetectionResult> PostprocessYoloV5Results(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
            int inputSize,
            float confidenceThreshold,
            float scaleX, float scaleY, float padX, float padY,
            int origWidth, int origHeight,
            int targetClassId)
        {
            var detections = new List<IconDetectionResult>();

            var outputTensor = results.First(r => r.Name == _outputLayerName);
            var output = outputTensor.AsEnumerable<float>().ToArray();

            int elementsPerDetection = 5 + _outputClassCount;
            if (_outputClassCount == 1 && _outputShape[2] == 5)
            {
                elementsPerDetection = 5;
            }
            else if (_outputClassCount == 1 && _outputShape[2] == 6)
            {
                elementsPerDetection = 6;
            }

            int numDetections = output.Length / elementsPerDetection;

            for (int i = 0; i < numDetections; i++)
            {
                int offset = i * elementsPerDetection;

                float cx = output[offset + 0];
                float cy = output[offset + 1];
                float w = output[offset + 2];
                float h = output[offset + 3];
                float objectness = output[offset + 4];

                if (objectness < confidenceThreshold) continue;

                int bestClass = 0;
                float bestScore = objectness;

                if (elementsPerDetection > 5)
                {
                    if (_outputClassCount == 1)
                    {
                        bestClass = 0;
                        if (elementsPerDetection == 6)
                        {
                            float classScore = output[offset + 5];
                            bestScore = objectness * classScore;
                        }
                    }
                    else
                    {
                        bestClass = -1;
                        bestScore = 0;
                        for (int c = 0; c < _outputClassCount; c++)
                        {
                            float score = output[offset + 5 + c] * objectness;
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestClass = c;
                            }
                        }
                    }
                }

                if (bestScore < confidenceThreshold) continue;
                if (bestClass != targetClassId) continue;

                cx = (cx - padX) / scaleX;
                cy = (cy - padY) / scaleY;
                w = w / scaleX;
                h = h / scaleY;

                int x1 = (int)(cx - w / 2);
                int y1 = (int)(cy - h / 2);
                int width = (int)w;
                int height = (int)h;

                x1 = Math.Max(0, x1);
                y1 = Math.Max(0, y1);
                width = Math.Min(width, origWidth - x1);
                height = Math.Min(height, origHeight - y1);

                if (width > 0 && height > 0)
                {
                    detections.Add(new IconDetectionResult
                    {
                        Bounds = new Rectangle(x1, y1, width, height),
                        Confidence = bestScore,
                        Center = new Point(x1 + width / 2, y1 + height / 2),
                        ClassId = bestClass
                    });
                }
            }

            return detections;
        }

        // 获取所有检测结果（不过滤类别）- 用于预览
        public List<IconDetectionResult> GetAllDetections(
            string imagePath,
            string modelPath,
            int inputSize,
            float confidenceThreshold,
            int modelClassCount = -1,
            YoloVersion modelVersion = YoloVersion.V5)
        {
            var results = new List<IconDetectionResult>();

            try
            {
                _modelVersion = modelVersion;

                // 设置用户指定的类别数
                if (modelClassCount > 0)
                {
                    _userSpecifiedClassCount = modelClassCount;
                }

                // 初始化会话
                InitializeOnnxSession(modelPath, msg => Console.WriteLine(msg));

                // 预处理图片
                var (inputTensor, scaleX, scaleY, padX, padY, origWidth, origHeight) =
                    PreprocessImage(imagePath, inputSize);

                // 运行推理
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("images", inputTensor)
                };

                lock (_sessionLock)
                {
                    using var outputs = _session.Run(inputs);

                    results = _modelVersion switch
                    {
                        YoloVersion.V11 => PostprocessYoloV11AllResults(
                            outputs, inputSize, confidenceThreshold,
                            scaleX, scaleY, padX, padY, origWidth, origHeight),
                        YoloVersion.V10 => PostprocessYoloV10AllResults(
                            outputs, inputSize, confidenceThreshold,
                            scaleX, scaleY, padX, padY, origWidth, origHeight),
                        YoloVersion.V8 => PostprocessYoloV8AllResults(
                            outputs, inputSize, confidenceThreshold,
                            scaleX, scaleY, padX, padY, origWidth, origHeight),
                        _ => PostprocessYoloV5AllResults(
                            outputs, inputSize, confidenceThreshold,
                            scaleX, scaleY, padX, padY, origWidth, origHeight)
                    };
                }

                // 应用NMS
                results = ApplyNMS(results, 0.45f);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检测错误: {ex.Message}");
            }
            finally
            {
                lock (_sessionLock)
                {
                    _session?.Dispose();
                    _session = null;
                }
            }

            return results;
        }

        // YOLOv11格式 - 获取所有检测（不过滤类别）
        private List<IconDetectionResult> PostprocessYoloV11AllResults(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
            int inputSize,
            float confidenceThreshold,
            float scaleX, float scaleY, float padX, float padY,
            int origWidth, int origHeight)
        {
            var detections = new List<IconDetectionResult>();

            var outputTensor = results.First(r => r.Name == _outputLayerName);
            var output = outputTensor.AsEnumerable<float>().ToArray();

            for (int i = 0; i < _numAnchors; i++)
            {
                float cx = output[0 * _numAnchors + i];
                float cy = output[1 * _numAnchors + i];
                float w = output[2 * _numAnchors + i];
                float h = output[3 * _numAnchors + i];

                float maxScore = 0;
                int bestClass = -1;

                for (int c = 0; c < _outputClassCount; c++)
                {
                    float score = output[(4 + c) * _numAnchors + i];
                    if (score > maxScore)
                    {
                        maxScore = score;
                        bestClass = c;
                    }
                }

                if (maxScore < confidenceThreshold) continue;
                if (bestClass < 0) continue;

                cx = (cx - padX) / scaleX;
                cy = (cy - padY) / scaleY;
                w = w / scaleX;
                h = h / scaleY;

                int x1 = (int)(cx - w / 2);
                int y1 = (int)(cy - h / 2);
                int width = (int)w;
                int height = (int)h;

                x1 = Math.Max(0, x1);
                y1 = Math.Max(0, y1);
                width = Math.Min(width, origWidth - x1);
                height = Math.Min(height, origHeight - y1);

                if (width > 0 && height > 0)
                {
                    detections.Add(new IconDetectionResult
                    {
                        Bounds = new Rectangle(x1, y1, width, height),
                        Confidence = maxScore,
                        Center = new Point(x1 + width / 2, y1 + height / 2),
                        ClassId = bestClass
                    });
                }
            }

            return detections;
        }

        // YOLOv10格式 - 获取所有检测
        private List<IconDetectionResult> PostprocessYoloV10AllResults(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
            int inputSize,
            float confidenceThreshold,
            float scaleX, float scaleY, float padX, float padY,
            int origWidth, int origHeight)
        {
            var detections = new List<IconDetectionResult>();
            var outputTensor = results.First(r => r.Name == _outputLayerName);
            var output = outputTensor.AsEnumerable<float>().ToArray();

            if (_outputShape.Length == 3 && _outputShape[2] == 6)
            {
                int numDetections = (int)_outputShape[1];

                for (int i = 0; i < numDetections; i++)
                {
                    int offset = i * 6;

                    float confidence = output[offset + 4];
                    if (confidence < confidenceThreshold) continue;

                    int classId = (int)output[offset + 5];

                    float x1 = output[offset + 0];
                    float y1 = output[offset + 1];
                    float x2 = output[offset + 2];
                    float y2 = output[offset + 3];

                    float cx = (x1 + x2) / 2;
                    float cy = (y1 + y2) / 2;
                    float w = x2 - x1;
                    float h = y2 - y1;

                    cx = (cx - padX) / scaleX;
                    cy = (cy - padY) / scaleY;
                    w = w / scaleX;
                    h = h / scaleY;

                    int ix1 = (int)(cx - w / 2);
                    int iy1 = (int)(cy - h / 2);
                    int width = (int)w;
                    int height = (int)h;

                    ix1 = Math.Max(0, ix1);
                    iy1 = Math.Max(0, iy1);
                    width = Math.Min(width, origWidth - ix1);
                    height = Math.Min(height, origHeight - iy1);

                    if (width > 0 && height > 0)
                    {
                        detections.Add(new IconDetectionResult
                        {
                            Bounds = new Rectangle(ix1, iy1, width, height),
                            Confidence = confidence,
                            Center = new Point(ix1 + width / 2, iy1 + height / 2),
                            ClassId = classId
                        });
                    }
                }
            }
            else
            {
                return PostprocessYoloV5AllResults(results, inputSize, confidenceThreshold,
                    scaleX, scaleY, padX, padY, origWidth, origHeight);
            }

            return detections;
        }

        // YOLOv8格式 - 获取所有检测
        private List<IconDetectionResult> PostprocessYoloV8AllResults(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
            int inputSize,
            float confidenceThreshold,
            float scaleX, float scaleY, float padX, float padY,
            int origWidth, int origHeight)
        {
            return PostprocessYoloV5AllResults(results, inputSize, confidenceThreshold,
                scaleX, scaleY, padX, padY, origWidth, origHeight);
        }

        // YOLOv5格式 - 获取所有检测（不过滤类别）
        private List<IconDetectionResult> PostprocessYoloV5AllResults(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
            int inputSize,
            float confidenceThreshold,
            float scaleX, float scaleY, float padX, float padY,
            int origWidth, int origHeight)
        {
            var detections = new List<IconDetectionResult>();

            var outputTensor = results.First(r => r.Name == _outputLayerName);
            var output = outputTensor.AsEnumerable<float>().ToArray();

            int elementsPerDetection = 5 + _outputClassCount;
            if (_outputClassCount == 1 && _outputShape[2] == 5)
            {
                elementsPerDetection = 5;
            }
            else if (_outputClassCount == 1 && _outputShape[2] == 6)
            {
                elementsPerDetection = 6;
            }

            int numDetections = output.Length / elementsPerDetection;

            for (int i = 0; i < numDetections; i++)
            {
                int offset = i * elementsPerDetection;

                float cx = output[offset + 0];
                float cy = output[offset + 1];
                float w = output[offset + 2];
                float h = output[offset + 3];
                float objectness = output[offset + 4];

                if (objectness < confidenceThreshold) continue;

                int bestClass = 0;
                float bestScore = objectness;

                if (elementsPerDetection > 5)
                {
                    if (_outputClassCount == 1)
                    {
                        bestClass = 0;
                        if (elementsPerDetection == 6)
                        {
                            float classScore = output[offset + 5];
                            bestScore = objectness * classScore;
                        }
                    }
                    else
                    {
                        bestClass = -1;
                        bestScore = 0;
                        for (int c = 0; c < _outputClassCount; c++)
                        {
                            float score = output[offset + 5 + c] * objectness;
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestClass = c;
                            }
                        }
                    }
                }

                if (bestScore < confidenceThreshold) continue;

                cx = (cx - padX) / scaleX;
                cy = (cy - padY) / scaleY;
                w = w / scaleX;
                h = h / scaleY;

                int x1 = (int)(cx - w / 2);
                int y1 = (int)(cy - h / 2);
                int width = (int)w;
                int height = (int)h;

                x1 = Math.Max(0, x1);
                y1 = Math.Max(0, y1);
                width = Math.Min(width, origWidth - x1);
                height = Math.Min(height, origHeight - y1);

                if (width > 0 && height > 0)
                {
                    detections.Add(new IconDetectionResult
                    {
                        Bounds = new Rectangle(x1, y1, width, height),
                        Confidence = bestScore,
                        Center = new Point(x1 + width / 2, y1 + height / 2),
                        ClassId = bestClass
                    });
                }
            }

            return detections;
        }

        private List<IconDetectionResult> ApplyNMS(List<IconDetectionResult> detections, float iouThreshold)
        {
            if (detections.Count == 0) return detections;

            var result = new List<IconDetectionResult>();

            // 按类别分组进行NMS
            var groupedByClass = detections.GroupBy(d => d.ClassId);

            foreach (var classGroup in groupedByClass)
            {
                var classDetections = classGroup.OrderByDescending(d => d.Confidence).ToList();

                while (classDetections.Count > 0)
                {
                    var best = classDetections[0];
                    result.Add(best);
                    classDetections.RemoveAt(0);

                    classDetections.RemoveAll(detection =>
                    {
                        double iou = CalculateIOU(best.Bounds, detection.Bounds);
                        return iou > iouThreshold;
                    });
                }
            }

            return result.OrderByDescending(d => d.Confidence).ToList();
        }

        private double CalculateIOU(Rectangle r1, Rectangle r2)
        {
            var intersectX = Math.Max(r1.Left, r2.Left);
            var intersectY = Math.Max(r1.Top, r2.Top);
            var intersectRight = Math.Min(r1.Right, r2.Right);
            var intersectBottom = Math.Min(r1.Bottom, r2.Bottom);

            if (intersectRight < intersectX || intersectBottom < intersectY)
                return 0;

            var intersectArea = (intersectRight - intersectX) * (intersectBottom - intersectY);
            var area1 = r1.Width * r1.Height;
            var area2 = r2.Width * r2.Height;
            var unionArea = area1 + area2 - intersectArea;

            return unionArea > 0 ? (double)intersectArea / unionArea : 0;
        }

        // 以下为辅助方法（保持不变）
        private bool ProcessSingleImageTraditional(
            ImageItem imageItem,
            LabelAdjustmentConfig config,
            Action<string> statusCallback)
        {
            statusCallback?.Invoke("传统模板匹配方法暂未实现，请使用ONNX模型");
            return false;
        }

        private Annotation FindNearestAnnotation(
            List<Annotation> annotations,
            IconDetectionResult iconLocation,
            LabelPosition position,
            int imageWidth,
            int imageHeight)
        {
            double iconCenterX = (double)iconLocation.Center.X / imageWidth;
            double iconCenterY = (double)iconLocation.Center.Y / imageHeight;

            Annotation nearest = null;
            double minDistance = double.MaxValue;

            foreach (var annotation in annotations)
            {
                bool isCandidate = false;
                double distance = 0;

                switch (position)
                {
                    case LabelPosition.Above:
                        if (annotation.Y > iconCenterY)
                        {
                            isCandidate = true;
                            distance = annotation.Y - iconCenterY;
                        }
                        break;

                    case LabelPosition.Below:
                        if (annotation.Y < iconCenterY)
                        {
                            isCandidate = true;
                            distance = iconCenterY - annotation.Y;
                        }
                        break;

                    case LabelPosition.Left:
                        if (annotation.X > iconCenterX)
                        {
                            isCandidate = true;
                            distance = annotation.X - iconCenterX;
                        }
                        break;

                    case LabelPosition.Right:
                        if (annotation.X < iconCenterX)
                        {
                            isCandidate = true;
                            distance = iconCenterX - annotation.X;
                        }
                        break;
                }

                if (isCandidate)
                {
                    if (position == LabelPosition.Above || position == LabelPosition.Below)
                    {
                        double annotLeft = annotation.X - annotation.Width / 2;
                        double annotRight = annotation.X + annotation.Width / 2;
                        if (iconCenterX < annotLeft || iconCenterX > annotRight)
                        {
                            isCandidate = false;
                        }
                    }
                    else
                    {
                        double annotTop = annotation.Y - annotation.Height / 2;
                        double annotBottom = annotation.Y + annotation.Height / 2;
                        if (iconCenterY < annotTop || iconCenterY > annotBottom)
                        {
                            isCandidate = false;
                        }
                    }
                }

                if (isCandidate && distance < minDistance)
                {
                    minDistance = distance;
                    nearest = annotation;
                }
            }

            return nearest;
        }

        private bool AdjustAnnotationBounds(
            Annotation annotation,
            IconDetectionResult iconLocation,
            LabelAdjustmentConfig config,
            int imageWidth,
            int imageHeight)
        {
            double iconLeft = (double)iconLocation.Bounds.Left / imageWidth;
            double iconTop = (double)iconLocation.Bounds.Top / imageHeight;
            double iconRight = (double)iconLocation.Bounds.Right / imageWidth;
            double iconBottom = (double)iconLocation.Bounds.Bottom / imageHeight;

            double annotLeft = annotation.X - annotation.Width / 2;
            double annotTop = annotation.Y - annotation.Height / 2;
            double annotRight = annotation.X + annotation.Width / 2;
            double annotBottom = annotation.Y + annotation.Height / 2;

            bool modified = false;

            if (config.Mode == AdjustmentMode.Cover)
            {
                switch (config.Position)
                {
                    case LabelPosition.Above:
                        if (iconTop < annotTop)
                        {
                            annotTop = iconTop - 0.002;
                            modified = true;
                        }
                        break;

                    case LabelPosition.Below:
                        if (iconBottom > annotBottom)
                        {
                            annotBottom = iconBottom + 0.002;
                            modified = true;
                        }
                        break;

                    case LabelPosition.Left:
                        if (iconLeft < annotLeft)
                        {
                            annotLeft = iconLeft - 0.002;
                            modified = true;
                        }
                        break;

                    case LabelPosition.Right:
                        if (iconRight > annotRight)
                        {
                            annotRight = iconRight + 0.002;
                            modified = true;
                        }
                        break;
                }
            }
            else // AdjustmentMode.Avoid
            {
                switch (config.Position)
                {
                    case LabelPosition.Above:
                        if (annotTop < iconBottom)
                        {
                            annotTop = iconBottom + 0.002;
                            modified = true;
                        }
                        break;

                    case LabelPosition.Below:
                        if (annotBottom > iconTop)
                        {
                            annotBottom = iconTop - 0.002;
                            modified = true;
                        }
                        break;

                    case LabelPosition.Left:
                        if (annotLeft < iconRight)
                        {
                            annotLeft = iconRight + 0.002;
                            modified = true;
                        }
                        break;

                    case LabelPosition.Right:
                        if (annotRight > iconLeft)
                        {
                            annotRight = iconLeft - 0.002;
                            modified = true;
                        }
                        break;
                }
            }

            if (modified)
            {
                double newWidth = annotRight - annotLeft;
                double newHeight = annotBottom - annotTop;

                if (newWidth > 0.005 && newHeight > 0.005)
                {
                    annotation.X = (annotLeft + annotRight) / 2;
                    annotation.Y = (annotTop + annotBottom) / 2;
                    annotation.Width = newWidth;
                    annotation.Height = newHeight;

                    annotation.X = Math.Max(0, Math.Min(1, annotation.X));
                    annotation.Y = Math.Max(0, Math.Min(1, annotation.Y));
                    annotation.Width = Math.Max(0, Math.Min(1, annotation.Width));
                    annotation.Height = Math.Max(0, Math.Min(1, annotation.Height));

                    return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            lock (_sessionLock)
            {
                _session?.Dispose();
                _session = null;
            }
        }
    }
}
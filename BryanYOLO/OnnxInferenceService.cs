using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BryanYOLO.Models;
using BryanYOLO.ViewModels;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = SixLabors.ImageSharp.Color;
using Annotation = BryanYOLO.Models.Annotation;

namespace BryanYOLO.Services
{
    public class OnnxInferenceService : IDisposable
    {
        private InferenceSession _session;
        private readonly AnnotationService _annotationService;
        private readonly object _sessionLock = new object();
        private int _outputClassCount = 80; // 默认COCO类别数，会在初始化时更新
        private string _outputLayerName = "output0"; // 输出层名称
        private YoloVersion _modelVersion = YoloVersion.V5; // 模型版本
        private long[] _outputShape = null; // 输出形状
        private int _numAnchors = 8400; // YOLOv11默认anchor数
        private int _userSpecifiedClassCount = -1; // 用户指定的类别数

        // 用于存储每个检测框的置信度（用于后续的低置信度移除）
        public class AnnotationWithConfidence
        {
            public Annotation Annotation { get; set; }
            public float Confidence { get; set; }
        }

        public OnnxInferenceService()
        {
            _annotationService = new AnnotationService();
        }

        public async Task RunBatchInference(
            string modelPath,
            List<ImageItem> images,
            int inputSize,
            List<LabelMapping> labelMappings,
            float confidenceThreshold,
            bool overwriteAllLabels,  // 新增参数：是否覆盖所有标签
            bool removeOverlappingLowConfidence,  // 新增参数：是否移除低置信度重叠框
            float overlapRemovalThreshold,  // 新增参数：移除低置信度的阈值
            Action<double> progressCallback,
            Action<string> statusCallback,
            int modelClassCount = -1,
            YoloVersion modelVersion = YoloVersion.V5)
        {
            await Task.Run(() =>
            {
                try
                {
                    // 设置模型版本
                    _modelVersion = modelVersion;

                    // 设置用户指定的类别数
                    if (modelClassCount > 0)
                    {
                        _userSpecifiedClassCount = modelClassCount;
                        statusCallback?.Invoke($"使用用户指定的类别数: {modelClassCount}");
                    }

                    statusCallback?.Invoke($"正在初始化推理引擎（{modelVersion}）...");

                    // 创建推理会话，使用DML (DirectML)
                    var sessionOptions = new SessionOptions();
                    try
                    {
                        sessionOptions.AppendExecutionProvider_DML(0); // 尝试使用GPU
                        statusCallback?.Invoke("使用GPU加速 (DirectML)");
                    }
                    catch
                    {
                        // 如果GPU不可用，回退到CPU
                        sessionOptions.AppendExecutionProvider_CPU(0);
                        statusCallback?.Invoke("使用CPU推理");
                    }
                    sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

                    lock (_sessionLock)
                    {
                        _session?.Dispose();
                        _session = new InferenceSession(modelPath, sessionOptions);

                        // 检查模型的输入输出信息
                        var inputMeta = _session.InputMetadata;
                        var outputMeta = _session.OutputMetadata;

                        // 获取输出层名称
                        _outputLayerName = outputMeta.Keys.FirstOrDefault() ?? "output0";

                        // 获取输出形状
                        var outputNodeMeta = outputMeta[_outputLayerName];
                        var dimensions = outputNodeMeta.Dimensions;
                        if (dimensions != null)
                        {
                            _outputShape = dimensions.Select(d => (long)d).ToArray();
                        }

                        // 根据模型版本和输出形状判断类别数
                        DetectModelFormatAndClassCount(statusCallback);

                        // 调试输出
                        string shapeStr = _outputShape != null ? string.Join(", ", _outputShape.Select(x => x.ToString())) : "unknown";
                        statusCallback?.Invoke($"模型输出形状: [{shapeStr}]");
                        statusCallback?.Invoke($"模型版本: {_modelVersion}");
                        statusCallback?.Invoke($"最终使用的类别数: {_outputClassCount}");
                    }

                    // 输出映射信息用于调试
                    if (labelMappings != null && labelMappings.Count > 0)
                    {
                        statusCallback?.Invoke($"标签映射: {string.Join(", ", labelMappings.Select(m => $"{m.ModelClassId}→{m.ProjectClassId}"))}");
                    }

                    // 输出推理选项
                    if (overwriteAllLabels)
                    {
                        statusCallback?.Invoke("覆盖模式：将清空原有标签后重新标注");
                    }
                    if (removeOverlappingLowConfidence)
                    {
                        statusCallback?.Invoke($"移除低置信度：将移除置信度低于 {overlapRemovalThreshold:F2} 的重叠框");
                    }

                    int processedCount = 0;
                    int totalCount = images.Count;
                    int totalDetections = 0;
                    int totalAdded = 0;
                    int totalRemoved = 0;

                    // 按顺序处理，以便更好地显示进度
                    foreach (var image in images)
                    {
                        try
                        {
                            statusCallback?.Invoke($"正在处理: {image.FileName} ({processedCount + 1}/{totalCount})");
                            var (detectionCount, addedCount, removedCount) = ProcessSingleImage(
                                image,
                                inputSize,
                                labelMappings,
                                confidenceThreshold,
                                overwriteAllLabels,
                                removeOverlappingLowConfidence,
                                overlapRemovalThreshold,
                                statusCallback);

                            totalDetections += detectionCount;
                            totalAdded += addedCount;
                            totalRemoved += removedCount;
                            processedCount++;
                            progressCallback?.Invoke((double)processedCount / totalCount);
                        }
                        catch (Exception ex)
                        {
                            statusCallback?.Invoke($"处理 {image.FileName} 时出错: {ex.Message}");
                            Console.WriteLine($"处理图片 {image.FileName} 时出错: {ex}");
                        }
                    }

                    var finalMessage = $"推理完成！处理了 {processedCount} 张图片，检测到 {totalDetections} 个目标，新增 {totalAdded} 个标注";
                    if (totalRemoved > 0)
                    {
                        finalMessage += $"，移除 {totalRemoved} 个低置信度标注";
                    }
                    statusCallback?.Invoke(finalMessage);
                }
                catch (Exception ex)
                {
                    statusCallback?.Invoke($"推理失败: {ex.Message}");
                    throw;
                }
                finally
                {
                    lock (_sessionLock)
                    {
                        _session?.Dispose();
                        _session = null;
                    }
                }
            });
        }

        private void DetectModelFormatAndClassCount(Action<string> statusCallback)
        {
            if (_outputShape == null || _outputShape.Length < 2)
            {
                statusCallback?.Invoke("警告：无法识别输出形状");
                return;
            }

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

        private void ProcessYoloV11Format(Action<string> statusCallback)
        {
            // YOLOv11格式: [1, nc+4, 8400] 或 [1, nc+4, 6300] 或 [1, nc+4, 1600]
            if (_outputShape.Length == 3)
            {
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
            // YOLOv10格式: 通常是 [1, num_detections, 6] 或 [1, num_detections, 5+nc]
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

                statusCallback?.Invoke($"YOLOv10模型: {_outputClassCount} 个类别");
            }
        }

        private void ProcessYoloV8Format(Action<string> statusCallback)
        {
            // YOLOv8格式: 类似YOLOv5，但可能有细微差别
            ProcessYoloV5Format(statusCallback);
            statusCallback?.Invoke($"YOLOv8模型: {_outputClassCount} 个类别");
        }

        private void ProcessYoloV5Format(Action<string> statusCallback)
        {
            // YOLOv5格式: [1, 25200, 85] 对于COCO，或 [1, num_anchors, 5+nc]
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

        private (int detectionCount, int addedCount, int removedCount) ProcessSingleImage(
            ImageItem imageItem,
            int inputSize,
            List<LabelMapping> labelMappings,
            float confidenceThreshold,
            bool overwriteAllLabels,
            bool removeOverlappingLowConfidence,
            float overlapRemovalThreshold,
            Action<string> statusCallback)
        {
            // 加载并预处理图片
            var (inputTensor, scaleX, scaleY, padX, padY, origWidth, origHeight) = PreprocessImage(imageItem.ImagePath, inputSize);

            // 运行推理
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("images", inputTensor)
            };

            List<AnnotationWithConfidence> detectionsWithConfidence;
            lock (_sessionLock)
            {
                using var results = _session.Run(inputs);

                // 根据模型版本选择后处理方法，现在返回带置信度的检测结果
                detectionsWithConfidence = _modelVersion switch
                {
                    YoloVersion.V11 => PostprocessYoloV11ResultsWithConfidence(results, inputSize, confidenceThreshold,
                        scaleX, scaleY, padX, padY, origWidth, origHeight),
                    YoloVersion.V10 => PostprocessYoloV10ResultsWithConfidence(results, inputSize, confidenceThreshold,
                        scaleX, scaleY, padX, padY, origWidth, origHeight),
                    YoloVersion.V8 => PostprocessYoloV8ResultsWithConfidence(results, inputSize, confidenceThreshold,
                        scaleX, scaleY, padX, padY, origWidth, origHeight),
                    _ => PostprocessYoloV5ResultsWithConfidence(results, inputSize, confidenceThreshold,
                        scaleX, scaleY, padX, padY, origWidth, origHeight)
                };
            }

            // 应用NMS（非极大值抑制）
            detectionsWithConfidence = ApplyNMSWithConfidence(detectionsWithConfidence, 0.45f);

            int originalDetectionCount = detectionsWithConfidence.Count;

            // 应用标签映射并过滤
            if (labelMappings != null && labelMappings.Count > 0)
            {
                var mappedDetections = new List<AnnotationWithConfidence>();

                foreach (var detection in detectionsWithConfidence)
                {
                    // 查找映射
                    var mapping = labelMappings.FirstOrDefault(m => m.ModelClassId == detection.Annotation.ClassId);
                    if (mapping != null)
                    {
                        // 创建映射后的标注
                        var mappedAnnotation = detection.Annotation.Clone();
                        mappedAnnotation.ClassId = mapping.ProjectClassId;
                        mappedDetections.Add(new AnnotationWithConfidence
                        {
                            Annotation = mappedAnnotation,
                            Confidence = detection.Confidence
                        });
                    }
                }

                detectionsWithConfidence = mappedDetections;

                if (originalDetectionCount > 0 && detectionsWithConfidence.Count == 0)
                {
                    statusCallback?.Invoke($"  检测到 {originalDetectionCount} 个目标，但没有匹配的映射");
                }
            }

            // 加载现有标注或清空（如果选择覆盖模式）
            List<Annotation> existingAnnotations;
            if (overwriteAllLabels)
            {
                existingAnnotations = new List<Annotation>();
                statusCallback?.Invoke($"  覆盖模式：清空了 {imageItem.FileName} 的原有标注");
            }
            else
            {
                existingAnnotations = _annotationService.LoadAnnotations(imageItem.AnnotationPath);
            }

            int originalCount = existingAnnotations.Count;
            int removedCount = 0;

            // 合并新检测结果到现有标注
            if (removeOverlappingLowConfidence)
            {
                // 使用带置信度的合并方法
                var (hasChanges, removed) = MergeDetectionsWithConfidenceFilter(
                    existingAnnotations,
                    detectionsWithConfidence,
                    overlapRemovalThreshold);
                removedCount = removed;

                if (hasChanges || overwriteAllLabels)
                {
                    SaveAnnotations(imageItem, existingAnnotations);
                    int addedCount = existingAnnotations.Count - originalCount + removedCount;
                    return (detectionsWithConfidence.Count, addedCount, removedCount);
                }
            }
            else
            {
                // 使用原来的合并方法（不考虑置信度）
                var detections = detectionsWithConfidence.Select(d => d.Annotation).ToList();
                bool hasChanges = MergeDetections(existingAnnotations, detections);

                if (hasChanges || overwriteAllLabels)
                {
                    SaveAnnotations(imageItem, existingAnnotations);
                    int addedCount = existingAnnotations.Count - originalCount;
                    return (detections.Count, addedCount, 0);
                }
            }

            return (detectionsWithConfidence.Count, 0, removedCount);
        }

        private void SaveAnnotations(ImageItem imageItem, List<Annotation> annotations)
        {
            // 确保标注文件路径的目录存在
            var dir = Path.GetDirectoryName(imageItem.AnnotationPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _annotationService.SaveAnnotations(imageItem.AnnotationPath, annotations);
            imageItem.UpdateStatus();
        }

        private (bool hasChanges, int removedCount) MergeDetectionsWithConfidenceFilter(
            List<Annotation> existingAnnotations,
            List<AnnotationWithConfidence> newAnnotations,
            float confidenceThreshold)
        {
            bool hasChanges = false;
            int removedCount = 0;
            const double overlapThreshold = 0.5; // IOU阈值

            // 用于存储要移除的索引
            var toRemove = new HashSet<int>();

            foreach (var newDetection in newAnnotations)
            {
                bool isDuplicate = false;
                int overlapIndex = -1;
                double maxIou = 0;

                // 检查是否与现有标注重叠
                for (int i = 0; i < existingAnnotations.Count; i++)
                {
                    if (toRemove.Contains(i)) continue;

                    var existing = existingAnnotations[i];
                    if (existing.ClassId == newDetection.Annotation.ClassId)
                    {
                        double iou = CalculateIOU(existing, newDetection.Annotation);
                        if (iou > overlapThreshold)
                        {
                            isDuplicate = true;
                            if (iou > maxIou)
                            {
                                maxIou = iou;
                                overlapIndex = i;
                            }
                        }
                    }
                }

                if (isDuplicate && overlapIndex >= 0)
                {
                    // 如果新检测的置信度更高，替换原有的
                    if (newDetection.Confidence >= confidenceThreshold)
                    {
                        existingAnnotations[overlapIndex] = newDetection.Annotation;
                        hasChanges = true;
                    }
                    else
                    {
                        // 新检测的置信度低，标记原有的为移除
                        toRemove.Add(overlapIndex);
                        removedCount++;
                        hasChanges = true;
                    }
                }
                else if (!isDuplicate)
                {
                    // 不重叠，直接添加
                    existingAnnotations.Add(newDetection.Annotation);
                    hasChanges = true;
                }
            }

            // 移除标记的项（从后往前移除以避免索引问题）
            var sortedIndices = toRemove.OrderByDescending(i => i).ToList();
            foreach (var index in sortedIndices)
            {
                existingAnnotations.RemoveAt(index);
            }

            return (hasChanges, removedCount);
        }

        private bool MergeDetections(List<Annotation> existingAnnotations, List<Annotation> newAnnotations)
        {
            bool hasChanges = false;
            const double overlapThreshold = 0.5; // IOU阈值

            foreach (var newAnnotation in newAnnotations)
            {
                // 检查是否与现有标注重叠
                bool isDuplicate = false;
                foreach (var existing in existingAnnotations)
                {
                    if (existing.ClassId == newAnnotation.ClassId)
                    {
                        double iou = CalculateIOU(existing, newAnnotation);
                        if (iou > overlapThreshold)
                        {
                            isDuplicate = true;
                            break;
                        }
                    }
                }

                if (!isDuplicate)
                {
                    existingAnnotations.Add(newAnnotation);
                    hasChanges = true;
                }
            }

            return hasChanges;
        }

        // 后处理方法 - 返回带置信度的结果
        private List<AnnotationWithConfidence> PostprocessYoloV11ResultsWithConfidence(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
            int inputSize,
            float confidenceThreshold,
            float scaleX, float scaleY, float padX, float padY,
            int origWidth, int origHeight)
        {
            var detections = new List<AnnotationWithConfidence>();

            // 获取输出张量
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

                var annotation = new Annotation
                {
                    ClassId = bestClass,
                    X = cx / origWidth,
                    Y = cy / origHeight,
                    Width = w / origWidth,
                    Height = h / origHeight
                };

                annotation.X = Math.Max(0, Math.Min(1, annotation.X));
                annotation.Y = Math.Max(0, Math.Min(1, annotation.Y));
                annotation.Width = Math.Max(0, Math.Min(1, annotation.Width));
                annotation.Height = Math.Max(0, Math.Min(1, annotation.Height));

                if (annotation.X - annotation.Width / 2 < 0)
                    annotation.Width = annotation.X * 2;
                if (annotation.Y - annotation.Height / 2 < 0)
                    annotation.Height = annotation.Y * 2;
                if (annotation.X + annotation.Width / 2 > 1)
                    annotation.Width = (1 - annotation.X) * 2;
                if (annotation.Y + annotation.Height / 2 > 1)
                    annotation.Height = (1 - annotation.Y) * 2;

                detections.Add(new AnnotationWithConfidence
                {
                    Annotation = annotation,
                    Confidence = maxScore
                });
            }

            return detections;
        }

        private List<AnnotationWithConfidence> PostprocessYoloV10ResultsWithConfidence(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
            int inputSize,
            float confidenceThreshold,
            float scaleX, float scaleY, float padX, float padY,
            int origWidth, int origHeight)
        {
            var detections = new List<AnnotationWithConfidence>();
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

                    float x1 = output[offset + 0];
                    float y1 = output[offset + 1];
                    float x2 = output[offset + 2];
                    float y2 = output[offset + 3];
                    int classId = (int)output[offset + 5];

                    float cx = (x1 + x2) / 2;
                    float cy = (y1 + y2) / 2;
                    float w = x2 - x1;
                    float h = y2 - y1;

                    cx = (cx - padX) / scaleX;
                    cy = (cy - padY) / scaleY;
                    w = w / scaleX;
                    h = h / scaleY;

                    var annotation = new Annotation
                    {
                        ClassId = classId,
                        X = cx / origWidth,
                        Y = cy / origHeight,
                        Width = w / origWidth,
                        Height = h / origHeight
                    };

                    annotation.X = Math.Max(0, Math.Min(1, annotation.X));
                    annotation.Y = Math.Max(0, Math.Min(1, annotation.Y));
                    annotation.Width = Math.Max(0, Math.Min(1, annotation.Width));
                    annotation.Height = Math.Max(0, Math.Min(1, annotation.Height));

                    detections.Add(new AnnotationWithConfidence
                    {
                        Annotation = annotation,
                        Confidence = confidence
                    });
                }
            }
            else
            {
                return PostprocessYoloV5ResultsWithConfidence(results, inputSize, confidenceThreshold,
                    scaleX, scaleY, padX, padY, origWidth, origHeight);
            }

            return detections;
        }

        private List<AnnotationWithConfidence> PostprocessYoloV8ResultsWithConfidence(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
            int inputSize,
            float confidenceThreshold,
            float scaleX, float scaleY, float padX, float padY,
            int origWidth, int origHeight)
        {
            return PostprocessYoloV5ResultsWithConfidence(results, inputSize, confidenceThreshold,
                scaleX, scaleY, padX, padY, origWidth, origHeight);
        }

        private List<AnnotationWithConfidence> PostprocessYoloV5ResultsWithConfidence(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
            int inputSize,
            float confidenceThreshold,
            float scaleX, float scaleY, float padX, float padY,
            int origWidth, int origHeight)
        {
            var detections = new List<AnnotationWithConfidence>();

            var outputTensor = results.First(r => r.Name == _outputLayerName);
            var output = outputTensor.AsEnumerable<float>().ToArray();

            int elementsPerDetection = 5 + _outputClassCount;
            int numDetections = output.Length / elementsPerDetection;

            for (int i = 0; i < numDetections; i++)
            {
                int offset = i * elementsPerDetection;

                float confidence = output[offset + 4];
                if (confidence < confidenceThreshold) continue;

                float cx = output[offset + 0];
                float cy = output[offset + 1];
                float w = output[offset + 2];
                float h = output[offset + 3];

                int bestClass = -1;
                float bestScore = 0;
                for (int c = 0; c < _outputClassCount; c++)
                {
                    float score = output[offset + 5 + c];
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestClass = c;
                    }
                }

                float finalScore = bestScore * confidence;
                if (finalScore < confidenceThreshold || bestClass < 0) continue;

                cx = (cx - padX) / scaleX;
                cy = (cy - padY) / scaleY;
                w = w / scaleX;
                h = h / scaleY;

                var annotation = new Annotation
                {
                    ClassId = bestClass,
                    X = cx / origWidth,
                    Y = cy / origHeight,
                    Width = w / origWidth,
                    Height = h / origHeight
                };

                annotation.X = Math.Max(0, Math.Min(1, annotation.X));
                annotation.Y = Math.Max(0, Math.Min(1, annotation.Y));
                annotation.Width = Math.Max(0, Math.Min(1, annotation.Width));
                annotation.Height = Math.Max(0, Math.Min(1, annotation.Height));

                if (annotation.X - annotation.Width / 2 < 0)
                    annotation.Width = annotation.X * 2;
                if (annotation.Y - annotation.Height / 2 < 0)
                    annotation.Height = annotation.Y * 2;
                if (annotation.X + annotation.Width / 2 > 1)
                    annotation.Width = (1 - annotation.X) * 2;
                if (annotation.Y + annotation.Height / 2 > 1)
                    annotation.Height = (1 - annotation.Y) * 2;

                detections.Add(new AnnotationWithConfidence
                {
                    Annotation = annotation,
                    Confidence = finalScore
                });
            }

            return detections;
        }

        // NMS处理方法 - 带置信度版本
        private List<AnnotationWithConfidence> ApplyNMSWithConfidence(List<AnnotationWithConfidence> detections, float iouThreshold)
        {
            if (detections.Count == 0) return detections;

            var result = new List<AnnotationWithConfidence>();

            // 按类别分组
            var groupedByClass = detections.GroupBy(d => d.Annotation.ClassId);

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
                        double iou = CalculateIOU(best.Annotation, detection.Annotation);
                        return iou > iouThreshold;
                    });
                }
            }

            return result.OrderByDescending(d => d.Confidence).ToList();
        }


        // 保留原有的后处理方法（兼容性）
        private List<Annotation> PostprocessYoloV11Results(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
            int inputSize,
            float confidenceThreshold,
            float scaleX, float scaleY, float padX, float padY,
            int origWidth, int origHeight)
        {
            var detectionsWithConfidence = PostprocessYoloV11ResultsWithConfidence(
                results, inputSize, confidenceThreshold, scaleX, scaleY, padX, padY, origWidth, origHeight);
            return detectionsWithConfidence.Select(d => d.Annotation).ToList();
        }

        private List<Annotation> PostprocessYoloV10Results(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
            int inputSize,
            float confidenceThreshold,
            float scaleX, float scaleY, float padX, float padY,
            int origWidth, int origHeight)
        {
            var detectionsWithConfidence = PostprocessYoloV10ResultsWithConfidence(
                results, inputSize, confidenceThreshold, scaleX, scaleY, padX, padY, origWidth, origHeight);
            return detectionsWithConfidence.Select(d => d.Annotation).ToList();
        }

        private List<Annotation> PostprocessYoloV8Results(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
            int inputSize,
            float confidenceThreshold,
            float scaleX, float scaleY, float padX, float padY,
            int origWidth, int origHeight)
        {
            var detectionsWithConfidence = PostprocessYoloV8ResultsWithConfidence(
                results, inputSize, confidenceThreshold, scaleX, scaleY, padX, padY, origWidth, origHeight);
            return detectionsWithConfidence.Select(d => d.Annotation).ToList();
        }

        private List<Annotation> PostprocessYoloV5Results(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
            int inputSize,
            float confidenceThreshold,
            float scaleX, float scaleY, float padX, float padY,
            int origWidth, int origHeight)
        {
            var detectionsWithConfidence = PostprocessYoloV5ResultsWithConfidence(
                results, inputSize, confidenceThreshold, scaleX, scaleY, padX, padY, origWidth, origHeight);
            return detectionsWithConfidence.Select(d => d.Annotation).ToList();
        }

        private List<Annotation> ApplyNMS(List<Annotation> detections, float iouThreshold)
        {
            if (detections.Count == 0) return detections;

            var result = new List<Annotation>();

            // 按类别分组
            var groupedByClass = detections.GroupBy(d => d.ClassId);

            foreach (var classGroup in groupedByClass)
            {
                var classDetections = classGroup.OrderByDescending(d => d.Width * d.Height).ToList();

                while (classDetections.Count > 0)
                {
                    var best = classDetections[0];
                    result.Add(best);
                    classDetections.RemoveAt(0);

                    classDetections.RemoveAll(detection =>
                    {
                        double iou = CalculateIOU(best, detection);
                        return iou > iouThreshold;
                    });
                }
            }

            return result;
        }

        private (DenseTensor<float> tensor, float scaleX, float scaleY, float padX, float padY, int origWidth, int origHeight)
            PreprocessImage(string imagePath, int inputSize)
        {
            using var image = SixLabors.ImageSharp.Image.Load<Rgb24>(imagePath);

            var origWidth = image.Width;
            var origHeight = image.Height;

            // 计算缩放比例（保持宽高比）
            float scale = Math.Min((float)inputSize / origWidth, (float)inputSize / origHeight);
            int newWidth = (int)(origWidth * scale);
            int newHeight = (int)(origHeight * scale);

            // 计算填充
            int padX = (inputSize - newWidth) / 2;
            int padY = (inputSize - newHeight) / 2;

            // 调整图片大小
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(inputSize, inputSize),
                Mode = ResizeMode.Pad,
                PadColor = Color.Gray,
                Position = AnchorPositionMode.Center
            }));

            // 创建输入张量
            var tensor = new DenseTensor<float>(new[] { 1, 3, inputSize, inputSize });

            // 归一化并转换到张量
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

        private double CalculateIOU(Annotation a1, Annotation a2)
        {
            double x1_min = a1.X - a1.Width / 2;
            double y1_min = a1.Y - a1.Height / 2;
            double x1_max = a1.X + a1.Width / 2;
            double y1_max = a1.Y + a1.Height / 2;

            double x2_min = a2.X - a2.Width / 2;
            double y2_min = a2.Y - a2.Height / 2;
            double x2_max = a2.X + a2.Width / 2;
            double y2_max = a2.Y + a2.Height / 2;

            double intersect_x_min = Math.Max(x1_min, x2_min);
            double intersect_y_min = Math.Max(y1_min, y2_min);
            double intersect_x_max = Math.Min(x1_max, x2_max);
            double intersect_y_max = Math.Min(y1_max, y2_max);

            if (intersect_x_max < intersect_x_min || intersect_y_max < intersect_y_min)
                return 0;

            double intersect_area = (intersect_x_max - intersect_x_min) * (intersect_y_max - intersect_y_min);
            double area1 = a1.Width * a1.Height;
            double area2 = a2.Width * a2.Height;
            double union_area = area1 + area2 - intersect_area;

            return union_area > 0 ? intersect_area / union_area : 0;
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
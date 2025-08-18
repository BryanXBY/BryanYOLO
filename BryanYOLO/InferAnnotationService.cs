using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BryanYOLO.Models;

namespace BryanYOLO.Services
{
    public class InferAnnotationService
    {
        private readonly AnnotationService _annotationService;

        public enum InferPosition
        {
            Inside,     // 内部
            Above,      // 上方
            Below,      // 下方
            Left,       // 左边
            Right       // 右边
        }

        public enum AlignmentMode
        {
            Left,       // 左对齐
            Center,     // 居中对齐
            Right       // 右对齐
        }

        public class InferConfig
        {
            public int BaseClassId { get; set; }           // 基准框体的类别ID
            public int TargetClassId { get; set; }         // 推测框体的类别ID
            public InferPosition Position { get; set; }     // 推测框体相对位置
            public double XOffset { get; set; }            // X轴偏移（归一化坐标）
            public double YOffset { get; set; }            // Y轴偏移（归一化坐标）
            public double WidthPercent { get; set; }       // 宽度百分比 (0-100)
            public double HeightPercent { get; set; }      // 高度百分比 (0-100)
            public AlignmentMode Alignment { get; set; }    // 对齐方式
            public bool ReplaceExisting { get; set; }      // 是否替换已存在的目标类别标注
            public bool ProcessCurrentImageOnly { get; set; } // 仅处理当前图片
        }

        public InferAnnotationService()
        {
            _annotationService = new AnnotationService();
        }

        public async Task<int> ProcessBatchInference(
            List<ImageItem> images,
            InferConfig config,
            Action<double> progressCallback,
            Action<string> statusCallback)
        {
            return await Task.Run(() =>
            {
                int processedCount = 0;
                int modifiedCount = 0;

                // 使用并行处理提高效率
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };

                object lockObj = new object();

                Parallel.ForEach(images, parallelOptions, (imageItem) =>
                {
                    try
                    {
                        bool modified = ProcessSingleImage(imageItem, config);

                        lock (lockObj)
                        {
                            processedCount++;
                            if (modified) modifiedCount++;

                            double progress = (double)processedCount / images.Count;
                            progressCallback?.Invoke(progress);

                            if (processedCount % 10 == 0) // 每处理10张更新一次状态
                            {
                                statusCallback?.Invoke($"已处理 {processedCount}/{images.Count} 张图片");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (lockObj)
                        {
                            statusCallback?.Invoke($"处理 {imageItem.FileName} 时出错：{ex.Message}");
                        }
                    }
                });

                statusCallback?.Invoke($"推测完成，共修改了 {modifiedCount} 张图片");
                return modifiedCount;
            });
        }

        private bool ProcessSingleImage(ImageItem imageItem, InferConfig config)
        {
            // 加载现有标注
            var annotations = _annotationService.LoadAnnotations(imageItem.AnnotationPath);
            if (annotations.Count == 0) return false;

            // 找到所有基准框体
            var baseAnnotations = annotations.Where(a => a.ClassId == config.BaseClassId).ToList();
            if (baseAnnotations.Count == 0) return false;

            bool hasChanges = false;
            var newAnnotations = new List<Annotation>();

            foreach (var baseAnnotation in baseAnnotations)
            {
                // 检查是否已存在目标类别的标注（在附近）
                if (!config.ReplaceExisting)
                {
                    bool existsNearby = annotations.Any(a =>
                        a.ClassId == config.TargetClassId &&
                        IsNearby(a, baseAnnotation, 0.3)); // IOU阈值0.3

                    if (existsNearby) continue;
                }

                // 计算推测框体的位置和大小
                var inferredAnnotation = CalculateInferredAnnotation(baseAnnotation, config);
                if (inferredAnnotation != null)
                {
                    newAnnotations.Add(inferredAnnotation);
                    hasChanges = true;
                }
            }

            // 如果需要替换，先移除已存在的目标类别标注
            if (config.ReplaceExisting && hasChanges)
            {
                annotations.RemoveAll(a => a.ClassId == config.TargetClassId);
            }

            // 添加新的推测标注
            if (hasChanges)
            {
                annotations.AddRange(newAnnotations);
                _annotationService.SaveAnnotations(imageItem.AnnotationPath, annotations);
                imageItem.UpdateStatus();
                return true;
            }

            return false;
        }

        private Annotation CalculateInferredAnnotation(Annotation baseAnnotation, InferConfig config)
        {
            // 获取基准框体的边界
            double baseLeft = baseAnnotation.X - baseAnnotation.Width / 2;
            double baseRight = baseAnnotation.X + baseAnnotation.Width / 2;
            double baseTop = baseAnnotation.Y - baseAnnotation.Height / 2;
            double baseBottom = baseAnnotation.Y + baseAnnotation.Height / 2;

            // 计算推测框体的宽高
            double inferredWidth = baseAnnotation.Width * (config.WidthPercent / 100.0);
            double inferredHeight = baseAnnotation.Height * (config.HeightPercent / 100.0);

            // 根据位置和对齐方式计算中心点
            double inferredX = 0, inferredY = 0;

            switch (config.Position)
            {
                case InferPosition.Inside:
                    // 内部位置
                    inferredY = baseTop + inferredHeight / 2 + config.YOffset;

                    switch (config.Alignment)
                    {
                        case AlignmentMode.Left:
                            inferredX = baseLeft + inferredWidth / 2 + config.XOffset;
                            break;
                        case AlignmentMode.Center:
                            inferredX = baseAnnotation.X + config.XOffset;
                            break;
                        case AlignmentMode.Right:
                            inferredX = baseRight - inferredWidth / 2 + config.XOffset;
                            break;
                    }
                    break;

                case InferPosition.Above:
                    // 上方位置
                    inferredY = baseTop - inferredHeight / 2 + config.YOffset;

                    switch (config.Alignment)
                    {
                        case AlignmentMode.Left:
                            inferredX = baseLeft + inferredWidth / 2 + config.XOffset;
                            break;
                        case AlignmentMode.Center:
                            inferredX = baseAnnotation.X + config.XOffset;
                            break;
                        case AlignmentMode.Right:
                            inferredX = baseRight - inferredWidth / 2 + config.XOffset;
                            break;
                    }
                    break;

                case InferPosition.Below:
                    // 下方位置
                    inferredY = baseBottom + inferredHeight / 2 + config.YOffset;

                    switch (config.Alignment)
                    {
                        case AlignmentMode.Left:
                            inferredX = baseLeft + inferredWidth / 2 + config.XOffset;
                            break;
                        case AlignmentMode.Center:
                            inferredX = baseAnnotation.X + config.XOffset;
                            break;
                        case AlignmentMode.Right:
                            inferredX = baseRight - inferredWidth / 2 + config.XOffset;
                            break;
                    }
                    break;

                case InferPosition.Left:
                    // 左边位置
                    inferredX = baseLeft - inferredWidth / 2 + config.XOffset;

                    switch (config.Alignment)
                    {
                        case AlignmentMode.Left:
                            inferredY = baseTop + inferredHeight / 2 + config.YOffset;
                            break;
                        case AlignmentMode.Center:
                            inferredY = baseAnnotation.Y + config.YOffset;
                            break;
                        case AlignmentMode.Right:
                            inferredY = baseBottom - inferredHeight / 2 + config.YOffset;
                            break;
                    }
                    break;

                case InferPosition.Right:
                    // 右边位置
                    inferredX = baseRight + inferredWidth / 2 + config.XOffset;

                    switch (config.Alignment)
                    {
                        case AlignmentMode.Left:
                            inferredY = baseTop + inferredHeight / 2 + config.YOffset;
                            break;
                        case AlignmentMode.Center:
                            inferredY = baseAnnotation.Y + config.YOffset;
                            break;
                        case AlignmentMode.Right:
                            inferredY = baseBottom - inferredHeight / 2 + config.YOffset;
                            break;
                    }
                    break;
            }

            // 确保推测框体在有效范围内
            inferredX = Math.Max(inferredWidth / 2, Math.Min(1 - inferredWidth / 2, inferredX));
            inferredY = Math.Max(inferredHeight / 2, Math.Min(1 - inferredHeight / 2, inferredY));

            // 创建推测的标注
            return new Annotation
            {
                ClassId = config.TargetClassId,
                X = inferredX,
                Y = inferredY,
                Width = inferredWidth,
                Height = inferredHeight
            };
        }

        private bool IsNearby(Annotation a1, Annotation a2, double threshold)
        {
            // 计算两个框体的IOU
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
                return false;

            double intersect_area = (intersect_x_max - intersect_x_min) * (intersect_y_max - intersect_y_min);
            double area1 = a1.Width * a1.Height;
            double area2 = a2.Width * a2.Height;
            double union_area = area1 + area2 - intersect_area;

            double iou = union_area > 0 ? intersect_area / union_area : 0;
            return iou > threshold;
        }
    }
}
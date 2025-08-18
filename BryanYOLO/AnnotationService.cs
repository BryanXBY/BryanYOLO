using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Annotation = BryanYOLO.Models.Annotation;
using BryanYOLO.Models;

namespace BryanYOLO.Services
{
    public class AnnotationService
    {
        public List<Annotation> LoadAnnotations(string annotationPath)
        {
            var annotations = new List<Annotation>();

            if (!File.Exists(annotationPath))
                return annotations;

            try
            {
                var lines = File.ReadAllLines(annotationPath);
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var annotation = Annotation.FromYoloString(line);
                        if (annotation != null)
                        {
                            annotations.Add(annotation);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"加载标注文件失败: {ex.Message}", ex);
            }

            return annotations;
        }

        public void SaveAnnotations(string annotationPath, List<Annotation> annotations)
        {
            try
            {
                // 确保目录存在
                var dir = Path.GetDirectoryName(annotationPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var lines = annotations.Select(a => a.ToYoloString()).ToList();
                File.WriteAllLines(annotationPath, lines);
            }
            catch (Exception ex)
            {
                throw new Exception($"保存标注文件失败: {ex.Message}", ex);
            }
        }

        public void CreateEmptyAnnotation(string annotationPath)
        {
            try
            {
                // 确保目录存在
                var dir = Path.GetDirectoryName(annotationPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(annotationPath, string.Empty);
            }
            catch (Exception ex)
            {
                throw new Exception($"创建空标注文件失败: {ex.Message}", ex);
            }
        }

        // 注意：这个方法现在不再处理标签映射，映射应该在调用前就处理好
        public bool MergeAnnotations(List<Annotation> existingAnnotations, List<Annotation> newAnnotations, List<LabelMapping> mappings = null)
        {
            bool hasChanges = false;
            const double overlapThreshold = 0.5; // IOU阈值

            foreach (var newAnnotation in newAnnotations)
            {
                // 如果提供了映射，应用映射（为了兼容性保留，但推荐在调用前处理）
                var annotationToAdd = newAnnotation;
                if (mappings != null && mappings.Count > 0)
                {
                    var mapping = mappings.FirstOrDefault(m => m.ModelClassId == newAnnotation.ClassId);
                    if (mapping != null)
                    {
                        annotationToAdd = newAnnotation.Clone();
                        annotationToAdd.ClassId = mapping.ProjectClassId;
                    }
                    else
                    {
                        // 如果没有找到映射，跳过这个标注
                        continue;
                    }
                }

                // 检查是否与现有标注重叠
                bool isDuplicate = false;
                foreach (var existing in existingAnnotations)
                {
                    if (existing.ClassId == annotationToAdd.ClassId)
                    {
                        double iou = CalculateIOU(existing, annotationToAdd);
                        if (iou > overlapThreshold)
                        {
                            isDuplicate = true;
                            break;
                        }
                    }
                }

                if (!isDuplicate)
                {
                    existingAnnotations.Add(annotationToAdd);
                    hasChanges = true;
                }
            }

            return hasChanges;
        }

        private double CalculateIOU(Annotation a1, Annotation a2)
        {
            // 计算两个边界框的IOU（Intersection over Union）
            double x1_min = a1.X - a1.Width / 2;
            double y1_min = a1.Y - a1.Height / 2;
            double x1_max = a1.X + a1.Width / 2;
            double y1_max = a1.Y + a1.Height / 2;

            double x2_min = a2.X - a2.Width / 2;
            double y2_min = a2.Y - a2.Height / 2;
            double x2_max = a2.X + a2.Width / 2;
            double y2_max = a2.Y + a2.Height / 2;

            // 计算交集
            double intersect_x_min = Math.Max(x1_min, x2_min);
            double intersect_y_min = Math.Max(y1_min, y2_min);
            double intersect_x_max = Math.Min(x1_max, x2_max);
            double intersect_y_max = Math.Min(y1_max, y2_max);

            if (intersect_x_max < intersect_x_min || intersect_y_max < intersect_y_min)
                return 0;

            double intersect_area = (intersect_x_max - intersect_x_min) * (intersect_y_max - intersect_y_min);

            // 计算并集
            double area1 = a1.Width * a1.Height;
            double area2 = a2.Width * a2.Height;
            double union_area = area1 + area2 - intersect_area;

            return intersect_area / union_area;
        }
    }
}
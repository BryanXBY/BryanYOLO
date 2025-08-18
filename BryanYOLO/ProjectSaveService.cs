using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BryanYOLO.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BryanYOLO.Services
{
    public class ProjectSaveService
    {
        private readonly AnnotationService _annotationService;

        public ProjectSaveService()
        {
            _annotationService = new AnnotationService();
        }

        public async Task<bool> SavePartialProject(
            YoloProject currentProject,
            List<ImageItem> selectedImages,
            string outputYamlPath,
            string projectName,
            bool copyImages,
            bool generateTrainVal,
            Action<double> progressCallback,
            Action<string> statusCallback)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var outputDir = Path.GetDirectoryName(outputYamlPath);
                    var projectDir = Path.Combine(outputDir, projectName);

                    statusCallback?.Invoke("创建项目目录结构...");

                    // 创建项目目录结构
                    if (generateTrainVal)
                    {
                        // 创建训练集和验证集目录
                        CreateTrainValStructure(projectDir);
                    }
                    else
                    {
                        // 创建简单的images和labels目录
                        CreateSimpleStructure(projectDir);
                    }

                    statusCallback?.Invoke($"处理 {selectedImages.Count} 张图片...");

                    // 处理图片
                    if (generateTrainVal)
                    {
                        ProcessImagesTrainVal(selectedImages, projectDir, copyImages, progressCallback, statusCallback);
                    }
                    else
                    {
                        ProcessImagesSimple(selectedImages, projectDir, copyImages, progressCallback, statusCallback);
                    }

                    statusCallback?.Invoke("生成YAML配置文件...");

                    // 生成YAML配置
                    GenerateYamlConfig(outputYamlPath, projectName, currentProject.Classes.ToList(), generateTrainVal);

                    // 生成项目统计报告
                    GenerateProjectReport(projectDir, selectedImages, currentProject.Classes.ToList());

                    statusCallback?.Invoke($"项目保存成功！共 {selectedImages.Count} 张图片");
                    return true;
                }
                catch (Exception ex)
                {
                    statusCallback?.Invoke($"保存失败: {ex.Message}");
                    return false;
                }
            });
        }

        private void CreateTrainValStructure(string projectDir)
        {
            Directory.CreateDirectory(Path.Combine(projectDir, "train", "images"));
            Directory.CreateDirectory(Path.Combine(projectDir, "train", "labels"));
            Directory.CreateDirectory(Path.Combine(projectDir, "val", "images"));
            Directory.CreateDirectory(Path.Combine(projectDir, "val", "labels"));
        }

        private void CreateSimpleStructure(string projectDir)
        {
            Directory.CreateDirectory(Path.Combine(projectDir, "images"));
            Directory.CreateDirectory(Path.Combine(projectDir, "labels"));
        }

        private void ProcessImagesTrainVal(
            List<ImageItem> images,
            string projectDir,
            bool copyImages,
            Action<double> progressCallback,
            Action<string> statusCallback)
        {
            // 分割数据集 (80% 训练, 20% 验证)
            var random = new Random(42);
            var shuffled = images.OrderBy(x => random.Next()).ToList();
            int trainCount = (int)(shuffled.Count * 0.8);

            var trainImages = shuffled.Take(trainCount).ToList();
            var valImages = shuffled.Skip(trainCount).ToList();

            int processed = 0;
            int total = images.Count;

            // 处理训练集
            statusCallback?.Invoke($"处理训练集 ({trainImages.Count} 张)...");
            foreach (var image in trainImages)
            {
                ProcessSingleImage(
                    image,
                    Path.Combine(projectDir, "train", "images"),
                    Path.Combine(projectDir, "train", "labels"),
                    copyImages);

                processed++;
                progressCallback?.Invoke((double)processed / total);
            }

            // 处理验证集
            statusCallback?.Invoke($"处理验证集 ({valImages.Count} 张)...");
            foreach (var image in valImages)
            {
                ProcessSingleImage(
                    image,
                    Path.Combine(projectDir, "val", "images"),
                    Path.Combine(projectDir, "val", "labels"),
                    copyImages);

                processed++;
                progressCallback?.Invoke((double)processed / total);
            }
        }

        private void ProcessImagesSimple(
            List<ImageItem> images,
            string projectDir,
            bool copyImages,
            Action<double> progressCallback,
            Action<string> statusCallback)
        {
            int processed = 0;
            int total = images.Count;

            var imagesDir = Path.Combine(projectDir, "images");
            var labelsDir = Path.Combine(projectDir, "labels");

            foreach (var image in images)
            {
                ProcessSingleImage(image, imagesDir, labelsDir, copyImages);

                processed++;
                progressCallback?.Invoke((double)processed / total);

                if (processed % 10 == 0)
                {
                    statusCallback?.Invoke($"已处理 {processed}/{total} 张图片...");
                }
            }
        }

        private void ProcessSingleImage(
            ImageItem image,
            string imagesDir,
            string labelsDir,
            bool copyImages)
        {
            var imageName = Path.GetFileName(image.ImagePath);
            var labelName = Path.GetFileNameWithoutExtension(imageName) + ".txt";

            var destImagePath = Path.Combine(imagesDir, imageName);
            var destLabelPath = Path.Combine(labelsDir, labelName);

            // 处理图片
            if (copyImages)
            {
                // 复制图片文件
                if (File.Exists(image.ImagePath))
                {
                    File.Copy(image.ImagePath, destImagePath, true);
                }
            }
            else
            {
                // 创建符号链接或记录原始路径
                // 这里简单地创建一个引用文件
                var refPath = destImagePath + ".ref";
                File.WriteAllText(refPath, image.ImagePath);
            }

            // 处理标注文件
            if (File.Exists(image.AnnotationPath))
            {
                File.Copy(image.AnnotationPath, destLabelPath, true);
            }
            else if (image.Annotations != null && image.Annotations.Count > 0)
            {
                // 如果标注在内存中但文件不存在，创建标注文件
                var lines = image.Annotations.Select(a => a.ToYoloString());
                File.WriteAllLines(destLabelPath, lines);
            }
            else if (image.Status == AnnotationStatus.EmptyAnnotation)
            {
                // 创建空标注文件
                File.WriteAllText(destLabelPath, string.Empty);
            }
        }

        private void GenerateYamlConfig(
            string yamlPath,
            string projectName,
            List<string> classes,
            bool hasTrainVal)
        {
            var config = new Dictionary<string, object>
            {
                ["path"] = Path.GetDirectoryName(yamlPath)
            };

            if (hasTrainVal)
            {
                config["train"] = Path.Combine(projectName, "train", "images");
                config["val"] = Path.Combine(projectName, "val", "images");
            }
            else
            {
                config["train"] = Path.Combine(projectName, "images");
                config["val"] = Path.Combine(projectName, "images");
            }

            config["nc"] = classes.Count;
            config["names"] = classes.Select((name, index) => new { index, name })
                               .ToDictionary(x => x.index, x => x.name);

            var serializer = new SerializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            var yaml = serializer.Serialize(config);

            // 修正YAML格式
            yaml = yaml.Replace("nc:", "\nnc:");
            yaml = yaml.Replace("names:", "\nnames:");

            File.WriteAllText(yamlPath, yaml);
        }

        private void GenerateProjectReport(
            string projectDir,
            List<ImageItem> images,
            List<string> classes)
        {
            var reportPath = Path.Combine(projectDir, "project_info.txt");

            var report = new List<string>
            {
                "=" .PadRight(60, '='),
                "项目保存报告",
                "=" .PadRight(60, '='),
                $"保存时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                $"图片总数: {images.Count}",
                "",
                "状态统计:",
                $"  已标记: {images.Count(img => img.Status == AnnotationStatus.Annotated)} 张",
                $"  空标记: {images.Count(img => img.Status == AnnotationStatus.EmptyAnnotation)} 张",
                $"  未标记: {images.Count(img => img.Status == AnnotationStatus.NotAnnotated)} 张",
                "",
                $"类别总数: {classes.Count}",
                "类别列表:"
            };

            for (int i = 0; i < classes.Count; i++)
            {
                // 统计每个类别的标注数量
                int classCount = 0;
                foreach (var image in images)
                {
                    if (image.Annotations != null)
                    {
                        classCount += image.Annotations.Count(a => a.ClassId == i);
                    }
                }
                report.Add($"  {i}: {classes[i]} ({classCount} 个标注)");
            }

            report.Add("");
            report.Add("图片列表:");
            foreach (var image in images.Take(20))
            {
                var status = image.Status switch
                {
                    AnnotationStatus.Annotated => "✅",
                    AnnotationStatus.EmptyAnnotation => "⭕",
                    AnnotationStatus.NotAnnotated => "❌",
                    _ => "?"
                };
                report.Add($"  {status} {image.FileName}");
            }

            if (images.Count > 20)
            {
                report.Add($"  ... 还有 {images.Count - 20} 张图片");
            }

            File.WriteAllLines(reportPath, report);
        }

        public async Task<bool> ExportForTraining(
            YoloProject project,
            List<ImageItem> selectedImages,
            string outputPath,
            Action<double> progressCallback,
            Action<string> statusCallback)
        {
            return await Task.Run(() =>
            {
                try
                {
                    statusCallback?.Invoke("准备导出数据...");

                    // 创建临时目录
                    var tempDir = Path.Combine(Path.GetTempPath(), $"yolo_export_{Guid.NewGuid()}");
                    Directory.CreateDirectory(tempDir);

                    // 分类处理图片
                    var annotatedImages = selectedImages.Where(img => img.Status == AnnotationStatus.Annotated).ToList();
                    var emptyImages = selectedImages.Where(img => img.Status == AnnotationStatus.EmptyAnnotation).ToList();

                    statusCallback?.Invoke($"处理 {annotatedImages.Count} 张已标记图片，{emptyImages.Count} 张负样本...");

                    // 处理并导出
                    int processed = 0;
                    int total = selectedImages.Count;

                    foreach (var image in selectedImages)
                    {
                        // 复制图片和标注
                        var imageName = Path.GetFileName(image.ImagePath);
                        var destImagePath = Path.Combine(tempDir, "images", imageName);
                        var destLabelPath = Path.Combine(tempDir, "labels",
                            Path.GetFileNameWithoutExtension(imageName) + ".txt");

                        Directory.CreateDirectory(Path.GetDirectoryName(destImagePath));
                        Directory.CreateDirectory(Path.GetDirectoryName(destLabelPath));

                        File.Copy(image.ImagePath, destImagePath, true);

                        if (File.Exists(image.AnnotationPath))
                        {
                            File.Copy(image.AnnotationPath, destLabelPath, true);
                        }
                        else if (image.Status == AnnotationStatus.EmptyAnnotation)
                        {
                            File.WriteAllText(destLabelPath, string.Empty);
                        }

                        processed++;
                        progressCallback?.Invoke((double)processed / total);
                    }

                    // 创建压缩包
                    statusCallback?.Invoke("创建压缩包...");
                    System.IO.Compression.ZipFile.CreateFromDirectory(tempDir, outputPath);

                    // 清理临时目录
                    Directory.Delete(tempDir, true);

                    statusCallback?.Invoke("导出完成！");
                    return true;
                }
                catch (Exception ex)
                {
                    statusCallback?.Invoke($"导出失败: {ex.Message}");
                    return false;
                }
            });
        }
    }
}
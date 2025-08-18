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
    public class YoloExportService
    {
        public async Task ExportToYoloFormat(YoloProject project, string outputYamlPath)
        {
            await Task.Run(() =>
            {
                var outputDir = Path.GetDirectoryName(outputYamlPath);
                var datasetName = Path.GetFileNameWithoutExtension(outputYamlPath);

                // 创建目录结构
                var trainImagesDir = Path.Combine(outputDir, datasetName, "train", "images");
                var trainLabelsDir = Path.Combine(outputDir, datasetName, "train", "labels");
                var valImagesDir = Path.Combine(outputDir, datasetName, "val", "images");
                var valLabelsDir = Path.Combine(outputDir, datasetName, "val", "labels");

                Directory.CreateDirectory(trainImagesDir);
                Directory.CreateDirectory(trainLabelsDir);
                Directory.CreateDirectory(valImagesDir);
                Directory.CreateDirectory(valLabelsDir);

                // 分割数据集 (80% 训练, 20% 验证)
                var annotatedImages = project.Images
                    .Where(img => img.Status == AnnotationStatus.Annotated)
                    .ToList();

                var random = new Random(42);
                var shuffled = annotatedImages.OrderBy(x => random.Next()).ToList();

                int trainCount = (int)(shuffled.Count * 0.8);
                var trainImages = shuffled.Take(trainCount).ToList();
                var valImages = shuffled.Skip(trainCount).ToList();

                // 复制训练集文件
                foreach (var image in trainImages)
                {
                    CopyImageAndLabel(image, trainImagesDir, trainLabelsDir);
                }

                // 复制验证集文件
                foreach (var image in valImages)
                {
                    CopyImageAndLabel(image, valImagesDir, valLabelsDir);
                }

                // 创建YAML配置文件
                CreateYamlConfig(outputYamlPath, datasetName, project.Classes.ToList());
            });
        }

        private void CopyImageAndLabel(ImageItem image, string imagesDir, string labelsDir)
        {
            var imageName = Path.GetFileName(image.ImagePath);
            var labelName = Path.GetFileNameWithoutExtension(imageName) + ".txt";

            var destImagePath = Path.Combine(imagesDir, imageName);
            var destLabelPath = Path.Combine(labelsDir, labelName);

            // 复制图片
            File.Copy(image.ImagePath, destImagePath, true);

            // 复制或创建标注文件
            if (File.Exists(image.AnnotationPath))
            {
                File.Copy(image.AnnotationPath, destLabelPath, true);
            }
            else if (image.Annotations.Count > 0)
            {
                // 如果标注在内存中但文件不存在，创建标注文件
                var lines = image.Annotations.Select(a => a.ToYoloString());
                File.WriteAllLines(destLabelPath, lines);
            }
        }

        private void CreateYamlConfig(string yamlPath, string datasetName, List<string> classes)
        {
            var config = new Dictionary<string, object>
            {
                ["path"] = Path.GetDirectoryName(yamlPath),
                ["train"] = Path.Combine(datasetName, "train", "images"),
                ["val"] = Path.Combine(datasetName, "val", "images"),
                ["nc"] = classes.Count,
                ["names"] = classes.Select((name, index) => new { index, name })
                                   .ToDictionary(x => x.index, x => x.name)
            };

            var serializer = new SerializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            var yaml = serializer.Serialize(config);

            // 修正YAML格式
            yaml = yaml.Replace("nc:", "\nnc:");
            yaml = yaml.Replace("names:", "\nnames:");

            File.WriteAllText(yamlPath, yaml);
        }
    }
}
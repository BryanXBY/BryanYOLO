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
    public class ImageLoadingService
    {
        private readonly string[] _supportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };

        public async Task<List<ImageItem>> LoadImagesFromFolder(string folderPath)
        {
            return await Task.Run(() =>
            {
                var images = new List<ImageItem>();

                if (!Directory.Exists(folderPath))
                    throw new DirectoryNotFoundException($"文件夹不存在: {folderPath}");

                var files = Directory.GetFiles(folderPath)
                    .Where(f => _supportedExtensions.Contains(Path.GetExtension(f).ToLower()))
                    .OrderBy(f => f)
                    .ToList();

                foreach (var file in files)
                {
                    var imageItem = new ImageItem(file);
                    images.Add(imageItem);
                }

                return images;
            });
        }

        public async Task<YoloProject> LoadYoloProject(string yamlPath)
        {
            return await Task.Run(() =>
            {
                if (!File.Exists(yamlPath))
                    throw new FileNotFoundException($"YAML文件不存在: {yamlPath}");

                var project = new YoloProject();
                project.ProjectPath = yamlPath;

                // 解析YAML文件
                var yamlContent = File.ReadAllText(yamlPath);
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .Build();

                var yamlData = deserializer.Deserialize<Dictionary<string, object>>(yamlContent);

                // 获取路径信息
                var basePath = Path.GetDirectoryName(yamlPath);

                if (yamlData.ContainsKey("path"))
                {
                    var pathValue = yamlData["path"].ToString();
                    if (!Path.IsPathRooted(pathValue))
                    {
                        basePath = Path.GetFullPath(Path.Combine(basePath, pathValue));
                    }
                    else
                    {
                        basePath = pathValue;
                    }
                }

                // 加载训练集、验证集、测试集路径
                if (yamlData.ContainsKey("train"))
                {
                    project.TrainPath = GetAbsolutePath(basePath, yamlData["train"].ToString());
                    LoadImagesFromPath(project, project.TrainPath, "train");
                }

                if (yamlData.ContainsKey("val"))
                {
                    project.ValPath = GetAbsolutePath(basePath, yamlData["val"].ToString());
                    LoadImagesFromPath(project, project.ValPath, "val");
                }

                if (yamlData.ContainsKey("test") && yamlData["test"] != null)
                {
                    project.TestPath = GetAbsolutePath(basePath, yamlData["test"].ToString());
                    LoadImagesFromPath(project, project.TestPath, "test");
                }

                // 加载类别名称
                if (yamlData.ContainsKey("names"))
                {
                    var names = yamlData["names"] as Dictionary<object, object>;
                    if (names != null)
                    {
                        var sortedNames = names.OrderBy(kvp => Convert.ToInt32(kvp.Key));
                        foreach (var kvp in sortedNames)
                        {
                            project.Classes.Add(kvp.Value.ToString());
                        }
                    }
                }

                return project;
            });
        }

        private string GetAbsolutePath(string basePath, string relativePath)
        {
            if (Path.IsPathRooted(relativePath))
                return relativePath;

            return Path.GetFullPath(Path.Combine(basePath, relativePath));
        }

        private void LoadImagesFromPath(YoloProject project, string path, string setType = null)
        {
            if (!Directory.Exists(path))
                return;

            // 确定images和labels文件夹路径
            string imagesPath = path;
            string labelsPath = null;

            // 检查路径结构
            var pathParts = path.Replace('\\', '/').Split('/');

            // 如果路径以 images/train, images/val 等结尾
            if (pathParts.Length >= 2 && pathParts[pathParts.Length - 2] == "images")
            {
                // 标准YOLO结构: images/train -> labels/train
                var parentPath = Path.GetDirectoryName(Path.GetDirectoryName(path));
                var subFolder = Path.GetFileName(path); // train, val, test
                labelsPath = Path.Combine(parentPath, "labels", subFolder);
            }
            // 如果路径直接是 train, val, test 文件夹
            else if (setType != null && Directory.Exists(Path.Combine(path, "images")))
            {
                // 结构可能是 train/images 和 train/labels
                imagesPath = Path.Combine(path, "images");
                labelsPath = Path.Combine(path, "labels");
            }
            // 如果路径就是普通文件夹，可能标注在同一目录
            else
            {
                // 尝试查找同级的labels文件夹
                var parentPath = Path.GetDirectoryName(path);
                if (parentPath != null)
                {
                    var possibleLabelsPath = Path.Combine(parentPath, "labels");
                    if (Directory.Exists(possibleLabelsPath))
                    {
                        labelsPath = possibleLabelsPath;
                    }
                }
            }

            // 如果还没找到labels路径，检查是否labels和images在同一目录
            if (labelsPath == null && Path.GetFileName(path) == "images")
            {
                var parentPath = Path.GetDirectoryName(path);
                labelsPath = Path.Combine(parentPath, "labels");
            }

            // 验证labels路径是否存在
            if (labelsPath != null && !Directory.Exists(labelsPath))
            {
                labelsPath = null; // 如果不存在，设为null，将在图片同目录查找
            }

            // 加载图片文件
            var imageFiles = Directory.GetFiles(imagesPath)
                .Where(f => _supportedExtensions.Contains(Path.GetExtension(f).ToLower()))
                .ToList();

            foreach (var imageFile in imageFiles)
            {
                // 查找对应的标注文件
                var labelPath = GetLabelPath(imageFile, labelsPath);

                // 创建ImageItem，使用特殊构造函数来指定标注路径
                var imageItem = new ImageItem(imageFile, labelPath);

                // 避免重复添加
                if (!project.Images.Any(img => img.ImagePath == imageItem.ImagePath))
                {
                    project.Images.Add(imageItem);
                }
            }
        }

        private string GetLabelPath(string imagePath, string labelsDir)
        {
            var imageNameWithoutExt = Path.GetFileNameWithoutExtension(imagePath);

            // 如果指定了labels目录，优先在那里查找
            if (!string.IsNullOrEmpty(labelsDir) && Directory.Exists(labelsDir))
            {
                var labelPath = Path.Combine(labelsDir, $"{imageNameWithoutExt}.txt");
                if (File.Exists(labelPath))
                    return labelPath;
            }

            // 其次在图片同目录查找
            var imageDir = Path.GetDirectoryName(imagePath);
            var sameDirLabelPath = Path.Combine(imageDir, $"{imageNameWithoutExt}.txt");
            if (File.Exists(sameDirLabelPath))
                return sameDirLabelPath;

            // 如果都没找到，返回期望的路径（即使文件不存在）
            if (!string.IsNullOrEmpty(labelsDir))
            {
                return Path.Combine(labelsDir, $"{imageNameWithoutExt}.txt");
            }

            return sameDirLabelPath;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace BryanYOLO.Models
{
    public enum AnnotationStatus
    {
        NotAnnotated,    // 未标记（没有对应的txt文件）
        EmptyAnnotation, // 空标记（有txt文件但内容为空）
        Annotated        // 已标记（有txt文件且有内容）
    }

    public class ImageItem : INotifyPropertyChanged
    {
        private string _imagePath;
        private string _annotationPath;
        private AnnotationStatus _status;
        private ObservableCollection<Annotation> _annotations;
        private bool _isModified;

        public string ImagePath
        {
            get => _imagePath;
            set { _imagePath = value; OnPropertyChanged(); }
        }

        public string AnnotationPath
        {
            get => _annotationPath;
            set { _annotationPath = value; OnPropertyChanged(); }
        }

        public string FileName => Path.GetFileName(ImagePath);

        public AnnotationStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public ObservableCollection<Annotation> Annotations
        {
            get => _annotations;
            set { _annotations = value; OnPropertyChanged(); }
        }

        public bool IsModified
        {
            get => _isModified;
            set { _isModified = value; OnPropertyChanged(); }
        }

        // 默认构造函数 - 标注文件在图片同目录
        public ImageItem(string imagePath)
        {
            ImagePath = imagePath;
            AnnotationPath = GetDefaultAnnotationPath(imagePath);
            Annotations = new ObservableCollection<Annotation>();
            UpdateStatus();
        }

        // 新增构造函数 - 支持指定标注路径
        public ImageItem(string imagePath, string annotationPath)
        {
            ImagePath = imagePath;

            // 如果提供了标注路径，使用它；否则使用默认路径
            if (!string.IsNullOrEmpty(annotationPath))
            {
                AnnotationPath = annotationPath;
            }
            else
            {
                AnnotationPath = GetDefaultAnnotationPath(imagePath);
            }

            Annotations = new ObservableCollection<Annotation>();
            UpdateStatus();
        }

        private string GetDefaultAnnotationPath(string imagePath)
        {
            var dir = Path.GetDirectoryName(imagePath);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(imagePath);
            return Path.Combine(dir, $"{nameWithoutExt}.txt");
        }

        public void UpdateStatus()
        {
            if (!File.Exists(AnnotationPath))
            {
                Status = AnnotationStatus.NotAnnotated;
            }
            else
            {
                try
                {
                    var content = File.ReadAllText(AnnotationPath).Trim();
                    Status = string.IsNullOrEmpty(content) ? AnnotationStatus.EmptyAnnotation : AnnotationStatus.Annotated;
                }
                catch
                {
                    // 如果无法读取文件，视为未标注
                    Status = AnnotationStatus.NotAnnotated;
                }
            }
        }

        public void LoadAnnotations()
        {
            Annotations.Clear();
            if (File.Exists(AnnotationPath))
            {
                try
                {
                    var lines = File.ReadAllLines(AnnotationPath);
                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            var annotation = Annotation.FromYoloString(line);
                            if (annotation != null)
                            {
                                Annotations.Add(annotation);
                                // 触发DisplayName更新
                                annotation.OnPropertyChanged("DisplayName");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 记录错误但不抛出，允许程序继续运行
                    Console.WriteLine($"加载标注文件失败 {AnnotationPath}: {ex.Message}");
                }
            }
            IsModified = false;
        }

        public void SaveAnnotations()
        {
            try
            {
                // 确保目录存在
                var dir = Path.GetDirectoryName(AnnotationPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var lines = new List<string>();
                foreach (var annotation in Annotations)
                {
                    lines.Add(annotation.ToYoloString());
                }
                File.WriteAllLines(AnnotationPath, lines);
                UpdateStatus();
                IsModified = false;
            }
            catch (Exception ex)
            {
                // 记录错误但允许程序继续
                Console.WriteLine($"保存标注文件失败 {AnnotationPath}: {ex.Message}");
                throw; // 重新抛出以通知用户
            }
        }

        public void ClearAnnotations()
        {
            Annotations.Clear();

            try
            {
                // 确保目录存在
                var dir = Path.GetDirectoryName(AnnotationPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // 创建空的txt文件
                File.WriteAllText(AnnotationPath, string.Empty);
                Status = AnnotationStatus.EmptyAnnotation;
                IsModified = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"创建空标注文件失败 {AnnotationPath}: {ex.Message}");
                throw;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BryanYOLO.Models
{
    public class YoloProject : INotifyPropertyChanged
    {
        private string _projectPath;
        private string _trainPath;
        private string _valPath;
        private string _testPath;
        private ObservableCollection<string> _classes;
        private ObservableCollection<ImageItem> _images;

        public string ProjectPath
        {
            get => _projectPath;
            set { _projectPath = value; OnPropertyChanged(); }
        }

        public string TrainPath
        {
            get => _trainPath;
            set { _trainPath = value; OnPropertyChanged(); }
        }

        public string ValPath
        {
            get => _valPath;
            set { _valPath = value; OnPropertyChanged(); }
        }

        public string TestPath
        {
            get => _testPath;
            set { _testPath = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> Classes
        {
            get => _classes;
            set { _classes = value; OnPropertyChanged(); }
        }

        public ObservableCollection<ImageItem> Images
        {
            get => _images;
            set { _images = value; OnPropertyChanged(); }
        }

        public Dictionary<int, System.Windows.Media.Color> ClassColors { get; set; }

        // 预定义的颜色列表，用于确保一致性
        private static readonly System.Windows.Media.Color[] PredefinedColors = new[]
        {
            System.Windows.Media.Color.FromRgb(255, 50, 50),    // 红色
            System.Windows.Media.Color.FromRgb(50, 255, 50),    // 绿色
            System.Windows.Media.Color.FromRgb(50, 50, 255),    // 蓝色
            System.Windows.Media.Color.FromRgb(255, 255, 50),   // 黄色
            System.Windows.Media.Color.FromRgb(255, 50, 255),   // 洋红
            System.Windows.Media.Color.FromRgb(50, 255, 255),   // 青色
            System.Windows.Media.Color.FromRgb(255, 150, 50),   // 橙色
            System.Windows.Media.Color.FromRgb(150, 50, 255),   // 紫色
            System.Windows.Media.Color.FromRgb(50, 255, 150),   // 薄荷绿
            System.Windows.Media.Color.FromRgb(255, 50, 150),   // 粉红
            System.Windows.Media.Color.FromRgb(150, 255, 50),   // 黄绿
            System.Windows.Media.Color.FromRgb(50, 150, 255),   // 天蓝
            System.Windows.Media.Color.FromRgb(255, 200, 100),  // 浅橙
            System.Windows.Media.Color.FromRgb(200, 100, 255),  // 淡紫
            System.Windows.Media.Color.FromRgb(100, 255, 200),  // 浅绿
            System.Windows.Media.Color.FromRgb(255, 100, 200),  // 浅粉
            System.Windows.Media.Color.FromRgb(200, 255, 100),  // 浅黄绿
            System.Windows.Media.Color.FromRgb(100, 200, 255),  // 浅蓝
            System.Windows.Media.Color.FromRgb(255, 150, 150),  // 浅红
            System.Windows.Media.Color.FromRgb(150, 255, 150),  // 浅绿
        };

        public YoloProject()
        {
            Classes = new ObservableCollection<string>();
            Images = new ObservableCollection<ImageItem>();
            ClassColors = new Dictionary<int, System.Windows.Media.Color>();
        }

        public void GenerateClassColors()
        {
            ClassColors.Clear();

            for (int i = 0; i < Classes.Count; i++)
            {
                if (i < PredefinedColors.Length)
                {
                    // 使用预定义颜色
                    ClassColors[i] = PredefinedColors[i];
                }
                else
                {
                    // 如果类别数超过预定义颜色，使用算法生成
                    var hue = (i * 360.0 / Classes.Count) % 360;
                    ClassColors[i] = HsvToRgb(hue, 0.8, 0.9);
                }
            }
        }

        private System.Windows.Media.Color HsvToRgb(double hue, double saturation, double value)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            double f = hue / 60 - Math.Floor(hue / 60);

            value = value * 255;
            int v = Convert.ToInt32(value);
            int p = Convert.ToInt32(value * (1 - saturation));
            int q = Convert.ToInt32(value * (1 - f * saturation));
            int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

            switch (hi)
            {
                case 0:
                    return System.Windows.Media.Color.FromRgb((byte)v, (byte)t, (byte)p);
                case 1:
                    return System.Windows.Media.Color.FromRgb((byte)q, (byte)v, (byte)p);
                case 2:
                    return System.Windows.Media.Color.FromRgb((byte)p, (byte)v, (byte)t);
                case 3:
                    return System.Windows.Media.Color.FromRgb((byte)p, (byte)q, (byte)v);
                case 4:
                    return System.Windows.Media.Color.FromRgb((byte)t, (byte)p, (byte)v);
                default:
                    return System.Windows.Media.Color.FromRgb((byte)v, (byte)p, (byte)q);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class LabelMapping
    {
        public int ModelClassId { get; set; }
        public int ProjectClassId { get; set; }
        public string MappingText { get; set; }

        public LabelMapping(int modelId, int projectId)
        {
            ModelClassId = modelId;
            ProjectClassId = projectId;
            MappingText = $"{modelId}:{projectId}";
        }

        public static List<LabelMapping> ParseMappings(string mappingText)
        {
            var mappings = new List<LabelMapping>();
            if (string.IsNullOrWhiteSpace(mappingText)) return mappings;

            var parts = mappingText.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var mapping = part.Split(':');
                if (mapping.Length == 2 &&
                    int.TryParse(mapping[0].Trim(), out int modelId) &&
                    int.TryParse(mapping[1].Trim(), out int projectId))
                {
                    mappings.Add(new LabelMapping(modelId, projectId));
                }
            }
            return mappings;
        }
    }
}
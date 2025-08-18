using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BryanYOLO.Models
{
    public class Annotation : INotifyPropertyChanged
    {
        private int _classId;
        private double _x;
        private double _y;
        private double _width;
        private double _height;
        private bool _isSelected;

        public int ClassId
        {
            get => _classId;
            set { _classId = value; OnPropertyChanged(); }
        }

        // YOLO格式的中心点X坐标（归一化）
        public double X
        {
            get => _x;
            set { _x = value; OnPropertyChanged(); }
        }

        // YOLO格式的中心点Y坐标（归一化）
        public double Y
        {
            get => _y;
            set { _y = value; OnPropertyChanged(); }
        }

        // YOLO格式的宽度（归一化）
        public double Width
        {
            get => _width;
            set { _width = value; OnPropertyChanged(); }
        }

        // YOLO格式的高度（归一化）
        public double Height
        {
            get => _height;
            set { _height = value; OnPropertyChanged(); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        // 显示名称（用于界面显示）
        public string DisplayName
        {
            get
            {
                // 尝试从项目中获取类别名称
                var app = System.Windows.Application.Current;
                if (app?.MainWindow?.DataContext is BryanYOLO.ViewModels.MainViewModel viewModel)
                {
                    if (viewModel.CurrentProject?.Classes != null &&
                        ClassId >= 0 &&
                        ClassId < viewModel.CurrentProject.Classes.Count)
                    {
                        return viewModel.CurrentProject.Classes[ClassId];
                    }
                }
                return $"类别 {ClassId}";
            }
        }

        // 用于显示的像素坐标
        public double PixelLeft { get; set; }
        public double PixelTop { get; set; }
        public double PixelWidth { get; set; }
        public double PixelHeight { get; set; }

        public Annotation()
        {
        }

        public Annotation(int classId, double x, double y, double width, double height)
        {
            ClassId = classId;
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public void UpdateFromPixels(double left, double top, double width, double height, double imageWidth, double imageHeight)
        {
            // 转换像素坐标到YOLO格式（归一化坐标）
            X = (left + width / 2) / imageWidth;
            Y = (top + height / 2) / imageHeight;
            Width = width / imageWidth;
            Height = height / imageHeight;

            // 保存像素坐标
            PixelLeft = left;
            PixelTop = top;
            PixelWidth = width;
            PixelHeight = height;
        }

        public void UpdatePixelCoordinates(double imageWidth, double imageHeight)
        {
            // 从YOLO格式转换到像素坐标
            PixelWidth = Width * imageWidth;
            PixelHeight = Height * imageHeight;
            PixelLeft = (X * imageWidth) - (PixelWidth / 2);
            PixelTop = (Y * imageHeight) - (PixelHeight / 2);
        }

        public string ToYoloString()
        {
            return $"{ClassId} {X:F6} {Y:F6} {Width:F6} {Height:F6}";
        }

        public static Annotation FromYoloString(string line)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 5) return null;

            if (int.TryParse(parts[0], out int classId) &&
                double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double x) &&
                double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double y) &&
                double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double width) &&
                double.TryParse(parts[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double height))
            {
                return new Annotation(classId, x, y, width, height);
            }
            return null;
        }

        public Annotation Clone()
        {
            return new Annotation
            {
                ClassId = this.ClassId,
                X = this.X,
                Y = this.Y,
                Width = this.Width,
                Height = this.Height,
                PixelLeft = this.PixelLeft,
                PixelTop = this.PixelTop,
                PixelWidth = this.PixelWidth,
                PixelHeight = this.PixelHeight
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
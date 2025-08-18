using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using BryanYOLO.Services;

namespace BryanYOLO.Dialogs
{
    public partial class ModelPreviewDialog : Window
    {
        private readonly IconDetectionService _detectionService;
        private string _imagePath;
        private string _modelPath;
        private int _inputSize;
        private float _confidenceThreshold;
        private int _targetClassId;
        private int _modelClassCount;
        private List<IconDetectionService.IconDetectionResult> _allDetections;
        private ObservableCollection<IconDetectionService.IconDetectionResult> _displayedDetections;

        // 预定义的颜色列表
        private static readonly Color[] ClassColors = new[]
        {
            Colors.Blue,
            Colors.Green,
            Colors.Yellow,
            Colors.Magenta,
            Colors.Cyan,
            Colors.Orange,
            Colors.Purple,
            Colors.Pink,
            Colors.Lime,
            Colors.Brown,
            Colors.Teal,
            Colors.Indigo,
            Colors.Olive,
            Colors.Navy,
            Colors.Maroon,
            Colors.Aqua,
            Colors.Silver,
            Colors.Gold,
            Colors.Coral,
            Colors.Salmon
        };

        public ModelPreviewDialog(
            string imagePath,
            string modelPath,
            int inputSize,
            float confidenceThreshold,
            int targetClassId,
            int modelClassCount = -1)
        {
            InitializeComponent();

            _detectionService = new IconDetectionService();
            _imagePath = imagePath;
            _modelPath = modelPath;
            _inputSize = inputSize;
            _confidenceThreshold = confidenceThreshold;
            _targetClassId = targetClassId;
            _modelClassCount = modelClassCount;
            _displayedDetections = new ObservableCollection<IconDetectionService.IconDetectionResult>();

            DetectionListBox.ItemsSource = _displayedDetections;
            FilterClassIdTextBox.Text = targetClassId.ToString();
            ThresholdText.Text = $"置信度阈值: {confidenceThreshold:F2}";

            Loaded += OnWindowLoaded;
        }

        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            await RunDetection();
        }

        private async System.Threading.Tasks.Task RunDetection()
        {
            try
            {
                LoadingPanel.Visibility = Visibility.Visible;
                RefreshButton.IsEnabled = false;

                // 加载并显示图片
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(_imagePath);
                bitmap.EndInit();
                bitmap.Freeze();

                PreviewImage.Source = bitmap;
                DetectionCanvas.Width = bitmap.PixelWidth;
                DetectionCanvas.Height = bitmap.PixelHeight;

                // 运行检测，传入模型类别数
                await System.Threading.Tasks.Task.Run(() =>
                {
                    _allDetections = _detectionService.GetAllDetections(
                        _imagePath,
                        _modelPath,
                        _inputSize,
                        _confidenceThreshold,
                        _modelClassCount);
                });

                // 显示结果
                DrawDetections();
                UpdateStatistics();
                FilterDetections();

                if (_allDetections.Count == 0)
                {
                    InfoText.Text = "未检测到任何目标。请检查模型、置信度阈值和类别数设置。";
                    InfoText.Foreground = Brushes.Red;
                }
                else
                {
                    var targetCount = _allDetections.Count(d => d.ClassId == _targetClassId);
                    if (targetCount == 0)
                    {
                        var detectedClasses = _allDetections.Select(d => d.ClassId).Distinct().OrderBy(id => id);
                        InfoText.Text = $"检测到 {_allDetections.Count} 个目标，但没有类别ID为 {_targetClassId} 的目标。\n" +
                                       $"检测到的类别: {string.Join(", ", detectedClasses)}\n" +
                                       $"模型类别数: {(_modelClassCount > 0 ? _modelClassCount.ToString() : "自动检测")}";
                        InfoText.Foreground = Brushes.DarkOrange;
                    }
                    else
                    {
                        InfoText.Text = $"成功检测到 {targetCount} 个目标类别（ID: {_targetClassId}），" +
                                       $"总共 {_allDetections.Count} 个检测结果。红色框表示目标类别。\n" +
                                       $"模型类别数: {(_modelClassCount > 0 ? _modelClassCount.ToString() : "自动检测")}";
                        InfoText.Foreground = Brushes.DarkGreen;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"检测失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                InfoText.Text = $"错误: {ex.Message}";
                InfoText.Foreground = Brushes.Red;
            }
            finally
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
                RefreshButton.IsEnabled = true;
            }
        }

        private void DrawDetections()
        {
            DetectionCanvas.Children.Clear();

            if (_allDetections == null || _allDetections.Count == 0)
                return;

            // 按类别分组并绘制
            var groupedByClass = _allDetections.GroupBy(d => d.ClassId).OrderBy(g => g.Key);

            foreach (var group in groupedByClass)
            {
                var classId = group.Key;
                var color = GetColorForClass(classId);
                var isTarget = classId == _targetClassId;

                foreach (var detection in group)
                {
                    // 绘制边界框
                    var rect = new Rectangle
                    {
                        Width = detection.Bounds.Width,
                        Height = detection.Bounds.Height,
                        Stroke = new SolidColorBrush(isTarget ? Colors.Red : color),
                        StrokeThickness = isTarget ? 3 : 2,
                        Fill = new SolidColorBrush(isTarget ? Colors.Red : color) { Opacity = 0.1 }
                    };

                    if (isTarget)
                    {
                        rect.StrokeDashArray = new DoubleCollection { 5, 2 };
                    }

                    Canvas.SetLeft(rect, detection.Bounds.Left);
                    Canvas.SetTop(rect, detection.Bounds.Top);
                    DetectionCanvas.Children.Add(rect);

                    // 绘制标签
                    var labelBg = new Border
                    {
                        Background = new SolidColorBrush(isTarget ? Colors.Red : color),
                        CornerRadius = new CornerRadius(2),
                        Padding = new Thickness(3, 1, 3, 1)
                    };

                    var label = new TextBlock
                    {
                        Text = $"ID:{classId} ({detection.Confidence:F2})",
                        Foreground = Brushes.White,
                        FontSize = 11,
                        FontWeight = FontWeights.Bold
                    };

                    labelBg.Child = label;
                    Canvas.SetLeft(labelBg, detection.Bounds.Left);
                    Canvas.SetTop(labelBg, detection.Bounds.Top - 18);
                    DetectionCanvas.Children.Add(labelBg);

                    // 绘制中心点
                    var centerPoint = new Ellipse
                    {
                        Width = 6,
                        Height = 6,
                        Fill = new SolidColorBrush(isTarget ? Colors.Red : color),
                        Stroke = Brushes.White,
                        StrokeThickness = 1
                    };

                    Canvas.SetLeft(centerPoint, detection.Center.X - 3);
                    Canvas.SetTop(centerPoint, detection.Center.Y - 3);
                    DetectionCanvas.Children.Add(centerPoint);
                }
            }
        }

        private Color GetColorForClass(int classId)
        {
            if (classId >= 0 && classId < ClassColors.Length)
            {
                return ClassColors[classId];
            }

            // 如果超出预定义颜色，生成一个基于类别ID的颜色
            var hue = (classId * 137.5) % 360; // 使用黄金角度分布
            return HsvToRgb(hue, 0.8, 0.9);
        }

        private Color HsvToRgb(double hue, double saturation, double value)
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
                    return Color.FromRgb((byte)v, (byte)t, (byte)p);
                case 1:
                    return Color.FromRgb((byte)q, (byte)v, (byte)p);
                case 2:
                    return Color.FromRgb((byte)p, (byte)v, (byte)t);
                case 3:
                    return Color.FromRgb((byte)p, (byte)q, (byte)v);
                case 4:
                    return Color.FromRgb((byte)t, (byte)p, (byte)v);
                default:
                    return Color.FromRgb((byte)v, (byte)p, (byte)q);
            }
        }

        private void UpdateStatistics()
        {
            if (_allDetections == null)
            {
                TotalDetectionsText.Text = "总检测数: 0";
                FilteredDetectionsText.Text = "目标类别数: 0";
                return;
            }

            TotalDetectionsText.Text = $"总检测数: {_allDetections.Count}";

            var targetCount = _allDetections.Count(d => d.ClassId == _targetClassId);
            FilteredDetectionsText.Text = $"目标类别数: {targetCount}";

            if (targetCount > 0)
            {
                FilteredDetectionsText.Foreground = Brushes.Green;
            }
            else
            {
                FilteredDetectionsText.Foreground = Brushes.Red;
            }
        }

        private void FilterDetections()
        {
            _displayedDetections.Clear();

            if (_allDetections == null)
                return;

            IEnumerable<IconDetectionService.IconDetectionResult> filtered = _allDetections;

            // 根据输入的类别ID进行筛选
            if (!string.IsNullOrWhiteSpace(FilterClassIdTextBox.Text))
            {
                if (int.TryParse(FilterClassIdTextBox.Text, out int filterClassId))
                {
                    filtered = filtered.Where(d => d.ClassId == filterClassId);
                }
            }

            // 按置信度降序排序
            filtered = filtered.OrderByDescending(d => d.Confidence);

            foreach (var detection in filtered)
            {
                _displayedDetections.Add(detection);
            }
        }

        private void OnFilterClick(object sender, RoutedEventArgs e)
        {
            FilterDetections();
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await RunDetection();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _detectionService?.Dispose();
        }
    }
}
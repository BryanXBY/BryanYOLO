using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BryanYOLO.Models;

namespace BryanYOLO.Dialogs
{
    public partial class SaveProjectDialog : Window
    {
        public List<ImageItem> SelectedImages { get; private set; }
        public string ProjectName { get; private set; }
        public bool IncludeEmptyAnnotations { get; private set; }
        private List<ImageItem> _allImages;

        public SaveProjectDialog(List<ImageItem> images, ImageItem currentImage)
        {
            InitializeComponent();
            _allImages = images ?? new List<ImageItem>();
            SelectedImages = new List<ImageItem>();

            // 初始化UI
            TotalImagesText.Text = $"总共 {_allImages.Count} 张图片";

            // 设置当前图片索引
            if (currentImage != null && _allImages.Count > 0)
            {
                int currentIndex = _allImages.IndexOf(currentImage);
                if (currentIndex >= 0)
                {
                    FromIndexTextBox.Text = "1";
                    ToIndexTextBox.Text = (currentIndex + 1).ToString();
                    CurrentIndexText.Text = $"当前图片索引: {currentIndex + 1}";
                }
                else
                {
                    FromIndexTextBox.Text = "1";
                    ToIndexTextBox.Text = _allImages.Count.ToString();
                    CurrentIndexText.Text = "当前图片索引: 未知";
                }
            }
            else if (_allImages.Count > 0)
            {
                FromIndexTextBox.Text = "1";
                ToIndexTextBox.Text = _allImages.Count.ToString();
                CurrentIndexText.Text = "当前图片索引: 未知";
            }
            else
            {
                FromIndexTextBox.Text = "1";
                ToIndexTextBox.Text = "1";
                CurrentIndexText.Text = "没有图片";
            }

            // 默认项目名
            ProjectNameTextBox.Text = $"project_{DateTime.Now:yyyyMMdd_HHmmss}";

            // 统计信息
            UpdateStatistics();
        }

        private void UpdateStatistics()
        {
            // 安全检查
            if (FromIndexTextBox == null || ToIndexTextBox == null ||
                SelectedCountText == null || AnnotatedCountText == null ||
                EmptyCountText == null || NotAnnotatedCountText == null ||
                PreviewListBox == null || _allImages == null)
            {
                return;
            }

            if (!int.TryParse(FromIndexTextBox.Text, out int fromIndex) ||
                !int.TryParse(ToIndexTextBox.Text, out int toIndex))
            {
                // 如果解析失败，使用默认值
                fromIndex = 1;
                toIndex = Math.Max(1, _allImages.Count);
            }

            // 确保索引在有效范围内
            fromIndex = Math.Max(1, Math.Min(_allImages.Count > 0 ? _allImages.Count : 1, fromIndex));
            toIndex = Math.Max(fromIndex, Math.Min(_allImages.Count > 0 ? _allImages.Count : 1, toIndex));

            // 如果没有图片，显示空状态
            if (_allImages.Count == 0)
            {
                SelectedCountText.Text = "选中范围: 0 张";
                AnnotatedCountText.Text = "已标记: 0 张";
                EmptyCountText.Text = "空标记: 0 张";
                NotAnnotatedCountText.Text = "未标记: 0 张";
                PreviewListBox.Items.Clear();
                PreviewListBox.Items.Add("没有可用的图片");
                return;
            }

            var selectedRange = _allImages.Skip(fromIndex - 1).Take(toIndex - fromIndex + 1).ToList();

            int annotatedCount = selectedRange.Count(img => img.Status == AnnotationStatus.Annotated);
            int emptyCount = selectedRange.Count(img => img.Status == AnnotationStatus.EmptyAnnotation);
            int notAnnotatedCount = selectedRange.Count(img => img.Status == AnnotationStatus.NotAnnotated);

            SelectedCountText.Text = $"选中范围: {selectedRange.Count} 张";
            AnnotatedCountText.Text = $"已标记: {annotatedCount} 张";
            EmptyCountText.Text = $"空标记: {emptyCount} 张";
            NotAnnotatedCountText.Text = $"未标记: {notAnnotatedCount} 张";

            // 更新预览
            PreviewListBox.Items.Clear();
            int previewCount = Math.Min(10, selectedRange.Count);
            for (int i = 0; i < previewCount; i++)
            {
                var item = selectedRange[i];
                var displayText = $"{fromIndex + i}. {item.FileName} - ";
                switch (item.Status)
                {
                    case AnnotationStatus.Annotated:
                        displayText += "✅ 已标记";
                        break;
                    case AnnotationStatus.EmptyAnnotation:
                        displayText += "⭕ 空标记";
                        break;
                    case AnnotationStatus.NotAnnotated:
                        displayText += "❌ 未标记";
                        break;
                }
                PreviewListBox.Items.Add(displayText);
            }

            if (selectedRange.Count > 10)
            {
                PreviewListBox.Items.Add($"... 还有 {selectedRange.Count - 10} 张图片");
            }
        }

        private void OnRangeChanged(object sender, TextChangedEventArgs e)
        {
            UpdateStatistics();
        }

        private void OnSelectAllClick(object sender, RoutedEventArgs e)
        {
            if (_allImages != null && _allImages.Count > 0)
            {
                FromIndexTextBox.Text = "1";
                ToIndexTextBox.Text = _allImages.Count.ToString();
            }
            UpdateStatistics();
        }

        private void OnSelectAnnotatedClick(object sender, RoutedEventArgs e)
        {
            if (_allImages == null || _allImages.Count == 0)
            {
                MessageBox.Show("没有可用的图片", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 找到第一个和最后一个已标记的图片
            int firstAnnotated = -1;
            int lastAnnotated = -1;

            for (int i = 0; i < _allImages.Count; i++)
            {
                if (_allImages[i].Status == AnnotationStatus.Annotated)
                {
                    if (firstAnnotated == -1) firstAnnotated = i;
                    lastAnnotated = i;
                }
            }

            if (firstAnnotated >= 0)
            {
                FromIndexTextBox.Text = (firstAnnotated + 1).ToString();
                ToIndexTextBox.Text = (lastAnnotated + 1).ToString();
            }
            else
            {
                MessageBox.Show("没有找到已标记的图片", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            UpdateStatistics();
        }

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "选择保存位置",
                FileName = ProjectNameTextBox.Text + ".yaml",
                Filter = "YAML文件|*.yaml;*.yml|所有文件|*.*",
                DefaultExt = ".yaml"
            };

            if (dialog.ShowDialog() == true)
            {
                OutputPathTextBox.Text = dialog.FileName;

                // 更新项目名为文件名
                var fileName = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
                ProjectNameTextBox.Text = fileName;
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            // 验证输入
            if (string.IsNullOrWhiteSpace(ProjectNameTextBox.Text))
            {
                MessageBox.Show("请输入项目名称", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(OutputPathTextBox.Text))
            {
                MessageBox.Show("请选择输出路径", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_allImages == null || _allImages.Count == 0)
            {
                MessageBox.Show("没有可保存的图片", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(FromIndexTextBox.Text, out int fromIndex) ||
                !int.TryParse(ToIndexTextBox.Text, out int toIndex))
            {
                MessageBox.Show("请输入有效的索引范围", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            fromIndex = Math.Max(1, Math.Min(_allImages.Count, fromIndex));
            toIndex = Math.Max(fromIndex, Math.Min(_allImages.Count, toIndex));

            // 获取选中的图片
            SelectedImages = _allImages.Skip(fromIndex - 1).Take(toIndex - fromIndex + 1).ToList();

            if (SelectedImages.Count == 0)
            {
                MessageBox.Show("没有选中任何图片", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 根据选项过滤
            if (IncludeEmptyCheckBox != null && !IncludeEmptyCheckBox.IsChecked.Value)
            {
                SelectedImages = SelectedImages.Where(img => img.Status != AnnotationStatus.EmptyAnnotation).ToList();
            }

            if (IncludeNotAnnotatedCheckBox != null && !IncludeNotAnnotatedCheckBox.IsChecked.Value)
            {
                SelectedImages = SelectedImages.Where(img => img.Status != AnnotationStatus.NotAnnotated).ToList();
            }

            if (SelectedImages.Count == 0)
            {
                MessageBox.Show("根据当前筛选条件，没有符合的图片", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ProjectName = ProjectNameTextBox.Text;
            IncludeEmptyAnnotations = IncludeEmptyCheckBox?.IsChecked ?? true;

            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        public string OutputPath => OutputPathTextBox?.Text ?? string.Empty;
    }
}
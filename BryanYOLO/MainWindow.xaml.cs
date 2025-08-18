using BryanYOLO.Controls;
using BryanYOLO.Models;
using BryanYOLO.Services;
using BryanYOLO.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BryanYOLO
{
    public partial class MainWindow : Window
    {
        private double _currentZoom = 1.0;
        private const double _zoomStep = 0.1;
        private const double _minZoom = 0.1;
        private const double _maxZoom = 5.0;
        private bool _isPanning = false;
        private Point _lastPanPoint;
        private const double _scrollSpeed = 50;

        // 新增：记录当前缩放模式
        private enum ZoomMode
        {
            Custom,    // 自定义缩放
            Fit,       // 适应窗口
            Fixed100   // 100%固定
        }
        private ZoomMode _currentZoomMode = ZoomMode.Custom;

        public MainWindow()
        {
            InitializeComponent();

            // 订阅ViewModel的属性变化事件
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.PropertyChanged += OnViewModelPropertyChanged;

                // 确保 ImageCanvas 也有正确的 DataContext
                this.Loaded += (s, e) =>
                {
                    if (AnnotationCanvas != null)
                    {
                        // 显式设置 ImageCanvas 的 DataContext
                        AnnotationCanvas.DataContext = viewModel;
                    }
                };
            }

            // 注册键盘事件 - 使用 PreviewKeyDown 而不是 KeyDown
            this.PreviewKeyDown += OnMainWindowPreviewKeyDown;
            this.Loaded += OnMainWindowLoaded;

            // 设置筛选ComboBox的事件处理
            SetupFilterComboBox();
        }

        private void SetupFilterComboBox()
        {
            if (FilterComboBox != null)
            {
                FilterComboBox.SelectionChanged += (s, e) =>
                {
                    if (DataContext is MainViewModel viewModel)
                    {
                        var selectedItem = FilterComboBox.SelectedItem as ComboBoxItem;
                        if (selectedItem != null)
                        {
                            var tagValue = selectedItem.Tag?.ToString();
                            viewModel.FilterStatus = tagValue switch
                            {
                                "Annotated" => AnnotationStatus.Annotated,
                                "EmptyAnnotation" => AnnotationStatus.EmptyAnnotation,
                                "NotAnnotated" => AnnotationStatus.NotAnnotated,
                                _ => null
                            };
                        }
                    }
                };
            }
        }

        private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
        {
            // 确保窗口可以接收键盘焦点
            this.Focusable = true;
            this.Focus();
        }


        private void OnViewModelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentBitmap))
            {
                var viewModel = DataContext as MainViewModel;
                if (viewModel?.CurrentBitmap != null && viewModel.CurrentImage != null)
                {
                    var bitmap = viewModel.CurrentBitmap;

                    // 设置Canvas的大小
                    if (AnnotationCanvas != null)
                    {
                        // 确保 Canvas 有正确的 DataContext
                        if (AnnotationCanvas.DataContext == null)
                        {
                            AnnotationCanvas.DataContext = viewModel;
                        }

                        AnnotationCanvas.ImageWidth = bitmap.PixelWidth;
                        AnnotationCanvas.ImageHeight = bitmap.PixelHeight;
                        AnnotationCanvas.Width = bitmap.PixelWidth;
                        AnnotationCanvas.Height = bitmap.PixelHeight;

                        // 根据当前缩放模式应用缩放
                        ApplyCurrentZoomMode();

                        // 延迟一帧后重绘Canvas，确保图片已经加载显示
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            // 确保标注已经加载
                            if (viewModel.CurrentImage.Annotations != null)
                            {
                                // 更新所有标注的像素坐标
                                foreach (var annotation in viewModel.CurrentImage.Annotations)
                                {
                                    annotation.UpdatePixelCoordinates(bitmap.PixelWidth, bitmap.PixelHeight);
                                }

                                // 触发Canvas重绘
                                AnnotationCanvas.RedrawAnnotations();
                            }
                        }), System.Windows.Threading.DispatcherPriority.Loaded);
                    }
                }
                else if (viewModel?.CurrentBitmap == null)
                {
                    // 清空Canvas
                    if (AnnotationCanvas != null)
                    {
                        AnnotationCanvas.Children.Clear();
                    }
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.CurrentImage))
            {
                // 当切换图片时，清空选中状态和属性显示
                AnnotationListBox.SelectedItem = null;
                XValueText.Text = "-";
                YValueText.Text = "-";
                WValueText.Text = "-";
                HValueText.Text = "-";

                // 新增：滚动图片列表以显示当前图片
                ScrollToCurrentImage();

                // 确保 Canvas 的 DataContext 正确
                var viewModel = DataContext as MainViewModel;
                if (viewModel != null && AnnotationCanvas != null)
                {
                    AnnotationCanvas.DataContext = viewModel;
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.SelectedClassId))
            {
                // 当选中的类别改变时，更新 Canvas
                var viewModel = DataContext as MainViewModel;
                if (viewModel != null && AnnotationCanvas != null)
                {
                    AnnotationCanvas.SelectedClassId = viewModel.SelectedClassId;
                }
            }
        }

        private void SyncClassSelection()
        {
            var viewModel = DataContext as MainViewModel;
            if (viewModel != null && ClassSelector != null && AnnotationCanvas != null)
            {
                // 监听 ComboBox 的选择变化
                ClassSelector.SelectionChanged += (s, e) =>
                {
                    if (ClassSelector.SelectedIndex >= 0)
                    {
                        viewModel.SelectedClassId = ClassSelector.SelectedIndex;
                        AnnotationCanvas.SelectedClassId = ClassSelector.SelectedIndex;
                    }
                };

                // 监听 Canvas 的属性变化
                var dpd = DependencyPropertyDescriptor.FromProperty(
                    ImageCanvas.SelectedClassIdProperty,
                    typeof(ImageCanvas));

                if (dpd != null)
                {
                    dpd.AddValueChanged(AnnotationCanvas, (s, e) =>
                    {
                        viewModel.SelectedClassId = AnnotationCanvas.SelectedClassId;
                        ClassSelector.SelectedIndex = AnnotationCanvas.SelectedClassId;
                    });
                }
            }
        }
        // 新增：滚动到当前图片
        private void ScrollToCurrentImage()
        {
            var viewModel = DataContext as MainViewModel;
            if (viewModel?.CurrentImage != null && ImageListBox != null)
            {
                // 确保当前图片在列表中可见
                ImageListBox.ScrollIntoView(viewModel.CurrentImage);

                // 确保选中状态同步
                if (ImageListBox.SelectedItem != viewModel.CurrentImage)
                {
                    ImageListBox.SelectedItem = viewModel.CurrentImage;
                }
            }
        }

        // 新增：更新跳转输入框的提示
        private void UpdateJumpTextBoxHint()
        {
            var viewModel = DataContext as MainViewModel;
            if (viewModel != null && JumpToIndexTextBox != null)
            {
                // 可以设置占位符文本或工具提示
                JumpToIndexTextBox.ToolTip = $"输入1-{viewModel.FilteredImages.Count}之间的数字";
            }
        }

        // 新增：跳转到指定图片
        private void OnJumpToImage(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MainViewModel;
            if (viewModel == null || JumpToIndexTextBox == null)
                return;

            if (int.TryParse(JumpToIndexTextBox.Text, out int index))
            {
                // 索引从1开始，需要转换为0基索引
                index = index - 1;

                if (index >= 0 && index < viewModel.FilteredImages.Count)
                {
                    viewModel.CurrentImage = viewModel.FilteredImages[index];
                    JumpToIndexTextBox.Clear();
                }
                else
                {
                    MessageBox.Show(
                        $"请输入1到{viewModel.FilteredImages.Count}之间的数字",
                        "无效的索引",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            else
            {
                MessageBox.Show(
                    "请输入有效的数字",
                    "输入错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        // 新增：处理跳转输入框的回车键
        private void OnJumpToIndexKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OnJumpToImage(sender, null);
                e.Handled = true;
            }
        }

        // 新增：限制跳转输入框只能输入数字
        private void OnJumpNumberPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // 只允许输入数字
            e.Handled = !int.TryParse(e.Text, out _);
        }

        // 新增：应用当前缩放模式
        private void ApplyCurrentZoomMode()
        {
            switch (_currentZoomMode)
            {
                case ZoomMode.Fit:
                    FitImageToWindow();
                    break;
                case ZoomMode.Fixed100:
                    ZoomImage(1.0);
                    break;
                case ZoomMode.Custom:
                    // 保持当前缩放级别
                    ZoomImage(_currentZoom);
                    break;
            }
        }

        // 缩放相关方法
        private void OnZoomIn(object sender, RoutedEventArgs e)
        {
            _currentZoomMode = ZoomMode.Custom;
            ZoomImage(_currentZoom + _zoomStep);
        }

        private void OnZoomOut(object sender, RoutedEventArgs e)
        {
            _currentZoomMode = ZoomMode.Custom;
            ZoomImage(_currentZoom - _zoomStep);
        }

        private void OnZoom100(object sender, RoutedEventArgs e)
        {
            _currentZoomMode = ZoomMode.Fixed100;
            ZoomImage(1.0);
        }

        private void OnZoomFit(object sender, RoutedEventArgs e)
        {
            _currentZoomMode = ZoomMode.Fit;
            FitImageToWindow();
        }

        private void ZoomImage(double newZoom)
        {
            // 限制缩放范围
            newZoom = Math.Max(_minZoom, Math.Min(_maxZoom, newZoom));

            // 获取当前滚动位置
            var scrollViewer = ImageScrollViewer;
            var horizontalOffset = scrollViewer.HorizontalOffset;
            var verticalOffset = scrollViewer.VerticalOffset;
            var viewportWidth = scrollViewer.ViewportWidth;
            var viewportHeight = scrollViewer.ViewportHeight;

            // 计算缩放中心点
            var centerX = horizontalOffset + viewportWidth / 2;
            var centerY = verticalOffset + viewportHeight / 2;

            // 应用缩放
            _currentZoom = newZoom;
            ImageScaleTransform.ScaleX = _currentZoom;
            ImageScaleTransform.ScaleY = _currentZoom;

            // 将缩放级别传递给Canvas
            if (AnnotationCanvas != null)
            {
                AnnotationCanvas.CurrentZoomLevel = _currentZoom;
            }

            // 更新显示
            UpdateZoomDisplay();

            // 调整滚动位置以保持中心点
            scrollViewer.UpdateLayout();
            var newCenterX = centerX * (newZoom / (_currentZoom / (newZoom / _currentZoom)));
            var newCenterY = centerY * (newZoom / (_currentZoom / (newZoom / _currentZoom)));
            scrollViewer.ScrollToHorizontalOffset(newCenterX - viewportWidth / 2);
            scrollViewer.ScrollToVerticalOffset(newCenterY - viewportHeight / 2);
        }

        private void FitImageToWindow()
        {
            var viewModel = DataContext as MainViewModel;
            if (viewModel?.CurrentBitmap == null) return;

            var imageWidth = viewModel.CurrentBitmap.PixelWidth;
            var imageHeight = viewModel.CurrentBitmap.PixelHeight;
            var viewportWidth = ImageScrollViewer.ViewportWidth;
            var viewportHeight = ImageScrollViewer.ViewportHeight;

            if (imageWidth <= 0 || imageHeight <= 0) return;

            var scaleX = viewportWidth / imageWidth;
            var scaleY = viewportHeight / imageHeight;
            var scale = Math.Min(scaleX, scaleY);

            ZoomImage(scale);
        }

        private void UpdateZoomDisplay()
        {
            if (ZoomLevelText != null)
            {
                ZoomLevelText.Text = $"{(_currentZoom * 100):F0}%";
            }
        }

        // 鼠标滚轮缩放
        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                _currentZoomMode = ZoomMode.Custom;
                var delta = e.Delta > 0 ? _zoomStep : -_zoomStep;
                ZoomImage(_currentZoom + delta);
                e.Handled = true;
            }
        }

        // 右键拖动
        private void OnScrollViewerMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.RightButton == MouseButtonState.Pressed)
            {
                _isPanning = true;
                _lastPanPoint = e.GetPosition(ImageScrollViewer);
                ImageScrollViewer.CaptureMouse();
                ImageScrollViewer.Cursor = Cursors.Hand;
                e.Handled = true;
            }
        }

        private void OnScrollViewerMouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                var currentPoint = e.GetPosition(ImageScrollViewer);
                var deltaX = currentPoint.X - _lastPanPoint.X;
                var deltaY = currentPoint.Y - _lastPanPoint.Y;

                ImageScrollViewer.ScrollToHorizontalOffset(ImageScrollViewer.HorizontalOffset - deltaX);
                ImageScrollViewer.ScrollToVerticalOffset(ImageScrollViewer.VerticalOffset - deltaY);

                _lastPanPoint = currentPoint;
                e.Handled = true;
            }
        }

        private void OnScrollViewerMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                ImageScrollViewer.ReleaseMouseCapture();
                ImageScrollViewer.Cursor = Cursors.Arrow;
                e.Handled = true;
            }
        }

        private void OnMainWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var viewModel = DataContext as MainViewModel;
            if (viewModel == null) return;

            // 检查是否在文本框中输入
            if (e.OriginalSource is TextBox textBox)
            {
                // 如果是跳转输入框，允许回车键
                if (textBox == JumpToIndexTextBox && e.Key == Key.Enter)
                {
                    OnJumpToImage(null, null);
                    e.Handled = true;
                    return;
                }

                // 如果是其他文本框，不处理导航键
                if (!(e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Delete || e.Key == Key.Escape))
                {
                    return;
                }
            }

            bool handled = true;

            switch (e.Key)
            {
                // 箭头键导航
                case Key.Left:
                    if (viewModel.PreviousImageCommand.CanExecute(null))
                    {
                        viewModel.PreviousImageCommand.Execute(null);
                    }
                    break;

                case Key.Right:
                    if (viewModel.NextImageCommand.CanExecute(null))
                    {
                        viewModel.NextImageCommand.Execute(null);
                    }
                    break;

                // WASD导航（滚动视图）
                case Key.W:
                    ImageScrollViewer.ScrollToVerticalOffset(ImageScrollViewer.VerticalOffset - _scrollSpeed);
                    break;

                case Key.S:
                    if (Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        // Ctrl+S 保存
                        if (viewModel.SaveAnnotationsCommand.CanExecute(null))
                        {
                            viewModel.SaveAnnotationsCommand.Execute(null);
                        }
                    }
                    else
                    {
                        // 普通S键用于滚动
                        ImageScrollViewer.ScrollToVerticalOffset(ImageScrollViewer.VerticalOffset + _scrollSpeed);
                    }
                    break;

                case Key.A:
                    ImageScrollViewer.ScrollToHorizontalOffset(ImageScrollViewer.HorizontalOffset - _scrollSpeed);
                    break;

                case Key.D:
                    ImageScrollViewer.ScrollToHorizontalOffset(ImageScrollViewer.HorizontalOffset + _scrollSpeed);
                    break;

                // 快捷键缩放
                case Key.Add:
                case Key.OemPlus:
                    if (Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        _currentZoomMode = ZoomMode.Custom;
                        ZoomImage(_currentZoom + _zoomStep);
                    }
                    break;

                case Key.Subtract:
                case Key.OemMinus:
                    if (Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        _currentZoomMode = ZoomMode.Custom;
                        ZoomImage(_currentZoom - _zoomStep);
                    }
                    break;

                case Key.D0:
                case Key.NumPad0:
                    if (Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        _currentZoomMode = ZoomMode.Fixed100;
                        ZoomImage(1.0);
                    }
                    break;

                // 删除键
                case Key.Delete:
                    // Delete键处理已经在ImageCanvas中处理了
                    handled = false;
                    break;

                // Escape键
                case Key.Escape:
                    // Escape键处理已经在ImageCanvas中处理了
                    handled = false;
                    break;

                // F1 帮助
                case Key.F1:
                    if (viewModel.ShowHelpCommand.CanExecute(null))
                    {
                        viewModel.ShowHelpCommand.Execute(null);
                    }
                    break;

                default:
                    handled = false;
                    break;
            }

            if (handled)
                e.Handled = true;
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void OnAnnotationSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var listBox = sender as ListBox;
            var annotation = listBox?.SelectedItem as Annotation;

            if (annotation != null)
            {
                // 更新属性显示
                XValueText.Text = annotation.X.ToString("F4");
                YValueText.Text = annotation.Y.ToString("F4");
                WValueText.Text = annotation.Width.ToString("F4");
                HValueText.Text = annotation.Height.ToString("F4");

                // 在画布上高亮选中的标注
                var viewModel = DataContext as MainViewModel;
                if (viewModel?.CurrentImage != null)
                {
                    foreach (var ann in viewModel.CurrentImage.Annotations)
                    {
                        ann.IsSelected = ann == annotation;
                    }
                    // 触发重绘
                    AnnotationCanvas?.RedrawAnnotations();
                }
            }
            else
            {
                XValueText.Text = "-";
                YValueText.Text = "-";
                WValueText.Text = "-";
                HValueText.Text = "-";
            }
        }

        private void OnSelectAnnotation(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var annotation = button?.Tag as Annotation;

            if (annotation != null)
            {
                AnnotationListBox.SelectedItem = annotation;
            }
        }

        private void OnChangeAnnotationClass(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var annotation = button?.Tag as Annotation;
            var viewModel = DataContext as MainViewModel;

            if (annotation != null && viewModel?.CurrentProject?.Classes != null)
            {
                // 创建选择类别的对话框
                var dialog = new Window
                {
                    Title = "更换类别",
                    Width = 300,
                    Height = 400,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this
                };

                var grid = new Grid();
                grid.Margin = new Thickness(10);
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = $"当前类别: {annotation.DisplayName}",
                    Margin = new Thickness(0, 0, 0, 10)
                };
                Grid.SetRow(label, 0);
                grid.Children.Add(label);

                var newLabel = new TextBlock
                {
                    Text = "选择新类别:",
                    Margin = new Thickness(0, 0, 0, 5)
                };
                Grid.SetRow(newLabel, 1);
                grid.Children.Add(newLabel);

                var listBox = new ListBox
                {
                    ItemsSource = viewModel.CurrentProject.Classes,
                    SelectedIndex = annotation.ClassId,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                Grid.SetRow(listBox, 2);
                grid.Children.Add(listBox);

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var okButton = new Button
                {
                    Content = "确定",
                    Width = 80,
                    IsDefault = true,
                    Margin = new Thickness(0, 0, 5, 0)
                };
                okButton.Click += (s, args) =>
                {
                    if (listBox.SelectedIndex >= 0 && listBox.SelectedIndex != annotation.ClassId)
                    {
                        // 更新类别ID
                        annotation.ClassId = listBox.SelectedIndex;

                        // 标记图片已修改
                        if (viewModel?.CurrentImage != null)
                        {
                            viewModel.CurrentImage.IsModified = true;

                            // 更新文本预览
                            viewModel.UpdateAnnotationText();
                        }

                        // 通知属性更改
                        annotation.OnPropertyChanged("ClassId");
                        annotation.OnPropertyChanged("DisplayName");

                        // 立即触发画布重绘
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            // 强制重绘画布，确保颜色和标签都更新
                            AnnotationCanvas?.RedrawAnnotations();

                            // 刷新列表显示
                            AnnotationListBox.Items.Refresh();
                        }), System.Windows.Threading.DispatcherPriority.Render);
                    }
                    dialog.Close();
                };

                var cancelButton = new Button
                {
                    Content = "取消",
                    Width = 80,
                    IsCancel = true
                };
                cancelButton.Click += (s, args) => dialog.Close();

                buttonPanel.Children.Add(okButton);
                buttonPanel.Children.Add(cancelButton);
                Grid.SetRow(buttonPanel, 3);
                grid.Children.Add(buttonPanel);

                dialog.Content = grid;
                dialog.ShowDialog();
            }
        }

        // 删除标注的处理
        private void OnDeleteAnnotation(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var annotation = button?.Tag as Annotation;
            var viewModel = DataContext as MainViewModel;

            if (annotation != null && viewModel?.CurrentImage != null)
            {
                // 从集合中删除标注
                viewModel.CurrentImage.Annotations.Remove(annotation);
                viewModel.CurrentImage.IsModified = true;

                // 更新文本预览
                viewModel.UpdateAnnotationText();

                // 触发画布重绘
                AnnotationCanvas?.RedrawAnnotations();
            }
        }

        // 推测标注对话框
        private void OnOpenInferAnnotationDialog(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MainViewModel;
            if (viewModel == null || viewModel.CurrentProject == null) return;

            var dialog = new Window
            {
                Title = "推测标注",
                Width = 550,
                Height = 750,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var grid = new Grid();
            grid.Margin = new Thickness(10);

            // 动态添加行定义
            for (int i = 0; i < 18; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            int currentRow = 0;

            // 标题
            var titleLabel = new TextBlock
            {
                Text = "基于现有框体推测其他框体位置",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(titleLabel, currentRow++);
            grid.Children.Add(titleLabel);

            // 基准框体类别
            var baseClassLabel = new TextBlock { Text = "基准框体类别ID:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(baseClassLabel, currentRow++);
            grid.Children.Add(baseClassLabel);

            var baseClassPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var baseClassTextBox = new TextBox
            {
                Text = "0",
                Width = 60,
                Margin = new Thickness(0, 0, 10, 0)
            };

            var baseClassNameText = new TextBlock
            {
                Text = viewModel.CurrentProject?.Classes?.Count > 0 ? $"（{viewModel.CurrentProject.Classes[0]}）" : "",
                VerticalAlignment = VerticalAlignment.Center
            };
            baseClassTextBox.TextChanged += (s, args) =>
            {
                if (int.TryParse(baseClassTextBox.Text, out int id) &&
                    viewModel.CurrentProject?.Classes != null &&
                    id >= 0 && id < viewModel.CurrentProject.Classes.Count)
                {
                    baseClassNameText.Text = $"（{viewModel.CurrentProject.Classes[id]}）";
                }
                else
                {
                    baseClassNameText.Text = "";
                }
            };

            baseClassPanel.Children.Add(baseClassTextBox);
            baseClassPanel.Children.Add(baseClassNameText);
            Grid.SetRow(baseClassPanel, currentRow++);
            grid.Children.Add(baseClassPanel);

            // 目标框体类别
            var targetClassLabel = new TextBlock { Text = "推测框体类别ID:", Margin = new Thickness(0, 10, 0, 5) };
            Grid.SetRow(targetClassLabel, currentRow++);
            grid.Children.Add(targetClassLabel);

            var targetClassPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var targetClassTextBox = new TextBox
            {
                Text = "1",
                Width = 60,
                Margin = new Thickness(0, 0, 10, 0)
            };

            var targetClassNameText = new TextBlock
            {
                Text = viewModel.CurrentProject?.Classes?.Count > 1 ? $"（{viewModel.CurrentProject.Classes[1]}）" : "",
                VerticalAlignment = VerticalAlignment.Center
            };
            targetClassTextBox.TextChanged += (s, args) =>
            {
                if (int.TryParse(targetClassTextBox.Text, out int id) &&
                    viewModel.CurrentProject?.Classes != null &&
                    id >= 0 && id < viewModel.CurrentProject.Classes.Count)
                {
                    targetClassNameText.Text = $"（{viewModel.CurrentProject.Classes[id]}）";
                }
                else
                {
                    targetClassNameText.Text = "";
                }
            };

            targetClassPanel.Children.Add(targetClassTextBox);
            targetClassPanel.Children.Add(targetClassNameText);
            Grid.SetRow(targetClassPanel, currentRow++);
            grid.Children.Add(targetClassPanel);

            // 相对位置
            var positionLabel = new TextBlock { Text = "推测框体位置:", Margin = new Thickness(0, 10, 0, 5) };
            Grid.SetRow(positionLabel, currentRow++);
            grid.Children.Add(positionLabel);

            var positionComboBox = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
            positionComboBox.Items.Add(new ComboBoxItem { Content = "内部", Tag = "Inside", IsSelected = true });
            positionComboBox.Items.Add(new ComboBoxItem { Content = "上方", Tag = "Above" });
            positionComboBox.Items.Add(new ComboBoxItem { Content = "下方", Tag = "Below" });
            positionComboBox.Items.Add(new ComboBoxItem { Content = "左边", Tag = "Left" });
            positionComboBox.Items.Add(new ComboBoxItem { Content = "右边", Tag = "Right" });
            Grid.SetRow(positionComboBox, currentRow++);
            grid.Children.Add(positionComboBox);

            // 偏移量
            var offsetLabel = new TextBlock { Text = "偏移量（归一化坐标 0-1）:", Margin = new Thickness(0, 10, 0, 5) };
            Grid.SetRow(offsetLabel, currentRow++);
            grid.Children.Add(offsetLabel);

            var offsetPanel = new Grid();
            offsetPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            offsetPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            offsetPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            offsetPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            var xOffsetLabel = new TextBlock { Text = "X偏移: ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
            var xOffsetTextBox = new TextBox { Text = "0", Width = 80 };
            var yOffsetLabel = new TextBlock { Text = "Y偏移: ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 5, 0) };
            var yOffsetTextBox = new TextBox { Text = "0", Width = 80 };

            Grid.SetColumn(xOffsetLabel, 0);
            Grid.SetColumn(xOffsetTextBox, 1);
            Grid.SetColumn(yOffsetLabel, 2);
            Grid.SetColumn(yOffsetTextBox, 3);

            offsetPanel.Children.Add(xOffsetLabel);
            offsetPanel.Children.Add(xOffsetTextBox);
            offsetPanel.Children.Add(yOffsetLabel);
            offsetPanel.Children.Add(yOffsetTextBox);

            Grid.SetRow(offsetPanel, currentRow++);
            grid.Children.Add(offsetPanel);

            // 大小比例
            var sizeLabel = new TextBlock { Text = "框体大小（相对于基准框体的百分比）:", Margin = new Thickness(0, 10, 0, 5) };
            Grid.SetRow(sizeLabel, currentRow++);
            grid.Children.Add(sizeLabel);

            var sizePanel = new Grid();
            sizePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sizePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            sizePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sizePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            var widthPercentLabel = new TextBlock { Text = "宽度%: ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
            var widthPercentTextBox = new TextBox { Text = "80", Width = 80 };
            var heightPercentLabel = new TextBlock { Text = "高度%: ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 5, 0) };
            var heightPercentTextBox = new TextBox { Text = "40", Width = 80 };

            Grid.SetColumn(widthPercentLabel, 0);
            Grid.SetColumn(widthPercentTextBox, 1);
            Grid.SetColumn(heightPercentLabel, 2);
            Grid.SetColumn(heightPercentTextBox, 3);

            sizePanel.Children.Add(widthPercentLabel);
            sizePanel.Children.Add(widthPercentTextBox);
            sizePanel.Children.Add(heightPercentLabel);
            sizePanel.Children.Add(heightPercentTextBox);

            Grid.SetRow(sizePanel, currentRow++);
            grid.Children.Add(sizePanel);

            // 对齐方式
            var alignmentLabel = new TextBlock { Text = "对齐方式:", Margin = new Thickness(0, 10, 0, 5) };
            Grid.SetRow(alignmentLabel, currentRow++);
            grid.Children.Add(alignmentLabel);

            var alignmentComboBox = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
            alignmentComboBox.Items.Add(new ComboBoxItem { Content = "左对齐", Tag = "Left" });
            alignmentComboBox.Items.Add(new ComboBoxItem { Content = "居中对齐", Tag = "Center", IsSelected = true });
            alignmentComboBox.Items.Add(new ComboBoxItem { Content = "右对齐", Tag = "Right" });
            Grid.SetRow(alignmentComboBox, currentRow++);
            grid.Children.Add(alignmentComboBox);

            // 替换已存在的标注
            var replaceCheckBox = new CheckBox
            {
                Content = "替换已存在的目标类别标注",
                IsChecked = false,
                Margin = new Thickness(0, 10, 0, 5)
            };
            Grid.SetRow(replaceCheckBox, currentRow++);
            grid.Children.Add(replaceCheckBox);

            // 处理范围
            var processRangeCheckBox = new CheckBox
            {
                Content = "仅处理当前图片",
                IsChecked = false,
                Margin = new Thickness(0, 5, 0, 5)
            };
            Grid.SetRow(processRangeCheckBox, currentRow++);
            grid.Children.Add(processRangeCheckBox);

            // 说明文本
            var infoText = new TextBlock
            {
                Text = "说明：\n" +
                       "• 此功能基于现有框体（如身体）推测其他框体（如头部）的位置\n" +
                       "• 内部位置：推测框体在基准框体内部（如身体内的头部）\n" +
                       "• 偏移量使用归一化坐标（0-1），相对于图片尺寸\n" +
                       "• 大小百分比是相对于基准框体的比例\n" +
                       "• 对齐方式决定推测框体如何与基准框体对齐\n" +
                       "• 使用多线程并行处理，速度更快",
                Margin = new Thickness(0, 15, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 11
            };
            Grid.SetRow(infoText, currentRow++);
            grid.Children.Add(infoText);

            // 进度条
            var progressPanel = new StackPanel
            {
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var progressLabel = new TextBlock
            {
                Text = "处理进度:",
                Margin = new Thickness(0, 0, 0, 5)
            };
            progressPanel.Children.Add(progressLabel);

            var progressBar = new ProgressBar
            {
                Height = 20,
                Minimum = 0,
                Maximum = 1
            };
            progressPanel.Children.Add(progressBar);

            var progressText = new TextBlock
            {
                Text = "准备中...",
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0)
            };
            progressPanel.Children.Add(progressText);

            Grid.SetRow(progressPanel, currentRow++);
            grid.Children.Add(progressPanel);

            // 按钮面板
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 15, 0, 0)
            };

            var runButton = new Button
            {
                Content = "开始推测",
                Width = 100,
                IsDefault = true
            };

            runButton.Click += async (s, args) =>
            {
                // 验证输入
                if (!int.TryParse(baseClassTextBox.Text, out int baseClassId))
                {
                    MessageBox.Show("请输入有效的基准框体类别ID", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!int.TryParse(targetClassTextBox.Text, out int targetClassId))
                {
                    MessageBox.Show("请输入有效的推测框体类别ID", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!double.TryParse(xOffsetTextBox.Text, out double xOffset))
                {
                    xOffset = 0;
                }

                if (!double.TryParse(yOffsetTextBox.Text, out double yOffset))
                {
                    yOffset = 0;
                }

                if (!double.TryParse(widthPercentTextBox.Text, out double widthPercent) || widthPercent <= 0)
                {
                    MessageBox.Show("请输入有效的宽度百分比", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!double.TryParse(heightPercentTextBox.Text, out double heightPercent) || heightPercent <= 0)
                {
                    MessageBox.Show("请输入有效的高度百分比", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 创建配置
                var config = new InferAnnotationService.InferConfig
                {
                    BaseClassId = baseClassId,
                    TargetClassId = targetClassId,
                    Position = Enum.Parse<InferAnnotationService.InferPosition>((string)((ComboBoxItem)positionComboBox.SelectedItem).Tag),
                    XOffset = xOffset,
                    YOffset = yOffset,
                    WidthPercent = widthPercent,
                    HeightPercent = heightPercent,
                    Alignment = Enum.Parse<InferAnnotationService.AlignmentMode>((string)((ComboBoxItem)alignmentComboBox.SelectedItem).Tag),
                    ReplaceExisting = replaceCheckBox.IsChecked == true,
                    ProcessCurrentImageOnly = processRangeCheckBox.IsChecked == true
                };

                // 监听进度
                viewModel.PropertyChanged += OnProgressChanged;
                void OnProgressChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
                {
                    if (e.PropertyName == nameof(MainViewModel.InferenceProgress))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            progressBar.Value = viewModel.InferenceProgress;
                            progressText.Text = $"{(viewModel.InferenceProgress * 100):F0}%";
                        });
                    }
                    else if (e.PropertyName == nameof(MainViewModel.IsLoading))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (viewModel.IsLoading)
                            {
                                progressPanel.Visibility = Visibility.Visible;
                                runButton.IsEnabled = false;
                            }
                            else
                            {
                                progressPanel.Visibility = Visibility.Collapsed;
                                runButton.IsEnabled = true;
                                viewModel.PropertyChanged -= OnProgressChanged;
                                if (progressBar.Value >= 0.99)
                                {
                                    dialog.Close();
                                }
                            }
                        });
                    }
                    else if (e.PropertyName == nameof(MainViewModel.StatusMessage))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (viewModel.IsLoading && !string.IsNullOrEmpty(viewModel.StatusMessage))
                            {
                                progressText.Text = viewModel.StatusMessage;
                            }
                        });
                    }
                }

                // 执行推测
                await viewModel.RunAnnotationInference(config);
            };

            var cancelButton = new Button
            {
                Content = "取消",
                Width = 100,
                IsCancel = true
            };
            cancelButton.Click += (s, args) => dialog.Close();

            buttonPanel.Children.Add(runButton);
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, currentRow++);
            grid.Children.Add(buttonPanel);

            scrollViewer.Content = grid;
            dialog.Content = scrollViewer;
            dialog.ShowDialog();
        }

        private void OnOpenLabelAdjustmentDialog(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MainViewModel;
            if (viewModel == null || viewModel.CurrentProject == null) return;

            var dialog = new Window
            {
                Title = "标签识别修正",
                Width = 600,
                Height = 900,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var grid = new Grid();
            grid.Margin = new Thickness(10);

            // 动态添加行定义
            for (int i = 0; i < 28; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            int currentRow = 0;

            // 标题
            var titleLabel = new TextBlock
            {
                Text = "通过识别图标自动调整标注框",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(titleLabel, currentRow++);
            grid.Children.Add(titleLabel);

            // 检测方法选择
            var methodLabel = new TextBlock { Text = "检测方法:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(methodLabel, currentRow++);
            grid.Children.Add(methodLabel);

            var methodComboBox = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
            methodComboBox.Items.Add(new ComboBoxItem { Content = "ONNX模型检测", Tag = IconDetectionService.DetectionMethod.OnnxModel, IsSelected = true });
            methodComboBox.Items.Add(new ComboBoxItem { Content = "传统模板匹配", Tag = IconDetectionService.DetectionMethod.Traditional, IsEnabled = false });
            Grid.SetRow(methodComboBox, currentRow++);
            grid.Children.Add(methodComboBox);

            // ONNX模型部分
            var onnxGroupBox = new GroupBox
            {
                Header = "ONNX模型设置",
                Margin = new Thickness(0, 10, 0, 10)
            };
            var onnxGrid = new Grid();
            onnxGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            onnxGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            onnxGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            onnxGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            onnxGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            onnxGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            onnxGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            onnxGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            onnxGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            onnxGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            int onnxRow = 0;

            // 模型文件
            var modelLabel = new TextBlock { Text = "模型文件:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(modelLabel, onnxRow++);
            onnxGrid.Children.Add(modelLabel);

            var modelPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var modelTextBox = new TextBox
            {
                Width = 380,
                IsReadOnly = true,
                Margin = new Thickness(0, 0, 5, 0)
            };
            var browseButton = new Button
            {
                Content = "浏览...",
                Width = 80
            };
            browseButton.Click += (s, args) =>
            {
                var openDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择ONNX模型文件",
                    Filter = "ONNX模型|*.onnx|所有文件|*.*",
                    CheckFileExists = true
                };

                if (openDialog.ShowDialog() == true)
                {
                    modelTextBox.Text = openDialog.FileName;
                }
            };
            modelPanel.Children.Add(modelTextBox);
            modelPanel.Children.Add(browseButton);
            Grid.SetRow(modelPanel, onnxRow++);
            onnxGrid.Children.Add(modelPanel);

            // 模型版本
            var versionLabel = new TextBlock { Text = "模型版本:", Margin = new Thickness(0, 10, 0, 5) };
            Grid.SetRow(versionLabel, onnxRow++);
            onnxGrid.Children.Add(versionLabel);

            var versionComboBox = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
            versionComboBox.Items.Add(new ComboBoxItem { Content = "YOLOv5", Tag = YoloVersion.V5, IsSelected = true });
            versionComboBox.Items.Add(new ComboBoxItem { Content = "YOLOv8", Tag = YoloVersion.V8 });
            versionComboBox.Items.Add(new ComboBoxItem { Content = "YOLOv10", Tag = YoloVersion.V10 });
            versionComboBox.Items.Add(new ComboBoxItem { Content = "YOLOv11", Tag = YoloVersion.V11 });
            Grid.SetRow(versionComboBox, onnxRow++);
            onnxGrid.Children.Add(versionComboBox);

            // 模型输入大小
            var inputSizeLabel = new TextBlock { Text = "模型输入大小:", Margin = new Thickness(0, 10, 0, 5) };
            Grid.SetRow(inputSizeLabel, onnxRow++);
            onnxGrid.Children.Add(inputSizeLabel);

            var inputSizeTextBox = new TextBox { Text = "640", Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetRow(inputSizeTextBox, onnxRow++);
            onnxGrid.Children.Add(inputSizeTextBox);

            // 模型类别数
            var classCountLabel = new TextBlock { Text = "模型类别数 (留空自动检测):", Margin = new Thickness(0, 10, 0, 5) };
            Grid.SetRow(classCountLabel, onnxRow++);
            onnxGrid.Children.Add(classCountLabel);

            var classCountTextBox = new TextBox { Text = "", Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetRow(classCountTextBox, onnxRow++);
            onnxGrid.Children.Add(classCountTextBox);

            // 图标类别ID
            var iconClassLabel = new TextBlock { Text = "图标的类别ID (在模型中):", Margin = new Thickness(0, 10, 0, 5) };
            Grid.SetRow(iconClassLabel, onnxRow++);
            onnxGrid.Children.Add(iconClassLabel);

            var iconClassTextBox = new TextBox { Text = "0", Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetRow(iconClassTextBox, onnxRow++);
            onnxGrid.Children.Add(iconClassTextBox);

            // 置信度阈值
            var confidenceLabel = new TextBlock { Text = "置信度阈值:", Margin = new Thickness(0, 10, 0, 5) };
            Grid.SetRow(confidenceLabel, onnxRow++);
            onnxGrid.Children.Add(confidenceLabel);

            var confidencePanel = new StackPanel { Orientation = Orientation.Horizontal };
            var confidenceSlider = new Slider
            {
                Minimum = 0.1,
                Maximum = 1.0,
                Value = 0.5,
                Width = 200,
                TickFrequency = 0.1,
                IsSnapToTickEnabled = false,
                Margin = new Thickness(0, 0, 10, 0)
            };
            var confidenceTextBox = new TextBox
            {
                Text = "0.50",
                Width = 60
            };
            confidenceSlider.ValueChanged += (s, args) =>
            {
                confidenceTextBox.Text = confidenceSlider.Value.ToString("F2");
            };
            confidencePanel.Children.Add(confidenceSlider);
            confidencePanel.Children.Add(confidenceTextBox);
            Grid.SetRow(confidencePanel, onnxRow++);
            onnxGrid.Children.Add(confidencePanel);

            onnxGroupBox.Content = onnxGrid;
            Grid.SetRow(onnxGroupBox, currentRow++);
            grid.Children.Add(onnxGroupBox);

            // 预览按钮
            var previewButton = new Button
            {
                Content = "预览检测结果",
                Width = 120,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 5, 0, 10)
            };
            previewButton.Click += async (s, args) =>
            {
                if (string.IsNullOrEmpty(modelTextBox.Text))
                {
                    MessageBox.Show("请选择ONNX模型文件", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (viewModel.CurrentImage == null)
                {
                    MessageBox.Show("请先选择一张图片", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    previewButton.IsEnabled = false;
                    previewButton.Content = "检测中...";

                    // 获取用户输入
                    if (!int.TryParse(inputSizeTextBox.Text, out int inputSize) || inputSize <= 0)
                    {
                        inputSize = 640;
                    }

                    if (!float.TryParse(confidenceTextBox.Text, out float confidence) || confidence <= 0)
                    {
                        confidence = 0.5f;
                    }

                    if (!int.TryParse(iconClassTextBox.Text, out int iconClassId))
                    {
                        iconClassId = 0;
                    }

                    int modelClassCount = -1;
                    if (!string.IsNullOrWhiteSpace(classCountTextBox.Text))
                    {
                        int.TryParse(classCountTextBox.Text, out modelClassCount);
                    }

                    var selectedVersion = (versionComboBox.SelectedItem as ComboBoxItem)?.Tag;
                    var version = selectedVersion != null ? (YoloVersion)selectedVersion : YoloVersion.V5;

                    // 创建预览配置
                    var config = new IconDetectionService.LabelAdjustmentConfig
                    {
                        Method = IconDetectionService.DetectionMethod.OnnxModel,
                        OnnxModelPath = modelTextBox.Text,
                        ModelInputSize = inputSize,
                        IconClassId = iconClassId,
                        ConfidenceThreshold = confidence,
                        ModelClassCount = modelClassCount,
                        ModelVersion = version
                    };

                    // 创建服务并预览
                    using (var service = new IconDetectionService())
                    {
                        var result = await service.PreviewDetection(
                            viewModel.CurrentImage.ImagePath,
                            config,
                            msg => Console.WriteLine(msg));

                        if (result.Success)
                        {
                            // 显示预览对话框
                            var previewDialog = new BryanYOLO.Dialogs.ModelPreviewDialog(
                                viewModel.CurrentImage.ImagePath,
                                modelTextBox.Text,
                                inputSize,
                                confidence,
                                iconClassId,
                                modelClassCount);

                            previewDialog.Owner = dialog;
                            previewDialog.ShowDialog();
                        }
                        else
                        {
                            MessageBox.Show(result.Message, "检测结果", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"预览失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    previewButton.IsEnabled = true;
                    previewButton.Content = "预览检测结果";
                }
            };
            Grid.SetRow(previewButton, currentRow++);
            grid.Children.Add(previewButton);

            // 调整设置部分
            var adjustmentGroupBox = new GroupBox
            {
                Header = "标签调整设置",
                Margin = new Thickness(0, 10, 0, 10)
            };

            var adjustmentGrid = new Grid();
            for (int i = 0; i < 10; i++)
            {
                adjustmentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            int adjustRow = 0;

            // 目标标签
            var targetLabelLabel = new TextBlock { Text = "要调整的标签ID:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(targetLabelLabel, adjustRow++);
            adjustmentGrid.Children.Add(targetLabelLabel);

            var targetLabelPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var targetLabelTextBox = new TextBox
            {
                Text = "0",
                Width = 60,
                Margin = new Thickness(0, 0, 10, 0)
            };
            var targetLabelNameText = new TextBlock
            {
                Text = viewModel.CurrentProject?.Classes?.Count > 0 ? $"（{viewModel.CurrentProject.Classes[0]}）" : "",
                VerticalAlignment = VerticalAlignment.Center
            };
            targetLabelTextBox.TextChanged += (s, args) =>
            {
                if (int.TryParse(targetLabelTextBox.Text, out int id) &&
                    viewModel.CurrentProject?.Classes != null &&
                    id >= 0 && id < viewModel.CurrentProject.Classes.Count)
                {
                    targetLabelNameText.Text = $"（{viewModel.CurrentProject.Classes[id]}）";
                }
                else
                {
                    targetLabelNameText.Text = "";
                }
            };
            targetLabelPanel.Children.Add(targetLabelTextBox);
            targetLabelPanel.Children.Add(targetLabelNameText);
            Grid.SetRow(targetLabelPanel, adjustRow++);
            adjustmentGrid.Children.Add(targetLabelPanel);

            // 图标位置
            var positionLabel = new TextBlock { Text = "图标相对于标签的位置:", Margin = new Thickness(0, 10, 0, 5) };
            Grid.SetRow(positionLabel, adjustRow++);
            adjustmentGrid.Children.Add(positionLabel);

            var positionComboBox = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
            positionComboBox.Items.Add(new ComboBoxItem { Content = "标签上方", Tag = "Above", IsSelected = true });
            positionComboBox.Items.Add(new ComboBoxItem { Content = "标签下方", Tag = "Below" });
            positionComboBox.Items.Add(new ComboBoxItem { Content = "标签左边", Tag = "Left" });
            positionComboBox.Items.Add(new ComboBoxItem { Content = "标签右边", Tag = "Right" });
            Grid.SetRow(positionComboBox, adjustRow++);
            adjustmentGrid.Children.Add(positionComboBox);

            // 调整方式
            var modeLabel = new TextBlock { Text = "调整方式:", Margin = new Thickness(0, 10, 0, 5) };
            Grid.SetRow(modeLabel, adjustRow++);
            adjustmentGrid.Children.Add(modeLabel);

            var modeComboBox = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
            modeComboBox.Items.Add(new ComboBoxItem { Content = "包含图标（扩大框体）", Tag = "Cover", IsSelected = true });
            modeComboBox.Items.Add(new ComboBoxItem { Content = "避开图标（缩小框体）", Tag = "Avoid" });
            Grid.SetRow(modeComboBox, adjustRow++);
            adjustmentGrid.Children.Add(modeComboBox);

            // 更改标签
            var changeLabelCheckBox = new CheckBox
            {
                Content = "同时更改标签类型",
                IsChecked = false,
                Margin = new Thickness(0, 10, 0, 5)
            };
            Grid.SetRow(changeLabelCheckBox, adjustRow++);
            adjustmentGrid.Children.Add(changeLabelCheckBox);

            var newLabelPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Visibility = Visibility.Collapsed
            };
            var newLabelLabel = new TextBlock
            {
                Text = "新标签ID: ",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20, 0, 5, 0)
            };
            var newLabelTextBox = new TextBox
            {
                Text = "1",
                Width = 60,
                Margin = new Thickness(0, 0, 10, 0)
            };
            var newLabelNameText = new TextBlock
            {
                Text = viewModel.CurrentProject?.Classes?.Count > 1 ? $"（{viewModel.CurrentProject.Classes[1]}）" : "",
                VerticalAlignment = VerticalAlignment.Center
            };
            newLabelTextBox.TextChanged += (s, args) =>
            {
                if (int.TryParse(newLabelTextBox.Text, out int id) &&
                    viewModel.CurrentProject?.Classes != null &&
                    id >= 0 && id < viewModel.CurrentProject.Classes.Count)
                {
                    newLabelNameText.Text = $"（{viewModel.CurrentProject.Classes[id]}）";
                }
                else
                {
                    newLabelNameText.Text = "";
                }
            };
            newLabelPanel.Children.Add(newLabelLabel);
            newLabelPanel.Children.Add(newLabelTextBox);
            newLabelPanel.Children.Add(newLabelNameText);
            Grid.SetRow(newLabelPanel, adjustRow++);
            adjustmentGrid.Children.Add(newLabelPanel);

            changeLabelCheckBox.Checked += (s, args) => newLabelPanel.Visibility = Visibility.Visible;
            changeLabelCheckBox.Unchecked += (s, args) => newLabelPanel.Visibility = Visibility.Collapsed;

            // 处理范围
            var processRangeCheckBox = new CheckBox
            {
                Content = "仅处理当前图片",
                IsChecked = false,
                Margin = new Thickness(0, 10, 0, 5)
            };
            Grid.SetRow(processRangeCheckBox, adjustRow++);
            adjustmentGrid.Children.Add(processRangeCheckBox);

            adjustmentGroupBox.Content = adjustmentGrid;
            Grid.SetRow(adjustmentGroupBox, currentRow++);
            grid.Children.Add(adjustmentGroupBox);

            // 说明文本
            var infoText = new TextBlock
            {
                Text = "说明：\n" +
                       "• 使用ONNX模型检测图标位置\n" +
                       "• 根据图标位置自动调整标注框\n" +
                       "• 支持YOLOv5/v8/v10/v11模型\n" +
                       "• 使用GPU加速（DirectML）进行推理\n" +
                       "• 可以预览检测效果后再执行",
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 11
            };
            Grid.SetRow(infoText, currentRow++);
            grid.Children.Add(infoText);

            // 进度条
            var progressPanel = new StackPanel
            {
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var progressLabel = new TextBlock
            {
                Text = "处理进度:",
                Margin = new Thickness(0, 0, 0, 5)
            };
            progressPanel.Children.Add(progressLabel);

            var progressBar = new ProgressBar
            {
                Height = 20,
                Minimum = 0,
                Maximum = 1
            };
            progressPanel.Children.Add(progressBar);

            var progressText = new TextBlock
            {
                Text = "准备中...",
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0)
            };
            progressPanel.Children.Add(progressText);

            Grid.SetRow(progressPanel, currentRow++);
            grid.Children.Add(progressPanel);

            // 按钮面板
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var runButton = new Button
            {
                Content = "开始处理",
                Width = 100,
                IsDefault = true
            };

            runButton.Click += async (s, args) =>
            {
                // 验证输入
                if (string.IsNullOrEmpty(modelTextBox.Text))
                {
                    MessageBox.Show("请选择ONNX模型文件", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!int.TryParse(targetLabelTextBox.Text, out int targetLabelId))
                {
                    MessageBox.Show("请输入有效的目标标签ID", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                int newLabelId = targetLabelId;
                if (changeLabelCheckBox.IsChecked == true)
                {
                    if (!int.TryParse(newLabelTextBox.Text, out newLabelId))
                    {
                        MessageBox.Show("请输入有效的新标签ID", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                // 获取设置
                if (!int.TryParse(inputSizeTextBox.Text, out int inputSize) || inputSize <= 0)
                {
                    inputSize = 640;
                }

                if (!float.TryParse(confidenceTextBox.Text, out float confidence) || confidence <= 0)
                {
                    confidence = 0.5f;
                }

                if (!int.TryParse(iconClassTextBox.Text, out int iconClassId))
                {
                    iconClassId = 0;
                }

                int modelClassCount = -1;
                if (!string.IsNullOrWhiteSpace(classCountTextBox.Text))
                {
                    int.TryParse(classCountTextBox.Text, out modelClassCount);
                }

                var selectedVersion = (versionComboBox.SelectedItem as ComboBoxItem)?.Tag;
                var version = selectedVersion != null ? (YoloVersion)selectedVersion : YoloVersion.V5;

                // 显示进度
                progressPanel.Visibility = Visibility.Visible;
                runButton.IsEnabled = false;

                // 监听进度
                viewModel.PropertyChanged += OnProgressChanged;
                void OnProgressChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
                {
                    if (e.PropertyName == nameof(MainViewModel.InferenceProgress))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            progressBar.Value = viewModel.InferenceProgress;
                            progressText.Text = $"{(viewModel.InferenceProgress * 100):F0}%";
                        });
                    }
                    else if (e.PropertyName == nameof(MainViewModel.StatusMessage))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            progressText.Text = viewModel.StatusMessage;
                        });
                    }
                }

                try
                {
                    // 调用处理方法
                    await viewModel.RunLabelAdjustment(
                        (IconDetectionService.DetectionMethod)((ComboBoxItem)methodComboBox.SelectedItem).Tag,
                        modelTextBox.Text,
                        iconClassId,
                        inputSize,
                        confidence,
                        new Rect(), // 选择区域（当前未使用）
                        targetLabelId,
                        (string)((ComboBoxItem)positionComboBox.SelectedItem).Tag,
                        (string)((ComboBoxItem)modeComboBox.SelectedItem).Tag,
                        changeLabelCheckBox.IsChecked == true,
                        newLabelId,
                        0.7, // 匹配阈值（当前未使用）
                        processRangeCheckBox.IsChecked == true,
                        modelClassCount,
                        version);

                    dialog.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"处理失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    viewModel.PropertyChanged -= OnProgressChanged;
                    progressPanel.Visibility = Visibility.Collapsed;
                    runButton.IsEnabled = true;
                }
            };

            var cancelButton = new Button
            {
                Content = "取消",
                Width = 100,
                IsCancel = true
            };
            cancelButton.Click += (s, args) => dialog.Close();

            buttonPanel.Children.Add(runButton);
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, currentRow++);
            grid.Children.Add(buttonPanel);

            scrollViewer.Content = grid;
            dialog.Content = scrollViewer;
            dialog.ShowDialog();
        }

        private void OnOpenInferenceDialog(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MainViewModel;
            if (viewModel == null) return;

            var dialog = new Window
            {
                Title = "AI推理设置",
                Width = 550,
                Height = 950,  // 增加高度以容纳新选项
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var grid = new Grid();
            grid.Margin = new Thickness(10);

            // 动态添加行定义
            for (int i = 0; i < 22; i++)  // 增加行数
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            int currentRow = 0;

            // ONNX模型选择
            var modelLabel = new TextBlock { Text = "ONNX模型文件:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(modelLabel, currentRow++);
            grid.Children.Add(modelLabel);

            var modelPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var modelTextBox = new TextBox
            {
                Text = viewModel.OnnxModelPath,
                Width = 380,
                IsReadOnly = true,
                Margin = new Thickness(0, 0, 5, 0)
            };
            viewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.OnnxModelPath))
                {
                    modelTextBox.Text = viewModel.OnnxModelPath;
                }
            };

            var browseButton = new Button
            {
                Content = "浏览...",
                Width = 80,
                Command = viewModel.SelectOnnxModelCommand
            };
            modelPanel.Children.Add(modelTextBox);
            modelPanel.Children.Add(browseButton);
            Grid.SetRow(modelPanel, currentRow++);
            grid.Children.Add(modelPanel);

            // 模型版本选择
            var versionLabel = new TextBlock { Text = "模型版本:", Margin = new Thickness(0, 10, 0, 5) };
            Grid.SetRow(versionLabel, currentRow++);
            grid.Children.Add(versionLabel);

            var versionComboBox = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
            versionComboBox.Items.Add(new ComboBoxItem { Content = "YOLOv5", Tag = YoloVersion.V5, IsSelected = true });
            versionComboBox.Items.Add(new ComboBoxItem { Content = "YOLOv8", Tag = YoloVersion.V8 });
            versionComboBox.Items.Add(new ComboBoxItem { Content = "YOLOv10", Tag = YoloVersion.V10 });
            versionComboBox.Items.Add(new ComboBoxItem { Content = "YOLOv11", Tag = YoloVersion.V11 });

            TextBox classCountTextBox = null; // 提前声明
            TextBlock classCountHint = null;

            versionComboBox.SelectionChanged += (s, args) =>
            {
                if (versionComboBox.SelectedItem is ComboBoxItem item)
                {
                    viewModel.ModelVersion = (YoloVersion)item.Tag;

                    // 根据版本显示/隐藏类别数输入
                    if (classCountTextBox != null && classCountHint != null)
                    {
                        if (viewModel.ModelVersion == YoloVersion.V11 || viewModel.ModelVersion == YoloVersion.V10)
                        {
                            classCountTextBox.IsEnabled = true;
                            classCountHint.Text = "YOLOv10/v11建议手动填写类别数";
                        }
                        else
                        {
                            classCountTextBox.IsEnabled = true;
                            classCountHint.Text = "留空则自动检测";
                        }
                    }
                }
            };
            Grid.SetRow(versionComboBox, currentRow++);
            grid.Children.Add(versionComboBox);

            // 模型输入大小
            var sizeLabel = new TextBlock { Text = "模型输入大小:", Margin = new Thickness(0, 10, 0, 5) };
            Grid.SetRow(sizeLabel, currentRow++);
            grid.Children.Add(sizeLabel);

            var sizePanel = new StackPanel { Orientation = Orientation.Horizontal };
            var sizeTextBox = new TextBox
            {
                Text = viewModel.ModelInputSize.ToString(),
                Width = 100,
                Margin = new Thickness(0, 0, 10, 0)
            };
            sizeTextBox.TextChanged += (s, args) =>
            {
                if (int.TryParse(sizeTextBox.Text, out int size) && size > 0 && size <= 2048)
                {
                    viewModel.ModelInputSize = size;
                }
            };

            var sizeComboBox = new ComboBox { Width = 100 };
            sizeComboBox.Items.Add(new ComboBoxItem { Content = "320", Tag = 320 });
            sizeComboBox.Items.Add(new ComboBoxItem { Content = "416", Tag = 416 });
            sizeComboBox.Items.Add(new ComboBoxItem { Content = "640", Tag = 640, IsSelected = true });
            sizeComboBox.Items.Add(new ComboBoxItem { Content = "1280", Tag = 1280 });
            sizeComboBox.SelectionChanged += (s, args) =>
            {
                if (sizeComboBox.SelectedItem is ComboBoxItem item)
                {
                    viewModel.ModelInputSize = (int)item.Tag;
                    sizeTextBox.Text = item.Tag.ToString();
                }
            };

            var commonSizeLabel = new TextBlock
            {
                Text = "常用:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            };

            sizePanel.Children.Add(sizeTextBox);
            sizePanel.Children.Add(commonSizeLabel);
            sizePanel.Children.Add(sizeComboBox);
            Grid.SetRow(sizePanel, currentRow++);
            grid.Children.Add(sizePanel);

            // 模型类别数
            var classCountLabel = new TextBlock { Text = "模型类别数:", Margin = new Thickness(0, 10, 0, 5) };
            Grid.SetRow(classCountLabel, currentRow++);
            grid.Children.Add(classCountLabel);

            var classCountPanel = new StackPanel { Orientation = Orientation.Horizontal };
            classCountTextBox = new TextBox
            {
                Text = viewModel.ModelClassCount > 0 ? viewModel.ModelClassCount.ToString() : "",
                Width = 100,
                Margin = new Thickness(0, 0, 10, 0)
            };
            classCountTextBox.TextChanged += (s, args) =>
            {
                if (string.IsNullOrWhiteSpace(classCountTextBox.Text))
                {
                    viewModel.ModelClassCount = -1;  // 自动检测
                }
                else if (int.TryParse(classCountTextBox.Text, out int count) && count > 0)
                {
                    viewModel.ModelClassCount = count;
                }
            };

            classCountHint = new TextBlock
            {
                Text = "留空则自动检测",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 11
            };

            classCountPanel.Children.Add(classCountTextBox);
            classCountPanel.Children.Add(classCountHint);
            Grid.SetRow(classCountPanel, currentRow++);
            grid.Children.Add(classCountPanel);

            // 置信度阈值
            var confidenceLabel = new TextBlock { Text = "置信度阈值:", Margin = new Thickness(0, 10, 0, 5) };
            Grid.SetRow(confidenceLabel, currentRow++);
            grid.Children.Add(confidenceLabel);

            var confidencePanel = new StackPanel { Orientation = Orientation.Horizontal };
            var confidenceSlider = new Slider
            {
                Minimum = 0.01,
                Maximum = 1.0,
                Value = viewModel.ConfidenceThreshold,
                Width = 200,
                TickFrequency = 0.1,
                IsSnapToTickEnabled = false,
                Margin = new Thickness(0, 0, 10, 0)
            };

            var confidenceTextBox = new TextBox
            {
                Text = viewModel.ConfidenceThreshold.ToString("F2"),
                Width = 60,
                Margin = new Thickness(0, 0, 5, 0)
            };

            confidenceSlider.ValueChanged += (s, args) =>
            {
                viewModel.ConfidenceThreshold = (float)confidenceSlider.Value;
                confidenceTextBox.Text = confidenceSlider.Value.ToString("F2");
            };

            confidenceTextBox.TextChanged += (s, args) =>
            {
                if (float.TryParse(confidenceTextBox.Text, out float value))
                {
                    value = Math.Max(0.01f, Math.Min(1.0f, value));
                    viewModel.ConfidenceThreshold = value;
                    confidenceSlider.Value = value;
                }
            };

            var confidenceHint = new TextBlock
            {
                Text = "(默认0.1，越低检测越多)",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 11
            };

            confidencePanel.Children.Add(confidenceSlider);
            confidencePanel.Children.Add(confidenceTextBox);
            confidencePanel.Children.Add(confidenceHint);
            Grid.SetRow(confidencePanel, currentRow++);
            grid.Children.Add(confidencePanel);

            // 推理目标
            var targetLabel = new TextBlock { Text = "推理目标:", Margin = new Thickness(0, 10, 0, 5) };
            Grid.SetRow(targetLabel, currentRow++);
            grid.Children.Add(targetLabel);

            var targetComboBox = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
            targetComboBox.Items.Add(new ComboBoxItem { Content = "所有图片", Tag = InferenceTarget.All });
            targetComboBox.Items.Add(new ComboBoxItem { Content = "空标记图片", Tag = InferenceTarget.Empty });
            targetComboBox.Items.Add(new ComboBoxItem { Content = "未标记图片", Tag = InferenceTarget.NotAnnotated });

            // 设置初始选中项
            foreach (ComboBoxItem item in targetComboBox.Items)
            {
                if ((InferenceTarget)item.Tag == viewModel.InferenceTarget)
                {
                    item.IsSelected = true;
                    break;
                }
            }

            targetComboBox.SelectionChanged += (s, args) =>
            {
                if (targetComboBox.SelectedItem is ComboBoxItem item)
                {
                    viewModel.InferenceTarget = (InferenceTarget)item.Tag;
                }
            };
            Grid.SetRow(targetComboBox, currentRow++);
            grid.Children.Add(targetComboBox);

            // 标签映射
            var mappingLabel = new TextBlock
            {
                Text = "标签映射 (格式: 模型ID:项目ID，如 0:2,1:3):",
                Margin = new Thickness(0, 10, 0, 5)
            };
            Grid.SetRow(mappingLabel, currentRow++);
            grid.Children.Add(mappingLabel);

            var mappingTextBox = new TextBox
            {
                Text = viewModel.LabelMappings,
                Height = 60,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            mappingTextBox.TextChanged += (s, args) => viewModel.LabelMappings = mappingTextBox.Text;
            Grid.SetRow(mappingTextBox, currentRow++);
            grid.Children.Add(mappingTextBox);

            // ===== 新增的高级选项 =====
            var advancedLabel = new TextBlock
            {
                Text = "高级选项",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 15, 0, 10)
            };
            Grid.SetRow(advancedLabel, currentRow++);
            grid.Children.Add(advancedLabel);

            // 覆盖所有标签选项
            var overwriteCheckBox = new CheckBox
            {
                Content = "覆盖所有标签（清空现有标签后重新添加）",
                IsChecked = viewModel.OverwriteAllLabels,
                Margin = new Thickness(0, 5, 0, 5)
            };
            overwriteCheckBox.Checked += (s, args) => viewModel.OverwriteAllLabels = true;
            overwriteCheckBox.Unchecked += (s, args) => viewModel.OverwriteAllLabels = false;
            Grid.SetRow(overwriteCheckBox, currentRow++);
            grid.Children.Add(overwriteCheckBox);

            // 移除低置信度重叠框选项
            var removeOverlapCheckBox = new CheckBox
            {
                Content = "移除低置信度重叠框",
                IsChecked = viewModel.RemoveOverlappingLowConfidence,
                Margin = new Thickness(0, 5, 0, 5)
            };

            // 置信度阈值输入
            var overlapThresholdPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(20, 5, 0, 5),
                Visibility = viewModel.RemoveOverlappingLowConfidence ? Visibility.Visible : Visibility.Collapsed
            };

            var overlapThresholdLabel = new TextBlock
            {
                Text = "重叠框置信度阈值:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };

            var overlapThresholdSlider = new Slider
            {
                Minimum = 0.01,
                Maximum = 1.0,
                Value = viewModel.OverlapRemovalThreshold,
                Width = 150,
                TickFrequency = 0.1,
                IsSnapToTickEnabled = false,
                Margin = new Thickness(0, 0, 10, 0)
            };

            var overlapThresholdTextBox = new TextBox
            {
                Text = viewModel.OverlapRemovalThreshold.ToString("F2"),
                Width = 60,
                Margin = new Thickness(0, 0, 5, 0)
            };

            overlapThresholdSlider.ValueChanged += (s, args) =>
            {
                viewModel.OverlapRemovalThreshold = (float)overlapThresholdSlider.Value;
                overlapThresholdTextBox.Text = overlapThresholdSlider.Value.ToString("F2");
            };

            overlapThresholdTextBox.TextChanged += (s, args) =>
            {
                if (float.TryParse(overlapThresholdTextBox.Text, out float value))
                {
                    value = Math.Max(0.01f, Math.Min(1.0f, value));
                    viewModel.OverlapRemovalThreshold = value;
                    overlapThresholdSlider.Value = value;
                }
            };

            var overlapHint = new TextBlock
            {
                Text = "(低于此值的重叠框将被移除)",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 11
            };

            overlapThresholdPanel.Children.Add(overlapThresholdLabel);
            overlapThresholdPanel.Children.Add(overlapThresholdSlider);
            overlapThresholdPanel.Children.Add(overlapThresholdTextBox);
            overlapThresholdPanel.Children.Add(overlapHint);

            removeOverlapCheckBox.Checked += (s, args) =>
            {
                viewModel.RemoveOverlappingLowConfidence = true;
                overlapThresholdPanel.Visibility = Visibility.Visible;
            };
            removeOverlapCheckBox.Unchecked += (s, args) =>
            {
                viewModel.RemoveOverlappingLowConfidence = false;
                overlapThresholdPanel.Visibility = Visibility.Collapsed;
            };

            Grid.SetRow(removeOverlapCheckBox, currentRow++);
            grid.Children.Add(removeOverlapCheckBox);
            Grid.SetRow(overlapThresholdPanel, currentRow++);
            grid.Children.Add(overlapThresholdPanel);

            // 说明文本
            var infoText = new TextBlock
            {
                Text = "说明：\n" +
                       "• 选择正确的模型版本以确保正确解析输出\n" +
                       "• YOLOv5/v8: 输出格式 [1, num_anchors, 5+nc]\n" +
                       "• YOLOv10: 可能是端到端格式 [1, num_detections, 6]\n" +
                       "• YOLOv11: 输出格式 [1, nc+4, 8400]\n" +
                       "• 使用DirectML进行GPU加速推理（如不支持将自动使用CPU）\n" +
                       "• 标签映射用于将模型输出类别映射到项目类别\n" +
                       "• 只有映射中指定的类别会被添加到标注中\n" +
                       "• 推理会自动忽略重复的标注（IOU > 0.5）\n" +
                       "• 覆盖模式：清空所有现有标签后重新添加AI检测的标签\n" +
                       "• 移除低置信度：当多个框重叠时，保留高置信度的框",
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 11
            };
            Grid.SetRow(infoText, currentRow++);
            grid.Children.Add(infoText);

            // 进度条
            var progressPanel = new StackPanel
            {
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var progressLabel = new TextBlock
            {
                Text = "推理进度:",
                Margin = new Thickness(0, 0, 0, 5)
            };
            progressPanel.Children.Add(progressLabel);

            var progressBar = new ProgressBar
            {
                Height = 20,
                Minimum = 0,
                Maximum = 1
            };
            progressPanel.Children.Add(progressBar);

            var progressText = new TextBlock
            {
                Text = "准备中...",
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0)
            };
            progressPanel.Children.Add(progressText);

            Grid.SetRow(progressPanel, currentRow++);
            grid.Children.Add(progressPanel);

            // 按钮面板
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var runButton = new Button
            {
                Content = "开始推理",
                Width = 100,
                IsDefault = true,
                Command = viewModel.RunInferenceCommand
            };

            // 监听推理进度
            viewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.InferenceProgress))
                {
                    Dispatcher.Invoke(() =>
                    {
                        progressBar.Value = viewModel.InferenceProgress;
                        progressText.Text = $"{(viewModel.InferenceProgress * 100):F0}%";
                    });
                }
                else if (args.PropertyName == nameof(MainViewModel.IsLoading))
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (viewModel.IsLoading)
                        {
                            progressPanel.Visibility = Visibility.Visible;
                            runButton.IsEnabled = false;
                        }
                        else
                        {
                            progressPanel.Visibility = Visibility.Collapsed;
                            runButton.IsEnabled = true;
                            // 推理完成后关闭对话框
                            if (progressBar.Value >= 0.99)
                            {
                                dialog.Close();
                            }
                        }
                    });
                }
                else if (args.PropertyName == nameof(MainViewModel.StatusMessage))
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (viewModel.IsLoading && !string.IsNullOrEmpty(viewModel.StatusMessage))
                        {
                            progressText.Text = viewModel.StatusMessage;
                        }
                    });
                }
            };

            var cancelButton = new Button
            {
                Content = "取消",
                Width = 100,
                IsCancel = true
            };
            cancelButton.Click += (s, args) => dialog.Close();

            buttonPanel.Children.Add(runButton);
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, currentRow++);
            grid.Children.Add(buttonPanel);

            scrollViewer.Content = grid;
            dialog.Content = scrollViewer;
            dialog.ShowDialog();
        }
    }
}
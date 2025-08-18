using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Annotation = BryanYOLO.Models.Annotation;
using BryanYOLO.ViewModels;

namespace BryanYOLO.Controls
{
    public class ImageCanvas : Canvas
    {
        private enum EditMode
        {
            None,
            Drawing,
            Moving,
            Resizing
        }

        private EditMode _currentMode = EditMode.None;
        private Rectangle _currentRectangle;
        private Rectangle _previewRectangle;
        private Annotation _currentAnnotation;
        private Annotation _editingAnnotation;
        private Point _startPoint;
        private Point _lastPoint;
        private ResizeHandle _activeHandle;

        // 保存原始状态用于取消操作
        private double _originalLeft;
        private double _originalTop;
        private double _originalWidth;
        private double _originalHeight;

        // 十字线
        private Line _crosshairH;
        private Line _crosshairV;
        private bool _showCrosshair = true;

        // 记录上次使用的类别ID，用于右键切换
        private int? _lastUsedClassId = null;
        // 防止递归更新的标志
        private bool _isUpdatingClassId = false;

        public static readonly DependencyProperty AnnotationsProperty =
            DependencyProperty.Register(nameof(Annotations), typeof(ObservableCollection<Annotation>),
                typeof(ImageCanvas), new PropertyMetadata(null, OnAnnotationsChanged));

        public static readonly DependencyProperty ClassColorsProperty =
            DependencyProperty.Register(nameof(ClassColors), typeof(System.Collections.Generic.Dictionary<int, Color>),
                typeof(ImageCanvas), new PropertyMetadata(null, OnClassColorsChanged));

        public static readonly DependencyProperty SelectedClassIdProperty =
            DependencyProperty.Register(nameof(SelectedClassId), typeof(int),
                typeof(ImageCanvas), new PropertyMetadata(0, OnSelectedClassIdChanged));

        public static readonly DependencyProperty ImageWidthProperty =
            DependencyProperty.Register(nameof(ImageWidth), typeof(double),
                typeof(ImageCanvas), new PropertyMetadata(0.0, OnImageSizeChanged));

        public static readonly DependencyProperty ImageHeightProperty =
            DependencyProperty.Register(nameof(ImageHeight), typeof(double),
                typeof(ImageCanvas), new PropertyMetadata(0.0, OnImageSizeChanged));

        public static readonly DependencyProperty CurrentZoomLevelProperty =
            DependencyProperty.Register(nameof(CurrentZoomLevel), typeof(double),
                typeof(ImageCanvas), new PropertyMetadata(1.0, OnZoomChanged));

        public ObservableCollection<Annotation> Annotations
        {
            get => (ObservableCollection<Annotation>)GetValue(AnnotationsProperty);
            set => SetValue(AnnotationsProperty, value);
        }

        public System.Collections.Generic.Dictionary<int, Color> ClassColors
        {
            get => (System.Collections.Generic.Dictionary<int, Color>)GetValue(ClassColorsProperty);
            set => SetValue(ClassColorsProperty, value);
        }

        public int SelectedClassId
        {
            get => (int)GetValue(SelectedClassIdProperty);
            set => SetValue(SelectedClassIdProperty, value);
        }

        public double ImageWidth
        {
            get => (double)GetValue(ImageWidthProperty);
            set => SetValue(ImageWidthProperty, value);
        }

        public double ImageHeight
        {
            get => (double)GetValue(ImageHeightProperty);
            set => SetValue(ImageHeightProperty, value);
        }

        public double CurrentZoomLevel
        {
            get => (double)GetValue(CurrentZoomLevelProperty);
            set => SetValue(CurrentZoomLevelProperty, value);
        }

        public ImageCanvas()
        {
            Background = Brushes.Transparent;
            ClipToBounds = true;

            // 创建十字线
            InitializeCrosshair();

            // 注册事件
            MouseEnter += OnMouseEnter;
            MouseLeave += OnMouseLeave;
            MouseMove += OnMouseMove;
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseLeftButtonUp += OnMouseLeftButtonUp;
            MouseRightButtonDown += OnMouseRightButtonDown;
            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;

            // 使用 PreviewKeyDown 来确保捕获小键盘数字键
            PreviewKeyDown += OnCanvasPreviewKeyDown;

            // 设置可接收键盘焦点
            Focusable = true;
        }

        // 专门处理小键盘数字键的方法
        private void OnCanvasPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 如果当前没有加载项目或类别，直接返回
            var viewModel = GetViewModel(this);
            if (viewModel?.CurrentProject?.Classes == null || viewModel.CurrentProject.Classes.Count == 0)
            {
                return;
            }

            // 处理小键盘数字键切换类别
            if (TryHandleNumpadKey(e.Key))
            {
                e.Handled = true;
                return;
            }

            // 处理主键盘数字键（需要Alt键）
            if (TryHandleNumberKey(e.Key))
            {
                e.Handled = true;
                return;
            }
        }

        // 处理小键盘数字键
        private bool TryHandleNumpadKey(Key key)
        {
            int classId = -1;

            switch (key)
            {
                case Key.NumPad0:
                    classId = 0;
                    break;
                case Key.NumPad1:
                    classId = 1;
                    break;
                case Key.NumPad2:
                    classId = 2;
                    break;
                case Key.NumPad3:
                    classId = 3;
                    break;
                case Key.NumPad4:
                    classId = 4;
                    break;
                case Key.NumPad5:
                    classId = 5;
                    break;
                case Key.NumPad6:
                    classId = 6;
                    break;
                case Key.NumPad7:
                    classId = 7;
                    break;
                case Key.NumPad8:
                    classId = 8;
                    break;
                case Key.NumPad9:
                    classId = 9;
                    break;
                default:
                    return false;
            }

            if (classId >= 0)
            {
                SwitchToClass(classId);
                return true;
            }

            return false;
        }

        // 处理主键盘数字键（可选功能）
        private bool TryHandleNumberKey(Key key)
        {
            // 如果不需要主键盘数字键，可以注释掉这个方法的内容
            // 或者检查是否按住了某个修饰键（如Alt）
            if (Keyboard.Modifiers != ModifierKeys.Alt)
                return false;

            int classId = -1;

            switch (key)
            {
                case Key.D1:
                    classId = 0;
                    break;
                case Key.D2:
                    classId = 1;
                    break;
                case Key.D3:
                    classId = 2;
                    break;
                case Key.D4:
                    classId = 3;
                    break;
                case Key.D5:
                    classId = 4;
                    break;
                case Key.D6:
                    classId = 5;
                    break;
                case Key.D7:
                    classId = 6;
                    break;
                case Key.D8:
                    classId = 7;
                    break;
                case Key.D9:
                    classId = 8;
                    break;
                case Key.D0:
                    classId = 9;
                    break;
                default:
                    return false;
            }

            if (classId >= 0)
            {
                SwitchToClass(classId);
                return true;
            }

            return false;
        }

        // 切换到指定类别的统一方法
        private void SwitchToClass(int classId)
        {
            try
            {
                var viewModel = GetViewModel(this);
                if (viewModel?.CurrentProject?.Classes != null)
                {
                    // 检查该类别是否存在
                    if (classId >= 0 && classId < viewModel.CurrentProject.Classes.Count)
                    {
                        // 避免重复设置相同的值
                        if (SelectedClassId == classId)
                        {
                            return;
                        }

                        // 保存当前类别为上次使用的
                        _lastUsedClassId = SelectedClassId;

                        // 切换到新类别 - 只设置一次，避免循环
                        _isUpdatingClassId = true;  // 添加标志防止递归
                        try
                        {
                            SelectedClassId = classId;
                            if (viewModel.SelectedClassId != classId)
                            {
                                viewModel.SelectedClassId = classId;
                            }
                        }
                        finally
                        {
                            _isUpdatingClassId = false;
                        }

                        // 显示切换信息
                        var className = viewModel.CurrentProject.Classes[classId];
                        viewModel.StatusMessage = $"切换到类别: {className} (ID: {classId})";

                        // 触发重绘以更新颜色
                        RedrawAnnotations();
                    }
                    else
                    {
                        viewModel.StatusMessage = $"类别 {classId} 不存在";
                    }
                }
            }
            catch (Exception ex)
            {
                // 捕获异常避免程序崩溃
                System.Diagnostics.Debug.WriteLine($"切换类别时出错: {ex.Message}");
            }
        }

        // 获取 ViewModel 的辅助方法
        private static MainViewModel GetViewModel(DependencyObject obj)
        {
            try
            {
                // 尝试直接获取
                var viewModel = (obj as FrameworkElement)?.DataContext as MainViewModel;

                // 如果失败，尝试从 Window 获取
                if (viewModel == null)
                {
                    var window = Window.GetWindow(obj);
                    viewModel = window?.DataContext as MainViewModel;
                }

                return viewModel;
            }
            catch
            {
                return null;
            }
        }

        // 处理SelectedClassId变化
        private static void OnSelectedClassIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var canvas = d as ImageCanvas;
            if (canvas != null && e.NewValue is int newClassId)
            {
                // 如果正在内部更新，不处理
                if (canvas._isUpdatingClassId)
                {
                    return;
                }

                // 保存上次使用的类别ID（如果不是同一个）
                if (e.OldValue is int oldClassId && oldClassId != newClassId)
                {
                    canvas._lastUsedClassId = oldClassId;
                }
            }
        }

        private void InitializeCrosshair()
        {
            _crosshairH = new Line
            {
                Stroke = Brushes.LimeGreen,
                StrokeThickness = 0.5,
                Opacity = 0.7,
                IsHitTestVisible = false,
                StrokeDashArray = new DoubleCollection { 2, 2 }
            };

            _crosshairV = new Line
            {
                Stroke = Brushes.LimeGreen,
                StrokeThickness = 0.5,
                Opacity = 0.7,
                IsHitTestVisible = false,
                StrokeDashArray = new DoubleCollection { 2, 2 }
            };
        }

        private void OnMouseEnter(object sender, MouseEventArgs e)
        {
            if (_currentMode == EditMode.None)
            {
                ShowCrosshair();
                Cursor = Cursors.Cross;
            }

            // 获取键盘焦点
            Focus();
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            if (_currentMode == EditMode.None)
            {
                HideCrosshair();
                Cursor = Cursors.Arrow;
            }
        }

        private void ShowCrosshair()
        {
            if (_showCrosshair && !Children.Contains(_crosshairH))
            {
                Children.Add(_crosshairH);
                Children.Add(_crosshairV);
            }
        }

        private void HideCrosshair()
        {
            if (Children.Contains(_crosshairH))
            {
                Children.Remove(_crosshairH);
                Children.Remove(_crosshairV);
            }
        }

        private void UpdateCrosshair(Point position)
        {
            if (_showCrosshair && Children.Contains(_crosshairH))
            {
                // 水平线
                _crosshairH.X1 = 0;
                _crosshairH.Y1 = position.Y;
                _crosshairH.X2 = ImageWidth;
                _crosshairH.Y2 = position.Y;

                // 垂直线
                _crosshairV.X1 = position.X;
                _crosshairV.Y1 = 0;
                _crosshairV.X2 = position.X;
                _crosshairV.Y2 = ImageHeight;
            }
        }

        private static void OnAnnotationsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var canvas = d as ImageCanvas;
            if (canvas != null)
            {
                // 取消订阅旧集合的事件
                if (e.OldValue is ObservableCollection<Annotation> oldCollection)
                {
                    oldCollection.CollectionChanged -= canvas.OnAnnotationsCollectionChanged;
                    foreach (var annotation in oldCollection)
                    {
                        annotation.PropertyChanged -= canvas.OnAnnotationPropertyChanged;
                    }
                }

                // 订阅新集合的事件
                if (e.NewValue is ObservableCollection<Annotation> newCollection)
                {
                    newCollection.CollectionChanged += canvas.OnAnnotationsCollectionChanged;
                    foreach (var annotation in newCollection)
                    {
                        annotation.PropertyChanged += canvas.OnAnnotationPropertyChanged;
                    }
                }

                // 立即重绘
                canvas.RedrawAnnotations();
            }
        }

        private void OnAnnotationsCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // 处理集合变化时的属性变化事件订阅
            if (e.OldItems != null)
            {
                foreach (Annotation annotation in e.OldItems)
                {
                    annotation.PropertyChanged -= OnAnnotationPropertyChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (Annotation annotation in e.NewItems)
                {
                    annotation.PropertyChanged += OnAnnotationPropertyChanged;
                }
            }

            // 重绘
            RedrawAnnotations();
        }

        private void OnAnnotationPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // 当标注的属性变化时重绘
            if (e.PropertyName == "ClassId" || e.PropertyName == "IsSelected")
            {
                RedrawAnnotations();
            }
        }

        private static void OnClassColorsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var canvas = d as ImageCanvas;
            canvas?.RedrawAnnotations();
        }

        private static void OnImageSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var canvas = d as ImageCanvas;
            if (canvas != null)
            {
                // 更新Canvas的实际大小
                canvas.Width = canvas.ImageWidth;
                canvas.Height = canvas.ImageHeight;
                canvas.RedrawAnnotations();
            }
        }

        private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var canvas = d as ImageCanvas;
            canvas?.RedrawAnnotations();
        }

        public void RedrawAnnotations()
        {
            // 如果还没有设置尺寸，先返回
            if (ImageWidth <= 0 || ImageHeight <= 0)
                return;

            // 清除所有除了十字线和预览矩形的元素
            var itemsToRemove = Children.Cast<UIElement>()
                .Where(c => c != _crosshairH && c != _crosshairV && c != _previewRectangle)
                .ToList();

            foreach (var item in itemsToRemove)
            {
                Children.Remove(item);
            }

            if (Annotations == null)
                return;

            // 确保所有标注都有正确的像素坐标
            foreach (var annotation in Annotations)
            {
                // 如果像素坐标未初始化，更新它们
                if (annotation.PixelWidth <= 0 || annotation.PixelHeight <= 0)
                {
                    annotation.UpdatePixelCoordinates(ImageWidth, ImageHeight);
                }
                DrawAnnotation(annotation);
            }

            // 确保十字线和预览矩形在最上层
            if (Children.Contains(_crosshairH))
            {
                Children.Remove(_crosshairH);
                Children.Remove(_crosshairV);
                Children.Add(_crosshairH);
                Children.Add(_crosshairV);
            }

            if (_previewRectangle != null && Children.Contains(_previewRectangle))
            {
                Children.Remove(_previewRectangle);
                Children.Add(_previewRectangle);
            }
        }

        private void DrawAnnotation(Annotation annotation)
        {
            // 如果在编辑模式中且不是当前编辑的标注，降低透明度
            bool isDimmed = (_currentMode == EditMode.Moving || _currentMode == EditMode.Resizing)
                            && annotation != _editingAnnotation;

            // 确保像素坐标是最新的
            annotation.UpdatePixelCoordinates(ImageWidth, ImageHeight);

            var color = GetColorForClass(annotation.ClassId);

            // 根据缩放级别调整线条粗细
            double baseThickness = annotation.IsSelected ? 3 : 2;
            double scaledThickness = baseThickness / CurrentZoomLevel;
            scaledThickness = Math.Max(1, Math.Min(5, scaledThickness)); // 限制在1-5之间

            var rect = new Rectangle
            {
                Width = annotation.PixelWidth,
                Height = annotation.PixelHeight,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = scaledThickness,
                Fill = new SolidColorBrush(color) { Opacity = isDimmed ? 0.05 : 0.1 },
                Tag = annotation,
                IsHitTestVisible = false, // 禁用框体的点击选择
                Opacity = isDimmed ? 0.3 : 1.0  // 编辑时降低其他标注的不透明度
            };

            if (annotation.IsSelected)
            {
                rect.StrokeDashArray = new DoubleCollection { 5, 2 };
                rect.Fill = new SolidColorBrush(color) { Opacity = isDimmed ? 0.1 : 0.2 };
                Canvas.SetZIndex(rect, 100);
            }

            Canvas.SetLeft(rect, annotation.PixelLeft);
            Canvas.SetTop(rect, annotation.PixelTop);

            Children.Add(rect);

            // 如果选中且不在编辑模式，添加调整手柄
            if (annotation.IsSelected && !isDimmed)
            {
                AddResizeHandles(annotation);
            }
        }

        private Color GetColorForClass(int classId)
        {
            if (ClassColors != null && ClassColors.ContainsKey(classId))
            {
                return ClassColors[classId];
            }
            return Colors.Yellow;
        }

        private void AddResizeHandles(Annotation annotation)
        {
            if (annotation == null || !annotation.IsSelected) return;

            // 确保标注的像素坐标是最新的
            if (annotation.PixelWidth <= 0 || annotation.PixelHeight <= 0)
            {
                annotation.UpdatePixelCoordinates(ImageWidth, ImageHeight);
            }

            var handles = new[]
            {
                new ResizeHandle(annotation.PixelLeft, annotation.PixelTop, ResizeDirection.TopLeft, annotation),
                new ResizeHandle(annotation.PixelLeft + annotation.PixelWidth, annotation.PixelTop, ResizeDirection.TopRight, annotation),
                new ResizeHandle(annotation.PixelLeft, annotation.PixelTop + annotation.PixelHeight, ResizeDirection.BottomLeft, annotation),
                new ResizeHandle(annotation.PixelLeft + annotation.PixelWidth, annotation.PixelTop + annotation.PixelHeight, ResizeDirection.BottomRight, annotation),
                new ResizeHandle(annotation.PixelLeft + annotation.PixelWidth / 2, annotation.PixelTop, ResizeDirection.Top, annotation),
                new ResizeHandle(annotation.PixelLeft + annotation.PixelWidth, annotation.PixelTop + annotation.PixelHeight / 2, ResizeDirection.Right, annotation),
                new ResizeHandle(annotation.PixelLeft + annotation.PixelWidth / 2, annotation.PixelTop + annotation.PixelHeight, ResizeDirection.Bottom, annotation),
                new ResizeHandle(annotation.PixelLeft, annotation.PixelTop + annotation.PixelHeight / 2, ResizeDirection.Left, annotation)
            };

            // 根据缩放级别调整手柄大小
            double handleSize = Math.Max(6, 8 / CurrentZoomLevel);
            handleSize = Math.Min(12, handleSize);

            foreach (var handle in handles)
            {
                var handleRect = new Rectangle
                {
                    Width = handleSize,
                    Height = handleSize,
                    Fill = Brushes.White,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1,
                    Tag = handle,
                    Cursor = GetCursorForDirection(handle.Direction)
                };

                Canvas.SetLeft(handleRect, handle.X - handleSize / 2);
                Canvas.SetTop(handleRect, handle.Y - handleSize / 2);
                Canvas.SetZIndex(handleRect, 1000);
                Children.Add(handleRect);
            }
        }

        private Cursor GetCursorForDirection(ResizeDirection direction)
        {
            switch (direction)
            {
                case ResizeDirection.TopLeft:
                case ResizeDirection.BottomRight:
                    return Cursors.SizeNWSE;
                case ResizeDirection.TopRight:
                case ResizeDirection.BottomLeft:
                    return Cursors.SizeNESW;
                case ResizeDirection.Top:
                case ResizeDirection.Bottom:
                    return Cursors.SizeNS;
                case ResizeDirection.Left:
                case ResizeDirection.Right:
                    return Cursors.SizeWE;
                default:
                    return Cursors.Arrow;
            }
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(this);
            _lastPoint = _startPoint;

            var hitTest = VisualTreeHelper.HitTest(this, _startPoint);
            if (hitTest?.VisualHit is FrameworkElement element)
            {
                if (element.Tag is ResizeHandle handle)
                {
                    // 开始调整大小
                    _currentMode = EditMode.Resizing;
                    _activeHandle = handle;
                    _editingAnnotation = handle.AssociatedAnnotation;

                    // 保存原始状态
                    _originalLeft = _editingAnnotation.PixelLeft;
                    _originalTop = _editingAnnotation.PixelTop;
                    _originalWidth = _editingAnnotation.PixelWidth;
                    _originalHeight = _editingAnnotation.PixelHeight;

                    HideCrosshair();
                    // 重绘以显示半透明效果
                    RedrawAnnotations();
                    CaptureMouse();
                }
                else
                {
                    // 直接开始绘制新标注
                    StartDrawing();
                }
            }
            else
            {
                // 开始绘制新标注
                StartDrawing();
            }

            e.Handled = true;
        }

        private void StartDrawing()
        {
            _currentMode = EditMode.Drawing;
            HideCrosshair();
            Cursor = Cursors.Cross;

            // 清除其他选中状态
            if (Annotations != null)
            {
                foreach (var ann in Annotations)
                {
                    ann.IsSelected = false;
                }
            }

            // 将所有现有标注设置为半透明
            SetAnnotationsOpacity(0.2);

            // 创建预览矩形
            var color = GetColorForClass(SelectedClassId);

            // 根据缩放级别调整预览框的线条粗细
            double scaledThickness = 2.0 / CurrentZoomLevel;
            scaledThickness = Math.Max(1, Math.Min(3, scaledThickness));

            _previewRectangle = new Rectangle
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = scaledThickness,
                StrokeDashArray = new DoubleCollection { 5, 2 },
                Fill = new SolidColorBrush(color) { Opacity = 0.2 }
            };

            Canvas.SetLeft(_previewRectangle, _startPoint.X);
            Canvas.SetTop(_previewRectangle, _startPoint.Y);
            Children.Add(_previewRectangle);

            CaptureMouse();
        }

        private void SetAnnotationsOpacity(double opacity)
        {
            foreach (var child in Children)
            {
                if (child is Rectangle rect && rect != _previewRectangle && !(rect.Tag is ResizeHandle))
                {
                    rect.Opacity = opacity;
                }
                else if (child is Border border)
                {
                    border.Opacity = opacity;
                }
                else if (child is Canvas canvas)
                {
                    canvas.Opacity = opacity;
                }
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var currentPoint = e.GetPosition(this);

            // 限制在画布范围内
            currentPoint.X = Math.Max(0, Math.Min(ImageWidth, currentPoint.X));
            currentPoint.Y = Math.Max(0, Math.Min(ImageHeight, currentPoint.Y));

            // 更新十字线
            if (_currentMode == EditMode.None && _showCrosshair)
            {
                UpdateCrosshair(currentPoint);

                // 检查是否悬停在调整手柄上
                var hitTest = VisualTreeHelper.HitTest(this, currentPoint);
                if (hitTest?.VisualHit is FrameworkElement element)
                {
                    if (element.Tag is ResizeHandle handle)
                    {
                        Cursor = GetCursorForDirection(handle.Direction);
                    }
                    else
                    {
                        Cursor = Cursors.Cross;
                    }
                }
                else
                {
                    Cursor = Cursors.Cross;
                }
            }

            if (!IsMouseCaptured) return;

            switch (_currentMode)
            {
                case EditMode.Drawing:
                    UpdateDrawing(currentPoint);
                    break;
                case EditMode.Moving:
                    MoveAnnotation(currentPoint);
                    break;
                case EditMode.Resizing:
                    ResizeAnnotation(currentPoint);
                    break;
            }

            _lastPoint = currentPoint;
        }

        private void UpdateDrawing(Point currentPoint)
        {
            if (_previewRectangle == null) return;

            var x = Math.Min(currentPoint.X, _startPoint.X);
            var y = Math.Min(currentPoint.Y, _startPoint.Y);
            var width = Math.Abs(currentPoint.X - _startPoint.X);
            var height = Math.Abs(currentPoint.Y - _startPoint.Y);

            Canvas.SetLeft(_previewRectangle, x);
            Canvas.SetTop(_previewRectangle, y);
            _previewRectangle.Width = width;
            _previewRectangle.Height = height;
        }

        private void MoveAnnotation(Point currentPoint)
        {
            if (_editingAnnotation == null) return;

            var deltaX = currentPoint.X - _lastPoint.X;
            var deltaY = currentPoint.Y - _lastPoint.Y;

            // 计算新位置
            var newLeft = _editingAnnotation.PixelLeft + deltaX;
            var newTop = _editingAnnotation.PixelTop + deltaY;

            // 限制移动范围
            newLeft = Math.Max(0, Math.Min(ImageWidth - _editingAnnotation.PixelWidth, newLeft));
            newTop = Math.Max(0, Math.Min(ImageHeight - _editingAnnotation.PixelHeight, newTop));

            // 更新标注的像素坐标
            _editingAnnotation.PixelLeft = newLeft;
            _editingAnnotation.PixelTop = newTop;

            // 更新YOLO格式的归一化坐标
            _editingAnnotation.UpdateFromPixels(
                _editingAnnotation.PixelLeft,
                _editingAnnotation.PixelTop,
                _editingAnnotation.PixelWidth,
                _editingAnnotation.PixelHeight,
                ImageWidth, ImageHeight);

            // 实时更新显示
            UpdateAnnotationDisplay(_editingAnnotation);
        }

        private void ResizeAnnotation(Point currentPoint)
        {
            if (_editingAnnotation == null || _activeHandle == null) return;

            // 获取当前标注的边界
            var left = _editingAnnotation.PixelLeft;
            var top = _editingAnnotation.PixelTop;
            var right = left + _editingAnnotation.PixelWidth;
            var bottom = top + _editingAnnotation.PixelHeight;

            // 根据拖动的手柄方向调整边界
            switch (_activeHandle.Direction)
            {
                case ResizeDirection.TopLeft:
                    left = Math.Min(currentPoint.X, right - 10);
                    top = Math.Min(currentPoint.Y, bottom - 10);
                    break;
                case ResizeDirection.TopRight:
                    right = Math.Max(currentPoint.X, left + 10);
                    top = Math.Min(currentPoint.Y, bottom - 10);
                    break;
                case ResizeDirection.BottomLeft:
                    left = Math.Min(currentPoint.X, right - 10);
                    bottom = Math.Max(currentPoint.Y, top + 10);
                    break;
                case ResizeDirection.BottomRight:
                    right = Math.Max(currentPoint.X, left + 10);
                    bottom = Math.Max(currentPoint.Y, top + 10);
                    break;
                case ResizeDirection.Top:
                    top = Math.Min(currentPoint.Y, bottom - 10);
                    break;
                case ResizeDirection.Bottom:
                    bottom = Math.Max(currentPoint.Y, top + 10);
                    break;
                case ResizeDirection.Left:
                    left = Math.Min(currentPoint.X, right - 10);
                    break;
                case ResizeDirection.Right:
                    right = Math.Max(currentPoint.X, left + 10);
                    break;
            }

            // 限制在画布范围内
            left = Math.Max(0, left);
            top = Math.Max(0, top);
            right = Math.Min(ImageWidth, right);
            bottom = Math.Min(ImageHeight, bottom);

            // 计算新的位置和尺寸
            var newLeft = left;
            var newTop = top;
            var newWidth = right - left;
            var newHeight = bottom - top;

            // 更新标注坐标
            _editingAnnotation.PixelLeft = newLeft;
            _editingAnnotation.PixelTop = newTop;
            _editingAnnotation.PixelWidth = newWidth;
            _editingAnnotation.PixelHeight = newHeight;

            // 更新归一化坐标
            _editingAnnotation.UpdateFromPixels(newLeft, newTop, newWidth, newHeight, ImageWidth, ImageHeight);

            // 实时更新显示
            UpdateAnnotationDisplay(_editingAnnotation);
        }

        private void UpdateAnnotationDisplay(Annotation annotation)
        {
            // 找到对应的矩形并更新
            foreach (var child in Children)
            {
                if (child is Rectangle rect && rect.Tag == annotation)
                {
                    Canvas.SetLeft(rect, annotation.PixelLeft);
                    Canvas.SetTop(rect, annotation.PixelTop);
                    rect.Width = annotation.PixelWidth;
                    rect.Height = annotation.PixelHeight;
                }
            }

            // 更新调整手柄
            if (annotation.IsSelected)
            {
                UpdateResizeHandles(annotation);
            }
        }

        private void UpdateResizeHandles(Annotation annotation)
        {
            // 移除旧的调整手柄
            var handlesToRemove = Children.OfType<Rectangle>()
                .Where(r => r.Tag is ResizeHandle h && h.AssociatedAnnotation == annotation)
                .ToList();

            foreach (var handle in handlesToRemove)
            {
                Children.Remove(handle);
            }

            // 添加新的调整手柄
            AddResizeHandles(annotation);
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_currentMode == EditMode.Drawing && _previewRectangle != null)
            {
                // 完成绘制
                if (_previewRectangle.Width > 5 && _previewRectangle.Height > 5)
                {
                    var newAnnotation = new Annotation
                    {
                        ClassId = SelectedClassId,
                        IsSelected = true
                    };

                    newAnnotation.UpdateFromPixels(
                        Canvas.GetLeft(_previewRectangle),
                        Canvas.GetTop(_previewRectangle),
                        _previewRectangle.Width,
                        _previewRectangle.Height,
                        ImageWidth, ImageHeight);

                    if (Annotations != null)
                    {
                        // 取消其他选中
                        foreach (var ann in Annotations)
                        {
                            ann.IsSelected = false;
                        }

                        Annotations.Add(newAnnotation);

                        // 标记图片已修改
                        var viewModel = GetViewModel(this);
                        if (viewModel?.CurrentImage != null)
                        {
                            viewModel.CurrentImage.IsModified = true;
                        }
                    }
                }

                Children.Remove(_previewRectangle);
                _previewRectangle = null;
            }
            else if (_currentMode == EditMode.Moving || _currentMode == EditMode.Resizing)
            {
                // 标记图片已修改
                var viewModel = GetViewModel(this);
                if (viewModel?.CurrentImage != null)
                {
                    viewModel.CurrentImage.IsModified = true;
                }
            }

            _currentMode = EditMode.None;
            _activeHandle = null;
            _editingAnnotation = null;
            ReleaseMouseCapture();

            // 恢复显示所有标注
            RedrawAnnotations();

            // 恢复十字线
            if (IsMouseOver)
            {
                ShowCrosshair();
                Cursor = Cursors.Cross;
            }
        }

        private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var point = e.GetPosition(this);

            // 首先检查是否点击在标注框上（用于删除）
            var annotation = GetAnnotationAtPoint(point);
            if (annotation != null && Annotations != null)
            {
                // 如果点击在标注框上，执行删除
                Annotations.Remove(annotation);

                // 标记图片已修改
                var viewModel = GetViewModel(this);
                if (viewModel?.CurrentImage != null)
                {
                    viewModel.CurrentImage.IsModified = true;
                }

                RedrawAnnotations();
                e.Handled = true;
                return;
            }

            // 如果没有点击在标注框上，执行右键切换类别功能
            // 这里使用简单的两类别切换或者循环切换
            var vm = GetViewModel(this);
            if (vm != null && vm.CurrentProject?.Classes != null && vm.CurrentProject.Classes.Count > 1)
            {
                int nextClassId;

                // 如果有上次使用的类别ID，切换到它
                if (_lastUsedClassId.HasValue &&
                    _lastUsedClassId.Value >= 0 &&
                    _lastUsedClassId.Value < vm.CurrentProject.Classes.Count &&
                    _lastUsedClassId.Value != SelectedClassId)
                {
                    nextClassId = _lastUsedClassId.Value;
                    _lastUsedClassId = SelectedClassId;
                }
                else
                {
                    // 否则循环到下一个类别
                    nextClassId = (SelectedClassId + 1) % vm.CurrentProject.Classes.Count;
                    _lastUsedClassId = SelectedClassId;
                }

                // 使用标志防止递归
                _isUpdatingClassId = true;
                try
                {
                    SelectedClassId = nextClassId;
                    vm.SelectedClassId = nextClassId;
                }
                finally
                {
                    _isUpdatingClassId = false;
                }

                var className = vm.CurrentProject.Classes[nextClassId];
                vm.StatusMessage = $"右键切换到类别: {className} (ID: {nextClassId})";
            }

            e.Handled = true;
        }

        // 获取指定点处的标注
        private Annotation GetAnnotationAtPoint(Point point)
        {
            if (Annotations == null) return null;

            // 遍历所有标注，检查点是否在标注框内
            foreach (var annotation in Annotations)
            {
                var left = annotation.PixelLeft;
                var top = annotation.PixelTop;
                var right = left + annotation.PixelWidth;
                var bottom = top + annotation.PixelHeight;

                if (point.X >= left && point.X <= right &&
                    point.Y >= top && point.Y <= bottom)
                {
                    return annotation;
                }
            }

            return null;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                _showCrosshair = false;
                HideCrosshair();
            }
            else if (e.Key == Key.Delete)
            {
                // Delete键删除选中的标注
                if (Annotations != null)
                {
                    var selectedAnnotation = Annotations.FirstOrDefault(a => a.IsSelected);
                    if (selectedAnnotation != null)
                    {
                        Annotations.Remove(selectedAnnotation);

                        var viewModel = GetViewModel(this);
                        if (viewModel?.CurrentImage != null)
                        {
                            viewModel.CurrentImage.IsModified = true;
                        }

                        RedrawAnnotations();
                    }
                }
            }
            else if (e.Key == Key.Escape)
            {
                // ESC键取消当前操作
                if (_currentMode == EditMode.Drawing)
                {
                    if (_previewRectangle != null)
                    {
                        Children.Remove(_previewRectangle);
                        _previewRectangle = null;
                    }
                }
                else if (_currentMode == EditMode.Moving || _currentMode == EditMode.Resizing)
                {
                    // 恢复到原始位置
                    if (_editingAnnotation != null)
                    {
                        _editingAnnotation.PixelLeft = _originalLeft;
                        _editingAnnotation.PixelTop = _originalTop;
                        _editingAnnotation.PixelWidth = _originalWidth;
                        _editingAnnotation.PixelHeight = _originalHeight;
                        _editingAnnotation.UpdateFromPixels(
                            _originalLeft, _originalTop, _originalWidth, _originalHeight,
                            ImageWidth, ImageHeight);
                    }
                }

                _currentMode = EditMode.None;
                _activeHandle = null;
                _editingAnnotation = null;
                ReleaseMouseCapture();

                RedrawAnnotations();

                if (IsMouseOver)
                {
                    ShowCrosshair();
                    Cursor = Cursors.Cross;
                }
            }
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                _showCrosshair = true;
                if (IsMouseOver && _currentMode == EditMode.None)
                {
                    ShowCrosshair();
                }
            }
        }

        private class ResizeHandle
        {
            public double X { get; set; }
            public double Y { get; set; }
            public ResizeDirection Direction { get; set; }
            public Annotation AssociatedAnnotation { get; set; }

            public ResizeHandle(double x, double y, ResizeDirection direction, Annotation annotation = null)
            {
                X = x;
                Y = y;
                Direction = direction;
                AssociatedAnnotation = annotation;
            }
        }

        private enum ResizeDirection
        {
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight,
            Top,
            Bottom,
            Left,
            Right
        }
    }
}
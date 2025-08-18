using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using BryanYOLO.Models;
using BryanYOLO.Services;
using Annotation = BryanYOLO.Models.Annotation;

namespace BryanYOLO.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ImageLoadingService _imageLoadingService;
        private readonly BryanYOLO.Services.AnnotationService _annotationService;
        private readonly YoloExportService _exportService;
        private readonly OnnxInferenceService _inferenceService;
        private readonly IconDetectionService _iconDetectionService;
        private readonly InferAnnotationService _inferAnnotationService;
        private readonly ProjectSaveService _projectSaveService;

        private YoloProject _currentProject;
        private ImageItem _currentImage;
        private BitmapImage _currentBitmap;
        private ObservableCollection<BitmapImage> _previousThumbnails;
        private ObservableCollection<BitmapImage> _nextThumbnails;
        private string _statusMessage;
        private bool _isLoading;
        private string _annotationText;
        private ObservableCollection<ImageItem> _filteredImages;
        private AnnotationStatus? _filterStatus;
        private int _currentImageIndex;
        private int _selectedClassId = 0;

        // 推理相关
        private string _onnxModelPath;
        private int _modelInputSize = 640;
        private string _labelMappings;
        private InferenceTarget _inferenceTarget = InferenceTarget.NotAnnotated;
        private float _confidenceThreshold = 0.1f;
        private double _inferenceProgress = 0;
        private int _modelClassCount = -1;
        private YoloVersion _modelVersion = YoloVersion.V5;

        // 新增的推理选项
        private bool _overwriteAllLabels = false;
        private bool _removeOverlappingLowConfidence = false;
        private float _overlapRemovalThreshold = 0.5f;

        public YoloProject CurrentProject
        {
            get => _currentProject;
            set
            {
                _currentProject = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ClassColors));
            }
        }

        public ImageItem CurrentImage
        {
            get => _currentImage;
            set
            {
                // 保存之前图片的标注
                if (_currentImage != null)
                {
                    if (_currentImage.IsModified)
                    {
                        _currentImage.SaveAnnotations();
                    }
                    else if (_currentImage.Status == AnnotationStatus.NotAnnotated && _currentImage.Annotations.Count == 0)
                    {
                        _currentImage.ClearAnnotations();
                    }
                }

                _currentImage = value;
                OnPropertyChanged();

                // 触发Annotations属性变更，确保UI更新
                OnPropertyChanged(nameof(CurrentAnnotations));

                // 加载新图片
                if (_currentImage != null)
                {
                    LoadCurrentImage();
                    UpdateThumbnails();
                    UpdateAnnotationText();
                }
            }
        }

        public BitmapImage CurrentBitmap
        {
            get => _currentBitmap;
            set
            {
                _currentBitmap = value;
                OnPropertyChanged();

                // 当图片加载完成后，通知Canvas更新
                if (_currentBitmap != null && CurrentImage != null)
                {
                    // 更新所有标注的像素坐标
                    foreach (var annotation in CurrentImage.Annotations)
                    {
                        annotation.UpdatePixelCoordinates(_currentBitmap.PixelWidth, _currentBitmap.PixelHeight);
                    }
                }
            }
        }

        public ObservableCollection<BitmapImage> PreviousThumbnails
        {
            get => _previousThumbnails;
            set { _previousThumbnails = value; OnPropertyChanged(); }
        }

        public ObservableCollection<BitmapImage> NextThumbnails
        {
            get => _nextThumbnails;
            set { _nextThumbnails = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        public string AnnotationText
        {
            get => _annotationText;
            set { _annotationText = value; OnPropertyChanged(); }
        }

        public ObservableCollection<ImageItem> FilteredImages
        {
            get => _filteredImages;
            set { _filteredImages = value; OnPropertyChanged(); }
        }

        public AnnotationStatus? FilterStatus
        {
            get => _filterStatus;
            set
            {
                _filterStatus = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        public string OnnxModelPath
        {
            get => _onnxModelPath;
            set { _onnxModelPath = value; OnPropertyChanged(); }
        }

        public int ModelInputSize
        {
            get => _modelInputSize;
            set { _modelInputSize = value; OnPropertyChanged(); }
        }

        public int ModelClassCount
        {
            get => _modelClassCount;
            set { _modelClassCount = value; OnPropertyChanged(); }
        }

        public YoloVersion ModelVersion
        {
            get => _modelVersion;
            set { _modelVersion = value; OnPropertyChanged(); }
        }

        public string LabelMappings
        {
            get => _labelMappings;
            set { _labelMappings = value; OnPropertyChanged(); }
        }

        public InferenceTarget InferenceTarget
        {
            get => _inferenceTarget;
            set { _inferenceTarget = value; OnPropertyChanged(); }
        }

        public float ConfidenceThreshold
        {
            get => _confidenceThreshold;
            set
            {
                _confidenceThreshold = Math.Max(0.01f, Math.Min(1.0f, value));
                OnPropertyChanged();
            }
        }

        // 新增属性
        public bool OverwriteAllLabels
        {
            get => _overwriteAllLabels;
            set { _overwriteAllLabels = value; OnPropertyChanged(); }
        }

        public bool RemoveOverlappingLowConfidence
        {
            get => _removeOverlappingLowConfidence;
            set { _removeOverlappingLowConfidence = value; OnPropertyChanged(); }
        }

        public float OverlapRemovalThreshold
        {
            get => _overlapRemovalThreshold;
            set
            {
                _overlapRemovalThreshold = Math.Max(0.01f, Math.Min(1.0f, value));
                OnPropertyChanged();
            }
        }

        public double InferenceProgress
        {
            get => _inferenceProgress;
            set { _inferenceProgress = value; OnPropertyChanged(); }
        }

        public int CurrentImageIndex
        {
            get => _currentImageIndex;
            private set { _currentImageIndex = value; OnPropertyChanged(); }
        }

        public int SelectedClassId
        {
            get => _selectedClassId;
            set
            {
                _selectedClassId = value;
                OnPropertyChanged();
            }
        }

        public System.Collections.Generic.Dictionary<int, System.Windows.Media.Color> ClassColors
        {
            get => CurrentProject?.ClassColors;
        }

        // 添加一个属性用于绑定当前图片的标注
        public ObservableCollection<Annotation> CurrentAnnotations
        {
            get => CurrentImage?.Annotations;
        }

        // Commands
        public ICommand LoadFolderCommand { get; }
        public ICommand LoadYamlCommand { get; }
        public ICommand SaveAnnotationsCommand { get; }
        public ICommand ClearAnnotationsCommand { get; }
        public ICommand ExportYoloCommand { get; }
        public ICommand SaveProjectAsCommand { get; }
        public ICommand PreviousImageCommand { get; }
        public ICommand NextImageCommand { get; }
        public ICommand SelectOnnxModelCommand { get; }
        public ICommand RunInferenceCommand { get; }
        public ICommand DeleteAnnotationCommand { get; }
        public ICommand DeleteCurrentImageCommand { get; }
        public ICommand ManageClassesCommand { get; }
        public ICommand ChangeAnnotationClassCommand { get; }
        public ICommand ShowHelpCommand { get; }
        public ICommand ShowAboutCommand { get; }
        public ICommand InferAnnotationCommand { get; }

        public MainViewModel()
        {
            _imageLoadingService = new ImageLoadingService();
            _annotationService = new BryanYOLO.Services.AnnotationService();
            _exportService = new YoloExportService();
            _inferenceService = new OnnxInferenceService();
            _iconDetectionService = new IconDetectionService();
            _inferAnnotationService = new InferAnnotationService();
            _projectSaveService = new ProjectSaveService();

            FilteredImages = new ObservableCollection<ImageItem>();
            PreviousThumbnails = new ObservableCollection<BitmapImage>();
            NextThumbnails = new ObservableCollection<BitmapImage>();
            CurrentProject = new YoloProject();

            // 初始化命令
            LoadFolderCommand = new RelayCommand(async _ => await LoadFolder());
            LoadYamlCommand = new RelayCommand(async _ => await LoadYamlProject());
            SaveAnnotationsCommand = new RelayCommand(_ => SaveCurrentAnnotations());
            ClearAnnotationsCommand = new RelayCommand(_ => ClearCurrentAnnotations());
            ExportYoloCommand = new RelayCommand(async _ => await ExportToYolo());
            SaveProjectAsCommand = new RelayCommand(async _ => await SaveProjectAs(), _ => CurrentProject?.Images?.Count > 0);
            PreviousImageCommand = new RelayCommand(_ => NavigateToPrevious(), _ => CanNavigatePrevious());
            NextImageCommand = new RelayCommand(_ => NavigateToNext(), _ => CanNavigateNext());
            SelectOnnxModelCommand = new RelayCommand(_ => SelectOnnxModel());
            RunInferenceCommand = new RelayCommand(async _ => await RunInference(), _ => !string.IsNullOrEmpty(OnnxModelPath));
            DeleteAnnotationCommand = new RelayCommand<Annotation>(DeleteAnnotation);
            DeleteCurrentImageCommand = new RelayCommand(_ => DeleteCurrentImage(), _ => CurrentImage != null);
            ManageClassesCommand = new RelayCommand(_ => ManageClasses());
            ChangeAnnotationClassCommand = new RelayCommand<Annotation>(ChangeAnnotationClass);
            ShowHelpCommand = new RelayCommand(_ => ShowHelp());
            ShowAboutCommand = new RelayCommand(_ => ShowAbout());
            InferAnnotationCommand = new RelayCommand(_ => { }); // 将在MainWindow中处理
        }

        private async Task SaveProjectAs()
        {
            if (CurrentProject == null || CurrentProject.Images.Count == 0)
            {
                System.Windows.MessageBox.Show(
                    "没有可保存的项目",
                    "提示",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            // 先保存当前编辑
            if (CurrentImage != null && CurrentImage.IsModified)
            {
                CurrentImage.SaveAnnotations();
            }

            // 打开保存对话框
            var dialog = new BryanYOLO.Dialogs.SaveProjectDialog(
                CurrentProject.Images.ToList(),
                CurrentImage);

            if (dialog.ShowDialog() == true)
            {
                IsLoading = true;
                InferenceProgress = 0;

                try
                {
                    StatusMessage = "正在保存项目...";

                    // 获取对话框中的设置
                    var selectedImages = dialog.SelectedImages;
                    var outputPath = dialog.OutputPath;
                    var projectName = dialog.ProjectName;
                    bool copyImages = dialog.CopyImagesCheckBox?.IsChecked ?? true;
                    bool generateTrainVal = dialog.GenerateTrainValCheckBox?.IsChecked ?? true;

                    // 保存项目
                    bool success = await _projectSaveService.SavePartialProject(
                        CurrentProject,
                        selectedImages,
                        outputPath,
                        projectName,
                        copyImages,
                        generateTrainVal,
                        progress =>
                        {
                            InferenceProgress = progress;
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                OnPropertyChanged(nameof(InferenceProgress));
                            });
                        },
                        status =>
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                StatusMessage = status;
                            });
                        });

                    if (success)
                    {
                        var projectDir = Path.Combine(Path.GetDirectoryName(outputPath), projectName);

                        System.Windows.MessageBox.Show(
                            $"项目保存成功！\n\n" +
                            $"项目名称: {projectName}\n" +
                            $"保存位置: {projectDir}\n" +
                            $"图片数量: {selectedImages.Count}\n" +
                            $"  已标记: {selectedImages.Count(img => img.Status == AnnotationStatus.Annotated)}\n" +
                            $"  空标记: {selectedImages.Count(img => img.Status == AnnotationStatus.EmptyAnnotation)}\n" +
                            $"  未标记: {selectedImages.Count(img => img.Status == AnnotationStatus.NotAnnotated)}\n\n" +
                            $"您现在可以使用生成的YAML文件训练模型了。",
                            "保存成功",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);

                        // 询问是否打开项目文件夹
                        var result = System.Windows.MessageBox.Show(
                            "是否打开项目文件夹？",
                            "打开文件夹",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Question);

                        if (result == System.Windows.MessageBoxResult.Yes)
                        {
                            System.Diagnostics.Process.Start("explorer.exe", projectDir);
                        }
                    }
                    else
                    {
                        System.Windows.MessageBox.Show(
                            "项目保存失败，请查看状态栏信息。",
                            "保存失败",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"保存失败: {ex.Message}";
                    System.Windows.MessageBox.Show(
                        $"保存项目时发生错误：\n{ex.Message}",
                        "错误",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
                finally
                {
                    IsLoading = false;
                    InferenceProgress = 0;
                }
            }
        }

        private async Task LoadFolder()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择图片文件夹中的任一图片",
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp|所有文件|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                IsLoading = true;
                StatusMessage = "正在加载图片...";

                try
                {
                    var folderPath = Path.GetDirectoryName(dialog.FileName);
                    var images = await _imageLoadingService.LoadImagesFromFolder(folderPath);

                    CurrentProject.Images.Clear();
                    foreach (var image in images)
                    {
                        CurrentProject.Images.Add(image);
                    }

                    // 提示用户设置类别
                    if (CurrentProject.Classes.Count == 0)
                    {
                        var result = System.Windows.MessageBox.Show(
                            "检测到这是一个新项目，是否现在设置类别标签？",
                            "设置类别",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Question);

                        if (result == System.Windows.MessageBoxResult.Yes)
                        {
                            ManageClasses();
                        }
                        else
                        {
                            // 添加默认类别
                            CurrentProject.Classes.Add("object");
                        }
                    }

                    CurrentProject.GenerateClassColors();
                    OnPropertyChanged(nameof(ClassColors));

                    ApplyFilter();
                    if (FilteredImages.Count > 0)
                    {
                        CurrentImage = FilteredImages[0];
                    }

                    StatusMessage = $"已加载 {images.Count} 张图片";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"加载失败: {ex.Message}";
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }

        private async Task LoadYamlProject()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择YOLO项目YAML文件",
                Filter = "YAML文件|*.yaml;*.yml|所有文件|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                IsLoading = true;
                StatusMessage = "正在加载项目...";

                try
                {
                    CurrentProject = await _imageLoadingService.LoadYoloProject(dialog.FileName);
                    CurrentProject.GenerateClassColors();
                    OnPropertyChanged(nameof(ClassColors));

                    ApplyFilter();
                    if (FilteredImages.Count > 0)
                    {
                        CurrentImage = FilteredImages[0];
                    }

                    StatusMessage = $"已加载项目，共 {CurrentProject.Images.Count} 张图片，{CurrentProject.Classes.Count} 个类别";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"加载失败: {ex.Message}";
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }

        private void LoadCurrentImage()
        {
            if (CurrentImage == null) return;

            try
            {
                // 加载图片
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(CurrentImage.ImagePath);
                bitmap.EndInit();
                bitmap.Freeze();

                // 加载标注
                CurrentImage.LoadAnnotations();

                // 设置CurrentBitmap，这会触发属性变更事件
                CurrentBitmap = bitmap;

                CurrentImageIndex = FilteredImages.IndexOf(CurrentImage) + 1;

                // 触发标注列表更新
                OnPropertyChanged(nameof(CurrentAnnotations));

                // 确保每个标注的DisplayName都是最新的
                foreach (var annotation in CurrentImage.Annotations)
                {
                    annotation.OnPropertyChanged("DisplayName");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载图片失败: {ex.Message}";
                CurrentBitmap = null;
            }
        }

        private void UpdateThumbnails()
        {
            PreviousThumbnails.Clear();
            NextThumbnails.Clear();

            if (FilteredImages == null || FilteredImages.Count == 0) return;

            var currentIndex = FilteredImages.IndexOf(CurrentImage);

            // 加载前面的3-5张缩略图
            for (int i = Math.Max(0, currentIndex - 5); i < currentIndex && i >= 0; i++)
            {
                var prevImage = FilteredImages[i];
                var thumbnail = LoadThumbnail(prevImage.ImagePath);
                if (thumbnail != null)
                {
                    PreviousThumbnails.Add(thumbnail);
                }
            }

            // 加载后面的3-5张缩略图
            for (int i = currentIndex + 1; i < Math.Min(FilteredImages.Count, currentIndex + 6); i++)
            {
                var nextImage = FilteredImages[i];
                var thumbnail = LoadThumbnail(nextImage.ImagePath);
                if (thumbnail != null)
                {
                    NextThumbnails.Add(thumbnail);
                }
            }
        }

        private BitmapImage LoadThumbnail(string imagePath)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(imagePath);
                bitmap.DecodePixelHeight = 100; // 缩略图高度
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public void UpdateAnnotationText()
        {
            if (CurrentImage == null)
            {
                AnnotationText = string.Empty;
                return;
            }

            var lines = CurrentImage.Annotations.Select(a => a.ToYoloString());
            AnnotationText = string.Join(Environment.NewLine, lines);
        }

        private void ApplyFilter()
        {
            FilteredImages.Clear();

            var images = CurrentProject?.Images ?? new ObservableCollection<ImageItem>();
            var filtered = images.AsEnumerable();

            if (FilterStatus.HasValue)
            {
                filtered = filtered.Where(img => img.Status == FilterStatus.Value);
            }

            foreach (var image in filtered)
            {
                FilteredImages.Add(image);
            }
        }

        private void SaveCurrentAnnotations()
        {
            CurrentImage?.SaveAnnotations();
            StatusMessage = "标注已保存";
        }

        private void ClearCurrentAnnotations()
        {
            if (CurrentImage == null) return;

            CurrentImage.ClearAnnotations();
            UpdateAnnotationText();

            // 触发标注列表更新
            OnPropertyChanged(nameof(CurrentAnnotations));

            StatusMessage = "已清空标注并创建空标记文件";
        }

        private async Task ExportToYolo()
        {
            if (CurrentProject == null || CurrentProject.Images.Count == 0)
            {
                StatusMessage = "没有可导出的项目";
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "选择导出位置",
                FileName = "dataset.yaml",
                Filter = "YAML文件|*.yaml;*.yml|所有文件|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                IsLoading = true;
                StatusMessage = "正在导出...";

                try
                {
                    await _exportService.ExportToYoloFormat(CurrentProject, dialog.FileName);
                    StatusMessage = "导出成功";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"导出失败: {ex.Message}";
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }

        private bool CanNavigatePrevious()
        {
            var currentIndex = FilteredImages.IndexOf(CurrentImage);
            return currentIndex > 0 && FilteredImages.Count > 0;
        }

        private void NavigateToPrevious()
        {
            if (CanNavigatePrevious())
            {
                var currentIndex = FilteredImages.IndexOf(CurrentImage);
                CurrentImage = FilteredImages[currentIndex - 1];
            }
        }

        private bool CanNavigateNext()
        {
            var currentIndex = FilteredImages.IndexOf(CurrentImage);
            return currentIndex < FilteredImages.Count - 1 && FilteredImages.Count > 0;
        }

        private void NavigateToNext()
        {
            if (CanNavigateNext())
            {
                var currentIndex = FilteredImages.IndexOf(CurrentImage);
                CurrentImage = FilteredImages[currentIndex + 1];
            }
        }

        private void SelectOnnxModel()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择ONNX模型文件",
                Filter = "ONNX模型|*.onnx|所有文件|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                OnnxModelPath = dialog.FileName;
                StatusMessage = $"已选择模型: {Path.GetFileName(OnnxModelPath)}";
            }
        }

        private async Task RunInference()
        {
            if (string.IsNullOrEmpty(OnnxModelPath) || CurrentProject == null)
            {
                StatusMessage = "请先选择ONNX模型";
                return;
            }

            IsLoading = true;
            InferenceProgress = 0;
            StatusMessage = "正在初始化推理引擎...";

            try
            {
                // 解析标签映射
                var mappings = LabelMapping.ParseMappings(LabelMappings);

                // 输出调试信息
                if (mappings.Count > 0)
                {
                    var mappingInfo = string.Join(", ", mappings.Select(m => $"{m.ModelClassId}→{m.ProjectClassId}"));
                    StatusMessage = $"标签映射: {mappingInfo}";
                    await Task.Delay(1000); // 让用户看到映射信息

                    // 验证映射的有效性
                    foreach (var mapping in mappings)
                    {
                        if (mapping.ProjectClassId >= CurrentProject.Classes.Count)
                        {
                            StatusMessage = $"错误：项目中不存在类别ID {mapping.ProjectClassId}";
                            return;
                        }

                        // 输出映射的类别名称，帮助调试
                        var className = CurrentProject.Classes[mapping.ProjectClassId];
                        System.Diagnostics.Debug.WriteLine($"映射: 模型类别 {mapping.ModelClassId} → 项目类别 {mapping.ProjectClassId} ({className})");
                    }
                }
                else
                {
                    StatusMessage = "警告：没有设置标签映射，将使用模型原始类别ID";
                    await Task.Delay(1000);
                }

                // 根据推理目标筛选图片
                var targetImages = InferenceTarget switch
                {
                    InferenceTarget.All => CurrentProject.Images.ToList(),
                    InferenceTarget.Empty => CurrentProject.Images.Where(img => img.Status == AnnotationStatus.EmptyAnnotation).ToList(),
                    InferenceTarget.NotAnnotated => CurrentProject.Images.Where(img => img.Status == AnnotationStatus.NotAnnotated).ToList(),
                    _ => CurrentProject.Images.ToList()
                };

                if (targetImages.Count == 0)
                {
                    StatusMessage = "没有符合条件的图片需要推理";
                    return;
                }

                StatusMessage = $"正在对 {targetImages.Count} 张图片进行推理（{InferenceTarget}）...";

                // 记录开始时间
                var startTime = DateTime.Now;

                // 调用更新后的推理方法，传入新增的参数
                await _inferenceService.RunBatchInference(
                    OnnxModelPath,
                    targetImages,
                    ModelInputSize,
                    mappings,
                    ConfidenceThreshold,
                    OverwriteAllLabels,  // 新增参数
                    RemoveOverlappingLowConfidence,  // 新增参数
                    OverlapRemovalThreshold,  // 新增参数
                    progress =>
                    {
                        InferenceProgress = progress;
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            OnPropertyChanged(nameof(InferenceProgress));
                        });
                    },
                    status =>
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = status;
                            // 同时输出到调试控制台
                            System.Diagnostics.Debug.WriteLine($"[推理状态] {status}");
                        });
                    },
                    ModelClassCount,
                    ModelVersion);

                // 计算耗时
                var elapsed = DateTime.Now - startTime;

                // 刷新当前图片的标注
                if (CurrentImage != null)
                {
                    // 保存当前修改（如果有）
                    if (CurrentImage.IsModified)
                    {
                        CurrentImage.SaveAnnotations();
                    }

                    // 重新加载标注
                    CurrentImage.LoadAnnotations();
                    UpdateAnnotationText();
                    OnPropertyChanged(nameof(CurrentAnnotations));

                    // 通知Canvas重绘
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        // 触发所有标注的DisplayName更新
                        foreach (var annotation in CurrentImage.Annotations)
                        {
                            annotation.OnPropertyChanged("DisplayName");
                            annotation.OnPropertyChanged("ClassId");
                        }
                    });
                }

                // 更新所有图片的状态
                foreach (var image in targetImages)
                {
                    image.UpdateStatus();
                }

                StatusMessage = $"推理完成！处理 {targetImages.Count} 张图片，耗时 {elapsed.TotalSeconds:F1} 秒";

                // 显示详细的成功信息
                var detailMessage = $"推理完成！\n\n" +
                    $"• 处理图片数：{targetImages.Count}\n" +
                    $"• 耗时：{elapsed.TotalSeconds:F1} 秒\n" +
                    $"• 置信度阈值：{ConfidenceThreshold:F2}\n" +
                    $"• 模型版本：{ModelVersion}\n" +
                    $"• 模型类别数：{(ModelClassCount > 0 ? ModelClassCount.ToString() : "自动检测")}\n" +
                    $"• 标签映射：{(mappings.Count > 0 ? string.Join(", ", mappings.Select(m => $"{m.ModelClassId}→{m.ProjectClassId}")) : "无")}\n";

                if (OverwriteAllLabels)
                {
                    detailMessage += "• 覆盖模式：已清空原有标签\n";
                }

                if (RemoveOverlappingLowConfidence)
                {
                    detailMessage += $"• 移除低置信度：阈值 {OverlapRemovalThreshold:F2}\n";
                }

                detailMessage += "\n请检查图片的标注是否已更新。";

                System.Windows.MessageBox.Show(
                    detailMessage,
                    "推理完成",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = $"推理失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[推理错误] {ex}");

                System.Windows.MessageBox.Show(
                    $"推理过程中发生错误：\n{ex.Message}\n\n" +
                    $"详细错误：\n{ex.StackTrace}\n\n" +
                    $"请检查：\n" +
                    $"1. 模型文件是否正确\n" +
                    $"2. 模型版本是否正确选择\n" +
                    $"3. 标签映射格式是否正确（如 0:2,1:3）\n" +
                    $"4. 输入尺寸是否与模型匹配\n" +
                    $"5. 模型类别数是否正确\n" +
                    $"6. 标注文件路径是否可访问",
                    "推理错误",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                InferenceProgress = 0;
            }
        }

        public async Task RunAnnotationInference(InferAnnotationService.InferConfig config)
        {
            if (CurrentProject == null || CurrentProject.Images.Count == 0)
            {
                StatusMessage = "没有可处理的项目";
                return;
            }

            IsLoading = true;
            StatusMessage = "正在执行标注推测...";

            try
            {
                // 获取要处理的图片
                var targetImages = config.ProcessCurrentImageOnly && CurrentImage != null
                    ? new System.Collections.Generic.List<ImageItem> { CurrentImage }
                    : CurrentProject.Images.Where(img => img.Status == AnnotationStatus.Annotated).ToList();

                if (targetImages.Count == 0)
                {
                    StatusMessage = "没有已标注的图片需要处理";
                    return;
                }

                StatusMessage = $"正在处理 {targetImages.Count} 张图片...";

                // 执行批量处理
                int modifiedCount = await _inferAnnotationService.ProcessBatchInference(
                    targetImages,
                    config,
                    progress =>
                    {
                        InferenceProgress = progress;
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            OnPropertyChanged(nameof(InferenceProgress));
                        });
                    },
                    status =>
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = status;
                        });
                    });

                // 刷新当前图片的标注
                if (CurrentImage != null)
                {
                    CurrentImage.LoadAnnotations();
                    UpdateAnnotationText();
                    OnPropertyChanged(nameof(CurrentAnnotations));
                }

                StatusMessage = $"推测完成，共修改了 {modifiedCount} 张图片";
            }
            catch (Exception ex)
            {
                StatusMessage = $"处理失败: {ex.Message}";
                System.Windows.MessageBox.Show(
                    $"标注推测过程中发生错误：\n{ex.Message}",
                    "处理错误",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                InferenceProgress = 0;
            }
        }

        public async Task RunLabelAdjustment(
            IconDetectionService.DetectionMethod method,
            string modelOrIconPath,
            int iconClassId,
            int modelInputSize,
            float confidenceThreshold,
            System.Windows.Rect selectionRect,
            int targetLabelId,
            string position,
            string mode,
            bool changeLabel,
            int newLabelId,
            double matchThreshold,
            bool processCurrentOnly,
            int modelClassCount = -1,
            YoloVersion modelVersion = YoloVersion.V5)
        {
            if (CurrentProject == null || CurrentProject.Images.Count == 0)
            {
                StatusMessage = "没有可处理的项目";
                return;
            }

            IsLoading = true;
            StatusMessage = "正在初始化标签识别修正...";

            try
            {
                // 转换 WPF Rect 到 System.Drawing.Rectangle
                var drawingRect = new System.Drawing.Rectangle(
                    (int)selectionRect.X,
                    (int)selectionRect.Y,
                    (int)selectionRect.Width,
                    (int)selectionRect.Height
                );

                // 创建配置
                var config = new IconDetectionService.LabelAdjustmentConfig
                {
                    Method = method,
                    OnnxModelPath = method == IconDetectionService.DetectionMethod.OnnxModel ? modelOrIconPath : null,
                    IconImagePath = method == IconDetectionService.DetectionMethod.Traditional ? modelOrIconPath : null,
                    ModelInputSize = modelInputSize,
                    IconClassId = iconClassId,
                    ConfidenceThreshold = confidenceThreshold,
                    ModelClassCount = modelClassCount,
                    ModelVersion = modelVersion,
                    CurrentImagePath = CurrentImage?.ImagePath,
                    SelectionRect = drawingRect,
                    TargetLabelId = targetLabelId,
                    Position = Enum.Parse<IconDetectionService.LabelPosition>(position),
                    Mode = Enum.Parse<IconDetectionService.AdjustmentMode>(mode),
                    ChangeLabel = changeLabel,
                    NewLabelId = newLabelId,
                    MatchThreshold = matchThreshold,
                    ProcessCurrentImageOnly = processCurrentOnly
                };

                // 获取要处理的图片
                var targetImages = processCurrentOnly && CurrentImage != null
                    ? new System.Collections.Generic.List<ImageItem> { CurrentImage }
                    : CurrentProject.Images.Where(img => img.Status == AnnotationStatus.Annotated).ToList();

                if (targetImages.Count == 0)
                {
                    StatusMessage = "没有已标注的图片需要处理";
                    return;
                }

                StatusMessage = $"正在处理 {targetImages.Count} 张图片...";

                // 执行批量处理
                int modifiedCount = await _iconDetectionService.ProcessBatchAdjustment(
                    targetImages,
                    config,
                    progress =>
                    {
                        InferenceProgress = progress;
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            OnPropertyChanged(nameof(InferenceProgress));
                        });
                    },
                    status =>
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = status;
                        });
                    });

                // 刷新当前图片的标注
                if (CurrentImage != null)
                {
                    CurrentImage.LoadAnnotations();
                    UpdateAnnotationText();
                    OnPropertyChanged(nameof(CurrentAnnotations));
                }

                // 更详细的状态报告
                StatusMessage = $"标签识别修正完成！\n" +
                               $"• 处理图片数：{targetImages.Count}\n" +
                               $"• 修改图片数：{modifiedCount}\n" +
                               $"• 目标标签：{targetLabelId}" +
                               (changeLabel ? $" → {newLabelId}" : "");

                // 如果没有修改任何图片，给出可能的原因
                if (modifiedCount == 0)
                {
                    System.Windows.MessageBox.Show(
                        "没有图片被修改，可能的原因：\n\n" +
                        "1. 图片中没有检测到指定的图标\n" +
                        "2. 置信度阈值设置过高\n" +
                        "3. 图标类别ID设置不正确\n" +
                        "4. 模型版本选择不正确\n" +
                        "5. 模型类别数设置不正确\n" +
                        "6. 没有找到符合条件的标注框\n\n" +
                        "建议：\n" +
                        "• 降低置信度阈值（如0.3）\n" +
                        "• 确认图标类别ID正确\n" +
                        "• 确认模型版本正确\n" +
                        "• 检查标注框位置关系是否正确",
                        "处理完成",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        $"成功修改了 {modifiedCount} 张图片的标注！",
                        "处理完成",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"处理失败: {ex.Message}";
                System.Windows.MessageBox.Show(
                    $"标签识别修正过程中发生错误：\n{ex.Message}",
                    "处理错误",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                InferenceProgress = 0;
            }
        }

        private void DeleteAnnotation(Annotation annotation)
        {
            if (CurrentImage != null && annotation != null)
            {
                CurrentImage.Annotations.Remove(annotation);
                CurrentImage.IsModified = true;
                UpdateAnnotationText();

                // 触发标注列表更新
                OnPropertyChanged(nameof(CurrentAnnotations));
            }
        }

        private void DeleteCurrentImage()
        {
            if (CurrentImage == null) return;

            var result = System.Windows.MessageBox.Show(
                $"确定要删除图片 '{CurrentImage.FileName}' 吗？\n\n" +
                "注意：\n" +
                "• 图片文件将被移到回收站\n" +
                "• 对应的标注文件也将被删除",
                "删除确认",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                try
                {
                    var imageToDelete = CurrentImage;
                    var currentIndex = FilteredImages.IndexOf(imageToDelete);

                    // 先切换到其他图片
                    if (FilteredImages.Count > 1)
                    {
                        if (currentIndex < FilteredImages.Count - 1)
                        {
                            CurrentImage = FilteredImages[currentIndex + 1];
                        }
                        else if (currentIndex > 0)
                        {
                            CurrentImage = FilteredImages[currentIndex - 1];
                        }
                    }
                    else
                    {
                        CurrentImage = null;
                        CurrentBitmap = null;
                    }

                    // 删除文件（移到回收站）
                    if (File.Exists(imageToDelete.ImagePath))
                    {
                        // 使用 Microsoft.VisualBasic.FileIO 来支持回收站
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                            imageToDelete.ImagePath,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    }

                    // 删除标注文件
                    if (File.Exists(imageToDelete.AnnotationPath))
                    {
                        File.Delete(imageToDelete.AnnotationPath);
                    }

                    // 从列表中移除
                    CurrentProject.Images.Remove(imageToDelete);
                    FilteredImages.Remove(imageToDelete);

                    StatusMessage = $"已删除图片: {imageToDelete.FileName}";
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        $"删除图片失败: {ex.Message}",
                        "错误",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
        }

        private void ChangeAnnotationClass(Annotation annotation)
        {
            // 这个方法将在MainWindow.xaml.cs中调用并处理
        }

        private void ManageClasses()
        {
            var dialog = new BryanYOLO.Dialogs.ClassManagementDialog(CurrentProject.Classes);

            if (dialog.ShowDialog() == true)
            {
                // 更新类别列表
                CurrentProject.Classes.Clear();
                foreach (var classItem in dialog.Classes)
                {
                    CurrentProject.Classes.Add(classItem.Name);
                }

                // 重新生成颜色
                CurrentProject.GenerateClassColors();

                // 通知颜色更新
                OnPropertyChanged(nameof(ClassColors));

                // 通知所有标注更新DisplayName
                if (CurrentImage != null && CurrentImage.Annotations != null)
                {
                    foreach (var annotation in CurrentImage.Annotations)
                    {
                        annotation.OnPropertyChanged("DisplayName");
                    }
                }

                StatusMessage = $"已更新类别列表，共 {CurrentProject.Classes.Count} 个类别";
            }
        }

        private void ShowHelp()
        {
            var helpText = "BryanYOLO 使用说明\n\n" +
                "基本操作：\n" +
                "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                "🖱️ 鼠标操作：\n" +
                "  • 左键拖拽：绘制新的标注框\n" +
                "  • 左键点击框体：选中并可以移动\n" +
                "  • 右键点击框体（在画布上）：删除标注框\n" +
                "  • 右键拖动（在空白处）：平移视图\n" +
                "  • Ctrl+鼠标滚轮：缩放图片\n" +
                "  • 拖拽角点/边缘：调整框体大小\n\n" +
                "⌨️ 快捷键：\n" +
                "  • ← / → 键：切换上一张/下一张图片\n" +
                "  • W/A/S/D 键：上/左/下/右移动视图\n" +
                "  • Delete 键：删除选中的标注框\n" +
                "  • Ctrl+S：保存当前标注\n" +
                "  • Ctrl + +/-：放大/缩小\n" +
                "  • Ctrl + 0：恢复100%缩放\n" +
                "  • Shift：按住时隐藏十字线\n" +
                "  • F1：打开使用说明\n\n" +
                "🔍 缩放与导航：\n" +
                "  • 工具栏按钮：点击放大/缩小按钮\n" +
                "  • 100%按钮：恢复原始大小\n" +
                "  • 适应按钮：自动缩放以适应窗口\n" +
                "  • 缩放范围：10% - 500%\n" +
                "  • WASD导航：在放大时快速移动视图\n" +
                "  • 右键拖动：平移查看不同区域\n\n" +
                "📝 标注技巧：\n" +
                "  • 绿色十字线帮助精确定位\n" +
                "  • 新绘制的框自动进入编辑状态\n" +
                "  • 切换图片时自动保存标注\n" +
                "  • 未标注的图片自动创建空标注（负样本）\n" +
                "  • 放大后可以进行更精确的标注\n\n" +
                "💾 项目保存：\n" +
                "  • 另存为：保存部分图片到新项目\n" +
                "  • 可选择保存范围（如1-1215张）\n" +
                "  • 自动分割训练集和验证集\n" +
                "  • 生成YAML配置文件\n" +
                "  • 用于训练模型后继续推理\n\n" +
                "工作流程：\n" +
                "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                "1️⃣ 打开项目\n" +
                "   • 文件夹：选择包含图片的文件夹\n" +
                "   • YAML：加载现有的YOLO项目\n\n" +
                "2️⃣ 设置类别\n" +
                "   • 点击\"管理类别\"设置标签\n" +
                "   • 每个类别自动分配颜色\n\n" +
                "3️⃣ 标注图片\n" +
                "   • 选择类别后在图片上绘制框体\n" +
                "   • 可随时调整框体位置和大小\n" +
                "   • 使用缩放功能查看细节\n\n" +
                "4️⃣ 保存项目\n" +
                "   • 另存为：保存部分已校准的图片\n" +
                "   • 导出：导出完整项目为YOLO格式\n\n" +
                "5️⃣ 训练模型\n" +
                "   • 使用保存的项目训练YOLO模型\n" +
                "   • 训练后继续推理剩余图片\n\n" +
                "AI推理功能：\n" +
                "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                "🤖 自动标注：\n" +
                "  1. 选择ONNX模型文件\n" +
                "  2. 选择模型版本（V5/V8/V10/V11）\n" +
                "  3. 设置模型输入大小（通常640）\n" +
                "  4. 设置模型类别数（部分版本需要）\n" +
                "  5. 设置置信度阈值（默认0.1）\n" +
                "  6. 配置标签映射（如 0:2 表示模型类别0映射到项目类别2）\n" +
                "  7. 选择推理目标（全部/空标记/未标记）\n" +
                "  8. 可选：覆盖所有标签（清空后重新标注）\n" +
                "  9. 可选：移除低置信度重叠框\n" +
                "  10. 开始批量推理\n\n" +
                "💡 使用建议：\n" +
                "  • 先标注一部分图片（如1000张）\n" +
                "  • 使用\"另存为\"保存已标注的部分\n" +
                "  • 训练模型后对剩余图片推理\n" +
                "  • 继续校准和优化标注\n" +
                "  • 迭代改进模型精度";

            System.Windows.MessageBox.Show(
                helpText,
                "使用说明",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        private void ShowAbout()
        {
            var aboutText = "BryanYOLO - AI图片标注工具\n" +
                "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                "版本：1.5.0\n" +
                "框架：.NET 8.0 + WPF\n" +
                "作者：Bryan\n\n" +
                "功能特性：\n" +
                "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                "✓ YOLO格式标注支持\n" +
                "✓ 批量图片管理\n" +
                "✓ AI自动标注（ONNX推理）\n" +
                "✓ 多版本YOLO模型支持（v5/v8/v10/v11）\n" +
                "✓ 标签识别修正\n" +
                "✓ 推测标注功能\n" +
                "✓ 项目另存为（部分保存）\n" +
                "✓ GPU加速支持（DirectML）\n" +
                "✓ 多线程并行处理\n" +
                "✓ 实时预览和编辑\n" +
                "✓ 负样本自动标记\n" +
                "✓ 标准YOLO格式导出\n" +
                "✓ 覆盖模式推理\n" +
                "✓ 智能移除低置信度重叠框\n\n" +
                "更新日志 v1.5.0：\n" +
                "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                "• 新增覆盖所有标签选项\n" +
                "• 新增移除低置信度重叠框功能\n" +
                "• 优化推理逻辑\n" +
                "• 提升推理准确度\n" +
                "• 改进用户体验\n\n" +
                "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                "如有问题或建议，欢迎联系作者\n" +
                "GitHub: 暂未提交\n\n";

            System.Windows.MessageBox.Show(
                aboutText,
                "关于 BryanYOLO",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum InferenceTarget
    {
        All,
        Empty,
        NotAnnotated
    }

    public enum YoloVersion
    {
        V5,
        V8,
        V10,
        V11
    }
}
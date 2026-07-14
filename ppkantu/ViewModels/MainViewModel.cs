using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Ppkantu.Config;
using Ppkantu.Models;
using Ppkantu.Services;
using Ppkantu.Utils;

namespace Ppkantu.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    #region 服务

    private readonly ImageLoaderService _imageLoader;
    private readonly ImageEditService _imageEdit;
    private readonly FileDialogService _fileDialog;
    private readonly ClipboardService _clipboard;
    private readonly IOcrService _ocrService;
    private readonly AppSettings _settings;
    private int _imageLoadVersion;

    #endregion

    #region 构造函数

    public MainViewModel()
    {
        _settings = AppSettings.LoadWithEnvironmentOverrides();
        _imageLoader = new ImageLoaderService();
        _imageEdit = new ImageEditService();
        _fileDialog = new FileDialogService();
        _clipboard = new ClipboardService();
        _ocrService = _settings.OcrProvider?.ToLowerInvariant() switch
        {
            "api" => new ApiOcrService(_settings),
            "mock" => new MockOcrService(),
            _ => new WindowsOcrService()
        };

        InitializeCommands();
    }

    #endregion

    #region 属性

    private BitmapImage? _currentImage;
    public BitmapImage? CurrentImage
    {
        get => _currentImage;
        set { _currentImage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasImage)); }
    }

    public bool HasImage => CurrentImage != null;

    private bool _isGif;
    public bool IsGif
    {
        get => _isGif;
        set { _isGif = value; OnPropertyChanged(); }
    }

    private System.Windows.Media.ImageSource? _currentGifSource;
    public System.Windows.Media.ImageSource? CurrentGifSource
    {
        get => _currentGifSource;
        set { _currentGifSource = value; OnPropertyChanged(); }
    }

    private string _title = "鹏鹏看图";
    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    private string _currentFileName = string.Empty;
    public string CurrentFileName
    {
        get => _currentFileName;
        set { _currentFileName = value; OnPropertyChanged(); }
    }

    private string _currentFilePath = string.Empty;
    public string CurrentFilePath
    {
        get => _currentFilePath;
        set { _currentFilePath = value; OnPropertyChanged(); }
    }

    private ObservableCollection<ImageFileInfo> _imageFiles = new();
    public ObservableCollection<ImageFileInfo> ImageFiles
    {
        get => _imageFiles;
        set { _imageFiles = value; OnPropertyChanged(); }
    }

    private int _currentIndex = -1;
    public int CurrentIndex
    {
        get => _currentIndex;
        set
        {
            if (_currentIndex != value)
            {
                _currentIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IndexDisplay));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
            }
        }
    }

    public string IndexDisplay =>
        ImageFiles.Count > 0 && CurrentIndex >= 0
            ? $"{CurrentIndex + 1} / {ImageFiles.Count}"
            : "0 / 0";

    private bool _isImageOperationBusy;
    public bool IsImageOperationBusy
    {
        get => _isImageOperationBusy;
        private set
        {
            if (_isImageOperationBusy != value)
            {
                _isImageOperationBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                NotifyImageCommandCanExecuteChanged();
                RefreshNavCommands();
            }
        }
    }

    private double _zoomLevel = 1.0;
    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            _zoomLevel = Math.Clamp(value, 0.05, 50.0);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ZoomDisplay));
        }
    }

    public string ZoomDisplay => $"{ZoomLevel * 100:F0}%";

    // 记录是否处于原始尺寸模式（旋转/翻转后恢复）
    private bool _isOriginalSizeMode;

    private string _statusMessage = "就绪";
    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    private string _imageDimensions = string.Empty;
    public string ImageDimensions
    {
        get => _imageDimensions;
        set { _imageDimensions = value; OnPropertyChanged(); }
    }

    private string _fileSizeDisplay = string.Empty;
    public string FileSizeDisplay
    {
        get => _fileSizeDisplay;
        set { _fileSizeDisplay = value; OnPropertyChanged(); }
    }

    private bool _isFullScreen;
    public bool IsFullScreen
    {
        get => _isFullScreen;
        set { _isFullScreen = value; OnPropertyChanged(); }
    }

    private bool _isEditMode;
    public bool IsEditMode
    {
        get => _isEditMode;
        set
        {
            if (_isEditMode != value)
            {
                _isEditMode = value;
                OnPropertyChanged();
                // 编辑模式和 OCR 面板互斥
                if (value && IsOcrPanelVisible) IsOcrPanelVisible = false;
            }
        }
    }

    private bool _isOcrPanelVisible;
    public bool IsOcrPanelVisible
    {
        get => _isOcrPanelVisible;
        set
        {
            if (_isOcrPanelVisible != value)
            {
                _isOcrPanelVisible = value;
                OnPropertyChanged();
                // OCR 和编辑模式互斥
                if (value && IsEditMode) IsEditMode = false;
            }
        }
    }

    private bool _isOcrRunning;
    public bool IsOcrRunning
    {
        get => _isOcrRunning;
        set { _isOcrRunning = value; OnPropertyChanged(); }
    }

    private string _ocrResultText = string.Empty;
    public string OcrResultText
    {
        get => _ocrResultText;
        set { _ocrResultText = value; OnPropertyChanged(); }
    }

    private double _ocrProgress;
    public double OcrProgress
    {
        get => _ocrProgress;
        set { _ocrProgress = value; OnPropertyChanged(); }
    }

    public string OcrServiceName => _ocrService.ServiceName;

    private CancellationTokenSource? _ocrCts;

    // 文件关联
    private bool _isFileAssociated;
    public bool IsFileAssociated
    {
        get => _isFileAssociated;
        set { _isFileAssociated = value; OnPropertyChanged(); OnPropertyChanged(nameof(FileAssocLabel)); OnPropertyChanged(nameof(FileAssocIcon)); OnPropertyChanged(nameof(FileAssocToolTip)); }
    }

    public string FileAssocLabel => IsFileAssociated ? "取消关联" : "关联";
    public string FileAssocIcon => IsFileAssociated ? "🔗" : "📎";
    public string FileAssocToolTip => IsFileAssociated
        ? "取消 ppkantu 图片打开方式关联"
        : "关联 ppkantu 图片打开方式";

    // 主题
    private bool _isDarkMode;
    public bool IsDarkMode
    {
        get => _isDarkMode;
        set { _isDarkMode = value; OnPropertyChanged(); UpdateThemeDisplay(); }
    }

    private string _themeIcon = "☀️";
    public string ThemeIcon
    {
        get => _themeIcon;
        set { _themeIcon = value; OnPropertyChanged(); }
    }

    private string _themeLabel = "浅色";
    public string ThemeLabel
    {
        get => _themeLabel;
        set { _themeLabel = value; OnPropertyChanged(); }
    }

    private string _themeToolTip = "切换为深色模式";
    public string ThemeToolTip
    {
        get => _themeToolTip;
        set { _themeToolTip = value; OnPropertyChanged(); }
    }

    // 裁切模式
    private bool _isCropMode;
    public bool IsCropMode
    {
        get => _isCropMode;
        set
        {
            if (_isCropMode != value)
            {
                _isCropMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsInteractiveEditMode));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                RefreshNavCommands();
                if (value) { IsAnnotationMode = false; IsEditMode = false; IsDoodleMode = false; }
            }
        }
    }

    // 裁切比例
    private double _cropAspectRatio; // 0=自由, 其他=宽/高
    public double CropAspectRatio
    {
        get => _cropAspectRatio;
        set { _cropAspectRatio = value; OnPropertyChanged(); OnPropertyChanged(nameof(CropRatioLabel)); }
    }

    public string CropRatioLabel => _cropAspectRatio switch
    {
        0 => "自由",
        1.0 => "1:1",
        4.0 / 3.0 => "4:3",
        3.0 / 2.0 => "3:2",
        16.0 / 9.0 => "16:9",
        9.0 / 16.0 => "9:16",
        _ => $"{_cropAspectRatio:F2}"
    };

    // 标注模式
    private bool _isAnnotationMode;
    public bool IsAnnotationMode
    {
        get => _isAnnotationMode;
        set
        {
            if (_isAnnotationMode != value)
            {
                _isAnnotationMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsInteractiveEditMode));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                RefreshNavCommands();
                if (value) { IsCropMode = false; IsEditMode = false; IsDoodleMode = false; }
            }
        }
    }

    // 标注工具
    private AnnotationTool _currentAnnotationTool = AnnotationTool.Rectangle;
    public AnnotationTool CurrentAnnotationTool
    {
        get => _currentAnnotationTool;
        set { _currentAnnotationTool = value; OnPropertyChanged(); }
    }

    // 涂抹模式
    private bool _isDoodleMode;
    public bool IsDoodleMode
    {
        get => _isDoodleMode;
        set
        {
            if (_isDoodleMode != value)
            {
                _isDoodleMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsInteractiveEditMode));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                RefreshNavCommands();
                if (value) { IsCropMode = false; IsAnnotationMode = false; IsEditMode = false; }
                // 涂抹模式退出时，如果有待处理的保存请求，触发保存
                if (!value && _pendingSaveAfterDoodle)
                {
                    _pendingSaveAfterDoodle = false;
                    NeedFlushAnnotations?.Invoke(this, EventArgs.Empty);
                    _ = SaveAsAsync();
                }
            }
        }
    }

    // 涂抹粗细（半径，单位像素）
    private double _doodleThickness = 30;
    public double DoodleThickness
    {
        get => _doodleThickness;
        set { _doodleThickness = value; OnPropertyChanged(); }
    }

    // 涂抹颜色（固定红色）
    public string DoodleColor => "#FFE53935";

    // 标注颜色
    private string _annotationColor = "#FFE53935";
    public string AnnotationColor
    {
        get => _annotationColor;
        set { _annotationColor = value; OnPropertyChanged(); }
    }

    // 标注文字大小
    private double _annotationFontSize = 24;
    public double AnnotationFontSize
    {
        get => _annotationFontSize;
        set { _annotationFontSize = value; OnPropertyChanged(); }
    }

    // 标注形状列表
    private List<AnnotationShape> _annotationShapes = new();
    public List<AnnotationShape> AnnotationShapes
    {
        get => _annotationShapes;
        set { _annotationShapes = value; OnPropertyChanged(); }
    }

    // 编辑属性
    private int _resizeWidth;
    public int ResizeWidth
    {
        get => _resizeWidth;
        set { _resizeWidth = value; OnPropertyChanged(); }
    }

    private int _resizeHeight;
    public int ResizeHeight
    {
        get => _resizeHeight;
        set { _resizeHeight = value; OnPropertyChanged(); }
    }

    private string _addTextContent = string.Empty;
    public string AddTextContent
    {
        get => _addTextContent;
        set { _addTextContent = value; OnPropertyChanged(); }
    }

    private int _compressionQuality = 60;
    public int CompressionQuality
    {
        get => _compressionQuality;
        set
        {
            var clamped = Math.Clamp(value, 1, 100);
            if (_compressionQuality == clamped) return;
            _compressionQuality = clamped;
            OnPropertyChanged();
        }
    }

    #endregion

    #region 命令

    public ICommand OpenFileCommand { get; private set; } = null!;
    public ICommand PreviousImageCommand { get; private set; } = null!;
    public ICommand NextImageCommand { get; private set; } = null!;
    public ICommand ZoomInCommand { get; private set; } = null!;
    public ICommand ZoomOutCommand { get; private set; } = null!;
    public ICommand FitToWindowCommand { get; private set; } = null!;
    public ICommand OriginalSizeCommand { get; private set; } = null!;
    public ICommand RotateLeftCommand { get; private set; } = null!;
    public ICommand RotateRightCommand { get; private set; } = null!;
    public ICommand FlipHorizontalCommand { get; private set; } = null!;
    public ICommand FlipVerticalCommand { get; private set; } = null!;
    public ICommand ToggleFullScreenCommand { get; private set; } = null!;
    public ICommand DeleteImageCommand { get; private set; } = null!;
    public ICommand SaveAsCommand { get; private set; } = null!;
    public ICommand CopyImageCommand { get; private set; } = null!;
    public ICommand CopyPathCommand { get; private set; } = null!;
    public ICommand ToggleEditModeCommand { get; private set; } = null!;
    public ICommand ToggleOcrPanelCommand { get; private set; } = null!;
    public ICommand RunOcrCommand { get; private set; } = null!;
    public ICommand CancelOcrCommand { get; private set; } = null!;
    public ICommand CopyOcrResultCommand { get; private set; } = null!;
    public ICommand ExportOcrResultCommand { get; private set; } = null!;
    public ICommand ClearOcrResultCommand { get; private set; } = null!;
    public ICommand SaveEditCommand { get; private set; } = null!;
    public ICommand CancelEditCommand { get; private set; } = null!;
    public ICommand ApplyResizeCommand { get; private set; } = null!;
    public ICommand ApplyMosaicCommand { get; private set; } = null!;
    public ICommand StartCropCommand { get; private set; } = null!;
    public ICommand ApplyCropCommand { get; private set; } = null!;
    public ICommand CancelCropCommand { get; private set; } = null!;
    public ICommand StartAnnotationCommand { get; private set; } = null!;
    public ICommand StartDoodleCommand { get; private set; } = null!;
    public ICommand ApplyAnnotationCommand { get; private set; } = null!;
    public ICommand CancelAnnotationCommand { get; private set; } = null!;
    public ICommand UndoCommand { get; private set; } = null!;
    public ICommand RedoCommand { get; private set; } = null!;
    public ICommand ToggleThemeCommand { get; private set; } = null!;
    public ICommand ToggleFileAssociationCommand { get; private set; } = null!;
    public ICommand CompressCommand { get; private set; } = null!;

    public bool CanGoPrevious => ImageFiles.Count > 1 && !IsInteractiveEditMode && !IsImageOperationBusy;
    public bool CanGoNext => ImageFiles.Count > 1 && !IsInteractiveEditMode && !IsImageOperationBusy;
    public bool IsInteractiveEditMode => IsCropMode || IsAnnotationMode || IsDoodleMode;
    private static bool IsTextEditingActive => Keyboard.FocusedElement is TextBox;

    private void InitializeCommands()
    {
        OpenFileCommand = new RelayCommand(OpenFile);
        PreviousImageCommand = new RelayCommand(GoPrevious, () => CanGoPrevious);
        NextImageCommand = new RelayCommand(GoNext, () => CanGoNext);
        ZoomInCommand = new RelayCommand(ZoomIn);
        ZoomOutCommand = new RelayCommand(ZoomOut);
        FitToWindowCommand = new RelayCommand(FitToWindow);
        OriginalSizeCommand = new RelayCommand(OriginalSize);
        RotateLeftCommand = new RelayCommand(RotateLeft);
        RotateRightCommand = new RelayCommand(RotateRight);
        FlipHorizontalCommand = new RelayCommand(FlipHorizontal);
        FlipVerticalCommand = new RelayCommand(FlipVertical);
        ToggleFullScreenCommand = new RelayCommand(ToggleFullScreen);
        DeleteImageCommand = new RelayCommand(DeleteImage);
        SaveAsCommand = new RelayCommand(SaveAs);
        CopyImageCommand = new RelayCommand(CopyImage);
        CopyPathCommand = new RelayCommand(CopyPath);
        ToggleEditModeCommand = new RelayCommand(ToggleEditMode);
        ToggleOcrPanelCommand = new RelayCommand(ToggleOcrPanel);
        RunOcrCommand = new AsyncRelayCommand(RunOcrAsync, () => !IsOcrRunning && HasImage);
        CancelOcrCommand = new RelayCommand(CancelOcr, () => IsOcrRunning);
        CopyOcrResultCommand = new RelayCommand(CopyOcrResult);
        ExportOcrResultCommand = new RelayCommand(ExportOcrResult);
        ClearOcrResultCommand = new RelayCommand(ClearOcrResult);
        SaveEditCommand = new AsyncRelayCommand(SaveEditAsync);
        CancelEditCommand = new RelayCommand(CancelEdit);
        ApplyResizeCommand = new AsyncRelayCommand(ApplyResizeAsync);
        ApplyMosaicCommand = new AsyncRelayCommand(ApplyMosaicAsync);
        StartCropCommand = new RelayCommand(StartCrop);
        ApplyCropCommand = new AsyncRelayCommand(ApplyCropAsync);
        CancelCropCommand = new RelayCommand(CancelCrop);
        StartAnnotationCommand = new RelayCommand(StartAnnotation);
        StartDoodleCommand = new RelayCommand(StartDoodle);
        ApplyAnnotationCommand = new AsyncRelayCommand(ApplyAnnotationAsync);
        CancelAnnotationCommand = new RelayCommand(CancelAnnotation);
        UndoCommand = new RelayCommand(Undo, () => IsAnnotationMode || CanUndo);
        RedoCommand = new RelayCommand(Redo, () => IsAnnotationMode || CanRedo);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        ToggleFileAssociationCommand = new RelayCommand(ToggleFileAssociation);
        CompressCommand = new AsyncRelayCommand(CompressImageAsync, () => HasImage && !IsImageOperationBusy);
        // 初始化主题状态：读取 ThemeService 实际应用的状态
        _isDarkMode = ThemeService.CurrentIsDark;
        UpdateThemeDisplay();
        // 文件关联状态延迟到 Window.Loaded 后通过 InitializeFileAssocAsync 读取，
        // 避免在构造函数里同步访问注册表拖慢启动速度。
    }

    #endregion

    #region 图片导航

    public event EventHandler? NeedFitToWindow;
    public event EventHandler? NeedOriginalSize;
    public event EventHandler? NeedZoomIn;
    public event EventHandler? NeedZoomOut;
    public event EventHandler? NeedConfirmCrop;
    public event EventHandler? NeedConfirmAnnotation;
    public event EventHandler? NeedConfirmDoodle;
    public event EventHandler? NeedDeleteAnnotation;
    public event EventHandler? NeedUndoAnnotation;
    public event EventHandler? NeedRedoAnnotation;
    public event EventHandler? NeedFlushAnnotations;

    // 当前工作副本路径（旋转/翻转后的临时文件）
    private string? _workingCopyPath;

    // 涂抹确认后自动保存标记
    private bool _pendingSaveAfterDoodle;

    // 前进后退历史
    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    public bool CanUndo => _historyIndex > 0;
    public bool CanRedo => _historyIndex < _history.Count - 1;

    /// <summary>
    /// 将当前状态推入历史栈
    /// </summary>
    private void PushHistory(string workingPath)
    {
        // 丢弃当前位置之后的历史（重做分支）
        if (_historyIndex < _history.Count - 1)
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);

        _history.Add(workingPath);
        _historyIndex = _history.Count - 1;
        NotifyHistoryChanged();
    }

    private void NotifyHistoryChanged()
    {
        (UndoCommand as IRelayCommand)?.NotifyCanExecuteChanged();
        (RedoCommand as IRelayCommand)?.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 撤销（Ctrl+Z）
    /// </summary>
    private async void Undo()
    {
        // 标注文字输入框拥有焦点时，Ctrl+Z 应交给输入框自身处理。
        if (IsTextEditingActive) return;

        if (IsAnnotationMode)
        {
            NeedUndoAnnotation?.Invoke(this, EventArgs.Empty);
            return;
        }

        // 涂抹模式下不退出模式，只撤销上一笔
        if (IsDoodleMode)
        {
            if (!CanUndo) return;
            _historyIndex--;
            await RestoreFromHistoryAsync();
            return;
        }
        if (!CanUndo) return;
        CancelModesIfActive();
        _historyIndex--;
        await RestoreFromHistoryAsync();
    }

    /// <summary>
    /// 重做（Ctrl+Y）
    /// </summary>
    private async void Redo()
    {
        // 标注文字输入框拥有焦点时，Ctrl+Y 应交给输入框自身处理。
        if (IsTextEditingActive) return;

        if (IsAnnotationMode)
        {
            NeedRedoAnnotation?.Invoke(this, EventArgs.Empty);
            return;
        }

        // 涂抹模式下不退出模式，只重做
        if (IsDoodleMode)
        {
            if (!CanRedo) return;
            _historyIndex++;
            await RestoreFromHistoryAsync();
            return;
        }
        if (!CanRedo) return;
        CancelModesIfActive();
        _historyIndex++;
        await RestoreFromHistoryAsync();
    }

    /// <summary>
    /// 重置 GIF 动画状态（编辑操作后调用）
    /// </summary>
    private void ResetGifState()
    {
        IsGif = false;
        CurrentGifSource = null;
    }

    private async Task RestoreFromHistoryAsync()
    {
        var path = _history[_historyIndex];
        if (!File.Exists(path)) return;

        _workingCopyPath = path;
        var image = await _imageLoader.LoadImageAsync(path);
        if (image != null)
        {
            CurrentImage = image;
            ImageDimensions = $"{image.PixelWidth} x {image.PixelHeight}";
            FileSizeDisplay = FileSizeFormatter.Format(new FileInfo(path).Length);
            ResetGifState();
            Title = CurrentFileName;
            StatusMessage = "已恢复历史状态";
            if (_isOriginalSizeMode) NeedOriginalSize?.Invoke(this, EventArgs.Empty);
            else NeedFitToWindow?.Invoke(this, EventArgs.Empty);
        }
        NotifyHistoryChanged();
    }

    private void OpenFile()
    {
        CancelModesIfActive();
        var filePath = _fileDialog.OpenImageFile();
        if (filePath != null) LoadImageFromPath(filePath);
    }

    private async void LoadImageFromPath(string filePath)
    {
        try
        {
            StatusMessage = "正在加载...";
            _workingCopyPath = null;

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                var files = _imageLoader.ScanDirectory(directory);
                ImageFiles.Clear();
                foreach (var f in files) ImageFiles.Add(f);
            }

            var index = ImageFiles.ToList().FindIndex(f =>
                string.Equals(f.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                CurrentIndex = index;
            }
            else
            {
                var info = ImageFileInfo.FromFile(filePath);
                if (info != null)
                {
                    var dims = ImageLoaderService.GetImageDimensions(filePath);
                    if (dims.HasValue) { info.Width = dims.Value.Width; info.Height = dims.Value.Height; }
                    ImageFiles.Clear();
                    ImageFiles.Add(info);
                    CurrentIndex = 0;
                }
            }

            await LoadCurrentImageAsync();
            RefreshNavCommands();
            StatusMessage = "就绪";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
            MessageBox.Show($"无法加载图片: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadCurrentImageAsync()
    {
        if (CurrentIndex < 0 || CurrentIndex >= ImageFiles.Count)
        {
            StatusMessage = "没有可加载的图片";
            return;
        }

        var loadVersion = ++_imageLoadVersion;
        var requestedIndex = CurrentIndex;
        var fileInfo = ImageFiles[requestedIndex];
        var requestedPath = fileInfo.FilePath;
        // 通过上一张/下一张切换到新文件时，必须丢弃上一张图的编辑/压缩工作副本。
        // 否则 GetWorkingPath() 会继续指向旧 temp 文件，导致在新图上点压缩时实际压缩旧图，表现为“跳转”。
        _workingCopyPath = null;
        var image = await _imageLoader.LoadImageAsync(requestedPath);

        // 防止上一次异步加载在压缩/导航后才返回，覆盖当前图片，看起来像“跳转”。
        if (loadVersion != _imageLoadVersion || CurrentIndex != requestedIndex ||
            !string.Equals(requestedPath, fileInfo.FilePath, StringComparison.OrdinalIgnoreCase))
            return;

        if (image == null)
        {
            var message = $"无法加载图片：{fileInfo.FileName}";
            StatusMessage = message;
            MessageBox.Show($"{message}\n\n文件路径：{fileInfo.FilePath}", "打开失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        CurrentImage = image;
        CurrentFileName = fileInfo.FileName;
        CurrentFilePath = fileInfo.FilePath;
        ImageDimensions = $"{image.PixelWidth} x {image.PixelHeight}";
        FileSizeDisplay = FileSizeFormatter.Format(fileInfo.FileSize);
        Title = fileInfo.FileName;
        ResizeWidth = image.PixelWidth;
        ResizeHeight = image.PixelHeight;
        ZoomLevel = 1.0;

        // GIF 动画支持
        var ext = Path.GetExtension(fileInfo.FilePath);
        if (string.Equals(ext, ".gif", StringComparison.OrdinalIgnoreCase))
        {
            var gifSource = _imageLoader.LoadGifForAnimation(fileInfo.FilePath);
            IsGif = gifSource != null;
            CurrentGifSource = gifSource;
        }
        else
        {
            IsGif = false;
            CurrentGifSource = null;
        }

        // 推入初始状态到历史
        _history.Clear();
        _history.Add(fileInfo.FilePath);
        _historyIndex = 0;
        NotifyHistoryChanged();
        NeedFitToWindow?.Invoke(this, EventArgs.Empty);
    }

    public void GoPrevious()
    {
        if (ImageFiles.Count == 0 || IsInteractiveEditMode || IsImageOperationBusy) return;
        CurrentIndex = (CurrentIndex <= 0) ? ImageFiles.Count - 1 : CurrentIndex - 1;
        _ = LoadCurrentImageAsync();
        RefreshNavCommands();
    }

    public void GoNext()
    {
        if (ImageFiles.Count == 0 || IsInteractiveEditMode || IsImageOperationBusy) return;
        CurrentIndex = (CurrentIndex >= ImageFiles.Count - 1 || CurrentIndex < 0) ? 0 : CurrentIndex + 1;
        _ = LoadCurrentImageAsync();
        RefreshNavCommands();
    }

    private void RefreshNavCommands()
    {
        (PreviousImageCommand as IRelayCommand)?.NotifyCanExecuteChanged();
        (NextImageCommand as IRelayCommand)?.NotifyCanExecuteChanged();
    }

    #endregion

    #region 缩放

    public void ZoomIn() { CancelModesIfActive(); NeedZoomIn?.Invoke(this, EventArgs.Empty); }
    public void ZoomOut() { CancelModesIfActive(); NeedZoomOut?.Invoke(this, EventArgs.Empty); }
    public void FitToWindow() { CancelModesIfActive(); _isOriginalSizeMode = false; NeedFitToWindow?.Invoke(this, EventArgs.Empty); }
    public void OriginalSize() { CancelModesIfActive(); _isOriginalSizeMode = true; ZoomLevel = 1.0; NeedOriginalSize?.Invoke(this, EventArgs.Empty); }

    #endregion

    #region 变换

    private void RotateLeft() { CancelModesIfActive(); Rotate(-90); }
    private void RotateRight() { CancelModesIfActive(); Rotate(90); }

    /// <summary>
    /// 获取当前实际图片路径（可能是旋转/翻转后的工作副本）
    /// </summary>
    private string GetWorkingPath()
    {
        if (!string.IsNullOrEmpty(_workingCopyPath) && File.Exists(_workingCopyPath))
            return _workingCopyPath;
        return CurrentFilePath;
    }

    /// <summary>
    /// 创建编辑预览用临时文件。
    /// 使用 BMP 作为中间格式，避免每次旋转/翻转/涂抹都进行 PNG 压缩；
    /// 最终另存时仍按用户选择的扩展名编码。
    /// </summary>
    private static string CreatePreviewTempPath()
    {
        return Path.Combine(Path.GetTempPath(), $"lvi_{Guid.NewGuid():N}.bmp");
    }

    private async void Rotate(int degrees)
    {
        if (!HasImage || string.IsNullOrEmpty(CurrentFilePath)) return;
        if (IsImageOperationBusy) return;
        try
        {
            IsImageOperationBusy = true;
            StatusMessage = $"旋转 {degrees}\u00b0...";
            var src = GetWorkingPath();
            var tempPath = CreatePreviewTempPath();
            await _imageEdit.RotateImageAsync(src, tempPath, degrees, _settings.JpegQuality);
            var newImage = await _imageLoader.LoadImageAsync(tempPath);
            if (newImage != null)
            {
                CurrentImage = newImage;
                ResetGifState();
                ImageDimensions = $"{newImage.PixelWidth} \u00d7 {newImage.PixelHeight}";
                _workingCopyPath = tempPath;
                PushHistory(tempPath);
                if (_isOriginalSizeMode) NeedOriginalSize?.Invoke(this, EventArgs.Empty);
                else NeedFitToWindow?.Invoke(this, EventArgs.Empty);
            }
            StatusMessage = "就绪";
        }
        catch (Exception ex) { StatusMessage = $"旋转失败: {ex.Message}"; }
        finally { IsImageOperationBusy = false; }
    }

    private async void FlipHorizontal()
    {
        CancelModesIfActive();
        if (!HasImage || string.IsNullOrEmpty(CurrentFilePath)) return;
        if (IsImageOperationBusy) return;
        try
        {
            IsImageOperationBusy = true;
            var src = GetWorkingPath();
            var tempPath = CreatePreviewTempPath();
            await _imageEdit.FlipImageAsync(src, tempPath, true, _settings.JpegQuality);
            var newImage = await _imageLoader.LoadImageAsync(tempPath);
            if (newImage != null)
            {
                CurrentImage = newImage;
                ResetGifState();
                ImageDimensions = $"{newImage.PixelWidth} \u00d7 {newImage.PixelHeight}";
                _workingCopyPath = tempPath;
                PushHistory(tempPath);
                if (_isOriginalSizeMode) NeedOriginalSize?.Invoke(this, EventArgs.Empty);
                else NeedFitToWindow?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex) { StatusMessage = $"翻转失败: {ex.Message}"; }
        finally { IsImageOperationBusy = false; }
    }

    private async void FlipVertical()
    {
        CancelModesIfActive();
        if (!HasImage || string.IsNullOrEmpty(CurrentFilePath)) return;
        if (IsImageOperationBusy) return;
        try
        {
            IsImageOperationBusy = true;
            var src = GetWorkingPath();
            var tempPath = CreatePreviewTempPath();
            await _imageEdit.FlipImageAsync(src, tempPath, false, _settings.JpegQuality);
            var newImage = await _imageLoader.LoadImageAsync(tempPath);
            if (newImage != null)
            {
                CurrentImage = newImage;
                ResetGifState();
                ImageDimensions = $"{newImage.PixelWidth} \u00d7 {newImage.PixelHeight}";
                _workingCopyPath = tempPath;
                PushHistory(tempPath);
                if (_isOriginalSizeMode) NeedOriginalSize?.Invoke(this, EventArgs.Empty);
                else NeedFitToWindow?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex) { StatusMessage = $"翻转失败: {ex.Message}"; }
        finally { IsImageOperationBusy = false; }
    }

    #endregion

    #region 文件操作

    private void ToggleFullScreen() => IsFullScreen = !IsFullScreen;

    private async void DeleteImage()
    {
        if (!HasImage || string.IsNullOrEmpty(CurrentFilePath)) return;

        var r = MessageBox.Show(
            $"确定删除 [{CurrentFileName}] ?\n此操作不可恢复。",
            "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (r != MessageBoxResult.Yes) return;

        try
        {
            File.Delete(CurrentFilePath);
            var removedIndex = CurrentIndex;
            ImageFiles.RemoveAt(CurrentIndex);

            if (ImageFiles.Count == 0)
            {
                CurrentImage = null; CurrentFileName = ""; CurrentFilePath = "";
                CurrentIndex = -1; ImageDimensions = ""; FileSizeDisplay = "";
                Title = "";
                ResetGifState();
            }
            else
            {
                CurrentIndex = Math.Min(removedIndex, ImageFiles.Count - 1);
                await LoadCurrentImageAsync();
            }
            RefreshNavCommands();
            StatusMessage = "已删除";
        }
        catch (IOException ex)
        {
            MessageBox.Show($"删除失败: 文件可能被占用。\n{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show("删除失败: 没有写入权限。", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"删除失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveAs()
    {
        // 如果正在涂抹，先确认涂抹再保存（而非直接取消丢失涂抹内容）
        if (IsDoodleMode)
        {
            _pendingSaveAfterDoodle = true;
            NeedConfirmDoodle?.Invoke(this, EventArgs.Empty);
            return;
        }

        CancelModesIfActive();
        if (!HasImage || string.IsNullOrEmpty(CurrentFilePath)) return;
        NeedFlushAnnotations?.Invoke(this, EventArgs.Empty);
        _ = SaveAsAsync();
    }

    private async Task SaveAsAsync()
    {
        if (!HasImage || string.IsNullOrEmpty(CurrentFilePath)) return;
        var savePath = _fileDialog.SaveImageFile(Path.GetFileNameWithoutExtension(CurrentFileName), CurrentFilePath);
        if (savePath == null) return;
        try
        {
            var src = GetWorkingPath();
            if (AnnotationShapes.Count > 0)
            {
                await _imageEdit.DrawAnnotationsAsync(src, savePath, AnnotationShapes,
                    CurrentImage!.PixelWidth, CurrentImage!.PixelHeight, _settings.JpegQuality);
            }
            else
            {
                await _imageEdit.SaveAsync(src, savePath, _settings.JpegQuality);
            }
            StatusMessage = $"已保存: {savePath}";
            // 保存后打开新文件
            LoadImageFromPath(savePath);
        }
        catch (Exception ex) { MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void CopyImage()
    {
        CancelModesIfActive();
        if (CurrentImage == null) return;
        StatusMessage = _clipboard.CopyImageToClipboard(CurrentImage)
            ? "已复制到剪贴板"
            : "复制失败: 剪贴板可能被其他程序占用";
    }

    private void CopyPath()
    {
        if (string.IsNullOrEmpty(CurrentFilePath)) return;
        StatusMessage = _clipboard.CopyPathToClipboard(CurrentFilePath)
            ? "路径已复制"
            : "复制失败: 剪贴板可能被其他程序占用";
    }

    #endregion

    #region 编辑模式

    private void ToggleEditMode() => IsEditMode = !IsEditMode;
    private void CancelEdit() => IsEditMode = false;

    private async Task SaveEditAsync()
    {
        if (!HasImage || string.IsNullOrEmpty(CurrentFilePath)) return;
        var savePath = _fileDialog.SaveImageFile(Path.GetFileNameWithoutExtension(CurrentFileName), CurrentFilePath);
        if (savePath == null) return;
        try
        {
            var src = GetWorkingPath();
            await _imageEdit.SaveAsync(src, savePath, _settings.JpegQuality);
            StatusMessage = $"已保存: {savePath}";
            IsEditMode = false;
        }
        catch (Exception ex) { MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async Task ApplyResizeAsync()
    {
        if (!HasImage || string.IsNullOrEmpty(CurrentFilePath)) return;
        if (IsImageOperationBusy) return;
        if (ResizeWidth <= 0 || ResizeHeight <= 0)
        { MessageBox.Show("请输入有效的宽高。", "提示"); return; }
        try
        {
            IsImageOperationBusy = true;
            var src = GetWorkingPath();
            var tempPath = CreatePreviewTempPath();
            await _imageEdit.ResizeImageAsync(src, tempPath, ResizeWidth, ResizeHeight, _settings.JpegQuality);
            var newImage = await _imageLoader.LoadImageAsync(tempPath);
            if (newImage != null)
            {
                CurrentImage = newImage;
                ResetGifState();
                ImageDimensions = $"{newImage.PixelWidth} \u00d7 {newImage.PixelHeight}";
                _workingCopyPath = tempPath;
                PushHistory(tempPath);
                if (_isOriginalSizeMode) NeedOriginalSize?.Invoke(this, EventArgs.Empty);
                else NeedFitToWindow?.Invoke(this, EventArgs.Empty);
            }
            StatusMessage = "尺寸已调整";
        }
        catch (Exception ex) { StatusMessage = $"调整失败: {ex.Message}"; }
        finally { IsImageOperationBusy = false; }
    }

    private async Task ApplyMosaicAsync()
    {
        if (!HasImage || string.IsNullOrEmpty(CurrentFilePath)) return;
        if (IsImageOperationBusy) return;
        if (string.IsNullOrWhiteSpace(AddTextContent))
        { MessageBox.Show("请输入区域: X,Y,宽,高", "提示"); return; }

        try
        {
            IsImageOperationBusy = true;
            var parts = AddTextContent.Split(',', ' ').Select(s => int.TryParse(s.Trim(), out var v) ? v : 0).ToArray();
            if (parts.Length < 4) { MessageBox.Show("格式: X,Y,宽,高", "提示"); return; }

            var src = GetWorkingPath();
            var tempPath = CreatePreviewTempPath();
            await _imageEdit.AddMosaicAsync(src, tempPath, parts[0], parts[1], parts[2], parts[3],
                blockSize: 10, jpegQuality: _settings.JpegQuality);
            var newImage = await _imageLoader.LoadImageAsync(tempPath);
            if (newImage != null)
            {
                CurrentImage = newImage;
                ResetGifState();
                ImageDimensions = $"{newImage.PixelWidth} \u00d7 {newImage.PixelHeight}";
                _workingCopyPath = tempPath;
                PushHistory(tempPath);
                if (_isOriginalSizeMode) NeedOriginalSize?.Invoke(this, EventArgs.Empty);
                else NeedFitToWindow?.Invoke(this, EventArgs.Empty);
            }
            StatusMessage = "马赛克已添加";
        }
        catch (Exception ex) { StatusMessage = $"马赛克失败: {ex.Message}"; }
        finally { IsImageOperationBusy = false; }
    }

    /// <summary>
    /// 按原格式压缩当前图片（保留格式和透明通道）
    /// </summary>
    private async Task CompressImageAsync()
    {
        if (!HasImage || string.IsNullOrEmpty(CurrentFilePath)) return;

        string? tempPath = null;
        try
        {
            IsImageOperationBusy = true;
            _imageLoadVersion++;
            var sourceFilePath = CurrentFilePath;
            var sourceFileName = CurrentFileName;
            var sourceIndex = CurrentIndex;
            var previousWorkingCopyPath = _workingCopyPath;
            StatusMessage = $"压缩中... (质量={CompressionQuality})";
            var src = GetWorkingPath();
            var ext = Path.GetExtension(sourceFilePath);
            var formatLabel = string.IsNullOrWhiteSpace(ext) ? "无扩展名" : ext;
            if (!ImageFormatHelper.IsCompressSupportedExtension(ext))
            {
                StatusMessage = $"压缩失败: 不支持 {formatLabel} 格式";
                var hint = string.Equals(ext, ".bmp", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(ext, ".dib", StringComparison.OrdinalIgnoreCase)
                    ? "BMP/DIB 是位图格式，保留原格式压缩通常不会变小，DIB 还可能因容器差异导致输出异常。\n请先另存为 JPG 或 WebP 后再压缩。"
                    : "请先另存为 JPG、PNG、GIF、WebP 或 TIFF 后再压缩。";
                MessageBox.Show(
                    $"当前格式 {formatLabel} 不支持直接压缩。\n{hint}",
                    "不支持压缩", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var tempExt = ext switch
            {
                ".jpeg" or ".jpe" or ".jfif" => ".jpg",
                _ => ext.ToLowerInvariant()
            };
            tempPath = Path.Combine(Path.GetTempPath(), $"lvi_{Guid.NewGuid():N}{tempExt}");
            var originalSize = new FileInfo(src).Length;
            await _imageEdit.CompressImageAsync(src, tempPath, CompressionQuality);
            var compressedSize = new FileInfo(tempPath).Length;
            var newImage = await _imageLoader.LoadImageAsync(tempPath);
            if (newImage == null)
            {
                DeleteTempFileQuietly(tempPath);
                _workingCopyPath = previousWorkingCopyPath;
                CurrentFilePath = sourceFilePath;
                CurrentFileName = sourceFileName;
                if (sourceIndex >= 0 && sourceIndex < ImageFiles.Count)
                    CurrentIndex = sourceIndex;
                Title = sourceFileName;
                StatusMessage = "压缩失败: 输出文件无法读取";
                MessageBox.Show(
                    "压缩文件已生成，但程序无法重新读取该输出。\n已保留当前原图，未切换图片、未写入历史。",
                    "压缩失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CurrentImage = newImage;
            ResetGifState();
            ImageDimensions = $"{newImage.PixelWidth} \u00d7 {newImage.PixelHeight}";
            _workingCopyPath = tempPath;
            CurrentFilePath = sourceFilePath;
            CurrentFileName = sourceFileName;
            if (sourceIndex >= 0 && sourceIndex < ImageFiles.Count)
                CurrentIndex = sourceIndex;
            Title = sourceFileName;
            PushHistory(tempPath);
            if (_isOriginalSizeMode) NeedOriginalSize?.Invoke(this, EventArgs.Empty);
            else NeedFitToWindow?.Invoke(this, EventArgs.Empty);

            var ratio = originalSize > 0 ? (1.0 - (double)compressedSize / originalSize) * 100 : 0;
            FileSizeDisplay = $"{FileSizeFormatter.Format(compressedSize)} (压缩 {ratio:F1}%)";
            StatusMessage = $"压缩完成: {FileSizeFormatter.Format(originalSize)} → {FileSizeFormatter.Format(compressedSize)}";
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrEmpty(tempPath))
                DeleteTempFileQuietly(tempPath);
            StatusMessage = $"压缩失败: {ex.Message}";
            MessageBox.Show($"压缩失败：{ex.Message}", "压缩失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { IsImageOperationBusy = false; }
    }

    private static void DeleteTempFileQuietly(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    #endregion

    #region 裁切

    public void StartCrop()
    {
        if (!HasImage) return;
        IsCropMode = true;
        CropAspectRatio = 0; // 默认自由比例
        StatusMessage = "拖拽选择裁切区域，Enter 确认，Esc 取消";
    }

    public Task ApplyCropAsync()
    {
        if (!HasImage || string.IsNullOrEmpty(CurrentFilePath)) return Task.CompletedTask;
        IsCropMode = false;
        StatusMessage = "就绪";
        return Task.CompletedTask;
    }

    private void CancelCrop()
    {
        IsCropMode = false;
        StatusMessage = "就绪";
    }

    /// <summary>
    /// 执行实际裁切（由 View 传入图像坐标矩形）
    /// </summary>
    public async Task ExecuteCropAsync(double imgX, double imgY, double imgW, double imgH)
    {
        if (!HasImage || string.IsNullOrEmpty(CurrentFilePath)) return;
        if (IsImageOperationBusy) return;
        try
        {
            IsImageOperationBusy = true;
            var src = GetWorkingPath();
            // 使用原文件扩展名，保持格式不变
            var ext = Path.GetExtension(CurrentFilePath);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";
            var tempPath = Path.Combine(Path.GetTempPath(), $"lvi_{Guid.NewGuid():N}{ext}");
            var cropArea = new SixLabors.ImageSharp.Rectangle((int)imgX, (int)imgY, (int)imgW, (int)imgH);
            await _imageEdit.CropImageAsync(src, tempPath, cropArea, _settings.JpegQuality);
            var newImage = await _imageLoader.LoadImageAsync(tempPath);
            if (newImage != null)
            {
                CurrentImage = newImage;
                ResetGifState();
                ImageDimensions = $"{newImage.PixelWidth} x {newImage.PixelHeight}";
                _workingCopyPath = tempPath;
                PushHistory(tempPath);
                if (_isOriginalSizeMode) NeedOriginalSize?.Invoke(this, EventArgs.Empty);
                else NeedFitToWindow?.Invoke(this, EventArgs.Empty);
            }
            IsCropMode = false;
            StatusMessage = "裁切完成";
        }
        catch (Exception ex) { StatusMessage = $"裁切失败: {ex.Message}"; }
        finally { IsImageOperationBusy = false; }
    }

    #endregion

    #region 标注

    private void StartAnnotation()
    {
        if (!HasImage) return;
        IsAnnotationMode = true;
        StatusMessage = "拖拽绘制标注；点击已有标注可选中，Delete 删除；保存/另存时才写入文件";
    }

    public void StartDoodle()
    {
        if (!HasImage) return;
        IsDoodleMode = true;
        StatusMessage = "涂抹即生效，Esc 取消，Enter 退出涂抹模式";
    }

    /// <summary>
    /// 单笔涂抹 — 画完立即应用马赛克（实时预览）
    /// </summary>
    public async Task ApplySingleDoodleStrokeAsync(List<System.Windows.Point> imgPoints, int imgRadius)
    {
        if (!HasImage || string.IsNullOrEmpty(CurrentFilePath) || imgPoints.Count < 2) return;
        if (IsImageOperationBusy) return;

        try
        {
            IsImageOperationBusy = true;
            var src = GetWorkingPath();
            var tempPath = CreatePreviewTempPath();
            int blockSize = Math.Max(5, imgRadius / 3);
            await _imageEdit.ApplyMosaicBrushAsync(src, tempPath, imgPoints,
                radius: imgRadius, blockSize: blockSize, jpegQuality: _settings.JpegQuality);

            var newImage = await _imageLoader.LoadImageAsync(tempPath);
            if (newImage != null)
            {
                CurrentImage = newImage;
                ResetGifState();
                ImageDimensions = $"{newImage.PixelWidth} x {newImage.PixelHeight}";
                _workingCopyPath = tempPath;
                PushHistory(tempPath);
                if (_isOriginalSizeMode) NeedOriginalSize?.Invoke(this, EventArgs.Empty);
                else NeedFitToWindow?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex) { StatusMessage = $"涂抹失败: {ex.Message}"; }
        finally { IsImageOperationBusy = false; }
    }

    /// <summary>
    /// 立即烘焙单个标注（实时预览，支持逐步撤销）
    /// </summary>
    public async Task BakeSingleAnnotationAsync(AnnotationShape shape)
    {
        if (!HasImage || string.IsNullOrEmpty(CurrentFilePath)) return;
        if (IsImageOperationBusy) return;

        try
        {
            IsImageOperationBusy = true;
            var src = GetWorkingPath();
            var tempPath = CreatePreviewTempPath();
            await _imageEdit.DrawAnnotationsAsync(src, tempPath, new List<AnnotationShape> { shape },
                CurrentImage!.PixelWidth, CurrentImage!.PixelHeight, _settings.JpegQuality);
            var newImage = await _imageLoader.LoadImageAsync(tempPath);
            if (newImage != null)
            {
                CurrentImage = newImage;
                ResetGifState();
                ImageDimensions = $"{newImage.PixelWidth} x {newImage.PixelHeight}";
                _workingCopyPath = tempPath;
                PushHistory(tempPath);
                if (_isOriginalSizeMode) NeedOriginalSize?.Invoke(this, EventArgs.Empty);
                else NeedFitToWindow?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex) { StatusMessage = $"标注失败: {ex.Message}"; }
        finally { IsImageOperationBusy = false; }
    }

    public async Task ExecuteDoodleAsync(List<AnnotationShape> shapes)
    {
        if (IsImageOperationBusy) return;
        if (!HasImage || string.IsNullOrEmpty(CurrentFilePath) || shapes.Count == 0)
        {
            IsDoodleMode = false;
            StatusMessage = "就绪";
            return;
        }

        try
        {
            IsImageOperationBusy = true;
            // Collect all points from all doodle strokes into one flat list
            // 同时取最大的半径（图片像素空间）
            var allPoints = new List<System.Windows.Point>();
            int maxRadius = 10;
            foreach (var shape in shapes)
            {
                if (shape.Points != null)
                    allPoints.AddRange(shape.Points);
                if (shape.StrokeThickness > maxRadius)
                    maxRadius = (int)shape.StrokeThickness;
            }

            if (allPoints.Count == 0)
            {
                IsDoodleMode = false;
                StatusMessage = "就绪";
                return;
            }

            var src = GetWorkingPath();
            var tempPath = CreatePreviewTempPath();
            // blockSize 取半径的 1/3，至少 5
            int blockSize = Math.Max(5, maxRadius / 3);
            await _imageEdit.ApplyMosaicBrushAsync(src, tempPath, allPoints,
                radius: maxRadius, blockSize: blockSize, jpegQuality: _settings.JpegQuality);
            var newImage = await _imageLoader.LoadImageAsync(tempPath);
            if (newImage != null)
            {
                CurrentImage = newImage;
                ResetGifState();
                ImageDimensions = $"{newImage.PixelWidth} x {newImage.PixelHeight}";
                _workingCopyPath = tempPath;
                PushHistory(tempPath);
                if (_isOriginalSizeMode) NeedOriginalSize?.Invoke(this, EventArgs.Empty);
                else NeedFitToWindow?.Invoke(this, EventArgs.Empty);
            }
            IsDoodleMode = false;

            // 如果涂抹确认前触发了保存，涂抹烘焙完成后继续执行保存
            if (_pendingSaveAfterDoodle)
            {
                _pendingSaveAfterDoodle = false;
                NeedFlushAnnotations?.Invoke(this, EventArgs.Empty);
                _ = SaveAsAsync();
            }

            StatusMessage = "马赛克已应用";
        }
        catch (Exception ex) { StatusMessage = $"马赛克失败: {ex.Message}"; }
        finally { IsImageOperationBusy = false; }
    }

    public Task ApplyAnnotationAsync()
    {
        if (!HasImage || string.IsNullOrEmpty(CurrentFilePath)) return Task.CompletedTask;
        IsAnnotationMode = false;
        AnnotationShapes.Clear();
        StatusMessage = "就绪";
        return Task.CompletedTask;
    }

    private void CancelAnnotation()
    {
        IsAnnotationMode = false;
        StatusMessage = "就绪";
    }

    /// <summary>
    /// 执行实际标注烘焙（由 View 传入形状列表）
    /// </summary>
    public async Task ExecuteAnnotationAsync(List<AnnotationShape> shapes)
    {
        AnnotationShapes = shapes.ToList();
        StatusMessage = shapes.Count > 0 ? "标注待保存" : "就绪";
        NotifyHistoryChanged();
        await Task.CompletedTask;
    }

    #endregion

    #region OCR

    private void ToggleOcrPanel() { CancelModesIfActive(); IsOcrPanelVisible = !IsOcrPanelVisible; }

    private async Task RunOcrAsync()
    {
        if (!HasImage || string.IsNullOrEmpty(CurrentFilePath)) return;
        IsOcrRunning = true;
        OcrProgress = 0;
        OcrResultText = "识别中...";
        StatusMessage = "OCR 识别中...";
        _ocrCts = new CancellationTokenSource();

        try
        {
            var result = await _ocrService.RecognizeAsync(CurrentFilePath, _ocrCts.Token);
            if (result.IsSuccess)
            {
                OcrResultText = result.Text;
                StatusMessage = $"识别完成 ({result.Blocks.Count} 个文本块)";
            }
            else
            {
                OcrResultText = "识别失败";
                StatusMessage = "OCR 识别失败";
            }
        }
        catch (OperationCanceledException)
        {
            OcrResultText = "识别失败";
            StatusMessage = "OCR 已取消";
        }
        catch (Exception)
        {
            OcrResultText = "识别失败";
            StatusMessage = "OCR 识别失败";
        }
        finally
        {
            IsOcrRunning = false;
            OcrProgress = 100;
            _ocrCts?.Dispose();
            _ocrCts = null;
            (RunOcrCommand as IRelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    private void CancelOcr() => _ocrCts?.Cancel();

    private void CopyOcrResult()
    {
        if (string.IsNullOrEmpty(OcrResultText)) return;
        _clipboard.CopyTextToClipboard(OcrResultText);
        StatusMessage = "结果已复制";
    }

    private void ExportOcrResult()
    {
        if (string.IsNullOrEmpty(OcrResultText)) return;
        var path = _fileDialog.SaveTextFile("ocr_result");
        if (path == null) return;
        try { File.WriteAllText(path, OcrResultText, System.Text.Encoding.UTF8); StatusMessage = $"已导出: {path}"; }
        catch (Exception ex) { MessageBox.Show($"导出失败: {ex.Message}", "错误"); }
    }

    private void ClearOcrResult() { OcrResultText = ""; OcrProgress = 0; }

    #endregion

    #region 主题切换

    /// <summary>
    /// 如果处于裁切/标注模式，自动取消（点击其他按钮时调用）
    /// </summary>
    private void CancelModesIfActive()
    {
        _pendingSaveAfterDoodle = false;
        if (IsCropMode) { IsCropMode = false; StatusMessage = "就绪"; }
        if (IsAnnotationMode) { IsAnnotationMode = false; StatusMessage = "就绪"; }
        if (IsDoodleMode) { IsDoodleMode = false; StatusMessage = "就绪"; }
    }

    private void ToggleTheme()
    {
        CancelModesIfActive();
        IsDarkMode = ThemeService.Toggle(IsDarkMode);
    }

    private void UpdateThemeDisplay()
    {
        ThemeIcon = _isDarkMode ? "🌙" : "☀️";
        ThemeLabel = _isDarkMode ? "深色" : "浅色";
        ThemeToolTip = _isDarkMode ? "切换为浅色模式" : "切换为深色模式";
    }

    #endregion

    #region 文件关联

    private void ToggleFileAssociation()
    {
        CancelModesIfActive();

        if (IsFileAssociated)
        {
            if (!FileAssociationService.IsRegisteredForCurrentExecutable())
            {
                IsFileAssociated = false;
                StatusMessage = "程序目录已变化，请重新关联";
                return;
            }

            if (FileAssociationService.Unassociate())
            {
                IsFileAssociated = false;
                StatusMessage = "已取消关联";
            }
            else
            {
                StatusMessage = "取消失败";
            }
        }
        else
        {
            if (FileAssociationService.Associate())
            {
                IsFileAssociated = FileAssociationService.IsRegisteredForCurrentExecutable();
                var blocked = FileAssociationService.GetUserChoiceBlockedExtensions();
                StatusMessage = blocked.Count == 0
                    ? "已关联"
                    : $"已关联，{blocked.Count}个格式需手动确认";
            }
            else
            {
                StatusMessage = "关联失败";
            }
        }
    }

    #endregion

    #region 公开方法

    /// <summary>
    /// 异步初始化文件关联状态（在 Window.Loaded 后调用，避免拖慢启动）
    /// </summary>
    public async Task InitializeFileAssociationAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                // 用户改名后，固定注册项仍保留，但命令路径会失效；先清理再按当前路径重建。
                FileAssociationService.RepairAssociationIfNeeded();
                var isAssociated = FileAssociationService.IsRegisteredForCurrentExecutable();
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _isFileAssociated = isAssociated;
                    OnPropertyChanged(nameof(IsFileAssociated));
                    OnPropertyChanged(nameof(FileAssocLabel));
                    OnPropertyChanged(nameof(FileAssocIcon));
                    OnPropertyChanged(nameof(FileAssocToolTip));
                }));
            });
        }
        catch { }
    }

    public void HandleDrop(string[] files)
    {
        var first = files.FirstOrDefault(f => ImageFormatHelper.IsSupportedImage(f));
        if (first != null) LoadImageFromPath(first);
        else MessageBox.Show("不支持的格式。\n支持: JPG, PNG, BMP, GIF, WebP, TIFF", "提示");
    }

    public void HandleKeyDown(Key key, bool ctrl, bool alt)
    {
        if (IsTextEditingActive) return;
        if (IsImageOperationBusy)
        {
            if (key == Key.F11) ToggleFullScreen();
            return;
        }

        if (IsInteractiveEditMode)
        {
            switch (key)
            {
                case Key.F11:
                    ToggleFullScreen();
                    break;
                case Key.Escape:
                    if (IsFullScreen) IsFullScreen = false;
                    else if (IsCropMode) CancelCrop();
                    else if (IsDoodleMode) { _pendingSaveAfterDoodle = false; IsDoodleMode = false; StatusMessage = "就绪"; }
                    else if (IsAnnotationMode) CancelAnnotation();
                    break;
                case Key.Enter:
                    if (IsCropMode) NeedConfirmCrop?.Invoke(this, EventArgs.Empty);
                    else if (IsDoodleMode) NeedConfirmDoodle?.Invoke(this, EventArgs.Empty);
                    else if (IsAnnotationMode) NeedConfirmAnnotation?.Invoke(this, EventArgs.Empty);
                    break;
                case Key.Delete:
                    if (IsAnnotationMode) NeedDeleteAnnotation?.Invoke(this, EventArgs.Empty);
                    break;
            }

            if (ctrl) switch (key)
            {
                case Key.S: SaveAs(); break;
                case Key.Z: Undo(); break;
                case Key.Y: Redo(); break;
            }
            return;
        }

        switch (key)
        {
            case Key.Left:
            case Key.Up:
                GoPrevious();
                break;
            case Key.Right:
            case Key.Down:
                GoNext();
                break;
            case Key.F11: ToggleFullScreen(); break;
            case Key.Escape:
                if (IsFullScreen) IsFullScreen = false;
                else if (IsCropMode) CancelCrop();
                else if (IsDoodleMode) { _pendingSaveAfterDoodle = false; IsDoodleMode = false; StatusMessage = "就绪"; }
                else if (IsAnnotationMode) CancelAnnotation();
                else if (IsOcrPanelVisible) IsOcrPanelVisible = false;
                else if (IsEditMode) IsEditMode = false;
                break;
            case Key.Enter:
                if (IsCropMode) NeedConfirmCrop?.Invoke(this, EventArgs.Empty);
                else if (IsDoodleMode) NeedConfirmDoodle?.Invoke(this, EventArgs.Empty);
                else if (IsAnnotationMode) NeedConfirmAnnotation?.Invoke(this, EventArgs.Empty);
                break;
            case Key.Delete:
                if (IsAnnotationMode) NeedDeleteAnnotation?.Invoke(this, EventArgs.Empty);
                break;
        }
        if (ctrl) switch (key)
        {
            case Key.O: OpenFile(); break;
            case Key.S: SaveAs(); break;
            case Key.C: CopyImage(); break;
            case Key.Z: Undo(); break;
            case Key.Y: Redo(); break;
            case Key.OemPlus: case Key.Add: ZoomIn(); break;
            case Key.OemMinus: case Key.Subtract: ZoomOut(); break;
            case Key.D0: FitToWindow(); break;
            case Key.D1: OriginalSize(); break;
        }
    }

    #endregion

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        // HasImage 变化时刷新所有依赖图片状态的命令；否则首次打开图片后
        // 这些按钮仍保持禁用，看起来像“无法点击”。
        if (n == nameof(HasImage))
            NotifyImageCommandCanExecuteChanged();
    }

    private void NotifyImageCommandCanExecuteChanged()
    {
        (RunOcrCommand as IRelayCommand)?.NotifyCanExecuteChanged();
        (CompressCommand as IRelayCommand)?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }

    #endregion
}

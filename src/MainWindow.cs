using System.Collections.ObjectModel;
using System.Linq;
using CyberSync.Helpers;
using CyberSync.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace CyberSync;

public sealed class MainWindow : Window
{
    private readonly ConfigManager _configManager;
    private readonly SchedulerService _schedulerService;
    private readonly SyncService _syncService;
    private readonly NotificationService _notificationService;

    private readonly ObservableCollection<string> _sourcePaths = [];
    private readonly ListView _sourceListView;
    private readonly TextBox _destinationTextBox;
    private readonly TimePicker _dailyTimePicker;
    private readonly CheckBox _closeToTrayCheckBox;
    private readonly TextBlock _syncProgressTextBlock;
    private readonly TextBlock _syncEtaTextBlock;
    private readonly TextBlock _driveLabelTextBlock;
    private readonly TextBlock _lastRunTextBlock;
    private readonly TextBlock _scheduleTextBlock;
    private readonly TextBlock _statusTextBlock;
    private AppWindow? _appWindow;
    private TrayIconService? _trayIconService;
    private readonly Button _saveButton;
    private readonly Button _syncNowButton;
    private readonly Button _scheduleButton;
    private readonly Button _exitButton;
    private bool _allowClose;
    private bool _closeHintShown;
    private bool _isSyncRunning;
    private AppConfig _loadedConfig = new();

    public MainWindow(AppRuntime runtime)
    {
        _configManager = runtime.ConfigManager;
        _schedulerService = runtime.SchedulerService;
        _syncService = runtime.SyncService;
        _notificationService = runtime.NotificationService;

        Title = "CyberSync";

        _sourceListView = new ListView {
            ItemsSource = _sourcePaths,
            SelectionMode = ListViewSelectionMode.Single,
            Height = 132
        };
        _destinationTextBox = new TextBox { PlaceholderText = "Choose a folder on the external drive" };
        _dailyTimePicker = new TimePicker { Time = new TimeSpan(9, 0, 0), ClockIdentifier = "24HourClock" };
        _closeToTrayCheckBox = new CheckBox {
            Content = "Closing the window sends CyberSync to the notification area",
            IsChecked = true
        };
        _syncProgressTextBlock = new TextBlock {
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap
        };
        _syncEtaTextBlock = new TextBlock {
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap
        };
        _driveLabelTextBlock = CreateValueTextBlock("Not selected");
        _lastRunTextBlock = CreateValueTextBlock("Never run");
        _scheduleTextBlock = CreateValueTextBlock("Task not created yet");
        _statusTextBlock = new TextBlock {
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        };
        _saveButton = new Button { Content = "Save" };
        _syncNowButton = new Button { Content = "Sync now" };
        _scheduleButton = new Button { Content = "Create or update schedule" };
        _exitButton = new Button { Content = "Exit app" };

        Content = BuildContent();
        _ = LoadConfigurationAsync();
    }

    public void InitializeWindowIntegration()
    {
        if (_appWindow is not null) {
            return;
        }

        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Closing += OnAppWindowClosing;
    }

    private UIElement BuildContent()
    {
        var root = new Grid {
            Padding = new Thickness(24),
            RowSpacing = 20
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var headerPanel = new StackPanel { Spacing = 8 };
        headerPanel.Children.Add(new TextBlock {
            Text = "CyberSync",
            FontSize = 28,
            FontWeight = FontWeights.SemiBold
        });
        headerPanel.Children.Add(new TextBlock {
            Text = "Mirror source folders onto an external drive with a daily Windows schedule.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78
        });
        root.Children.Add(headerPanel);

        var cardBorder = new Border {
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1)
        };
        Grid.SetRow(cardBorder, 1);

        var cardStack = new StackPanel { Spacing = 16 };
        cardBorder.Child = cardStack;

        cardStack.Children.Add(CreateSourceSection());
        cardStack.Children.Add(CreatePathRow("Destination folder", _destinationTextBox, BrowseDestination_Click));

        var infoGrid = new Grid { ColumnSpacing = 24 };
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var timePanel = new StackPanel { Spacing = 8 };
        timePanel.Children.Add(CreateLabel("Daily time"));
        timePanel.Children.Add(_dailyTimePicker);
        infoGrid.Children.Add(timePanel);

        var summaryGrid = new Grid { ColumnSpacing = 16, RowSpacing = 8 };
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        summaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        summaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        summaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(summaryGrid, 1);

        AddSummaryRow(summaryGrid, 0, "Drive label", _driveLabelTextBlock);
        AddSummaryRow(summaryGrid, 1, "Last sync", _lastRunTextBlock);
        AddSummaryRow(summaryGrid, 2, "Schedule", _scheduleTextBlock);

        infoGrid.Children.Add(summaryGrid);
        cardStack.Children.Add(infoGrid);
        cardStack.Children.Add(_closeToTrayCheckBox);
        cardStack.Children.Add(_syncProgressTextBlock);
        cardStack.Children.Add(_syncEtaTextBlock);
        cardStack.Children.Add(_statusTextBlock);

        root.Children.Add(cardBorder);

        var buttonPanel = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12
        };
        Grid.SetRow(buttonPanel, 2);

        _saveButton.Click += Save_Click;
        _syncNowButton.Click += SyncNow_Click;
        _scheduleButton.Click += Schedule_Click;
        _exitButton.Click += ExitApp_Click;
        buttonPanel.Children.Add(_saveButton);
        buttonPanel.Children.Add(_syncNowButton);
        buttonPanel.Children.Add(_scheduleButton);
        buttonPanel.Children.Add(_exitButton);

        root.Children.Add(buttonPanel);

        return new ScrollViewer { Content = root };
    }

    private UIElement CreateSourceSection()
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(CreateLabel("Source folders"));

        var listBorder = new Border {
            BorderThickness = new Thickness(1),
            Child = _sourceListView
        };
        stack.Children.Add(listBorder);

        stack.Children.Add(new TextBlock {
            Text = "Each source folder is mirrored into its own folder inside the destination root.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72
        });

        var buttonPanel = new StackPanel {
            Orientation = Orientation.Horizontal,
            Spacing = 12
        };

        var addButton = new Button { Content = "Add folder" };
        addButton.Click += BrowseSource_Click;
        buttonPanel.Children.Add(addButton);

        var removeButton = new Button { Content = "Remove selected" };
        removeButton.Click += RemoveSource_Click;
        buttonPanel.Children.Add(removeButton);

        var clearButton = new Button { Content = "Clear all" };
        clearButton.Click += ClearSources_Click;
        buttonPanel.Children.Add(clearButton);

        stack.Children.Add(buttonPanel);
        return stack;
    }

    private static UIElement CreatePathRow(string label, TextBox textBox, RoutedEventHandler clickHandler)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(CreateLabel(label));

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(textBox);

        var button = new Button { Content = "Browse" };
        button.Click += clickHandler;
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);

        stack.Children.Add(grid);
        return stack;
    }

    private static TextBlock CreateLabel(string text)
    {
        return new TextBlock {
            Text = text,
            FontWeight = FontWeights.SemiBold
        };
    }

    private static TextBlock CreateValueTextBlock(string text)
    {
        return new TextBlock {
            Text = text,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static void AddSummaryRow(Grid grid, int row, string label, FrameworkElement value)
    {
        var labelBlock = CreateLabel(label);
        Grid.SetRow(labelBlock, row);
        grid.Children.Add(labelBlock);

        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
    }

    private async Task LoadConfigurationAsync()
    {
        _loadedConfig = _configManager.Load();

        ReplaceSourcePaths(_loadedConfig.SourcePaths);
        _dailyTimePicker.Time = _loadedConfig.DailyTime.ToTimeSpan();
        _closeToTrayCheckBox.IsChecked = _loadedConfig.CloseToTrayOnClose;

        if (!string.IsNullOrWhiteSpace(_loadedConfig.DestinationVolumeLabel)) {
            DriveResolution resolution = DriveResolver.ResolveConfiguredDestination(_loadedConfig);
            if (resolution.Success) {
                _destinationTextBox.Text = resolution.ResolvedPath;
                UpdateDriveSummary(_loadedConfig);
            } else {
                _destinationTextBox.Text = string.Empty;
                _destinationTextBox.PlaceholderText = DescribeConfiguredDestination(_loadedConfig);
                UpdateDriveSummary(_loadedConfig, resolution.ErrorMessage);
            }
        }

        UpdateLastRunSummary(_loadedConfig);
        await RefreshScheduleStatusAsync();
    }

    private async Task RefreshScheduleStatusAsync()
    {
        CommandResult result = await _schedulerService.QueryTaskAsync();
        _scheduleTextBlock.Text = result.Success
            ? $"Daily at {TimeOnly.FromTimeSpan(_dailyTimePicker.Time):HH\\:mm}"
            : "Task not created yet";
    }

    private void UpdateDriveSummary(AppConfig config, string fallbackMessage = "")
    {
        if (!string.IsNullOrWhiteSpace(fallbackMessage)) {
            _driveLabelTextBlock.Text = $"{config.DestinationVolumeLabel} ({fallbackMessage})";
            return;
        }

        if (string.IsNullOrWhiteSpace(config.DestinationVolumeLabel)) {
            _driveLabelTextBlock.Text = "Not selected";
            return;
        }

        string relative = string.IsNullOrWhiteSpace(config.DestinationRelativePath)
            ? "(drive root)"
            : config.DestinationRelativePath;
        _driveLabelTextBlock.Text = $"{config.DestinationVolumeLabel} [{relative}]";
    }

    private void UpdateLastRunSummary(AppConfig config)
    {
        if (config.LastRunAt is null) {
            _lastRunTextBlock.Text = "Never run";
            return;
        }

        string status = string.IsNullOrWhiteSpace(config.LastRunStatus) ? "unknown" : config.LastRunStatus;
        _lastRunTextBlock.Text = $"{status} at {config.LastRunAt.Value.LocalDateTime:yyyy-MM-dd HH:mm:ss}";
    }

    private string DescribeConfiguredDestination(AppConfig config)
    {
        string relative = string.IsNullOrWhiteSpace(config.DestinationRelativePath)
            ? "(drive root)"
            : config.DestinationRelativePath;
        return $"Saved destination: {config.DestinationVolumeLabel} [{relative}]";
    }

    private bool BuildConfigFromUi(out AppConfig config, out string errorMessage)
    {
        List<string> sourcePaths = GetSourcePathsFromUi();
        config = new AppConfig {
            SourcePaths = sourcePaths,
            DestinationVolumeLabel = _loadedConfig.DestinationVolumeLabel,
            DestinationRelativePath = _loadedConfig.DestinationRelativePath,
            DailyTime = TimeOnly.FromTimeSpan(_dailyTimePicker.Time),
            CloseToTrayOnClose = _closeToTrayCheckBox.IsChecked != false,
            LastRunStatus = _loadedConfig.LastRunStatus,
            LastRunAt = _loadedConfig.LastRunAt
        };

        errorMessage = string.Empty;

        if (config.SourcePaths.Count == 0) {
            errorMessage = "Choose at least one source folder.";
            return false;
        }

        foreach (string sourcePath in config.SourcePaths) {
            if (!Directory.Exists(sourcePath)) {
                errorMessage = $"The source folder does not exist: {sourcePath}";
                return false;
            }
        }

        string? duplicateFolderName = FindDuplicateDestinationFolderName(config.SourcePaths);
        if (!string.IsNullOrWhiteSpace(duplicateFolderName)) {
            errorMessage = $"Two source folders would both sync into '{duplicateFolderName}'. Remove one or rename a folder.";
            return false;
        }

        string destinationText = _destinationTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(destinationText)) {
            DestinationSelection selection = DriveResolver.AnalyzeDestinationSelection(destinationText);
            if (!selection.Success) {
                errorMessage = selection.ErrorMessage;
                return false;
            }

            config.DestinationVolumeLabel = selection.VolumeLabel;
            config.DestinationRelativePath = selection.RelativePath;
        } else if (string.IsNullOrWhiteSpace(config.DestinationVolumeLabel)) {
            errorMessage = "Choose a destination folder on the external drive.";
            return false;
        }

        return true;
    }

    private async Task<bool> PersistConfigAsync(bool showSuccessMessage)
    {
        if (!BuildConfigFromUi(out AppConfig config, out string errorMessage)) {
            await ShowDialogAsync("CyberSync", errorMessage);
            return false;
        }

        if (!_configManager.Save(config, out errorMessage)) {
            await ShowDialogAsync("CyberSync", errorMessage);
            return false;
        }

        _loadedConfig = config;
        UpdateDriveSummary(_loadedConfig);
        UpdateLastRunSummary(_loadedConfig);
        await RefreshScheduleStatusAsync();
        ShowStatus("Configuration saved.");

        if (showSuccessMessage) {
            await ShowDialogAsync("CyberSync", "Configuration saved.");
        }

        return true;
    }

    private async void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        string? selected = await FolderPickerHelper.PickFolderAsync(this);
        if (string.IsNullOrWhiteSpace(selected)) {
            return;
        }

        string normalized = NormalizePath(selected);
        if (_sourcePaths.Any(path => string.Equals(path, normalized, StringComparison.OrdinalIgnoreCase))) {
            ShowStatus("That source folder is already selected.");
            return;
        }

        _sourcePaths.Add(normalized);
        ShowStatus("Source folder added.");
    }

    private void RemoveSource_Click(object sender, RoutedEventArgs e)
    {
        if (_sourceListView.SelectedItem is not string selectedPath) {
            ShowStatus("Select a source folder to remove.");
            return;
        }

        _sourcePaths.Remove(selectedPath);
        ShowStatus("Source folder removed.");
    }

    private void ClearSources_Click(object sender, RoutedEventArgs e)
    {
        if (_sourcePaths.Count == 0) {
            return;
        }

        _sourcePaths.Clear();
        ShowStatus("Source folders cleared.");
    }

    private async void BrowseDestination_Click(object sender, RoutedEventArgs e)
    {
        string? selected = await FolderPickerHelper.PickFolderAsync(this);
        if (string.IsNullOrWhiteSpace(selected)) {
            return;
        }

        DestinationSelection selection = DriveResolver.AnalyzeDestinationSelection(selected);
        if (!selection.Success) {
            await ShowDialogAsync("CyberSync", selection.ErrorMessage);
            return;
        }

        _destinationTextBox.Text = selection.SelectedPath;

        AppConfig preview = new() {
            SourcePaths = [.. _loadedConfig.SourcePaths],
            DestinationVolumeLabel = selection.VolumeLabel,
            DestinationRelativePath = selection.RelativePath,
            DailyTime = _loadedConfig.DailyTime,
            LastRunStatus = _loadedConfig.LastRunStatus,
            LastRunAt = _loadedConfig.LastRunAt
        };

        UpdateDriveSummary(preview);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await PersistConfigAsync(true);
    }

    private async void SyncNow_Click(object sender, RoutedEventArgs e)
    {
        await RunSyncAsync(true);
    }

    private async void Schedule_Click(object sender, RoutedEventArgs e)
    {
        if (!await PersistConfigAsync(false)) {
            return;
        }

        CommandResult result = await _schedulerService.CreateOrUpdateDailyTaskAsync(_loadedConfig.DailyTime);
        await RefreshScheduleStatusAsync();

        if (!result.Success) {
            string details = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            ShowStatus(result.Message);
            await ShowDialogAsync("CyberSync", $"{result.Message}\n\n{details}");
            return;
        }

        ShowStatus(result.Message);
        await ShowDialogAsync("CyberSync", result.Message);
    }

    private void ShowStatus(string message)
    {
        _statusTextBlock.Text = message;
        _statusTextBlock.Visibility = Visibility.Visible;
    }

    private void ExitApp_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        _trayIconService?.Dispose();
        Close();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose) {
            _trayIconService?.Dispose();
            return;
        }

        if (!_closeToTrayCheckBox.IsChecked.GetValueOrDefault(true)) {
            _trayIconService?.Dispose();
            return;
        }

        args.Cancel = true;
        MoveToBackground();
    }

    private void MoveToBackground()
    {
        if (!EnsureTrayIntegration()) {
            ShowStatus("Tray mode is unavailable on this device.");
            return;
        }

        _trayIconService!.ShowIcon();
        _appWindow!.Hide();
        ShowStatus("CyberSync is running in the background.");

        if (!_closeHintShown) {
            _closeHintShown = true;
            _trayIconService.ShowBackgroundHint();
        }
    }

    private void OnTrayIconActivated(object? sender, EventArgs e)
    {
        _trayIconService?.HideIcon();
        _appWindow?.Show();
        Activate();
    }

    private async void OnTraySyncNowRequested(object? sender, EventArgs e)
    {
        if (_isSyncRunning) {
            _notificationService.ShowInfo("CyberSync", "A sync is already running.");
            return;
        }

        await RunSyncAsync(false);
    }

    private void OnTrayExitRequested(object? sender, EventArgs e)
    {
        _allowClose = true;
        _trayIconService?.Dispose();
        Close();
    }

    private async Task RunSyncAsync(bool showDialogs)
    {
        if (_isSyncRunning) {
            if (!showDialogs) {
                _notificationService.ShowInfo("CyberSync", "A sync is already running.");
            } else {
                await ShowDialogAsync("CyberSync", "A sync is already running.");
            }
            return;
        }

        if (!await PersistConfigAsync(false)) {
            return;
        }

        _isSyncRunning = true;
        SetSyncUiState(true);
        ShowStatus("Running sync...");
        var progress = new Progress<SyncProgress>(UpdateSyncProgress);

        try {
            SyncResult result = await _syncService.RunSyncAsync(_loadedConfig, progress);
            _configManager.UpdateLastRun(result.Status, result.FinishedAt, out string saveError);
            _loadedConfig = _configManager.Load();
            UpdateLastRunSummary(_loadedConfig);

            _notificationService.ShowSyncResult(result);
            ShowStatus(result.Message);
            FinishSyncProgress(result.Message);

            if (showDialogs) {
                if (!string.IsNullOrWhiteSpace(saveError)) {
                    await ShowDialogAsync("CyberSync", saveError);
                }

                await ShowDialogAsync("CyberSync", result.Message);
                return;
            }

            if (!string.IsNullOrWhiteSpace(saveError)) {
                _notificationService.ShowWarning("CyberSync", saveError);
            }
        } finally {
            _isSyncRunning = false;
            SetSyncUiState(false);
        }
    }

    private async Task ShowDialogAsync(string title, string content)
    {
        FrameworkElement root = (FrameworkElement)Content;
        var dialog = new ContentDialog {
            Title = title,
            Content = content,
            CloseButtonText = "OK",
            XamlRoot = root.XamlRoot
        };

        await dialog.ShowAsync();
    }

    private static string NormalizePath(string value)
    {
        return Path.GetFullPath(value.Trim());
    }

    private List<string> GetSourcePathsFromUi()
    {
        return _sourcePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ReplaceSourcePaths(IEnumerable<string> sourcePaths)
    {
        _sourcePaths.Clear();
        foreach (string sourcePath in sourcePaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase)) {
            _sourcePaths.Add(sourcePath);
        }
    }

    private static string? FindDuplicateDestinationFolderName(IEnumerable<string> sourcePaths)
    {
        return sourcePaths
            .GroupBy(GetDestinationFolderName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
    }

    private static string GetDestinationFolderName(string sourcePath)
    {
        string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath));
        string folderName = Path.GetFileName(normalized);
        if (!string.IsNullOrWhiteSpace(folderName)) {
            return folderName;
        }

        return normalized
            .Replace(':', '_')
            .Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_');
    }

    private void SetSyncUiState(bool isRunning)
    {
        _saveButton.IsEnabled = !isRunning;
        _syncNowButton.IsEnabled = !isRunning;
        _scheduleButton.IsEnabled = !isRunning;
        _sourceListView.IsEnabled = !isRunning;
        _destinationTextBox.IsEnabled = !isRunning;
        _dailyTimePicker.IsEnabled = !isRunning;
        _closeToTrayCheckBox.IsEnabled = !isRunning;
    }

    private void UpdateSyncProgress(SyncProgress progress)
    {
        _syncProgressTextBlock.Visibility = Visibility.Visible;
        _syncEtaTextBlock.Visibility = Visibility.Visible;

        string bytesSummary = progress.TotalBytesToCopy > 0
            ? $"{FormatBytes(progress.CompletedBytes)} / {FormatBytes(progress.TotalBytesToCopy)}"
            : "Scanning changes...";
        string sourceSummary = progress.TotalSourceCount > 0
            ? $"Source {Math.Min(progress.CompletedSourceCount + 1, progress.TotalSourceCount)} of {progress.TotalSourceCount}"
            : string.Empty;
        string fileSummary = string.IsNullOrWhiteSpace(progress.CurrentFilePath)
            ? progress.StatusText
            : $"{progress.StatusText} - {Path.GetFileName(progress.CurrentFilePath)} ({progress.CurrentFilePercent}%)";
        _syncProgressTextBlock.Text = $"{bytesSummary}   {sourceSummary}".Trim();

        TimeSpan elapsed = DateTimeOffset.Now - progress.StartedAt;
        if (!progress.IsIndeterminate && progress.CompletedBytes > 0 && progress.TotalBytesToCopy > progress.CompletedBytes) {
            double bytesPerSecond = progress.CompletedBytes / Math.Max(elapsed.TotalSeconds, 1);
            long remainingBytes = progress.TotalBytesToCopy - progress.CompletedBytes;
            TimeSpan eta = TimeSpan.FromSeconds(remainingBytes / Math.Max(bytesPerSecond, 1));
            _syncEtaTextBlock.Text = $"{fileSummary}   ETA {FormatDuration(eta)}";
        } else {
            _syncEtaTextBlock.Text = fileSummary;
        }
    }

    private void FinishSyncProgress(string message)
    {
        _syncProgressTextBlock.Visibility = Visibility.Visible;
        _syncEtaTextBlock.Visibility = Visibility.Visible;
        _syncProgressTextBlock.Text = message;
        _syncEtaTextBlock.Text = string.Empty;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1) {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1) {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        if (duration.TotalMinutes >= 1) {
            return $"{duration.Minutes}m {duration.Seconds}s";
        }

        return $"{Math.Max(1, duration.Seconds)}s";
    }

    private bool EnsureTrayIntegration()
    {
        try {
            InitializeWindowIntegration();
            if (_appWindow is null) {
                return false;
            }

            if (_trayIconService is null) {
                IntPtr hwnd = WindowNative.GetWindowHandle(this);
                _trayIconService = new TrayIconService(hwnd);
                _trayIconService.Activated += OnTrayIconActivated;
                _trayIconService.SyncNowRequested += OnTraySyncNowRequested;
                _trayIconService.ExitRequested += OnTrayExitRequested;
            }

            return true;
        } catch {
            return false;
        }
    }
}

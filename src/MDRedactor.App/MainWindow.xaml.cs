using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MDRedactor.App.ViewModels;
using MDRedactor.Core.Documents;
using MDRedactor.Core.EditTags;
using MDRedactor.Core.Services;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;

namespace MDRedactor.App;

public partial class MainWindow : Window
{
    private const string EditorVirtualHostName = "editor.mdredactor.local";

    private readonly IMarkdownFileService _fileService = new MarkdownFileService();
    private readonly EditTagValidator _validator = new();
    private readonly MainWindowViewModel _viewModel;
    private MarkdownDocument? _currentDocument;
    private TaskCompletionSource<string?>? _pendingMarkdownRequest;
    private AppThemePreference _themePreference;
    private AppLanguagePreference _languagePreference;
    private string _effectiveTheme = "light";
    private AppLanguage _effectiveLanguage = AppLanguage.English;
    private bool _isSelectingTheme;
    private bool _isSelectingLanguage;
    private bool _editorReady;
    private bool _isSaving;
    private bool _allowClose;
    private string? _pendingStartupFilePath;
    private bool _isOpeningStartupFile;
    private string _statusKey = AppText.StatusNoFile;

    public MainWindow()
    {
        InitializeComponent();

        _pendingStartupFilePath = GetStartupFilePathFromArguments();
        var initialSettings = AppSettingsStore.Load();
        _themePreference = initialSettings.Theme;
        _languagePreference = initialSettings.Language;
        ApplyTheme(_themePreference, saveSettings: false);
        SelectTheme(_themePreference);
        SelectLanguage(_languagePreference);

        _viewModel = new MainWindowViewModel(OpenFileAsync, SaveFileAsync);
        DataContext = _viewModel;
        ApplyLanguage(_languagePreference, saveSettings: false);
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await InitializeEditorAsync();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (EditorWebView.CoreWebView2 is not null)
        {
            EditorWebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            EditorWebView.CoreWebView2.NavigationCompleted -= OnEditorNavigationCompleted;
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || !_viewModel.HasUnsavedChanges)
        {
            return;
        }

        e.Cancel = true;

        if (await ConfirmUnsavedChangesAsync())
        {
            _allowClose = true;
            Close();
        }
    }

    private async Task InitializeEditorAsync()
    {
        var indexPath = FindEditorIndexPath();
        if (indexPath is null)
        {
            ShowStartupError(T("WebEditorMissing", GetExpectedEditorIndexPath()));
            return;
        }

        try
        {
            var editorDistFolder = Path.GetDirectoryName(indexPath)
                ?? throw new InvalidOperationException(T("EditorFolderError"));
            var webViewUserDataFolder = GetWebView2UserDataFolder();
            Directory.CreateDirectory(webViewUserDataFolder);
            var webViewEnvironment = await CoreWebView2Environment.CreateAsync(userDataFolder: webViewUserDataFolder);

            await EditorWebView.EnsureCoreWebView2Async(webViewEnvironment);
            EditorWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            EditorWebView.CoreWebView2.NavigationCompleted += OnEditorNavigationCompleted;
            EditorWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                EditorVirtualHostName,
                editorDistFolder,
                CoreWebView2HostResourceAccessKind.Allow);
            EditorWebView.Source = new Uri($"https://{EditorVirtualHostName}/index.html");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or COMException or WebView2RuntimeNotFoundException)
        {
            AppLogger.LogError(ex, "WebView2 startup error");
            ShowStartupError(T("WebViewStartError", ex.Message));
        }
    }

    private void OnEditorNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            return;
        }

        var message = T("WebViewNavigationError", e.WebErrorStatus);
        AppLogger.LogWarning(message);
        ShowStartupError(message);
    }

    private async Task OpenFileAsync()
    {
        if (!await ConfirmUnsavedChangesAsync())
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = T("OpenDialogTitle"),
            Filter = T("OpenDialogFilter"),
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await OpenDocumentAsync(dialog.FileName);
    }

    private async Task OpenDocumentAsync(string filePath)
    {
        try
        {
            var document = await _fileService.ReadAsync(filePath);
            _currentDocument = document;
            _viewModel.CurrentFileTitle = document.FileName;
            _viewModel.HasUnsavedChanges = false;
            SetStatus(document.Diagnostics.Count > 0
                ? AppText.StatusSavedDetectedEncoding
                : AppText.StatusSaved);

            foreach (var diagnostic in document.Diagnostics)
            {
                AppLogger.LogWarning($"Open diagnostic: {diagnostic}", document.FilePath);
            }

            SendDocumentToEditor(document);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            AppLogger.LogError(ex, "Markdown open error", filePath);
            SetStatus(AppText.StatusOpenError);
            ShowMessage(T("OpenFileFailedTitle"), T("OpenFileFailedMessage", ex.Message));
        }
    }

    private async Task OpenStartupDocumentAsync()
    {
        if (_isOpeningStartupFile || _pendingStartupFilePath is null)
        {
            return;
        }

        var filePath = _pendingStartupFilePath;
        _pendingStartupFilePath = null;
        _isOpeningStartupFile = true;

        try
        {
            if (!File.Exists(filePath))
            {
                AppLogger.LogWarning("Startup file not found.", filePath);
                SetStatus(AppText.StatusOpenError);
                ShowMessage(
                    T("OpenFileFailedTitle"),
                    T("StartupFileMissingMessage", filePath));
                return;
            }

            await OpenDocumentAsync(filePath);
        }
        finally
        {
            _isOpeningStartupFile = false;
        }
    }

    private async Task SaveFileAsync()
    {
        await SaveCurrentFileAsync();
    }

    private async Task<bool> SaveCurrentFileAsync()
    {
        if (_currentDocument is null)
        {
            SetStatus(AppText.StatusNoFile);
            return false;
        }

        var markdown = await RequestMarkdownFromEditorAsync();
        if (markdown is null)
        {
            SetStatus(AppText.StatusSaveError);
            ShowMessage(T(AppText.StatusSaveError), T("SaveNoMarkdownMessage"));
            return false;
        }

        return await SaveMarkdownAsync(markdown);
    }

    private async Task<bool> SaveMarkdownAsync(string markdown)
    {
        if (_currentDocument is null)
        {
            SetStatus(AppText.StatusNoFile);
            return false;
        }

        if (_isSaving)
        {
            return false;
        }

        var errors = _validator.Validate(markdown)
            .Where(diagnostic => diagnostic.Severity == EditDiagnosticSeverity.Error)
            .ToList();
        if (errors.Count > 0)
        {
            SetStatus(AppText.StatusMarkupError);
            var details = FormatDiagnostics(errors);
            AppLogger.LogWarning($"Save blocked by edit markup errors:\n{details}", _currentDocument.FilePath);
            ShowMessage(
                T(AppText.StatusMarkupError),
                T("MarkupSaveBlockedMessage", details));
            return false;
        }

        if (FileWasChangedExternally(_currentDocument) && !ShowOverwriteExternalChangeDialog())
        {
            SetStatus(AppText.StatusDirty);
            return false;
        }

        try
        {
            _isSaving = true;
            SetStatus(AppText.StatusSaving);

            var documentToSave = _currentDocument with { Markdown = markdown };
            var saveResult = await _fileService.SaveAtomicAsync(documentToSave);
            _currentDocument = documentToSave with
            {
                LastWriteTimeUtc = saveResult.LastWriteTimeUtc,
                BackupCreatedInSession = documentToSave.BackupCreatedInSession || saveResult.BackupCreated
            };
            _viewModel.HasUnsavedChanges = false;
            SetStatus(AppText.StatusSaved);
            SendDocumentToEditor(_currentDocument);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.LogError(ex, "Markdown save error", _currentDocument.FilePath);
            SetStatus(AppText.StatusSaveError);
            ShowMessage(T(AppText.StatusSaveError), T("SaveFileFailedMessage", ex.Message));
            return false;
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task<string?> RequestMarkdownFromEditorAsync()
    {
        if (!_editorReady || EditorWebView.CoreWebView2 is null)
        {
            return null;
        }

        var request = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingMarkdownRequest = request;

        try
        {
            PostToEditor(new { type = "host.requestMarkdown" });

            var completed = await Task.WhenAny(request.Task, Task.Delay(TimeSpan.FromSeconds(15)));
            return completed == request.Task
                ? await request.Task
                : null;
        }
        finally
        {
            if (ReferenceEquals(_pendingMarkdownRequest, request))
            {
                _pendingMarkdownRequest = null;
            }
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;

            if (!root.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            switch (typeElement.GetString())
            {
                case "editor.ready":
                    _editorReady = true;
                    SendThemeToEditor();
                    SendLanguageToEditor();
                    if (_pendingStartupFilePath is not null)
                    {
                        _ = OpenStartupDocumentAsync();
                    }
                    else if (_currentDocument is not null)
                    {
                        SendDocumentToEditor(_currentDocument);
                    }

                    break;

                case "editor.dirtyChanged":
                    var hasUnsavedChanges = root.TryGetProperty("isDirty", out var dirtyElement)
                        && dirtyElement.ValueKind == JsonValueKind.True;
                    _viewModel.HasUnsavedChanges = hasUnsavedChanges;
                    SetStatus(hasUnsavedChanges
                        ? AppText.StatusDirty
                        : _currentDocument is null ? AppText.StatusNoFile : AppText.StatusSaved);
                    break;

                case "editor.saveRequested":
                    var markdown = root.TryGetProperty("markdown", out var markdownElement)
                        ? markdownElement.GetString() ?? string.Empty
                        : string.Empty;

                    if (_pendingMarkdownRequest is not null)
                    {
                        _pendingMarkdownRequest.TrySetResult(markdown);
                    }
                    else
                    {
                        _ = SaveMarkdownAsync(markdown);
                    }

                    break;

                case "editor.error":
                    var message = root.TryGetProperty("message", out var messageElement)
                        ? messageElement.GetString()
                        : T("UnknownEditorError");
                    AppLogger.LogWarning($"Editor error: {message}", _currentDocument?.FilePath);
                    SetError(T("EditorErrorPrefix", message));
                    break;
            }
        }
        catch (JsonException ex)
        {
            AppLogger.LogError(ex, "WebView2 protocol error");
            SetError(T("EditorProtocolError", ex.Message));
        }
    }

    private async void OnWindowDrop(object sender, DragEventArgs e)
    {
        var filePath = GetMarkdownPathFromDrop(e);
        if (filePath is null)
        {
            return;
        }

        e.Handled = true;

        if (!await ConfirmUnsavedChangesAsync())
        {
            return;
        }

        await OpenDocumentAsync(filePath);
    }

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = GetMarkdownPathFromDrop(e) is null
            ? DragDropEffects.None
            : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSelectingTheme || ThemeSelector.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var tag = item.Tag?.ToString();
        if (Enum.TryParse<AppThemePreference>(tag, ignoreCase: true, out var theme))
        {
            ApplyTheme(theme, saveSettings: true);
        }
    }

    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSelectingLanguage || LanguageSelector.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var tag = item.Tag?.ToString();
        if (Enum.TryParse<AppLanguagePreference>(tag, ignoreCase: true, out var language))
        {
            ApplyLanguage(language, saveSettings: true);
        }
    }

    private void ApplyTheme(AppThemePreference preference, bool saveSettings)
    {
        _themePreference = preference;
        var dark = preference == AppThemePreference.Dark
            || preference == AppThemePreference.System && IsSystemDarkTheme();
        _effectiveTheme = dark ? "dark" : "light";

        if (dark)
        {
            SetBrush("WindowBackgroundBrush", "#101412");
            SetBrush("SurfaceBrush", "#18211C");
            SetBrush("SurfaceMutedBrush", "#202A24");
            SetBrush("BorderBrush", "#34433A");
            SetBrush("PrimaryTextBrush", "#F4F7F5");
            SetBrush("MutedTextBrush", "#AAB8B0");
            SetBrush("AccentBrush", "#8DD9BF");
            SetBrush("AccentSoftBrush", "#223A31");
            SetBrush("WarningSurfaceBrush", "#3A2718");
            SetBrush("WarningBorderBrush", "#B87939");
            SetBrush("WarningTextBrush", "#FDBA74");
        }
        else
        {
            SetBrush("WindowBackgroundBrush", "#EEF3F0");
            SetBrush("SurfaceBrush", "#FFFFFF");
            SetBrush("SurfaceMutedBrush", "#F5F8F6");
            SetBrush("BorderBrush", "#D7E0DA");
            SetBrush("PrimaryTextBrush", "#17211D");
            SetBrush("MutedTextBrush", "#60716A");
            SetBrush("AccentBrush", "#3B7667");
            SetBrush("AccentSoftBrush", "#E2F0EA");
            SetBrush("WarningSurfaceBrush", "#FFF7ED");
            SetBrush("WarningBorderBrush", "#FDBA74");
            SetBrush("WarningTextBrush", "#7C2D12");
        }

        if (saveSettings)
        {
            SaveCurrentSettings();
        }

        SendThemeToEditor();
    }

    private void ApplyLanguage(AppLanguagePreference preference, bool saveSettings)
    {
        _languagePreference = preference;
        _effectiveLanguage = AppLocalizer.Resolve(preference);

        if (saveSettings)
        {
            SaveCurrentSettings();
        }

        UpdateLocalizedText();
        SendLanguageToEditor();
    }

    private void SelectTheme(AppThemePreference preference)
    {
        _isSelectingTheme = true;
        try
        {
            foreach (var item in ThemeSelector.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), preference.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    ThemeSelector.SelectedItem = item;
                    return;
                }
            }

            ThemeSelector.SelectedIndex = 0;
        }
        finally
        {
            _isSelectingTheme = false;
        }
    }

    private void SelectLanguage(AppLanguagePreference preference)
    {
        _isSelectingLanguage = true;
        try
        {
            foreach (var item in LanguageSelector.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), preference.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    LanguageSelector.SelectedItem = item;
                    return;
                }
            }

            LanguageSelector.SelectedIndex = 0;
        }
        finally
        {
            _isSelectingLanguage = false;
        }
    }

    private void SendThemeToEditor()
    {
        if (_editorReady && EditorWebView.CoreWebView2 is not null)
        {
            PostToEditor(new { type = "host.setTheme", theme = _effectiveTheme });
        }
    }

    private void SendLanguageToEditor()
    {
        if (_editorReady && EditorWebView.CoreWebView2 is not null)
        {
            PostToEditor(new { type = "host.setLanguage", language = AppLocalizer.Code(_effectiveLanguage) });
        }
    }

    private void SaveCurrentSettings()
    {
        AppSettingsStore.Save(new AppSettings
        {
            Theme = _themePreference,
            Language = _languagePreference
        });
    }

    private void SetBrush(string key, string hexColor)
    {
        Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
    }

    private void UpdateLocalizedText()
    {
        ThemeLabel.Text = T("ThemeLabel");
        LanguageLabel.Text = T("LanguageLabel");

        SetComboBoxItemContent(ThemeSelector, AppThemePreference.System.ToString(), T("ThemeSystem"));
        SetComboBoxItemContent(ThemeSelector, AppThemePreference.Light.ToString(), T("ThemeLight"));
        SetComboBoxItemContent(ThemeSelector, AppThemePreference.Dark.ToString(), T("ThemeDark"));
        ThemeSelector.ToolTip = T("ThemeTooltip");

        SetComboBoxItemContent(LanguageSelector, AppLanguagePreference.System.ToString(), T("LanguageSystem"));
        SetComboBoxItemContent(LanguageSelector, AppLanguagePreference.Russian.ToString(), T("LanguageRussian"));
        SetComboBoxItemContent(LanguageSelector, AppLanguagePreference.English.ToString(), T("LanguageEnglish"));
        LanguageSelector.ToolTip = T("LanguageTooltip");

        OpenButton.Content = T("OpenButton");
        OpenButton.ToolTip = T("OpenTooltip");
        SaveButton.Content = T("SaveButton");
        SaveButton.ToolTip = T("SaveTooltip");

        if (_currentDocument is null)
        {
            _viewModel.CurrentFileTitle = T(AppText.FileNotOpened);
        }

        RefreshStatus();
    }

    private static void SetComboBoxItemContent(ComboBox selector, string tag, string content)
    {
        foreach (var item in selector.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                item.Content = content;
                return;
            }
        }
    }

    private string T(string key)
    {
        return AppLocalizer.Get(_effectiveLanguage, key);
    }

    private string T(string key, params object?[] args)
    {
        return AppLocalizer.Format(_effectiveLanguage, key, args);
    }

    private void SetStatus(string key)
    {
        _statusKey = key;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        _viewModel.StatusText = T(_statusKey);
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int appsUseLightTheme && appsUseLightTheme == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            AppLogger.LogError(ex, "Could not resolve the Windows system theme");
            return false;
        }
    }

    private static string? GetMarkdownPathFromDrop(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return null;
        }

        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        return files?
            .FirstOrDefault(file => string.Equals(Path.GetExtension(file), ".md", StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetStartupFilePathFromArguments()
    {
        var filePath = Environment.GetCommandLineArgs()
            .Skip(1)
            .FirstOrDefault(argument => !string.IsNullOrWhiteSpace(argument));

        if (filePath is null)
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(filePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or SecurityException)
        {
            return filePath;
        }
    }

    private async Task<bool> ConfirmUnsavedChangesAsync()
    {
        if (!_viewModel.HasUnsavedChanges)
        {
            return true;
        }

        return ShowUnsavedChangesDialog() switch
        {
            UnsavedChangesChoice.Save => await SaveCurrentFileAsync(),
            UnsavedChangesChoice.Discard => true,
            _ => false
        };
    }

    private bool FileWasChangedExternally(MarkdownDocument document)
    {
        if (document.LastWriteTimeUtc is null || !File.Exists(document.FilePath))
        {
            return false;
        }

        var currentWriteTimeUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(document.FilePath), TimeSpan.Zero);
        return currentWriteTimeUtc - document.LastWriteTimeUtc.Value > TimeSpan.FromSeconds(1);
    }

    private string FormatDiagnostics(IReadOnlyList<EditDiagnostic> diagnostics)
    {
        var lines = diagnostics
            .Take(8)
            .Select(diagnostic =>
            {
                var edit = diagnostic.EditId is null
                    ? string.Empty
                    : T("DiagnosticEditSuffix", diagnostic.EditId);
                var message = AppLocalizer.DiagnosticMessage(_effectiveLanguage, diagnostic);
                return T("DiagnosticLine", diagnostic.Line, diagnostic.Column, edit, message);
            })
            .ToList();

        if (diagnostics.Count > lines.Count)
        {
            lines.Add(T("DiagnosticMoreErrors", diagnostics.Count - lines.Count));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private UnsavedChangesChoice ShowUnsavedChangesDialog()
    {
        var dialog = CreateDialogWindow(T("UnsavedChangesTitle"));
        var panel = CreateDialogPanel();

        panel.Children.Add(new TextBlock
        {
            Text = T("UnsavedChangesMessage"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("PrimaryTextBrush"),
            Margin = new Thickness(0, 0, 0, 18)
        });

        var result = UnsavedChangesChoice.Cancel;
        var buttons = CreateDialogButtons();
        buttons.Children.Add(CreateDialogButton(T("SaveChoice"), isPrimary: true, isCancel: false, onClick: () =>
        {
            result = UnsavedChangesChoice.Save;
            dialog.DialogResult = true;
        }));
        buttons.Children.Add(CreateDialogButton(T("DiscardChoice"), isPrimary: false, isCancel: false, onClick: () =>
        {
            result = UnsavedChangesChoice.Discard;
            dialog.DialogResult = true;
        }));
        buttons.Children.Add(CreateDialogButton(T("CancelChoice"), isPrimary: false, isCancel: true, onClick: () =>
        {
            result = UnsavedChangesChoice.Cancel;
            dialog.DialogResult = false;
        }));

        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.ShowDialog();
        return result;
    }

    private bool ShowOverwriteExternalChangeDialog()
    {
        var dialog = CreateDialogWindow(T("ExternalChangeTitle"));
        var panel = CreateDialogPanel();

        panel.Children.Add(new TextBlock
        {
            Text = T("ExternalChangeMessage"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("PrimaryTextBrush"),
            Margin = new Thickness(0, 0, 0, 18)
        });

        var overwrite = false;
        var buttons = CreateDialogButtons();
        buttons.Children.Add(CreateDialogButton(T("OverwriteChoice"), isPrimary: true, isCancel: false, onClick: () =>
        {
            overwrite = true;
            dialog.DialogResult = true;
        }));
        buttons.Children.Add(CreateDialogButton(T("CancelChoice"), isPrimary: false, isCancel: true, onClick: () =>
        {
            overwrite = false;
            dialog.DialogResult = false;
        }));

        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.ShowDialog();
        return overwrite;
    }

    private void ShowMessage(string title, string message)
    {
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private Window CreateDialogWindow(string title)
    {
        return new Window
        {
            Title = title,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 420,
            MaxWidth = 620,
            Background = (Brush)FindResource("SurfaceBrush")
        };
    }

    private static StackPanel CreateDialogPanel()
    {
        return new StackPanel
        {
            Margin = new Thickness(22)
        };
    }

    private static StackPanel CreateDialogButtons()
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
    }

    private Button CreateDialogButton(string text, bool isPrimary, bool isCancel, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 104,
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = (Brush)FindResource("PrimaryTextBrush"),
            Background = (Brush)FindResource(isPrimary ? "AccentSoftBrush" : "SurfaceMutedBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            IsDefault = isPrimary,
            IsCancel = isCancel
        };

        button.Click += (_, _) => onClick();
        return button;
    }

    private void SendDocumentToEditor(MarkdownDocument document)
    {
        if (!_editorReady || EditorWebView.CoreWebView2 is null)
        {
            return;
        }

        PostToEditor(new
        {
            type = "host.loadDocument",
            filePath = document.FilePath,
            fileName = document.FileName,
            markdown = document.Markdown,
            encodingName = document.EncodingName
        });
    }

    private void PostToEditor(object message)
    {
        EditorWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message));
    }

    private void ShowStartupError(string message)
    {
        StartupErrorText.Text = message;
        StartupErrorPanel.Visibility = Visibility.Visible;
        EditorWebView.Visibility = Visibility.Collapsed;
        SetError(T(AppText.StatusError));
    }

    private void SetError(string message)
    {
        var errorPrefix = T(AppText.StatusError);
        _viewModel.StatusText = message.StartsWith(errorPrefix, StringComparison.Ordinal)
            ? message
            : $"{errorPrefix}: {message}";
        _statusKey = AppText.StatusError;
    }

    private static string? FindEditorIndexPath()
    {
        foreach (var startPath in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(startPath);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "web", "editor", "dist", "index.html");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    private static string GetExpectedEditorIndexPath()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MDRedactor.sln")))
            {
                return Path.Combine(directory.FullName, "web", "editor", "dist", "index.html");
            }

            directory = directory.Parent;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "web", "editor", "dist", "index.html");
    }

    private static string GetWebView2UserDataFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MDRedactor",
            "WebView2");
    }
}

internal enum UnsavedChangesChoice
{
    Save,
    Discard,
    Cancel
}

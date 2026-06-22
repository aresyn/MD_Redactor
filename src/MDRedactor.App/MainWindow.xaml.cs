using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MDRedactor.App.ViewModels;
using MDRedactor.Core.Documents;
using MDRedactor.Core.EditTags;
using MDRedactor.Core.Services;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;

namespace MDRedactor.App;

public partial class MainWindow : Window
{
    private readonly IMarkdownFileService _fileService = new MarkdownFileService();
    private readonly EditTagValidator _validator = new();
    private readonly MainWindowViewModel _viewModel;
    private MarkdownDocument? _currentDocument;
    private TaskCompletionSource<string?>? _pendingMarkdownRequest;
    private bool _editorReady;
    private bool _isSaving;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel(OpenFileAsync, SaveFileAsync);
        DataContext = _viewModel;
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
            ShowStartupError(
                "Web-редактор не собран. Ожидается файл:\n" +
                $"{GetExpectedEditorIndexPath()}\n\n" +
                "Запустите scripts\\build.ps1 и откройте приложение снова.");
            return;
        }

        try
        {
            await EditorWebView.EnsureCoreWebView2Async();
            EditorWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            EditorWebView.Source = new Uri(indexPath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or WebView2RuntimeNotFoundException)
        {
            AppLogger.LogError(ex, "Ошибка запуска WebView2");
            ShowStartupError($"Не удалось запустить WebView2. Установите WebView2 Runtime и повторите запуск.\n\n{ex.Message}");
        }
    }

    private async Task OpenFileAsync()
    {
        if (!await ConfirmUnsavedChangesAsync())
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Открыть Markdown",
            Filter = "Markdown (*.md)|*.md|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var document = await _fileService.ReadAsync(dialog.FileName);
            _currentDocument = document;
            _viewModel.CurrentFileTitle = document.FileName;
            _viewModel.HasUnsavedChanges = false;
            _viewModel.StatusText = document.Diagnostics.Count > 0
                ? "Сохранено. Кодировка определена автоматически"
                : "Сохранено";

            foreach (var diagnostic in document.Diagnostics)
            {
                AppLogger.LogWarning($"Диагностика открытия: {diagnostic}", document.FilePath);
            }

            SendDocumentToEditor(document);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            AppLogger.LogError(ex, "Ошибка открытия Markdown-файла", dialog.FileName);
            _viewModel.StatusText = "Ошибка открытия";
            ShowMessage("Ошибка открытия", $"Не удалось открыть файл.\n\n{ex.Message}");
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
            _viewModel.StatusText = "Нет файла";
            return false;
        }

        var markdown = await RequestMarkdownFromEditorAsync();
        if (markdown is null)
        {
            _viewModel.StatusText = "Ошибка сохранения";
            ShowMessage("Ошибка сохранения", "Редактор не вернул текст для сохранения.");
            return false;
        }

        return await SaveMarkdownAsync(markdown);
    }

    private async Task<bool> SaveMarkdownAsync(string markdown)
    {
        if (_currentDocument is null)
        {
            _viewModel.StatusText = "Нет файла";
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
            _viewModel.StatusText = "Ошибка разметки правок";
            var details = FormatDiagnostics(errors);
            AppLogger.LogWarning($"Сохранение заблокировано из-за ошибок разметки правок:\n{details}", _currentDocument.FilePath);
            ShowMessage(
                "Ошибка разметки правок",
                "Файл не сохранен, потому что в служебной разметке правок есть ошибки.\n\n" + details);
            return false;
        }

        if (FileWasChangedExternally(_currentDocument) && !ShowOverwriteExternalChangeDialog())
        {
            _viewModel.StatusText = "Есть несохраненные изменения";
            return false;
        }

        try
        {
            _isSaving = true;
            _viewModel.StatusText = "Сохранение...";

            var documentToSave = _currentDocument with { Markdown = markdown };
            var saveResult = await _fileService.SaveAtomicAsync(documentToSave);
            _currentDocument = documentToSave with
            {
                LastWriteTimeUtc = saveResult.LastWriteTimeUtc,
                BackupCreatedInSession = documentToSave.BackupCreatedInSession || saveResult.BackupCreated
            };
            _viewModel.HasUnsavedChanges = false;
            _viewModel.StatusText = "Сохранено";
            SendDocumentToEditor(_currentDocument);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.LogError(ex, "Ошибка сохранения Markdown-файла", _currentDocument.FilePath);
            _viewModel.StatusText = "Ошибка сохранения";
            ShowMessage("Ошибка сохранения", $"Не удалось сохранить файл. Исходный файл не был перезаписан.\n\n{ex.Message}");
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
                    PostToEditor(new { type = "host.setTheme", theme = "light" });
                    if (_currentDocument is not null)
                    {
                        SendDocumentToEditor(_currentDocument);
                    }

                    break;

                case "editor.dirtyChanged":
                    var hasUnsavedChanges = root.TryGetProperty("isDirty", out var dirtyElement)
                        && dirtyElement.ValueKind == JsonValueKind.True;
                    _viewModel.HasUnsavedChanges = hasUnsavedChanges;
                    _viewModel.StatusText = hasUnsavedChanges
                        ? "Есть несохраненные изменения"
                        : _currentDocument is null ? "Нет файла" : "Сохранено";
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
                        : "неизвестная ошибка";
                    AppLogger.LogWarning($"Ошибка редактора: {message}", _currentDocument?.FilePath);
                    SetError($"Ошибка редактора: {message}");
                    break;
            }
        }
        catch (JsonException ex)
        {
            AppLogger.LogError(ex, "Ошибка протокола WebView2");
            SetError($"Ошибка протокола редактора: {ex.Message}");
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

    private static string FormatDiagnostics(IReadOnlyList<EditDiagnostic> diagnostics)
    {
        var lines = diagnostics
            .Take(8)
            .Select(diagnostic =>
            {
                var edit = diagnostic.EditId is null ? string.Empty : $" Правка #{diagnostic.EditId}.";
                return $"Строка {diagnostic.Line}, колонка {diagnostic.Column}.{edit} {diagnostic.Message}";
            })
            .ToList();

        if (diagnostics.Count > lines.Count)
        {
            lines.Add($"Еще ошибок: {diagnostics.Count - lines.Count}.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private UnsavedChangesChoice ShowUnsavedChangesDialog()
    {
        var dialog = CreateDialogWindow("Несохраненные изменения");
        var panel = CreateDialogPanel();

        panel.Children.Add(new TextBlock
        {
            Text = "Есть несохраненные изменения. Сохранить перед закрытием?",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18)
        });

        var result = UnsavedChangesChoice.Cancel;
        var buttons = CreateDialogButtons();
        buttons.Children.Add(CreateDialogButton("Сохранить", isPrimary: true, () =>
        {
            result = UnsavedChangesChoice.Save;
            dialog.DialogResult = true;
        }));
        buttons.Children.Add(CreateDialogButton("Не сохранять", isPrimary: false, () =>
        {
            result = UnsavedChangesChoice.Discard;
            dialog.DialogResult = true;
        }));
        buttons.Children.Add(CreateDialogButton("Отмена", isPrimary: false, () =>
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
        var dialog = CreateDialogWindow("Файл изменен");
        var panel = CreateDialogPanel();

        panel.Children.Add(new TextBlock
        {
            Text = "Файл был изменен другой программой. Перезаписать его?",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18)
        });

        var overwrite = false;
        var buttons = CreateDialogButtons();
        buttons.Children.Add(CreateDialogButton("Перезаписать", isPrimary: true, () =>
        {
            overwrite = true;
            dialog.DialogResult = true;
        }));
        buttons.Children.Add(CreateDialogButton("Отмена", isPrimary: false, () =>
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
            Background = System.Windows.Media.Brushes.White
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

    private static Button CreateDialogButton(string text, bool isPrimary, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 104,
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = isPrimary,
            IsCancel = text == "Отмена"
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
        SetError("Ошибка");
    }

    private void SetError(string message)
    {
        _viewModel.StatusText = message.StartsWith("Ошибка", StringComparison.Ordinal)
            ? message
            : $"Ошибка: {message}";
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
}

internal enum UnsavedChangesChoice
{
    Save,
    Discard,
    Cancel
}

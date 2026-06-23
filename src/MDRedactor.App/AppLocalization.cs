using System.Globalization;
using MDRedactor.Core.EditTags;

namespace MDRedactor.App;

internal enum AppLanguage
{
    Russian,
    English
}

internal static class AppText
{
    public const string FileNotOpened = "FileNotOpened";
    public const string StatusNoFile = "StatusNoFile";
    public const string StatusSaved = "StatusSaved";
    public const string StatusSavedDetectedEncoding = "StatusSavedDetectedEncoding";
    public const string StatusDirty = "StatusDirty";
    public const string StatusSaving = "StatusSaving";
    public const string StatusOpenError = "StatusOpenError";
    public const string StatusSaveError = "StatusSaveError";
    public const string StatusMarkupError = "StatusMarkupError";
    public const string StatusError = "StatusError";
}

internal static class AppLocalizer
{
    private static readonly IReadOnlyDictionary<string, (string Ru, string En)> Texts =
        new Dictionary<string, (string Ru, string En)>(StringComparer.Ordinal)
        {
            [AppText.FileNotOpened] = ("Файл не открыт", "No file open"),
            [AppText.StatusNoFile] = ("Нет файла", "No file"),
            [AppText.StatusSaved] = ("Сохранено", "Saved"),
            [AppText.StatusSavedDetectedEncoding] = ("Сохранено. Кодировка определена автоматически", "Saved. Encoding was detected automatically"),
            [AppText.StatusDirty] = ("Есть несохраненные изменения", "Unsaved changes"),
            [AppText.StatusSaving] = ("Сохранение...", "Saving..."),
            [AppText.StatusOpenError] = ("Ошибка открытия", "Open error"),
            [AppText.StatusSaveError] = ("Ошибка сохранения", "Save error"),
            [AppText.StatusMarkupError] = ("Ошибка разметки правок", "Edit markup error"),
            [AppText.StatusError] = ("Ошибка", "Error"),

            ["ThemeLabel"] = ("Тема", "Theme"),
            ["LanguageLabel"] = ("Язык", "Language"),
            ["ThemeSystem"] = ("Системная", "System"),
            ["ThemeLight"] = ("Светлая", "Light"),
            ["ThemeDark"] = ("Темная", "Dark"),
            ["LanguageSystem"] = ("Системный", "System"),
            ["LanguageRussian"] = ("Русский", "Russian"),
            ["LanguageEnglish"] = ("English", "English"),
            ["ThemeTooltip"] = ("Выбор темы интерфейса", "Choose interface theme"),
            ["LanguageTooltip"] = ("Выбор языка интерфейса", "Choose interface language"),
            ["OpenButton"] = ("Открыть", "Open"),
            ["SaveButton"] = ("Сохранить", "Save"),
            ["OpenTooltip"] = ("Открыть Markdown-файл (Ctrl+O)", "Open a Markdown file (Ctrl+O)"),
            ["SaveTooltip"] = ("Сохранить текущий Markdown-файл (Ctrl+S)", "Save the current Markdown file (Ctrl+S)"),
            ["OpenDialogTitle"] = ("Открыть Markdown", "Open Markdown"),
            ["OpenDialogFilter"] = ("Markdown (*.md)|*.md|Все файлы (*.*)|*.*", "Markdown (*.md)|*.md|All files (*.*)|*.*"),
            ["UntitledFile"] = ("Без имени", "Untitled"),

            ["WebEditorMissing"] = ("Web-редактор не собран. Ожидается файл:\n{0}\n\nЗапустите scripts\\build.ps1 и откройте приложение снова.", "The web editor has not been built. Expected file:\n{0}\n\nRun scripts\\build.ps1 and open the app again."),
            ["EditorFolderError"] = ("Не удалось определить каталог web-редактора.", "Could not resolve the web editor folder."),
            ["WebViewStartError"] = ("Не удалось запустить WebView2. Установите WebView2 Runtime и повторите запуск.\n\n{0}", "Could not start WebView2. Install WebView2 Runtime and start the app again.\n\n{0}"),
            ["WebViewNavigationError"] = ("Не удалось загрузить web-редактор. Статус WebView2: {0}.", "Could not load the web editor. WebView2 status: {0}."),
            ["OpenFileFailedTitle"] = ("Ошибка открытия", "Open error"),
            ["OpenFileFailedMessage"] = ("Не удалось открыть файл.\n\n{0}", "Could not open the file.\n\n{0}"),
            ["StartupFileMissingMessage"] = ("Не удалось открыть файл, переданный при запуске.\n\nФайл не найден:\n{0}", "Could not open the file passed on startup.\n\nFile not found:\n{0}"),
            ["SaveNoMarkdownMessage"] = ("Редактор не вернул текст для сохранения.", "The editor did not return Markdown to save."),
            ["MarkupSaveBlockedMessage"] = ("Файл не сохранен, потому что в служебной разметке правок есть ошибки.\n\n{0}", "The file was not saved because the edit markup contains errors.\n\n{0}"),
            ["SaveFileFailedMessage"] = ("Не удалось сохранить файл. Исходный файл не был перезаписан.\n\n{0}", "Could not save the file. The original file was not overwritten.\n\n{0}"),
            ["UnknownEditorError"] = ("неизвестная ошибка", "unknown error"),
            ["EditorErrorPrefix"] = ("Ошибка редактора: {0}", "Editor error: {0}"),
            ["EditorProtocolError"] = ("Ошибка протокола редактора: {0}", "Editor protocol error: {0}"),

            ["UnsavedChangesTitle"] = ("Несохраненные изменения", "Unsaved changes"),
            ["UnsavedChangesMessage"] = ("Есть несохраненные изменения. Сохранить перед закрытием?", "There are unsaved changes. Save before closing?"),
            ["SaveChoice"] = ("Сохранить", "Save"),
            ["DiscardChoice"] = ("Не сохранять", "Don't save"),
            ["CancelChoice"] = ("Отмена", "Cancel"),
            ["ExternalChangeTitle"] = ("Файл изменен", "File changed"),
            ["ExternalChangeMessage"] = ("Файл был изменен другой программой. Перезаписать его?", "The file was changed by another program. Overwrite it?"),
            ["OverwriteChoice"] = ("Перезаписать", "Overwrite"),

            ["DiagnosticLine"] = ("Строка {0}, колонка {1}.{2} {3}", "Line {0}, column {1}.{2} {3}"),
            ["DiagnosticEditSuffix"] = (" Правка #{0}.", " Edit #{0}."),
            ["DiagnosticMoreErrors"] = ("Еще ошибок: {0}.", "More errors: {0}."),

            [EditDiagnosticCodes.UnknownMarker] = ("Неизвестный служебный маркер правки.", "Unknown edit marker."),
            [EditDiagnosticCodes.NestedEdit] = ("Вложенные правки запрещены: найден ed-start внутри незакрытой правки.", "Nested edits are not allowed: ed-start was found inside an open edit."),
            [EditDiagnosticCodes.DuplicateId] = ("Дублирующийся id правки запрещен.", "Duplicate edit id is not allowed."),
            [EditDiagnosticCodes.CommentWithoutStart] = ("Маркер ed-comm найден без открывающего ed-start.", "ed-comm was found without an opening ed-start."),
            [EditDiagnosticCodes.CommentIdMismatch] = ("Id в ed-comm не совпадает с id ed-start.", "The id in ed-comm does not match ed-start."),
            [EditDiagnosticCodes.DuplicateComment] = ("Для правки найден повторный ed-comm.", "The edit contains a duplicate ed-comm."),
            [EditDiagnosticCodes.UnsafeComment] = ("Комментарий правки содержит запрещенную для HTML-comment последовательность \"--\".", "The edit comment contains the \"--\" sequence, which is not allowed inside an HTML comment."),
            [EditDiagnosticCodes.EndWithoutStart] = ("Маркер ed-end найден без открывающего ed-start.", "ed-end was found without an opening ed-start."),
            [EditDiagnosticCodes.MissingCommentBeforeEnd] = ("Правка закрыта без обязательного ed-comm.", "The edit was closed without the required ed-comm."),
            [EditDiagnosticCodes.EndIdMismatch] = ("Id в ed-end не совпадает с id ed-start.", "The id in ed-end does not match ed-start."),
            [EditDiagnosticCodes.MissingComment] = ("Правка не содержит обязательный ed-comm.", "The edit does not contain the required ed-comm."),
            [EditDiagnosticCodes.MissingEnd] = ("Правка не содержит закрывающий ed-end.", "The edit does not contain a closing ed-end."),
            [EditDiagnosticCodes.StatusAttribute] = ("Формат правок не поддерживает атрибут status.", "The edit format does not support the status attribute."),
            [EditDiagnosticCodes.MissingId] = ("Маркер правки должен содержать id в формате id=\"N\".", "The edit marker must contain id=\"N\"."),
            [EditDiagnosticCodes.InvalidId] = ("Id правки должен быть положительным целым числом.", "The edit id must be a positive integer."),
        };

    public static AppLanguage Resolve(AppLanguagePreference preference)
    {
        return preference switch
        {
            AppLanguagePreference.Russian => AppLanguage.Russian,
            AppLanguagePreference.English => AppLanguage.English,
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.Russian
                : AppLanguage.English
        };
    }

    public static string Code(AppLanguage language)
    {
        return language == AppLanguage.Russian ? "ru" : "en";
    }

    public static string Get(AppLanguage language, string key)
    {
        return Texts.TryGetValue(key, out var value)
            ? language == AppLanguage.Russian ? value.Ru : value.En
            : key;
    }

    public static string Format(AppLanguage language, string key, params object?[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Get(language, key), args);
    }

    public static string DiagnosticMessage(AppLanguage language, EditDiagnostic diagnostic)
    {
        return Texts.ContainsKey(diagnostic.Code)
            ? Get(language, diagnostic.Code)
            : diagnostic.Message;
    }
}

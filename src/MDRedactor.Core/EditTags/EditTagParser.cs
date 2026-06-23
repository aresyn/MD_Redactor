using System.Text;
using System.Text.RegularExpressions;

namespace MDRedactor.Core.EditTags;

public sealed class EditTagParser
{
    private const string MarkerPrefix = "<!-- ed-";
    private static readonly Regex IdRegex = new(@"\bid\s*=\s*""(?<id>\d+)""", RegexOptions.CultureInvariant);
    private static readonly Regex StatusRegex = new(@"\bstatus\s*=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public ParsedMarkdownDocument Parse(string markdown)
    {
        return ParseDetailed(markdown).Document;
    }

    public IReadOnlyList<EditAnnotation> GetEdits(string markdown)
    {
        return Parse(markdown).Edits;
    }

    public string StripEditMarkup(string markdown)
    {
        return Parse(markdown).MarkdownWithoutEditMarkup;
    }

    public int GetNextEditId(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var maxId = 0;
        var searchIndex = 0;

        while (TryReadNextToken(markdown, searchIndex, out var token))
        {
            if (token.Id is > 0 && token.Id.Value > maxId)
            {
                maxId = token.Id.Value;
            }

            searchIndex = token.EndIndex;
        }

        return maxId + 1;
    }

    internal EditTagParseResult ParseDetailed(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var stripped = new StringBuilder(markdown.Length);
        var diagnostics = new List<EditDiagnostic>();
        var edits = new List<EditAnnotation>();
        var spans = new List<EditAnnotationSpan>();
        var startedIds = new HashSet<int>();
        var current = default(ActiveEdit);
        var position = 0;

        while (TryReadNextToken(markdown, position, out var token))
        {
            stripped.Append(markdown, position, token.StartIndex - position);

            if (token.Kind == EditTagKind.Unknown)
            {
                stripped.Append(markdown, token.StartIndex, token.EndIndex - token.StartIndex);
                AddDiagnostic(
                    diagnostics,
                    markdown,
                    EditDiagnosticSeverity.Error,
                    EditDiagnosticCodes.UnknownMarker,
                    token.StartIndex,
                    token.Id,
                    "Неизвестный служебный маркер правки.");
                position = token.EndIndex;
                continue;
            }

            AddTokenDiagnostics(markdown, token, diagnostics);

            switch (token.Kind)
            {
                case EditTagKind.Start:
                    if (current is not null)
                    {
                        AddDiagnostic(
                            diagnostics,
                            markdown,
                            EditDiagnosticSeverity.Error,
                            EditDiagnosticCodes.NestedEdit,
                            token.StartIndex,
                            token.Id,
                            "Вложенные правки запрещены: найден ed-start внутри незакрытой правки.");
                        current.HasStructuralError = true;
                    }

                    var startHasError = token.Id is null || token.Id <= 0;
                    if (token.Id is > 0 && !startedIds.Add(token.Id.Value))
                    {
                        AddDiagnostic(
                            diagnostics,
                            markdown,
                            EditDiagnosticSeverity.Error,
                            EditDiagnosticCodes.DuplicateId,
                            token.StartIndex,
                            token.Id,
                            $"Дублирующийся id правки {token.Id.Value} запрещен.");
                        startHasError = true;
                    }

                    current = new ActiveEdit(token.Id ?? 0, token.StartIndex, token.EndIndex)
                    {
                        HasStructuralError = startHasError || current?.HasStructuralError == true
                    };
                    break;

                case EditTagKind.Comment:
                    if (current is null)
                    {
                        AddDiagnostic(
                            diagnostics,
                            markdown,
                            EditDiagnosticSeverity.Error,
                            EditDiagnosticCodes.CommentWithoutStart,
                            token.StartIndex,
                            token.Id,
                            "Маркер ed-comm найден без открывающего ed-start.");
                        break;
                    }

                    if (token.Id != current.Id)
                    {
                        AddDiagnostic(
                            diagnostics,
                            markdown,
                            EditDiagnosticSeverity.Error,
                            EditDiagnosticCodes.CommentIdMismatch,
                            token.StartIndex,
                            token.Id,
                            $"Id в ed-comm ({FormatId(token.Id)}) не совпадает с id ed-start ({current.Id}).");
                        current.HasStructuralError = true;
                    }

                    if (current.HasComment)
                    {
                        AddDiagnostic(
                            diagnostics,
                            markdown,
                            EditDiagnosticSeverity.Error,
                            EditDiagnosticCodes.DuplicateComment,
                            token.StartIndex,
                            token.Id,
                            $"Для правки id {current.Id} найден повторный ed-comm.");
                        current.HasStructuralError = true;
                    }

                    current.HasComment = true;
                    current.FragmentEndIndex = token.StartIndex;
                    current.Comment = TrimSingleTrailingLineBreak(token.Comment);
                    current.CommentRawStartIndex = token.CommentStartIndex;
                    current.CommentRawEndIndex = token.CommentEndIndex;

                    if (current.Comment.Contains("--", StringComparison.Ordinal))
                    {
                        AddDiagnostic(
                            diagnostics,
                            markdown,
                            EditDiagnosticSeverity.Error,
                            EditDiagnosticCodes.UnsafeComment,
                            token.CommentStartIndex,
                            current.Id,
                            "Комментарий правки содержит запрещенную для HTML-comment последовательность \"--\".");
                        current.HasStructuralError = true;
                    }

                    break;

                case EditTagKind.End:
                    if (current is null)
                    {
                        AddDiagnostic(
                            diagnostics,
                            markdown,
                            EditDiagnosticSeverity.Error,
                            EditDiagnosticCodes.EndWithoutStart,
                            token.StartIndex,
                            token.Id,
                            "Маркер ed-end найден без открывающего ed-start.");
                        break;
                    }

                    if (!current.HasComment)
                    {
                        AddDiagnostic(
                            diagnostics,
                            markdown,
                            EditDiagnosticSeverity.Error,
                            EditDiagnosticCodes.MissingCommentBeforeEnd,
                            token.StartIndex,
                            current.Id,
                            $"Правка id {current.Id} закрыта без обязательного ed-comm.");
                        current.HasStructuralError = true;
                    }

                    if (token.Id != current.Id)
                    {
                        AddDiagnostic(
                            diagnostics,
                            markdown,
                            EditDiagnosticSeverity.Error,
                            EditDiagnosticCodes.EndIdMismatch,
                            token.StartIndex,
                            token.Id,
                            $"Id в ed-end ({FormatId(token.Id)}) не совпадает с id ed-start ({current.Id}).");
                        current.HasStructuralError = true;
                    }

                    if (current.HasComment)
                    {
                        var fragmentMarkdown = markdown[current.FragmentStartIndex..current.FragmentEndIndex];
                        var annotation = new EditAnnotation
                        {
                            Id = current.Id,
                            FragmentMarkdown = fragmentMarkdown,
                            FragmentPlainText = ToPlainText(fragmentMarkdown),
                            Comment = current.Comment,
                            RawStartIndex = current.RawStartIndex,
                            RawEndIndex = token.EndIndex,
                            FragmentRawStartIndex = current.FragmentStartIndex,
                            FragmentRawEndIndex = current.FragmentEndIndex,
                            Kind = IsInlineFragment(fragmentMarkdown) ? EditAnnotationKind.Inline : EditAnnotationKind.Block
                        };

                        if (!current.HasStructuralError)
                        {
                            edits.Add(annotation);
                        }

                        spans.Add(new EditAnnotationSpan(
                            annotation,
                            current.CommentRawStartIndex,
                            current.CommentRawEndIndex,
                            !current.HasStructuralError));
                    }

                    current = null;
                    break;
            }

            position = token.EndIndex;
        }

        stripped.Append(markdown, position, markdown.Length - position);

        if (current is not null)
        {
            if (!current.HasComment)
            {
                AddDiagnostic(
                    diagnostics,
                    markdown,
                    EditDiagnosticSeverity.Error,
                    EditDiagnosticCodes.MissingComment,
                    current.RawStartIndex,
                    current.Id,
                    $"Правка id {current.Id} не содержит обязательный ed-comm.");
            }

            AddDiagnostic(
                diagnostics,
                markdown,
                EditDiagnosticSeverity.Error,
                EditDiagnosticCodes.MissingEnd,
                current.RawStartIndex,
                current.Id,
                $"Правка id {current.Id} не содержит закрывающий ed-end.");
        }

        return new EditTagParseResult(
            new ParsedMarkdownDocument
            {
                OriginalMarkdown = markdown,
                MarkdownWithoutEditMarkup = stripped.ToString(),
                Edits = edits,
                Diagnostics = diagnostics,
            },
            spans);
    }

    private static bool TryReadNextToken(string markdown, int startIndex, out EditTagToken token)
    {
        var markerStart = markdown.IndexOf(MarkerPrefix, startIndex, StringComparison.Ordinal);
        if (markerStart < 0)
        {
            token = default;
            return false;
        }

        var closeIndex = markdown.IndexOf("-->", markerStart + MarkerPrefix.Length, StringComparison.Ordinal);
        if (closeIndex < 0)
        {
            var header = markdown[markerStart..];
            token = new EditTagToken(
                EditTagKind.Unknown,
                null,
                markerStart,
                markdown.Length,
                header,
                string.Empty,
                markdown.Length,
                markdown.Length);
            return true;
        }

        var contentStart = markerStart + "<!--".Length;
        var headerEnd = FindHeaderEnd(markdown, contentStart, closeIndex, out var payloadStart);
        var headerText = markdown[contentStart..headerEnd].Trim();
        var kind = GetKind(headerText);
        var id = TryGetId(headerText);
        var comment = kind == EditTagKind.Comment
            ? markdown[payloadStart..closeIndex]
            : string.Empty;

        token = new EditTagToken(
            kind,
            id,
            markerStart,
            closeIndex + "-->".Length,
            headerText,
            comment,
            payloadStart,
            closeIndex);
        return true;
    }

    private static int FindHeaderEnd(string markdown, int contentStart, int closeIndex, out int payloadStart)
    {
        for (var index = contentStart; index < closeIndex; index++)
        {
            if (markdown[index] == '\r')
            {
                payloadStart = index + 1;
                if (payloadStart < closeIndex && markdown[payloadStart] == '\n')
                {
                    payloadStart++;
                }

                return index;
            }

            if (markdown[index] == '\n')
            {
                payloadStart = index + 1;
                return index;
            }
        }

        payloadStart = closeIndex;
        return closeIndex;
    }

    private static EditTagKind GetKind(string headerText)
    {
        if (headerText.StartsWith("ed-start", StringComparison.Ordinal))
        {
            return EditTagKind.Start;
        }

        if (headerText.StartsWith("ed-comm", StringComparison.Ordinal))
        {
            return EditTagKind.Comment;
        }

        return headerText.StartsWith("ed-end", StringComparison.Ordinal)
            ? EditTagKind.End
            : EditTagKind.Unknown;
    }

    private static int? TryGetId(string headerText)
    {
        var match = IdRegex.Match(headerText);
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups["id"].Value, out var id)
            ? id
            : null;
    }

    private static void AddTokenDiagnostics(string markdown, EditTagToken token, List<EditDiagnostic> diagnostics)
    {
        if (StatusRegex.IsMatch(token.HeaderText))
        {
            AddDiagnostic(
                diagnostics,
                markdown,
                EditDiagnosticSeverity.Error,
                EditDiagnosticCodes.StatusAttribute,
                token.StartIndex,
                token.Id,
                "Формат правок не поддерживает атрибут status.");
        }

        if (token.Id is null)
        {
            AddDiagnostic(
                diagnostics,
                markdown,
                EditDiagnosticSeverity.Error,
                EditDiagnosticCodes.MissingId,
                token.StartIndex,
                null,
                "Маркер правки должен содержать id в формате id=\"N\".");
        }
        else if (token.Id <= 0)
        {
            AddDiagnostic(
                diagnostics,
                markdown,
                EditDiagnosticSeverity.Error,
                EditDiagnosticCodes.InvalidId,
                token.StartIndex,
                token.Id,
                "Id правки должен быть положительным целым числом.");
        }
    }

    private static void AddDiagnostic(
        List<EditDiagnostic> diagnostics,
        string markdown,
        EditDiagnosticSeverity severity,
        string code,
        int rawIndex,
        int? editId,
        string message)
    {
        var (line, column) = GetLineColumn(markdown, rawIndex);
        diagnostics.Add(new EditDiagnostic
        {
            Severity = severity,
            Code = code,
            Message = message,
            RawIndex = rawIndex,
            Line = line,
            Column = column,
            EditId = editId
        });
    }

    private static (int Line, int Column) GetLineColumn(string text, int rawIndex)
    {
        var line = 1;
        var column = 1;
        var safeIndex = Math.Clamp(rawIndex, 0, text.Length);

        for (var index = 0; index < safeIndex; index++)
        {
            if (text[index] == '\r')
            {
                line++;
                column = 1;
                if (index + 1 < safeIndex && text[index + 1] == '\n')
                {
                    index++;
                }
            }
            else if (text[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    private static string TrimSingleTrailingLineBreak(string value)
    {
        if (value.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return value[..^2];
        }

        return value.EndsWith('\n') || value.EndsWith('\r')
            ? value[..^1]
            : value;
    }

    private static bool IsInlineFragment(string fragmentMarkdown)
    {
        return !fragmentMarkdown.Contains('\r') && !fragmentMarkdown.Contains('\n');
    }

    private static string ToPlainText(string fragmentMarkdown)
    {
        var withoutHtmlComments = Regex.Replace(fragmentMarkdown, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);
        var withoutMarkdownMarks = Regex.Replace(withoutHtmlComments, @"[`*_>#\[\]()]|!\[", string.Empty, RegexOptions.CultureInvariant);
        var normalizedWhitespace = Regex.Replace(withoutMarkdownMarks, @"\s+", " ", RegexOptions.CultureInvariant);
        return normalizedWhitespace.Trim();
    }

    private static string FormatId(int? id)
    {
        return id?.ToString() ?? "не указан";
    }

    private sealed class ActiveEdit
    {
        public ActiveEdit(int id, int rawStartIndex, int fragmentStartIndex)
        {
            Id = id;
            RawStartIndex = rawStartIndex;
            FragmentStartIndex = fragmentStartIndex;
            FragmentEndIndex = fragmentStartIndex;
        }

        public int Id { get; }

        public int RawStartIndex { get; }

        public int FragmentStartIndex { get; }

        public int FragmentEndIndex { get; set; }

        public string Comment { get; set; } = string.Empty;

        public bool HasComment { get; set; }

        public bool HasStructuralError { get; set; }

        public int CommentRawStartIndex { get; set; }

        public int CommentRawEndIndex { get; set; }
    }
}

internal sealed record EditTagParseResult(ParsedMarkdownDocument Document, IReadOnlyList<EditAnnotationSpan> Spans);

internal sealed record EditAnnotationSpan(
    EditAnnotation Annotation,
    int CommentRawStartIndex,
    int CommentRawEndIndex,
    bool IsStructurallyValid);

internal readonly record struct EditTagToken(
    EditTagKind Kind,
    int? Id,
    int StartIndex,
    int EndIndex,
    string HeaderText,
    string Comment,
    int CommentStartIndex,
    int CommentEndIndex);

internal enum EditTagKind
{
    Unknown,
    Start,
    Comment,
    End
}

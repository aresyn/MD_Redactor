namespace MDRedactor.Core.Documents;

public sealed record MarkdownDocument
{
    private string _text = string.Empty;

    public MarkdownDocument(string filePath, string markdown, string encodingName)
        : this(
            filePath,
            markdown,
            encodingName,
            IsBomEncoding(encodingName),
            DetectNewLineKind(markdown),
            Array.Empty<string>(),
            TryGetLastWriteTimeUtc(filePath),
            backupCreatedInSession: false)
    {
    }

    public MarkdownDocument(
        string filePath,
        string text,
        string encodingName,
        bool hasBom,
        MarkdownNewLineKind newLineKind,
        IReadOnlyList<string> diagnostics,
        DateTimeOffset? lastWriteTimeUtc,
        bool backupCreatedInSession)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(encodingName);

        FilePath = filePath;
        Text = text;
        EncodingName = encodingName;
        HasBom = hasBom;
        NewLineKind = newLineKind;
        Diagnostics = diagnostics;
        LastWriteTimeUtc = lastWriteTimeUtc;
        BackupCreatedInSession = backupCreatedInSession;
    }

    public string FilePath { get; init; }

    public string Text
    {
        get => _text;
        init => _text = value ?? string.Empty;
    }

    public string Markdown
    {
        get => _text;
        init => _text = value ?? string.Empty;
    }

    public string EncodingName { get; init; }

    public string Encoding => EncodingName;

    public bool HasBom { get; init; }

    public MarkdownNewLineKind NewLineKind { get; init; }

    public IReadOnlyList<string> Diagnostics { get; init; }

    public DateTimeOffset? LastWriteTimeUtc { get; init; }

    public bool BackupCreatedInSession { get; init; }

    public string FileName => Path.GetFileName(FilePath);

    public static MarkdownNewLineKind DetectNewLineKind(string text)
    {
        var hasCrLf = false;
        var hasLf = false;
        var hasCr = false;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    hasCrLf = true;
                    index++;
                }
                else
                {
                    hasCr = true;
                }
            }
            else if (text[index] == '\n')
            {
                hasLf = true;
            }
        }

        var kinds = Convert.ToInt32(hasCrLf) + Convert.ToInt32(hasLf) + Convert.ToInt32(hasCr);
        if (kinds == 0)
        {
            return MarkdownNewLineKind.Unknown;
        }

        if (kinds > 1)
        {
            return MarkdownNewLineKind.Mixed;
        }

        if (hasCrLf)
        {
            return MarkdownNewLineKind.CrLf;
        }

        return hasLf ? MarkdownNewLineKind.Lf : MarkdownNewLineKind.Cr;
    }

    private static bool IsBomEncoding(string encodingName)
    {
        return encodingName.Equals("utf-8-bom", StringComparison.OrdinalIgnoreCase)
            || encodingName.Equals("utf-16le", StringComparison.OrdinalIgnoreCase)
            || encodingName.Equals("utf-16be", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset? TryGetLastWriteTimeUtc(string filePath)
    {
        try
        {
            return File.Exists(filePath)
                ? new DateTimeOffset(File.GetLastWriteTimeUtc(filePath), TimeSpan.Zero)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

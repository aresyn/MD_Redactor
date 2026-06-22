using System.Text;
using MDRedactor.Core.Documents;

namespace MDRedactor.Core.Services;

public sealed class MarkdownFileService : IMarkdownFileService
{
    private const string Utf8EncodingName = "utf-8";
    private const string Utf8BomEncodingName = "utf-8-bom";
    private const string Utf16LeEncodingName = "utf-16le";
    private const string Utf16BeEncodingName = "utf-16be";
    private const string Windows1251EncodingName = "windows-1251";
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly UnicodeEncoding Utf16LeWithBom = new(bigEndian: false, byteOrderMark: true);
    private static readonly UnicodeEncoding Utf16BeWithBom = new(bigEndian: true, byteOrderMark: true);

    static MarkdownFileService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<MarkdownDocument> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);
        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        var decoded = Decode(bytes);

        return new MarkdownDocument(
            fullPath,
            decoded.Markdown,
            decoded.EncodingName,
            decoded.HasBom,
            MarkdownDocument.DetectNewLineKind(decoded.Markdown),
            decoded.Diagnostics,
            new DateTimeOffset(File.GetLastWriteTimeUtc(fullPath), TimeSpan.Zero),
            backupCreatedInSession: false);
    }

    public Task WriteAsync(MarkdownDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var encoding = ResolveEncoding(document.EncodingName, document.HasBom);
        return File.WriteAllTextAsync(document.FilePath, document.Markdown, encoding, cancellationToken);
    }

    public async Task<MarkdownSaveResult> SaveAtomicAsync(
        MarkdownDocument document,
        MarkdownSaveOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        options ??= new MarkdownSaveOptions();

        var filePath = Path.GetFullPath(document.FilePath);
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new IOException("Не удалось определить каталог для сохранения Markdown-файла.");
        }

        Directory.CreateDirectory(directory);

        var tempPath = filePath + options.TemporaryExtension;
        var backupPath = filePath + options.BackupExtension;
        var encoding = ResolveEncoding(document.EncodingName, document.HasBom);
        var backupCreated = false;

        try
        {
            await File.WriteAllTextAsync(tempPath, document.Markdown, encoding, cancellationToken);

            var shouldCreateBackup = options.CreateBackup
                && File.Exists(filePath)
                && (!options.BackupOnlyOncePerSession || !document.BackupCreatedInSession);

            if (shouldCreateBackup)
            {
                File.Copy(filePath, backupPath, overwrite: true);
                backupCreated = true;
            }

            if (File.Exists(filePath))
            {
                File.Replace(tempPath, filePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, filePath);
            }

            var lastWriteTimeUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath), TimeSpan.Zero);
            return new MarkdownSaveResult(
                filePath,
                lastWriteTimeUtc,
                backupCreated ? backupPath : null,
                backupCreated);
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    private static DecodedMarkdown Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new DecodedMarkdown(
                Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3),
                Utf8BomEncodingName,
                HasBom: true,
                Array.Empty<string>());
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return new DecodedMarkdown(
                Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2),
                Utf16LeEncodingName,
                HasBom: true,
                Array.Empty<string>());
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return new DecodedMarkdown(
                Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2),
                Utf16BeEncodingName,
                HasBom: true,
                Array.Empty<string>());
        }

        try
        {
            return new DecodedMarkdown(
                StrictUtf8.GetString(bytes),
                Utf8EncodingName,
                HasBom: false,
                Array.Empty<string>());
        }
        catch (DecoderFallbackException)
        {
            var windows1251 = Encoding.GetEncoding(1251);
            return new DecodedMarkdown(
                windows1251.GetString(bytes),
                Windows1251EncodingName,
                HasBom: false,
                new[] { "Кодировка определена как Windows-1251 после неуспешной проверки UTF-8." });
        }
    }

    private static Encoding ResolveEncoding(string encodingName, bool hasBom)
    {
        return encodingName.ToLowerInvariant() switch
        {
            Utf8BomEncodingName => Utf8WithBom,
            Utf8EncodingName => hasBom ? Utf8WithBom : Utf8WithoutBom,
            Utf16LeEncodingName => Utf16LeWithBom,
            Utf16BeEncodingName => Utf16BeWithBom,
            Windows1251EncodingName => Encoding.GetEncoding(1251),
            _ => Utf8WithoutBom
        };
    }

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record DecodedMarkdown(
        string Markdown,
        string EncodingName,
        bool HasBom,
        IReadOnlyList<string> Diagnostics);
}

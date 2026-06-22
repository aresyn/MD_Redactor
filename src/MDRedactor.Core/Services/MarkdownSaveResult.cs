namespace MDRedactor.Core.Services;

public sealed record MarkdownSaveResult(
    string FilePath,
    DateTimeOffset LastWriteTimeUtc,
    string? BackupFilePath,
    bool BackupCreated);

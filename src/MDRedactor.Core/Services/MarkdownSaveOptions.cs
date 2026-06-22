namespace MDRedactor.Core.Services;

public sealed record MarkdownSaveOptions
{
    public bool CreateBackup { get; init; } = true;

    public bool BackupOnlyOncePerSession { get; init; } = true;

    public string TemporaryExtension { get; init; } = ".tmp";

    public string BackupExtension { get; init; } = ".bak";
}

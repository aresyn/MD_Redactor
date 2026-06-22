using MDRedactor.Core.Documents;

namespace MDRedactor.Core.Services;

public interface IMarkdownFileService
{
    Task<MarkdownDocument> ReadAsync(string filePath, CancellationToken cancellationToken = default);

    Task WriteAsync(MarkdownDocument document, CancellationToken cancellationToken = default);

    Task<MarkdownSaveResult> SaveAtomicAsync(
        MarkdownDocument document,
        MarkdownSaveOptions? options = null,
        CancellationToken cancellationToken = default);
}

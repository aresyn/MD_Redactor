namespace MDRedactor.Core.EditTags;

public sealed record EditDiagnostic
{
    public required EditDiagnosticSeverity Severity { get; init; }

    public required string Code { get; init; }

    public required string Message { get; init; }

    public required int RawIndex { get; init; }

    public required int Line { get; init; }

    public required int Column { get; init; }

    public int? EditId { get; init; }
}

namespace MDRedactor.Core.EditTags;

public static class EditDiagnosticCodes
{
    public const string UnknownMarker = "edit.unknown_marker";
    public const string NestedEdit = "edit.nested";
    public const string DuplicateId = "edit.duplicate_id";
    public const string CommentWithoutStart = "edit.comment_without_start";
    public const string CommentIdMismatch = "edit.comment_id_mismatch";
    public const string DuplicateComment = "edit.duplicate_comment";
    public const string UnsafeComment = "edit.unsafe_comment";
    public const string EndWithoutStart = "edit.end_without_start";
    public const string MissingCommentBeforeEnd = "edit.missing_comment_before_end";
    public const string EndIdMismatch = "edit.end_id_mismatch";
    public const string MissingComment = "edit.missing_comment";
    public const string MissingEnd = "edit.missing_end";
    public const string StatusAttribute = "edit.status_attribute";
    public const string MissingId = "edit.missing_id";
    public const string InvalidId = "edit.invalid_id";
}

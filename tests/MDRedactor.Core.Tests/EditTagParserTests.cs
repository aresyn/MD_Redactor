using MDRedactor.Core.EditTags;

namespace MDRedactor.Core.Tests;

public sealed class EditTagParserTests
{
    private readonly EditTagParser _parser = new();
    private readonly EditTagSerializer _serializer = new();
    private readonly EditTagValidator _validator = new();

    [Fact]
    public void Parse_BlockEditWithRussianText_ReturnsAnnotation()
    {
        var markdown = Normalize("""
            До правки
            <!-- ed-start id="1" -->
            Фрагмент текста, который нужно доработать.
            Вторая строка фрагмента.
            <!-- ed-comm id="1"
            Комментарий пользователя к этой правке.
            Вторая строка комментария.
            -->
            <!-- ed-end id="1" -->
            После правки
            """);

        var document = _parser.Parse(markdown);

        Assert.False(document.HasErrors);
        var edit = Assert.Single(document.Edits);
        Assert.Equal(1, edit.Id);
        Assert.Equal(EditAnnotationKind.Block, edit.Kind);
        Assert.Contains("Фрагмент текста", edit.FragmentMarkdown, StringComparison.Ordinal);
        Assert.Contains("Вторая строка фрагмента", edit.FragmentPlainText, StringComparison.Ordinal);
        Assert.Equal(
            Normalize("Комментарий пользователя к этой правке.\r\nВторая строка комментария."),
            edit.Comment);
    }

    [Fact]
    public void Parse_InlineEditInsideParagraph_ReturnsInlineAnnotation()
    {
        var markdown = Normalize("""
            Текст до <!-- ed-start id="2" -->короткий фрагмент<!-- ed-comm id="2"
            Комментарий к короткому фрагменту.
            --><!-- ed-end id="2" --> текст после.
            """);

        var edit = Assert.Single(_parser.GetEdits(markdown));

        Assert.Equal(2, edit.Id);
        Assert.True(edit.IsInlineCandidate);
        Assert.Equal("короткий фрагмент", edit.FragmentMarkdown);
        Assert.Equal("короткий фрагмент", edit.FragmentPlainText);
        Assert.Equal("Комментарий к короткому фрагменту.", edit.Comment);
    }

    [Fact]
    public void GetNextEditId_WithSparseIds_ReturnsMaxPlusOne()
    {
        var markdown = string.Join(
            "\r\n",
            _serializer.BuildEditBlock(1, "Первый фрагмент", "Первый комментарий"),
            _serializer.BuildEditBlock(2, "Второй фрагмент", "Второй комментарий"),
            _serializer.BuildEditBlock(5, "Пятый фрагмент", "Пятый комментарий"));

        var nextId = _parser.GetNextEditId(markdown);

        Assert.Equal(6, nextId);
    }

    [Fact]
    public void RemoveEditKeepFragment_DoesNotRenumberOtherIds()
    {
        var markdown = string.Join(
            "\r\n",
            _serializer.BuildEditBlock(1, "Первый фрагмент", "Первый комментарий"),
            _serializer.BuildEditBlock(2, "Второй фрагмент", "Второй комментарий"),
            _serializer.BuildEditBlock(5, "Пятый фрагмент", "Пятый комментарий"));

        var updated = _serializer.RemoveEditKeepFragment(markdown, 2);
        var edits = _parser.GetEdits(updated);

        Assert.Collection(
            edits,
            edit => Assert.Equal(1, edit.Id),
            edit => Assert.Equal(5, edit.Id));
        Assert.DoesNotContain("id=\"2\"", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveEditKeepFragment_LeavesEditedFragmentText()
    {
        var markdown = _serializer.BuildEditBlock(2, "Фрагмент остается в документе", "Комментарий удаляется");

        var updated = _serializer.RemoveEditKeepFragment(markdown, 2);

        Assert.Contains("Фрагмент остается в документе", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("Комментарий удаляется", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("ed-start", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("ed-comm", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("ed-end", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateComment_ChangesOnlyRequestedEditComment()
    {
        var markdown = string.Join(
            "\r\n",
            _serializer.BuildEditBlock(1, "Первый фрагмент", "Старый первый комментарий"),
            _serializer.BuildEditBlock(2, "Второй фрагмент", "Старый второй комментарий"));

        var updated = _serializer.UpdateComment(markdown, 2, "Новый второй комментарий");
        var edits = _parser.GetEdits(updated);

        Assert.Equal("Старый первый комментарий", edits.Single(edit => edit.Id == 1).Comment);
        Assert.Equal("Новый второй комментарий", edits.Single(edit => edit.Id == 2).Comment);
        Assert.Contains("Первый фрагмент", updated, StringComparison.Ordinal);
        Assert.Contains("Второй фрагмент", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_DuplicateId_ReturnsError()
    {
        var markdown = string.Join(
            "\r\n",
            _serializer.BuildEditBlock(1, "Первый фрагмент", "Первый комментарий"),
            _serializer.BuildEditBlock(1, "Повторный фрагмент", "Повторный комментарий"));

        var diagnostics = _validator.Validate(markdown);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Severity == EditDiagnosticSeverity.Error &&
            diagnostic.Message.Contains("Дублирующийся id", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MismatchedIds_ReturnsError()
    {
        var markdown = Normalize("""
            <!-- ed-start id="1" -->Фрагмент<!-- ed-comm id="2"
            Комментарий
            --><!-- ed-end id="1" -->
            """);

        var diagnostics = _validator.Validate(markdown);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Severity == EditDiagnosticSeverity.Error &&
            diagnostic.Message.Contains("не совпадает", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MissingEnd_ReturnsError()
    {
        var markdown = Normalize("""
            <!-- ed-start id="1" -->Фрагмент<!-- ed-comm id="1"
            Комментарий
            -->
            """);

        var diagnostics = _validator.Validate(markdown);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Severity == EditDiagnosticSeverity.Error &&
            diagnostic.Message.Contains("ed-end", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_NestedStart_ReturnsError()
    {
        var markdown = Normalize("""
            <!-- ed-start id="1" -->
            Внешний фрагмент
            <!-- ed-start id="2" -->
            Внутренний фрагмент
            <!-- ed-comm id="2"
            Внутренний комментарий
            -->
            <!-- ed-end id="2" -->
            <!-- ed-comm id="1"
            Внешний комментарий
            -->
            <!-- ed-end id="1" -->
            """);

        var diagnostics = _validator.Validate(markdown);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Severity == EditDiagnosticSeverity.Error &&
            diagnostic.Message.Contains("Вложенные правки запрещены", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_StatusAttribute_ReturnsError()
    {
        var markdown = Normalize("""
            <!-- ed-start id="1" status="open" -->Фрагмент<!-- ed-comm id="1"
            Комментарий
            --><!-- ed-end id="1" -->
            """);

        var diagnostics = _validator.Validate(markdown);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Severity == EditDiagnosticSeverity.Error &&
            diagnostic.Message.Contains("status", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_CommentWithDoubleHyphen_ReturnsError()
    {
        var markdown = Normalize("""
            <!-- ed-start id="1" -->Фрагмент<!-- ed-comm id="1"
            Небезопасный -- комментарий
            --><!-- ed-end id="1" -->
            """);

        var diagnostics = _validator.Validate(markdown);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Severity == EditDiagnosticSeverity.Error &&
            diagnostic.Message.Contains("\"--\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Serializer_RussianComment_RoundTripsWithoutCorruption()
    {
        var markdown = _serializer.BuildEditBlock(7, "Русский фрагмент", "Комментарий с кириллицей: ёж, сцена, правка.");

        var edit = Assert.Single(_parser.GetEdits(markdown));

        Assert.Equal("Комментарий с кириллицей: ёж, сцена, правка.", edit.Comment);
    }

    [Fact]
    public void StripEditMarkup_RemovesMarkersAndCommentsButKeepsFragments()
    {
        var markdown = Normalize("""
            До <!-- ed-start id="3" -->важный фрагмент<!-- ed-comm id="3"
            Скрытый комментарий
            --><!-- ed-end id="3" --> после.
            """);

        var stripped = _parser.StripEditMarkup(markdown);

        Assert.Contains("До ", stripped, StringComparison.Ordinal);
        Assert.Contains("важный фрагмент", stripped, StringComparison.Ordinal);
        Assert.Contains(" после.", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("Скрытый комментарий", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("ed-start", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("ed-comm", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("ed-end", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WebEditorInlineMarkdownWithEmptyComment_ReturnsNoErrors()
    {
        var markdown = Normalize("""
            Он <!-- ed-start id="3" -->устало посмотрел<!-- ed-comm id="3"

            --><!-- ed-end id="3" --> в окно.
            """);

        var document = _parser.Parse(markdown);

        Assert.False(document.HasErrors);
        var edit = Assert.Single(document.Edits);
        Assert.Equal(3, edit.Id);
        Assert.Equal(string.Empty, edit.Comment);
        Assert.Equal("устало посмотрел", edit.FragmentPlainText);
    }

    private static string Normalize(string value)
    {
        return value.ReplaceLineEndings("\r\n");
    }
}

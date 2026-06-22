using System.Text;
using MDRedactor.Core.Documents;
using MDRedactor.Core.EditTags;
using MDRedactor.Core.Services;

namespace MDRedactor.Core.Tests;

public sealed class MarkdownFileServiceTests
{
    [Fact]
    public async Task ReadAsync_PreservesUtf8CyrillicMarkdown()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.md");
        var service = new MarkdownFileService();
        var expected = "# Заголовок\r\n\r\nТекст с кириллицей и правками.";

        try
        {
            await service.WriteAsync(new MarkdownDocument(filePath, expected, "utf-8"));

            var document = await service.ReadAsync(filePath);

            Assert.Equal(expected, document.Markdown);
            Assert.Equal(expected, document.Text);
            Assert.Equal("utf-8", document.EncodingName);
            Assert.False(document.HasBom);
            Assert.Equal(MarkdownNewLineKind.CrLf, document.NewLineKind);
            Assert.Empty(document.Diagnostics);
            Assert.NotNull(document.LastWriteTimeUtc);
            Assert.Equal(Path.GetFileName(filePath), document.FileName);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task ReadAsync_DetectsWindows1251Markdown()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.md");
        var service = new MarkdownFileService();
        var expected = "# Сцена\r\n\r\nТекст в Windows-1251.";
        var encoding = Encoding.GetEncoding(1251);

        try
        {
            await File.WriteAllTextAsync(filePath, expected, encoding);

            var document = await service.ReadAsync(filePath);

            Assert.Equal(expected, document.Markdown);
            Assert.Equal("windows-1251", document.EncodingName);
            Assert.False(document.HasBom);
            Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Contains("Windows-1251", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task ReadAsync_DetectsUtf8BomAndWriteAsync_PreservesBom()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.md");
        var service = new MarkdownFileService();
        var expected = "# UTF-8 BOM\r\n\r\nРусский текст.";

        try
        {
            await File.WriteAllTextAsync(filePath, expected, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var document = await service.ReadAsync(filePath);
            await service.WriteAsync(document with { Markdown = document.Markdown + "\r\nПродолжение." });
            var bytes = await File.ReadAllBytesAsync(filePath);

            Assert.Equal("utf-8-bom", document.EncodingName);
            Assert.True(document.HasBom);
            Assert.Equal(expected, document.Markdown);
            Assert.True(bytes.AsSpan(0, 3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task ReadAsync_DetectsUtf16BomAndWriteAsync_PreservesEncoding()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.md");
        var service = new MarkdownFileService();
        var expected = "# UTF-16\r\n\r\nРусский текст.";

        try
        {
            await File.WriteAllTextAsync(filePath, expected, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

            var document = await service.ReadAsync(filePath);
            await service.WriteAsync(document with { Markdown = document.Markdown + "\r\nПродолжение." });
            var bytes = await File.ReadAllBytesAsync(filePath);

            Assert.Equal("utf-16le", document.EncodingName);
            Assert.True(document.HasBom);
            Assert.Equal(expected, document.Markdown);
            Assert.True(bytes.AsSpan(0, 2).SequenceEqual(new byte[] { 0xFF, 0xFE }));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task ReadAsync_DetectsUtf16BigEndianBom()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.md");
        var service = new MarkdownFileService();
        var expected = "# UTF-16 BE\r\n\r\nРусский текст.";

        try
        {
            await File.WriteAllTextAsync(filePath, expected, new UnicodeEncoding(bigEndian: true, byteOrderMark: true));

            var document = await service.ReadAsync(filePath);

            Assert.Equal("utf-16be", document.EncodingName);
            Assert.True(document.HasBom);
            Assert.Equal(expected, document.Markdown);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task SaveAtomicAsync_CreatesBackupAndPreservesCyrillic()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"md-redactor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        var filePath = Path.Combine(directoryPath, "scene.md");
        var service = new MarkdownFileService();

        try
        {
            await File.WriteAllTextAsync(filePath, "Старый русский текст", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var document = await service.ReadAsync(filePath);
            var result = await service.SaveAtomicAsync(document with { Markdown = "Новый русский текст с буквой ё" });

            Assert.True(result.BackupCreated);
            Assert.True(File.Exists(filePath + ".bak"));
            Assert.False(File.Exists(filePath + ".tmp"));
            Assert.Equal("Старый русский текст", await File.ReadAllTextAsync(filePath + ".bak", Encoding.UTF8));
            Assert.Equal("Новый русский текст с буквой ё", await File.ReadAllTextAsync(filePath, Encoding.UTF8));
            Assert.Equal(new DateTimeOffset(File.GetLastWriteTimeUtc(filePath), TimeSpan.Zero), result.LastWriteTimeUtc);
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAtomicAsync_PreservesWindows1251Encoding()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"md-redactor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        var filePath = Path.Combine(directoryPath, "legacy.md");
        var service = new MarkdownFileService();
        var windows1251 = Encoding.GetEncoding(1251);

        try
        {
            await File.WriteAllTextAsync(filePath, "Старый текст", windows1251);
            var document = await service.ReadAsync(filePath);

            await service.SaveAtomicAsync(document with { Markdown = "Новый текст: ёлка и сцена" });
            var savedBytes = await File.ReadAllBytesAsync(filePath);
            var savedText = windows1251.GetString(savedBytes);

            Assert.Equal("windows-1251", document.EncodingName);
            Assert.Equal("Новый текст: ёлка и сцена", savedText);
            Assert.DoesNotContain("\\u", savedText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAtomicAsync_DoesNotOverwriteBackupAfterFirstSessionSave()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"md-redactor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        var filePath = Path.Combine(directoryPath, "backup.md");
        var service = new MarkdownFileService();

        try
        {
            await File.WriteAllTextAsync(filePath, "Исходная версия", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var document = await service.ReadAsync(filePath);
            var firstSave = await service.SaveAtomicAsync(document with { Markdown = "Первая сохраненная версия" });
            var afterFirstSave = document with
            {
                Markdown = "Вторая сохраненная версия",
                LastWriteTimeUtc = firstSave.LastWriteTimeUtc,
                BackupCreatedInSession = true
            };

            await service.SaveAtomicAsync(afterFirstSave);

            Assert.Equal("Исходная версия", await File.ReadAllTextAsync(filePath + ".bak", Encoding.UTF8));
            Assert.Equal("Вторая сохраненная версия", await File.ReadAllTextAsync(filePath, Encoding.UTF8));
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAtomicAsync_RemoveEditKeepFragmentPipeline_SavesValidMarkdown()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"md-redactor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        var filePath = Path.Combine(directoryPath, "edits.md");
        var service = new MarkdownFileService();
        var serializer = new EditTagSerializer();
        var validator = new EditTagValidator();
        var parser = new EditTagParser();

        try
        {
            var markdown = string.Join(
                "\r\n",
                serializer.BuildEditBlock(1, "Первый фрагмент", "Первый комментарий"),
                serializer.BuildEditBlock(2, "Второй фрагмент", "Второй комментарий"),
                serializer.BuildEditBlock(5, "Пятый фрагмент", "Пятый комментарий"));
            await File.WriteAllTextAsync(filePath, markdown, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var document = await service.ReadAsync(filePath);
            var afterRemoval = serializer.RemoveEditKeepFragment(document.Markdown, 2);
            var errors = validator.Validate(afterRemoval)
                .Where(diagnostic => diagnostic.Severity == EditDiagnosticSeverity.Error)
                .ToList();

            Assert.Empty(errors);

            await service.SaveAtomicAsync(document with { Markdown = afterRemoval });
            var saved = await service.ReadAsync(filePath);
            var parsed = parser.Parse(saved.Markdown);

            Assert.False(parsed.HasErrors);
            Assert.Equal(new[] { 1, 5 }, parsed.Edits.Select(edit => edit.Id));
            Assert.Contains("Второй фрагмент", parsed.MarkdownWithoutEditMarkup, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task ValidatorBlocksMalformedTagsBeforeSavePipeline_LeavesOriginalFileUntouched()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"md-redactor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        var filePath = Path.Combine(directoryPath, "broken.md");
        var service = new MarkdownFileService();
        var validator = new EditTagValidator();
        var original = "Исходный русский текст";
        var malformed = """
            <!-- ed-start id="1" -->Фрагмент<!-- ed-comm id="2"
            Комментарий
            --><!-- ed-end id="1" -->
            """;

        try
        {
            await File.WriteAllTextAsync(filePath, original, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var document = await service.ReadAsync(filePath);
            var errors = validator.Validate(malformed)
                .Where(diagnostic => diagnostic.Severity == EditDiagnosticSeverity.Error)
                .ToList();

            if (errors.Count == 0)
            {
                await service.SaveAtomicAsync(document with { Markdown = malformed });
            }

            Assert.NotEmpty(errors);
            Assert.Equal(original, await File.ReadAllTextAsync(filePath, Encoding.UTF8));
            Assert.False(File.Exists(filePath + ".bak"));
            Assert.False(File.Exists(filePath + ".tmp"));
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }
}

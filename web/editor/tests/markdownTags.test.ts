import { describe, expect, it } from 'vitest';
import { parseTaggedMarkdown } from '../src/editor/markdownTags';
import { buildTaggedFragment, serializeMarkdownWithTags } from '../src/editor/markdownSerializer';
import { sanitizeCommentForHtmlComment, sortAnnotationsForPanel } from '../src/editor/reviewModel';
import type { EditAnnotation } from '../src/editor/types';

describe('markdownTags', () => {
  it('разбирает блочную правку с русским текстом и скрывает служебные теги', () => {
    const markdown = normalize(`
<!-- ed-start id="1" -->
Фрагмент текста, который нужно доработать.
<!-- ed-comm id="1"
Комментарий пользователя.
-->
<!-- ed-end id="1" -->
`);

    const parsed = parseTaggedMarkdown(markdown);

    expect(parsed.diagnostics).toEqual([]);
    expect(parsed.cleanMarkdown).toContain('Фрагмент текста');
    expect(parsed.cleanMarkdown).not.toContain('ed-start');
    expect(parsed.cleanMarkdown).not.toContain('Комментарий пользователя');
    expect(parsed.annotations).toHaveLength(1);
    expect(parsed.annotations[0]).toMatchObject({
      id: 1,
      comment: 'Комментарий пользователя.',
      kind: 'block',
    });
  });

  it('разбирает несколько правок с id 1, 2, 5 без перенумерации', () => {
    const markdown = [
      buildTaggedFragment({ id: 1, kind: 'inline', comment: 'Первый' }, 'первый'),
      buildTaggedFragment({ id: 2, kind: 'inline', comment: 'Второй' }, 'второй'),
      buildTaggedFragment({ id: 5, kind: 'inline', comment: 'Пятый' }, 'пятый'),
    ].join(' ');

    const parsed = parseTaggedMarkdown(markdown);

    expect(parsed.annotations.map((annotation) => annotation.id)).toEqual([1, 2, 5]);
  });

  it('сериализует правки обратно в короткий формат и не меняет id', () => {
    const annotations: EditAnnotation[] = [
      {
        id: 1,
        fragmentText: 'первый',
        fragmentMarkdown: 'первый',
        comment: 'Первый комментарий',
        kind: 'inline',
        from: 1,
        to: 7,
      },
      {
        id: 5,
        fragmentText: 'пятый',
        fragmentMarkdown: 'пятый',
        comment: 'Пятый комментарий',
        kind: 'inline',
        from: 14,
        to: 19,
      },
    ];

    const markdown = serializeMarkdownWithTags('первый и пятый', annotations);

    expect(markdown).toContain('ed-start id="1"');
    expect(markdown).toContain('ed-comm id="1"');
    expect(markdown).toContain('ed-end id="1"');
    expect(markdown).toContain('ed-start id="5"');
    expect(markdown).not.toContain('id="2"');
  });

  it('возвращает русскую диагностическую ошибку для поврежденной разметки', () => {
    const markdown = normalize(`
<!-- ed-start id="1" -->Фрагмент<!-- ed-comm id="2"
Комментарий
--><!-- ed-end id="1" -->
`);

    const parsed = parseTaggedMarkdown(markdown);

    expect(parsed.diagnostics.some((diagnostic) => diagnostic.message.includes('не совпадает'))).toBe(true);
  });

  it('сохраняет исходную разметку несопоставленной правки при сериализации', () => {
    const raw = buildTaggedFragment({ id: 5, kind: 'inline', comment: 'Комментарий' }, 'исходный');
    const markdown = serializeMarkdownWithTags('Обновленный текст', [
      {
        id: 5,
        fragmentText: 'исходный',
        fragmentMarkdown: 'исходный',
        comment: 'Комментарий',
        kind: 'inline',
        rawTaggedMarkdown: raw,
      },
    ]);

    expect(markdown).toContain('Обновленный текст');
    expect(markdown).toContain(raw);
  });

  it('сохраняет измененный комментарий несопоставленной правки', () => {
    const raw = buildTaggedFragment({ id: 5, kind: 'inline', comment: 'Старый комментарий' }, 'исходный');
    const markdown = serializeMarkdownWithTags('Обновленный текст', [
      {
        id: 5,
        fragmentText: 'исходный',
        fragmentMarkdown: 'исходный',
        comment: 'Новый комментарий',
        kind: 'inline',
        rawTaggedMarkdown: raw,
      },
    ]);

    expect(markdown).toContain('Обновленный текст');
    expect(markdown).toContain('Новый комментарий');
    expect(markdown).not.toContain('Старый комментарий');
  });

  it('изменение комментария сериализуется в Markdown', () => {
    const markdown = serializeMarkdownWithTags('Русский фрагмент', [
      annotation(1, 'Русский фрагмент', 'Новый комментарий с кириллицей', 0, 15),
    ]);
    const parsed = parseTaggedMarkdown(markdown);

    expect(markdown).toContain('Новый комментарий с кириллицей');
    expect(markdown).toContain('ed-comm id="1"');
    expect(parsed.diagnostics).toEqual([]);
  });

  it('многострочный комментарий сохраняется внутри ed-comm', () => {
    const markdown = serializeMarkdownWithTags('Фрагмент', [
      annotation(1, 'Фрагмент', 'Первая строка\nВторая строка', 0, 8),
    ]);

    expect(markdown).toContain('Первая строка\r\nВторая строка');
  });

  it('удаление правки оставляет фрагмент и не меняет id других правок', () => {
    const annotations = [
      annotation(1, 'Первый', 'Комментарий 1', 0, 6),
      annotation(5, 'Пятый', 'Комментарий 5', 9, 14),
    ];

    const markdown = serializeMarkdownWithTags('Первый и Пятый', annotations);

    expect(markdown).toContain('Первый');
    expect(markdown).toContain('Пятый');
    expect(markdown).toContain('id="1"');
    expect(markdown).toContain('id="5"');
    expect(markdown).not.toContain('id="2"');
  });

  it('список правок сортируется по позиции в документе без перенумерации', () => {
    const sorted = sortAnnotationsForPanel([
      annotation(5, 'Пятый', 'Комментарий 5', 20, 25),
      annotation(1, 'Первый', 'Комментарий 1', 0, 6),
      annotation(2, 'Второй', 'Комментарий 2', 10, 16),
    ]);

    expect(sorted.map((item) => item.id)).toEqual([1, 2, 5]);
  });

  it('опасная последовательность в комментарии заменяется безопасно', () => {
    const sanitized = sanitizeCommentForHtmlComment('Нельзя -->, -- и ---- внутри комментария');
    const markdown = serializeMarkdownWithTags('Фрагмент', [
      annotation(1, 'Фрагмент', sanitized.value, 0, 8),
    ]);
    const parsed = parseTaggedMarkdown(markdown);

    expect(sanitized.changed).toBe(true);
    expect(parsed.diagnostics).toEqual([]);
    expect(parsed.annotations[0].comment).toBe('Нельзя - ->, - - и - - - - внутри комментария');
    expect(parsed.annotations[0].comment).not.toContain('--');
  });

  it('сериализатор обезвреживает опасный комментарий перед отправкой host', () => {
    const markdown = serializeMarkdownWithTags('Фрагмент', [
      annotation(1, 'Фрагмент', 'Опасный --> комментарий -- перед сохранением', 0, 8),
    ]);
    const parsed = parseTaggedMarkdown(markdown);

    expect(parsed.diagnostics).toEqual([]);
    expect(parsed.annotations[0].comment).toBe('Опасный - -> комментарий - - перед сохранением');
  });
});

function normalize(value: string): string {
  return value.trim().replace(/\n/g, '\r\n');
}

function annotation(id: number, fragment: string, comment: string, from: number, to: number): EditAnnotation {
  return {
    id,
    from,
    to,
    fragmentText: fragment,
    fragmentMarkdown: fragment,
    comment,
    kind: 'inline',
  };
}

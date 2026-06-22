import { describe, expect, it } from 'vitest';
import { parseTaggedMarkdown } from '../src/editor/markdownTags';
import { buildTaggedFragment, serializeMarkdownWithTags } from '../src/editor/markdownSerializer';
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
});

function normalize(value: string): string {
  return value.trim().replace(/\n/g, '\r\n');
}

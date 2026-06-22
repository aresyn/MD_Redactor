import { describe, expect, it } from 'vitest';
import { EditorState, TextSelection } from 'prosemirror-state';
import { buildCreateEditPlan } from '../src/editor/editCommands';
import { parseMarkdownToDoc } from '../src/editor/prosemirrorSetup';
import { serializeMarkdownWithTags } from '../src/editor/markdownSerializer';
import type { EditAnnotation } from '../src/editor/types';

describe('editCommands', () => {
  it('создание первой правки дает id 1 и пустой комментарий', () => {
    const state = createSelectedState('Русский фрагмент для правки', 'Русский');

    const plan = buildCreateEditPlan(state, []);

    expect(plan.kind).toBe('created');
    if (plan.kind !== 'created') {
      return;
    }

    expect(plan.annotation.id).toBe(1);
    expect(plan.annotation.comment).toBe('');
    expect(plan.annotation.kind).toBe('inline');
    expect(plan.annotation.fragmentText).toBe('Русский');
  });

  it('при существующих id 1, 2, 5 новая правка получает id 6', () => {
    const state = createSelectedState('Новый фрагмент', 'Новый');
    const existing = [
      annotation(1, 20, 25),
      annotation(2, 30, 35),
      annotation(5, 40, 45),
    ];

    const plan = buildCreateEditPlan(state, existing);

    expect(plan.kind).toBe('created');
    if (plan.kind !== 'created') {
      return;
    }

    expect(plan.annotation.id).toBe(6);
    expect(plan.annotations.map((item) => item.id)).toEqual([1, 2, 5, 6]);
  });

  it('создание правки не меняет старые id', () => {
    const state = createSelectedState('Текст для новой правки', 'новой');
    const existing = [annotation(1, 30, 35), annotation(5, 40, 45)];

    const plan = buildCreateEditPlan(state, existing);

    expect(plan.kind).toBe('created');
    if (plan.kind !== 'created') {
      return;
    }

    expect(plan.annotations.slice(0, 2).map((item) => item.id)).toEqual([1, 5]);
  });

  it('пересечение с существующей правкой запрещено', () => {
    const state = createSelectedState('Пересекающийся текст', 'кающий');
    const existing = [annotation(1, 1, 8)];

    const plan = buildCreateEditPlan(state, existing);

    expect(plan.kind).toBe('blocked');
    if (plan.kind !== 'blocked') {
      return;
    }

    expect(plan.reason).toBe('intersects-existing');
    expect(plan.existingId).toBe(1);
    expect(plan.message).toContain('пересекается');
  });

  it('выделение внутри существующей правки не создает новую', () => {
    const state = createSelectedState('Внутри существующей правки', 'существующей');
    const existing = [annotation(3, 1, 27)];

    const plan = buildCreateEditPlan(state, existing);

    expect(plan.kind).toBe('blocked');
    if (plan.kind !== 'blocked') {
      return;
    }

    expect(plan.reason).toBe('inside-existing');
    expect(plan.existingId).toBe(3);
  });

  it('сериализация новой правки содержит ed-start, ed-comm и ed-end', () => {
    const state = createSelectedState('Он устало посмотрел в окно.', 'устало посмотрел');
    const plan = buildCreateEditPlan(state, []);

    expect(plan.kind).toBe('created');
    if (plan.kind !== 'created') {
      return;
    }

    const markdown = serializeMarkdownWithTags('Он устало посмотрел в окно.', plan.annotations);

    expect(markdown).toContain('ed-start id="1"');
    expect(markdown).toContain('ed-comm id="1"');
    expect(markdown).toContain('ed-end id="1"');
    expect(markdown).toContain('устало посмотрел');
  });
});

function createSelectedState(markdown: string, selectedText: string): EditorState {
  const doc = parseMarkdownToDoc(markdown);
  const text = doc.textContent;
  const textOffset = text.indexOf(selectedText);

  if (textOffset < 0) {
    throw new Error(`Тестовый фрагмент не найден: ${selectedText}`);
  }

  const from = 1 + textOffset;
  const to = from + selectedText.length;

  return EditorState.create({
    doc,
    selection: TextSelection.create(doc, from, to),
  });
}

function annotation(id: number, from: number, to: number): EditAnnotation {
  return {
    id,
    from,
    to,
    fragmentText: `Фрагмент ${id}`,
    fragmentMarkdown: `Фрагмент ${id}`,
    comment: `Комментарий ${id}`,
    kind: 'inline',
  };
}

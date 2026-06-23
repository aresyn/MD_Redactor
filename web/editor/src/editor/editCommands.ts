import type { EditorState, Transaction } from 'prosemirror-state';
import type { EditorView } from 'prosemirror-view';
import { getAnnotations, replaceAnnotations, setActiveAnnotation } from './annotationPlugin';
import type { EditAnnotation } from './types';
import { t } from '../i18n';

export type CreateEditPlan =
  | { kind: 'none' }
  | {
      kind: 'blocked';
      reason: 'inside-existing' | 'intersects-existing' | 'partial-multiblock' | 'empty-text';
      message?: string;
      existingId?: number;
    }
  | {
      kind: 'created';
      annotation: EditAnnotation;
      annotations: EditAnnotation[];
    };

export type CreateEditFromSelectionOptions = {
  onCreated(annotation: EditAnnotation): void;
  onBlocked(message: string, existingId?: number): void;
};

export function createEditFromSelection(
  state: EditorState,
  dispatch: ((transaction: Transaction) => void) | undefined,
  _view: EditorView | undefined,
  options: CreateEditFromSelectionOptions,
): boolean {
  const annotations = getAnnotations(state);
  const plan = buildCreateEditPlan(state, annotations);

  if (plan.kind === 'none') {
    return false;
  }

  if (plan.kind === 'blocked') {
    if (dispatch && plan.existingId !== undefined) {
      dispatch(setActiveAnnotation(state.tr, plan.existingId));
    }

    if (plan.message) {
      options.onBlocked(plan.message, plan.existingId);
    }

    return true;
  }

  if (dispatch) {
    options.onCreated(plan.annotation);
    dispatch(replaceAnnotations(state.tr, plan.annotations, {
      activeId: plan.annotation.id,
      markDirty: true,
    }));
  }

  return true;
}

export function buildCreateEditPlan(state: EditorState, annotations: EditAnnotation[]): CreateEditPlan {
  const { selection } = state;
  const { from, to } = selection;

  if (selection.empty || from >= to) {
    return { kind: 'none' };
  }

  const intersecting = findIntersectingAnnotation(annotations, from, to);
  if (intersecting) {
    const inside = from >= intersecting.from! && to <= intersecting.to!;
    return {
      kind: 'blocked',
      reason: inside ? 'inside-existing' : 'intersects-existing',
      existingId: intersecting.id,
      message: inside ? undefined : t('selection.intersectsExisting'),
    };
  }

  const fragmentText = state.doc.textBetween(from, to, ' ', ' ').trim();
  if (fragmentText.length === 0) {
    return {
      kind: 'blocked',
      reason: 'empty-text',
      message: t('selection.emptyText'),
    };
  }

  const selectionKind = classifySelection(state, from, to);
  if (selectionKind === 'partial-multiblock') {
    return {
      kind: 'blocked',
      reason: 'partial-multiblock',
      message: t('selection.partialMultiblock'),
    };
  }

  const nextId = getNextEditId(annotations);
  const annotation: EditAnnotation = {
    id: nextId,
    fragmentText,
    fragmentMarkdown: fragmentText,
    comment: '',
    kind: selectionKind,
    from,
    to,
  };

  return {
    kind: 'created',
    annotation,
    annotations: [...annotations, annotation],
  };
}

export function getNextEditId(annotations: Pick<EditAnnotation, 'id'>[]): number {
  return annotations.reduce((maxId, annotation) => Math.max(maxId, annotation.id), 0) + 1;
}

function classifySelection(state: EditorState, from: number, to: number): 'inline' | 'block' | 'partial-multiblock' {
  const $from = state.doc.resolve(from);
  const $to = state.doc.resolve(to);

  if ($from.sameParent($to) && $from.parent.isTextblock) {
    return 'inline';
  }

  return coversTopLevelBlocks(state, from, to) ? 'block' : 'partial-multiblock';
}

function coversTopLevelBlocks(state: EditorState, from: number, to: number): boolean {
  const blocks: Array<{ contentStart: number; contentEnd: number }> = [];

  state.doc.forEach((node, offset) => {
    if (!node.isBlock) {
      return;
    }

    const contentStart = offset + 1;
    const contentEnd = offset + node.nodeSize - 1;
    if (to > contentStart && from < contentEnd) {
      blocks.push({ contentStart, contentEnd });
    }
  });

  if (blocks.length === 0) {
    return false;
  }

  return from <= blocks[0].contentStart && to >= blocks[blocks.length - 1].contentEnd;
}

function findIntersectingAnnotation(annotations: EditAnnotation[], from: number, to: number): EditAnnotation | undefined {
  return annotations.find((annotation) => {
    if (annotation.from === undefined || annotation.to === undefined) {
      return false;
    }

    return from < annotation.to && to > annotation.from;
  });
}

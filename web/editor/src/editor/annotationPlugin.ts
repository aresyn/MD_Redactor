import type { Node as ProseMirrorNode } from 'prosemirror-model';
import { Decoration, DecorationSet, EditorView } from 'prosemirror-view';
import { Plugin, PluginKey, type EditorState, type Transaction } from 'prosemirror-state';
import type { EditAnnotation, EditDiagnostic } from './types';

type AnnotationPluginState = {
  annotations: EditAnnotation[];
  decorations: DecorationSet;
  activeId?: number;
};

type AnnotationMeta =
  | { type: 'setAnnotations'; annotations: EditAnnotation[] }
  | { type: 'activate'; id?: number };

export const annotationPluginKey = new PluginKey<AnnotationPluginState>('edit-annotations');

export function createAnnotationPlugin(onActivate: (id: number) => void): Plugin<AnnotationPluginState> {
  return new Plugin<AnnotationPluginState>({
    key: annotationPluginKey,
    state: {
      init: (_, state) => ({
        annotations: [],
        decorations: DecorationSet.create(state.doc, []),
      }),
      apply(transaction, pluginState, _, newState) {
        const meta = transaction.getMeta(annotationPluginKey) as AnnotationMeta | undefined;

        if (meta?.type === 'setAnnotations') {
          return {
            annotations: meta.annotations,
            decorations: buildDecorations(newState.doc, meta.annotations, pluginState.activeId),
            activeId: pluginState.activeId,
          };
        }

        if (meta?.type === 'activate') {
          return {
            annotations: pluginState.annotations,
            decorations: buildDecorations(newState.doc, pluginState.annotations, meta.id),
            activeId: meta.id,
          };
        }

        if (transaction.docChanged) {
          const mappedAnnotations = pluginState.annotations.map((annotation) => {
            if (annotation.from === undefined || annotation.to === undefined) {
              return annotation;
            }

            return {
              ...annotation,
              from: transaction.mapping.map(annotation.from),
              to: transaction.mapping.map(annotation.to),
            };
          });

          return {
            annotations: mappedAnnotations,
            decorations: buildDecorations(newState.doc, mappedAnnotations, pluginState.activeId),
            activeId: pluginState.activeId,
          };
        }

        return pluginState;
      },
    },
    props: {
      decorations(state) {
        return annotationPluginKey.getState(state)?.decorations ?? null;
      },
      handleClick(view, _, event) {
        const target = event.target instanceof HTMLElement
          ? event.target.closest<HTMLElement>('[data-edit-id]')
          : null;

        if (!target) {
          return false;
        }

        const id = Number.parseInt(target.dataset.editId ?? '', 10);
        if (!Number.isFinite(id)) {
          return false;
        }

        activateAnnotation(view, id);
        onActivate(id);
        return true;
      },
    },
  });
}

export function setAnnotations(transaction: Transaction, annotations: EditAnnotation[]): Transaction {
  return transaction.setMeta(annotationPluginKey, { type: 'setAnnotations', annotations } satisfies AnnotationMeta);
}

export function activateAnnotation(view: EditorView, id?: number): void {
  view.dispatch(view.state.tr.setMeta(annotationPluginKey, { type: 'activate', id } satisfies AnnotationMeta));
}

export function getAnnotations(state: EditorState): EditAnnotation[] {
  return annotationPluginKey.getState(state)?.annotations ?? [];
}

export function mapAnnotationsToDocument(edits: EditAnnotation[], doc: ProseMirrorNode): { annotations: EditAnnotation[]; diagnostics: EditDiagnostic[] } {
  const textIndex = buildTextIndex(doc);
  const diagnostics: EditDiagnostic[] = [];
  let searchOffset = 0;

  const annotations = edits.map((edit) => {
    const match = findTextRange(textIndex, edit.fragmentText, searchOffset);

    if (!match) {
      const warning = `Не удалось надежно сопоставить правку #${edit.id} с текстом документа. При сохранении исходная разметка правки будет сохранена отдельно.`;
      diagnostics.push({ severity: 'warning', message: warning, index: 0, editId: edit.id });
      return { ...edit, warning };
    }

    searchOffset = match.textEnd;
    return { ...edit, from: match.from, to: match.to };
  });

  return { annotations, diagnostics };
}

function buildDecorations(doc: ProseMirrorNode, annotations: EditAnnotation[], activeId?: number): DecorationSet {
  const decorations: Decoration[] = [];

  for (const annotation of annotations) {
    if (annotation.from === undefined || annotation.to === undefined || annotation.from >= annotation.to) {
      continue;
    }

    const className = annotation.id === activeId
      ? 'edit-annotation edit-annotation-active'
      : 'edit-annotation';

    decorations.push(Decoration.inline(
      annotation.from,
      annotation.to,
      { class: className, 'data-edit-id': String(annotation.id) },
      { id: annotation.id },
    ));

    decorations.push(Decoration.widget(annotation.from, () => {
      const marker = document.createElement('button');
      marker.type = 'button';
      marker.className = annotation.id === activeId
        ? 'edit-annotation-badge edit-annotation-badge-active'
        : 'edit-annotation-badge';
      marker.dataset.editId = String(annotation.id);
      marker.textContent = `#${annotation.id}`;
      marker.title = `Правка #${annotation.id}`;
      return marker;
    }, { key: `annotation-${annotation.id}` }));
  }

  return DecorationSet.create(doc, decorations);
}

type TextIndex = {
  text: string;
  positions: Array<number | undefined>;
};

function buildTextIndex(doc: ProseMirrorNode): TextIndex {
  let text = '';
  const positions: Array<number | undefined> = [];

  doc.forEach((child, offset, index) => {
    if (index > 0) {
      text += '\n';
      positions.push(undefined);
    }

    const childIndex = appendNodeText(child, offset, text, positions);
    text = childIndex.text;
  });

  return { text, positions };
}

function appendNodeText(node: ProseMirrorNode, startPos: number, currentText: string, positions: Array<number | undefined>): { text: string } {
  let text = currentText;

  if (node.isText) {
    const value = node.text ?? '';
    for (let index = 0; index < value.length; index += 1) {
      text += value[index];
      positions.push(startPos + index);
    }

    return { text };
  }

  if (node.type.name === 'hard_break') {
    text += '\n';
    positions.push(undefined);
    return { text };
  }

  node.forEach((child, offset) => {
    const childIndex = appendNodeText(child, startPos + offset + 1, text, positions);
    text = childIndex.text;
  });

  return { text };
}

function findTextRange(index: TextIndex, needle: string, startOffset: number): { from: number; to: number; textEnd: number } | undefined {
  const normalizedNeedle = needle.replace(/\s+/g, ' ').trim();
  if (normalizedNeedle.length === 0) {
    return undefined;
  }

  for (let offset = startOffset; offset < index.text.length; offset += 1) {
    const match = matchAt(index, normalizedNeedle, offset);
    if (match) {
      return match;
    }
  }

  return undefined;
}

function matchAt(index: TextIndex, needle: string, offset: number): { from: number; to: number; textEnd: number } | undefined {
  let sourceIndex = offset;
  let needleIndex = 0;
  let from: number | undefined;
  let to: number | undefined;

  while (needleIndex < needle.length) {
    const needleChar = needle[needleIndex];

    if (/\s/.test(needleChar)) {
      while (needleIndex < needle.length && /\s/.test(needle[needleIndex])) {
        needleIndex += 1;
      }

      let consumedWhitespace = false;
      while (sourceIndex < index.text.length && /\s/.test(index.text[sourceIndex])) {
        consumedWhitespace = true;
        sourceIndex += 1;
      }

      if (!consumedWhitespace) {
        return undefined;
      }

      continue;
    }

    if (sourceIndex >= index.text.length || index.text[sourceIndex] !== needleChar) {
      return undefined;
    }

    const position = index.positions[sourceIndex];
    if (position !== undefined) {
      from ??= position;
      to = position + 1;
    }

    sourceIndex += 1;
    needleIndex += 1;
  }

  if (from === undefined || to === undefined) {
    return undefined;
  }

  return { from, to, textEnd: sourceIndex };
}

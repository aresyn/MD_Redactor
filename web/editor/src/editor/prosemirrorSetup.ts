import { baseKeymap } from 'prosemirror-commands';
import { history, redo, undo } from 'prosemirror-history';
import { keymap } from 'prosemirror-keymap';
import { defaultMarkdownParser, schema } from 'prosemirror-markdown';
import type { Node as ProseMirrorNode } from 'prosemirror-model';
import type { Plugin } from 'prosemirror-state';
import type { EditorView } from 'prosemirror-view';
import { createAnnotationPlugin } from './annotationPlugin';

export const editorSchema = schema;

export type EditorPluginCallbacks = {
  onAnnotationActivate(id: number): void;
  onCreateEditFromSelection: Parameters<typeof keymap>[0][string];
  onClearActive(view: EditorView): boolean;
};

export function parseMarkdownToDoc(markdown: string): ProseMirrorNode {
  return defaultMarkdownParser.parse(markdown || '');
}

export function createEditorPlugins(callbacks: EditorPluginCallbacks): Plugin[] {
  return [
    keymap({
      Enter: callbacks.onCreateEditFromSelection,
      Escape: (_state, _dispatch, view) => (view ? callbacks.onClearActive(view) : false),
    }),
    history(),
    keymap({ 'Mod-z': undo, 'Mod-y': redo, 'Shift-Mod-z': redo }),
    keymap(baseKeymap),
    createAnnotationPlugin(callbacks.onAnnotationActivate),
  ];
}

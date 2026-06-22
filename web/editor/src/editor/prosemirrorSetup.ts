import { baseKeymap } from 'prosemirror-commands';
import { history, redo, undo } from 'prosemirror-history';
import { keymap } from 'prosemirror-keymap';
import { defaultMarkdownParser, schema } from 'prosemirror-markdown';
import type { Node as ProseMirrorNode } from 'prosemirror-model';
import type { Plugin } from 'prosemirror-state';
import { createAnnotationPlugin } from './annotationPlugin';

export const editorSchema = schema;

export function parseMarkdownToDoc(markdown: string): ProseMirrorNode {
  return defaultMarkdownParser.parse(markdown || '');
}

export function createEditorPlugins(onAnnotationActivate: (id: number) => void): Plugin[] {
  return [
    history(),
    keymap({ 'Mod-z': undo, 'Mod-y': redo, 'Shift-Mod-z': redo }),
    keymap(baseKeymap),
    createAnnotationPlugin(onAnnotationActivate),
  ];
}

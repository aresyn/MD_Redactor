import type { Node as ProseMirrorNode } from 'prosemirror-model';
import { defaultMarkdownSerializer } from 'prosemirror-markdown';
import type { EditAnnotation } from './types';

type SerializableAnnotation = EditAnnotation & {
  currentFragmentText?: string;
};

export function serializeDocumentToMarkdown(doc: ProseMirrorNode): string {
  return defaultMarkdownSerializer.serialize(doc, { tightLists: true }).replace(/\n/g, '\r\n');
}

export function serializeMarkdownWithTags(cleanMarkdown: string, annotations: SerializableAnnotation[]): string {
  const ordered = [...annotations].sort((left, right) => {
    const leftPos = left.from ?? Number.MAX_SAFE_INTEGER;
    const rightPos = right.from ?? Number.MAX_SAFE_INTEGER;
    return leftPos - rightPos || left.id - right.id;
  });

  let result = cleanMarkdown;
  let searchStart = 0;
  let offset = 0;
  const unmapped: string[] = [];

  for (const annotation of ordered) {
    const fragmentText = (annotation.currentFragmentText || annotation.fragmentText || '').trim();
    const fallback = annotation.rawTaggedMarkdown || buildTaggedFragment(annotation, annotation.fragmentMarkdown || fragmentText);

    if (annotation.from === undefined || annotation.to === undefined || fragmentText.length === 0) {
      unmapped.push(fallback);
      continue;
    }

    const foundAt = result.indexOf(fragmentText, searchStart + offset);
    if (foundAt < 0) {
      unmapped.push(fallback);
      continue;
    }

    const end = foundAt + fragmentText.length;
    const replacement = buildTaggedFragment(annotation, result.slice(foundAt, end));
    result = result.slice(0, foundAt) + replacement + result.slice(end);
    offset += replacement.length - fragmentText.length;
    searchStart = foundAt + replacement.length - offset;
  }

  if (unmapped.length > 0) {
    const suffix = unmapped.join('\r\n\r\n');
    result = `${result.trimEnd()}\r\n\r\n${suffix}\r\n`;
  }

  return result;
}

export function buildTaggedFragment(annotation: Pick<EditAnnotation, 'id' | 'kind' | 'comment'>, fragmentMarkdown: string): string {
  const safeComment = sanitizeComment(annotation.comment);

  if (annotation.kind === 'block' || fragmentMarkdown.includes('\n') || fragmentMarkdown.includes('\r')) {
    const fragment = normalizeLineEndings(fragmentMarkdown).trimEnd();
    return [
      `<!-- ed-start id="${annotation.id}" -->`,
      fragment,
      `<!-- ed-comm id="${annotation.id}"`,
      safeComment,
      '-->',
      `<!-- ed-end id="${annotation.id}" -->`,
    ].join('\r\n');
  }

  return `<!-- ed-start id="${annotation.id}" -->${fragmentMarkdown}<!-- ed-comm id="${annotation.id}"\r\n${safeComment}\r\n--><!-- ed-end id="${annotation.id}" -->`;
}

function sanitizeComment(comment: string): string {
  return normalizeLineEndings(comment).replaceAll('--', '- -').trimEnd();
}

function normalizeLineEndings(value: string): string {
  return value.replace(/\r\n/g, '\n').replace(/\r/g, '\n').replace(/\n/g, '\r\n');
}

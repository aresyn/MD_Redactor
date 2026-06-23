import type { EditAnnotation } from './types';
import { t } from '../i18n';

const previewLimit = 150;

export function sortAnnotationsForPanel(annotations: EditAnnotation[]): EditAnnotation[] {
  return [...annotations].sort((left, right) => {
    const leftPos = left.from ?? Number.MAX_SAFE_INTEGER;
    const rightPos = right.from ?? Number.MAX_SAFE_INTEGER;
    return leftPos - rightPos || left.id - right.id;
  });
}

export function buildFragmentPreview(annotation: Pick<EditAnnotation, 'fragmentText' | 'fragmentMarkdown' | 'currentFragmentText'>): string {
  const source = annotation.currentFragmentText || annotation.fragmentText || annotation.fragmentMarkdown || t('editor.fragmentNotFound');
  const compact = source.replace(/\s+/g, ' ').trim();

  if (compact.length <= previewLimit) {
    return compact;
  }

  return `${compact.slice(0, previewLimit - 1).trimEnd()}…`;
}

export function sanitizeCommentForHtmlComment(comment: string): { value: string; changed: boolean } {
  let value = comment;

  while (value.includes('--')) {
    value = value.replaceAll('--', '- -');
  }

  return { value, changed: value !== comment };
}

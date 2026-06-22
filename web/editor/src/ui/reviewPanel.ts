import type { EditAnnotation, EditDiagnostic } from '../editor/types';

export type ReviewPanelOptions = {
  annotations: EditAnnotation[];
  diagnostics: EditDiagnostic[];
  activeId?: number;
  focusCommentId?: number;
  onSelect(id: number): void;
  onCommentFocus(id: number): void;
  onCommentChange(id: number, comment: string): void;
};

export function renderReviewPanel(container: HTMLElement, options: ReviewPanelOptions): void {
  container.innerHTML = '';

  const header = document.createElement('div');
  header.className = 'review-header';

  const title = document.createElement('h2');
  title.textContent = 'Правки';

  const count = document.createElement('span');
  count.className = 'review-count';
  count.textContent = String(options.annotations.length);

  header.append(title, count);
  container.append(header);

  if (options.diagnostics.length > 0) {
    const diagnostics = document.createElement('div');
    diagnostics.className = 'diagnostics';

    for (const diagnostic of options.diagnostics) {
      const item = document.createElement('div');
      item.className = diagnostic.severity === 'error' ? 'diagnostic diagnostic-error' : 'diagnostic diagnostic-warning';
      item.textContent = diagnostic.editId === undefined
        ? diagnostic.message
        : `#${diagnostic.editId}: ${diagnostic.message}`;
      diagnostics.append(item);
    }

    container.append(diagnostics);
  }

  if (options.annotations.length === 0) {
    const empty = document.createElement('p');
    empty.className = 'review-empty';
    empty.textContent = 'В документе нет правок.';
    container.append(empty);
    return;
  }

  const list = document.createElement('div');
  list.className = 'review-list';
  let commentToFocus: HTMLTextAreaElement | undefined;

  for (const annotation of options.annotations) {
    const card = document.createElement('div');
    card.className = annotation.id === options.activeId ? 'review-card review-card-active' : 'review-card';
    card.tabIndex = 0;
    card.addEventListener('click', (event) => {
      if (event.target instanceof HTMLTextAreaElement) {
        return;
      }

      options.onSelect(annotation.id);
    });
    card.addEventListener('keydown', (event) => {
      if (event.target instanceof HTMLTextAreaElement) {
        return;
      }

      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault();
        options.onSelect(annotation.id);
      }
    });

    const cardHeader = document.createElement('div');
    cardHeader.className = 'review-card-header';

    const id = document.createElement('strong');
    id.textContent = `#${annotation.id}`;

    const kind = document.createElement('span');
    kind.textContent = annotation.kind === 'inline' ? 'в строке' : 'блок';

    cardHeader.append(id, kind);

    const preview = document.createElement('p');
    preview.className = 'review-preview';
    preview.textContent = annotation.fragmentText || annotation.fragmentMarkdown || 'Фрагмент не найден';

    const comment = document.createElement('textarea');
    comment.className = 'review-comment';
    comment.value = annotation.comment;
    comment.placeholder = 'Что нужно исправить в этом фрагменте?';
    comment.setAttribute('aria-label', `Комментарий к правке #${annotation.id}`);
    comment.addEventListener('focus', () => options.onCommentFocus(annotation.id));
    comment.addEventListener('input', () => options.onCommentChange(annotation.id, comment.value));

    card.append(cardHeader, preview, comment);

    if (annotation.comment.trim().length === 0) {
      const emptyComment = document.createElement('div');
      emptyComment.className = 'review-warning';
      emptyComment.textContent = 'Комментарий не заполнен';
      card.append(emptyComment);
    }

    if (annotation.warning) {
      const warning = document.createElement('div');
      warning.className = 'review-warning';
      warning.textContent = annotation.warning;
      card.append(warning);
    }

    list.append(card);

    if (annotation.id === options.focusCommentId) {
      commentToFocus = comment;
    }
  }

  container.append(list);

  if (commentToFocus) {
    window.requestAnimationFrame(() => {
      commentToFocus?.focus();
      commentToFocus?.select();
    });
  }
}

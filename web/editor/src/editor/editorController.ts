import { EditorState } from 'prosemirror-state';
import { EditorView } from 'prosemirror-view';
import {
  activateAnnotation,
  getAnnotationMeta,
  getAnnotations,
  mapAnnotationsToDocument,
  replaceAnnotations,
  setActiveAnnotation,
  setAnnotations,
} from './annotationPlugin';
import { createEditFromSelection } from './editCommands';
import { parseTaggedMarkdown } from './markdownTags';
import { serializeDocumentToMarkdown, serializeMarkdownWithTags } from './markdownSerializer';
import { createEditorPlugins, parseMarkdownToDoc } from './prosemirrorSetup';
import { sortAnnotationsForPanel } from './reviewModel';
import type { EditAnnotation, EditDiagnostic, LoadedDocument } from './types';
import { renderReviewPanel } from '../ui/reviewPanel';
import { setLanguage, t } from '../i18n';

export type EditorControllerOptions = {
  editorHost: HTMLElement;
  reviewHost: HTMLElement;
  onDirtyChanged(isDirty: boolean): void;
  onError(message: string): void;
  onInfo(message: string): void;
};

export class EditorController {
  private readonly editorHost: HTMLElement;
  private readonly reviewHost: HTMLElement;
  private readonly onDirtyChanged: (isDirty: boolean) => void;
  private readonly onError: (message: string) => void;
  private readonly onInfo: (message: string) => void;
  private view?: EditorView;
  private annotations: EditAnnotation[] = [];
  private diagnostics: EditDiagnostic[] = [];
  private activeId?: number;
  private pendingFocusCommentId?: number;
  private isDirty = false;

  public constructor(options: EditorControllerOptions) {
    this.editorHost = options.editorHost;
    this.reviewHost = options.reviewHost;
    this.onDirtyChanged = options.onDirtyChanged;
    this.onError = options.onError;
    this.onInfo = options.onInfo;
    this.renderReviewPanel();
  }

  public loadDocument(document: LoadedDocument): void {
    const parsed = parseTaggedMarkdown(document.markdown ?? '');

    try {
      const doc = parseMarkdownToDoc(parsed.cleanMarkdown);
      const mapped = mapAnnotationsToDocument(parsed.annotations, doc);
      this.annotations = mapped.annotations;
      this.diagnostics = [...parsed.diagnostics, ...mapped.diagnostics];
      this.activeId = undefined;

      const state = EditorState.create({
        doc,
        plugins: createEditorPlugins({
          onAnnotationActivate: (id) => this.selectAnnotation(id),
          onCreateEditFromSelection: (state, dispatch, view) => createEditFromSelection(state, dispatch, view, {
            onCreated: (annotation) => {
              this.activeId = annotation.id;
              this.pendingFocusCommentId = annotation.id;
            },
            onBlocked: (message, existingId) => {
              if (existingId !== undefined) {
                this.activeId = existingId;
              }

              this.onInfo(message);
            },
          }),
          onClearActive: (view) => {
            this.activeId = undefined;
            view.dispatch(setActiveAnnotation(view.state.tr, undefined));
            return true;
          },
        }),
      });

      if (this.view) {
        this.view.updateState(state);
      } else {
        this.view = new EditorView(this.editorHost, {
          state,
          dispatchTransaction: (transaction) => this.dispatchTransaction(transaction),
          attributes: {
            'aria-label': t('editor.aria'),
          },
        });
      }

      this.view.dispatch(setAnnotations(this.view.state.tr, this.annotations));
      this.setDirty(false);
      this.renderReviewPanel();

      const firstError = this.diagnostics.find((diagnostic) => diagnostic.severity === 'error');
      if (firstError) {
        this.onError(firstError.message);
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      this.onError(t('editor.renderError', { message }));
    }
  }

  public focus(): void {
    this.view?.focus();
  }

  public getMarkdownWithTags(): string {
    if (!this.view) {
      return '';
    }

    const cleanMarkdown = serializeDocumentToMarkdown(this.view.state.doc);
    const annotations = this.annotations.map((annotation) => this.refreshAnnotationText(annotation));
    return serializeMarkdownWithTags(cleanMarkdown, annotations);
  }

  public setTheme(theme: string): void {
    this.editorHost.dataset.theme = theme || 'light';
  }

  public setLanguage(language: string): void {
    setLanguage(language);
    if (this.view) {
      this.view.setProps({
        attributes: {
          'aria-label': t('editor.aria'),
        },
      });
      this.view.dispatch(replaceAnnotations(this.view.state.tr, this.annotations, {
        activeId: this.activeId ?? null,
        shouldRender: false,
      }));
    }

    this.renderReviewPanel();
  }

  private dispatchTransaction(transaction: Parameters<EditorView['dispatch']>[0]): void {
    if (!this.view) {
      return;
    }

    const annotationMeta = getAnnotationMeta(transaction);
    const nextState = this.view.state.apply(transaction);
    this.view.updateState(nextState);
    this.annotations = getAnnotations(nextState);

    if (annotationMeta?.type === 'activate') {
      this.activeId = annotationMeta.id;
    }

    if (annotationMeta?.type === 'setAnnotations') {
      this.activeId = annotationMeta.activeId ?? this.activeId;
    }

    const annotationSetChanged = annotationMeta?.type === 'setAnnotations';

    if (transaction.docChanged || (annotationSetChanged && annotationMeta.markDirty === true)) {
      this.setDirty(true);
    }

    if (transaction.docChanged || (annotationMeta && (!annotationSetChanged || annotationMeta.shouldRender !== false))) {
      this.renderReviewPanel(this.pendingFocusCommentId);
      this.pendingFocusCommentId = undefined;
    }
  }

  private selectAnnotation(id: number, focusEditor = true): void {
    if (this.activeId === id && !focusEditor) {
      return;
    }

    this.activeId = id;
    let dispatchedActivation = false;

    if (this.view) {
      const annotation = getAnnotations(this.view.state).find((item) => item.id === id);
      if (annotation?.from !== undefined && annotation.to !== undefined) {
        if (!focusEditor) {
          this.pendingFocusCommentId = id;
        }

        activateAnnotation(this.view, id);
        if (focusEditor) {
          this.view.focus();
          this.scrollToAnnotation(id);
        }

        dispatchedActivation = true;
      }
    }

    if (!dispatchedActivation) {
      this.renderReviewPanel();
    }
  }

  private goToAnnotation(id: number): void {
    this.selectAnnotation(id);
    this.scrollToAnnotation(id);
  }

  private deleteAnnotation(id: number): void {
    if (!this.view) {
      return;
    }

    const confirmed = window.confirm(t('editor.deleteConfirm', { id }));
    if (!confirmed) {
      return;
    }

    const annotations = this.annotations.filter((annotation) => annotation.id !== id);
    this.annotations = annotations;
    this.activeId = this.activeId === id ? undefined : this.activeId;

    this.view.dispatch(replaceAnnotations(this.view.state.tr, annotations, {
      activeId: this.activeId ?? null,
      markDirty: true,
    }));

    this.onInfo(t('editor.deleteDone', { id }));
  }

  private updateAnnotationComment(id: number, comment: string): void {
    if (!this.view) {
      return;
    }

    const annotations = this.annotations.map((annotation) => annotation.id === id
      ? { ...annotation, comment }
      : annotation);

    this.annotations = annotations;
    this.view.dispatch(replaceAnnotations(this.view.state.tr, annotations, {
      activeId: this.activeId ?? null,
      shouldRender: false,
      markDirty: true,
    }));
  }

  private refreshAnnotationText(annotation: EditAnnotation): EditAnnotation {
    if (!this.view || annotation.from === undefined || annotation.to === undefined) {
      return annotation;
    }

    return {
      ...annotation,
      currentFragmentText: this.view.state.doc.textBetween(annotation.from, annotation.to, ' ', ' ').trim(),
    };
  }

  private scrollToAnnotation(id: number): void {
    if (!this.view) {
      return;
    }

    window.requestAnimationFrame(() => {
      const target = this.view?.dom.querySelector<HTMLElement>(`[data-edit-id="${id}"]`);
      const scrollContainer = target?.closest<HTMLElement>('.document-scroll');
      if (target && scrollContainer) {
        const targetRect = target.getBoundingClientRect();
        const containerRect = scrollContainer.getBoundingClientRect();
        const centeredTop = scrollContainer.scrollTop
          + targetRect.top
          - containerRect.top
          - (scrollContainer.clientHeight - targetRect.height) / 2;

        scrollContainer.scrollTo({
          top: Math.max(0, centeredTop),
          left: scrollContainer.scrollLeft,
          behavior: 'smooth',
        });
      } else if (target) {
        target.scrollIntoView({ block: 'center', inline: 'nearest', behavior: 'smooth' });
      }
    });
  }

  private setDirty(nextValue: boolean): void {
    if (this.isDirty === nextValue) {
      return;
    }

    this.isDirty = nextValue;
    this.onDirtyChanged(nextValue);
  }

  private renderReviewPanel(focusCommentId?: number): void {
    renderReviewPanel(this.reviewHost, {
      annotations: sortAnnotationsForPanel(this.annotations.map((annotation) => this.refreshAnnotationText(annotation))),
      diagnostics: this.diagnostics,
      activeId: this.activeId,
      focusCommentId,
      onSelect: (id) => this.selectAnnotation(id),
      onGoTo: (id) => this.goToAnnotation(id),
      onDelete: (id) => this.deleteAnnotation(id),
      onCommentFocus: (id) => this.selectAnnotation(id, false),
      onCommentChange: (id, comment) => this.updateAnnotationComment(id, comment),
      onUnsafeComment: () => this.onInfo(t('editor.unsafeComment')),
    });
  }
}

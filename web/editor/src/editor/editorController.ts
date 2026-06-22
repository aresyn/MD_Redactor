import { EditorState } from 'prosemirror-state';
import { EditorView } from 'prosemirror-view';
import { activateAnnotation, getAnnotations, mapAnnotationsToDocument, setAnnotations } from './annotationPlugin';
import { parseTaggedMarkdown } from './markdownTags';
import { serializeDocumentToMarkdown, serializeMarkdownWithTags } from './markdownSerializer';
import { createEditorPlugins, parseMarkdownToDoc } from './prosemirrorSetup';
import type { EditAnnotation, EditDiagnostic, LoadedDocument } from './types';
import { renderReviewPanel } from '../ui/reviewPanel';

export type EditorControllerOptions = {
  editorHost: HTMLElement;
  reviewHost: HTMLElement;
  onDirtyChanged(isDirty: boolean): void;
  onError(message: string): void;
};

export class EditorController {
  private readonly editorHost: HTMLElement;
  private readonly reviewHost: HTMLElement;
  private readonly onDirtyChanged: (isDirty: boolean) => void;
  private readonly onError: (message: string) => void;
  private view?: EditorView;
  private annotations: EditAnnotation[] = [];
  private diagnostics: EditDiagnostic[] = [];
  private activeId?: number;
  private isDirty = false;

  public constructor(options: EditorControllerOptions) {
    this.editorHost = options.editorHost;
    this.reviewHost = options.reviewHost;
    this.onDirtyChanged = options.onDirtyChanged;
    this.onError = options.onError;
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
        plugins: createEditorPlugins((id) => this.selectAnnotation(id)),
      });

      if (this.view) {
        this.view.updateState(state);
      } else {
        this.view = new EditorView(this.editorHost, {
          state,
          dispatchTransaction: (transaction) => this.dispatchTransaction(transaction),
          attributes: {
            'aria-label': 'Редактор Markdown',
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
      this.onError(`Не удалось отобразить Markdown: ${message}`);
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
    const annotations = getAnnotations(this.view.state).map((annotation) => this.refreshAnnotationText(annotation));
    return serializeMarkdownWithTags(cleanMarkdown, annotations);
  }

  public setTheme(theme: string): void {
    this.editorHost.dataset.theme = theme || 'light';
  }

  private dispatchTransaction(transaction: Parameters<EditorView['dispatch']>[0]): void {
    if (!this.view) {
      return;
    }

    const nextState = this.view.state.apply(transaction);
    this.view.updateState(nextState);
    this.annotations = getAnnotations(nextState);

    if (transaction.docChanged) {
      this.setDirty(true);
      this.renderReviewPanel();
    }
  }

  private selectAnnotation(id: number): void {
    this.activeId = id;

    if (this.view) {
      const annotation = getAnnotations(this.view.state).find((item) => item.id === id);
      if (annotation?.from !== undefined && annotation.to !== undefined) {
        activateAnnotation(this.view, id);
        this.view.focus();
      }
    }

    this.renderReviewPanel();
  }

  private refreshAnnotationText(annotation: EditAnnotation): EditAnnotation & { currentFragmentText?: string } {
    if (!this.view || annotation.from === undefined || annotation.to === undefined) {
      return annotation;
    }

    return {
      ...annotation,
      currentFragmentText: this.view.state.doc.textBetween(annotation.from, annotation.to, ' ', ' ').trim(),
    };
  }

  private setDirty(nextValue: boolean): void {
    if (this.isDirty === nextValue) {
      return;
    }

    this.isDirty = nextValue;
    this.onDirtyChanged(nextValue);
  }

  private renderReviewPanel(): void {
    renderReviewPanel(this.reviewHost, {
      annotations: this.annotations,
      diagnostics: this.diagnostics,
      activeId: this.activeId,
      onSelect: (id) => this.selectAnnotation(id),
    });
  }
}

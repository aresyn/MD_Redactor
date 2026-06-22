import './styles.css';
import { onHostMessage, postEditorMessage } from './bridge';
import type { HostToEditorMessage } from './bridge';
import { EditorController } from './editor/editorController';
import type { LoadedDocument } from './editor/types';
import { showNotification } from './ui/notifications';
import { applyTheme } from './ui/theme';

const appElement = document.querySelector<HTMLDivElement>('#app');

if (!appElement) {
  throw new Error('Корневой элемент редактора не найден.');
}

const app: HTMLDivElement = appElement;

app.innerHTML = `
  <main class="editor-shell">
    <section class="editor-pane" aria-label="Markdown-редактор">
      <div class="document-bar">
        <div>
          <div class="document-title" id="document-title">Файл не открыт</div>
          <div class="document-meta" id="document-meta">Кодировка: utf-8</div>
        </div>
      </div>
      <div class="document-scroll">
        <div id="editor-host" class="document-page"></div>
      </div>
    </section>
    <aside id="review-panel" class="changes-pane" aria-label="Правки"></aside>
    <div id="notification" class="notification" role="status" aria-live="polite"></div>
  </main>
`;

function requireElement<TElement extends Element>(selector: string): TElement {
  const element = app.querySelector<TElement>(selector);
  if (!element) {
    throw new Error(`Элемент редактора не найден: ${selector}`);
  }

  return element;
}

const documentTitle = requireElement<HTMLDivElement>('#document-title');
const documentMeta = requireElement<HTMLDivElement>('#document-meta');
const editorHost = requireElement<HTMLDivElement>('#editor-host');
const reviewPanel = requireElement<HTMLElement>('#review-panel');
const notification = requireElement<HTMLDivElement>('#notification');

let currentFileName = 'Файл не открыт';
let currentEncodingName = 'utf-8';

const controller = new EditorController({
  editorHost,
  reviewHost: reviewPanel,
  onDirtyChanged: (isDirty) => postEditorMessage({ type: 'editor.dirtyChanged', isDirty }),
  onError: (message) => {
    showNotification(notification, message, 'error');
    postEditorMessage({ type: 'editor.error', message });
  },
  onInfo: (message) => showNotification(notification, message, 'info'),
});

function updateDocumentInfo(): void {
  documentTitle.textContent = currentFileName;
  documentMeta.textContent = `Кодировка: ${currentEncodingName}`;
}

function requestSave(): void {
  postEditorMessage({ type: 'editor.saveRequested', markdown: controller.getMarkdownWithTags() });
}

function loadDocument(message: Extract<HostToEditorMessage, { type: 'host.loadDocument' }>): void {
  currentFileName = message.fileName || 'Без имени';
  currentEncodingName = message.encodingName || 'utf-8';
  updateDocumentInfo();

  controller.loadDocument(message satisfies LoadedDocument);
  controller.focus();
}

window.addEventListener('keydown', (event) => {
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') {
    event.preventDefault();
    requestSave();
  }
});

onHostMessage((message) => {
  try {
    switch (message.type) {
      case 'host.loadDocument':
        loadDocument(message);
        break;
      case 'host.requestMarkdown':
        requestSave();
        break;
      case 'host.setTheme':
        applyTheme(message.theme || 'light');
        controller.setTheme(message.theme || 'light');
        break;
    }
  } catch (error) {
    const errorMessage = error instanceof Error ? error.message : String(error);
    showNotification(notification, errorMessage, 'error');
    postEditorMessage({ type: 'editor.error', message: errorMessage });
  }
});

updateDocumentInfo();
postEditorMessage({ type: 'editor.ready' });

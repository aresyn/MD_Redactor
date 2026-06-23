import './styles.css';
import { onHostMessage, postEditorMessage } from './bridge';
import type { HostToEditorMessage } from './bridge';
import { EditorController } from './editor/editorController';
import type { LoadedDocument } from './editor/types';
import { setLanguage, t } from './i18n';
import { showNotification } from './ui/notifications';
import { applyTheme } from './ui/theme';

const appElement = document.querySelector<HTMLDivElement>('#app');

if (!appElement) {
  throw new Error(t('app.rootMissing'));
}

const app: HTMLDivElement = appElement;

app.innerHTML = `
  <main class="editor-shell">
    <section class="editor-pane" id="editor-pane" aria-label="${t('app.editorAria')}">
      <div class="document-bar">
        <div>
          <div class="document-title" id="document-title">${t('app.fileNotOpened')}</div>
          <div class="document-meta" id="document-meta">${t('app.encoding', { encoding: 'utf-8' })}</div>
        </div>
      </div>
      <div class="document-scroll">
        <div id="editor-host" class="document-page"></div>
      </div>
    </section>
    <aside id="review-panel" class="changes-pane" aria-label="${t('app.reviewAria')}"></aside>
    <div id="notification" class="notification" role="status" aria-live="polite"></div>
  </main>
`;

function requireElement<TElement extends Element>(selector: string): TElement {
  const element = app.querySelector<TElement>(selector);
  if (!element) {
    throw new Error(t('app.elementMissing', { selector }));
  }

  return element;
}

const editorPane = requireElement<HTMLElement>('#editor-pane');
const documentTitle = requireElement<HTMLDivElement>('#document-title');
const documentMeta = requireElement<HTMLDivElement>('#document-meta');
const editorHost = requireElement<HTMLDivElement>('#editor-host');
const reviewPanel = requireElement<HTMLElement>('#review-panel');
const notification = requireElement<HTMLDivElement>('#notification');

let currentFileName = t('app.fileNotOpened');
let currentEncodingName = 'utf-8';
let hasLoadedDocument = false;

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
  documentMeta.textContent = t('app.encoding', { encoding: currentEncodingName });
}

function updateStaticLabels(): void {
  editorPane.setAttribute('aria-label', t('app.editorAria'));
  reviewPanel.setAttribute('aria-label', t('app.reviewAria'));
  if (!hasLoadedDocument) {
    currentFileName = t('app.fileNotOpened');
  }

  updateDocumentInfo();
}

function requestSave(): void {
  postEditorMessage({ type: 'editor.saveRequested', markdown: controller.getMarkdownWithTags() });
}

function loadDocument(message: Extract<HostToEditorMessage, { type: 'host.loadDocument' }>): void {
  currentFileName = message.fileName || t('app.untitled');
  currentEncodingName = message.encodingName || 'utf-8';
  hasLoadedDocument = true;
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
      case 'host.setLanguage':
        setLanguage(message.language);
        controller.setLanguage(message.language);
        updateStaticLabels();
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

export type EditorToHostMessage =
  | { type: 'editor.ready' }
  | { type: 'editor.dirtyChanged'; isDirty: boolean }
  | { type: 'editor.saveRequested'; markdown: string }
  | { type: 'editor.error'; message: string };

export type HostToEditorMessage =
  | {
      type: 'host.loadDocument';
      filePath: string;
      fileName: string;
      markdown: string;
      encodingName: string;
    }
  | { type: 'host.requestMarkdown' }
  | { type: 'host.setTheme'; theme: 'light' | 'dark' | string };

type WebViewMessageEvent = {
  data: unknown;
};

type WebViewBridge = {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (event: WebViewMessageEvent) => void): void;
};

declare global {
  interface Window {
    chrome?: {
      webview?: WebViewBridge;
    };
  }
}

export function postEditorMessage(message: EditorToHostMessage): void {
  if (!window.chrome?.webview) {
    console.debug('Сообщение для приложения пропущено:', message);
    return;
  }

  window.chrome.webview.postMessage(message);
}

export function onHostMessage(handler: (message: HostToEditorMessage) => void): void {
  window.chrome?.webview?.addEventListener('message', (event) => {
    if (!isHostMessage(event.data)) {
      postEditorMessage({ type: 'editor.error', message: 'Получено неизвестное сообщение приложения.' });
      return;
    }

    handler(event.data);
  });
}

function isHostMessage(value: unknown): value is HostToEditorMessage {
  if (!value || typeof value !== 'object' || !('type' in value)) {
    return false;
  }

  const type = String((value as { type: unknown }).type);
  return type === 'host.loadDocument' || type === 'host.requestMarkdown' || type === 'host.setTheme';
}

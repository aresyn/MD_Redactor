export type NotificationKind = 'info' | 'error';

export function showNotification(container: HTMLElement, message: string, kind: NotificationKind = 'info'): void {
  container.textContent = message;
  container.className = kind === 'error' ? 'notification notification-error notification-visible' : 'notification notification-visible';

  window.setTimeout(() => {
    container.classList.remove('notification-visible');
  }, 5000);
}

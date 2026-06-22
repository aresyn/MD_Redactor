export function applyTheme(theme: string): void {
  document.documentElement.dataset.theme = theme || 'light';
}

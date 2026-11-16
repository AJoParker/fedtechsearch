import { Injectable, signal } from '@angular/core';

export type Theme = 'light' | 'dark';

const STORAGE_KEY = 'fedtech-theme';

/**
 * Light/dark theme. index.html sets data-theme before the app loads (saved choice, else the
 * system setting) so the page never flashes the wrong theme; this service takes over from there.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly theme = signal<Theme>(document.documentElement.dataset['theme'] === 'dark' ? 'dark' : 'light');

  constructor() {
    // Follow the system setting until the person picks a theme themselves.
    matchMedia('(prefers-color-scheme: dark)').addEventListener('change', (e) => {
      if (!this.saved()) this.apply(e.matches ? 'dark' : 'light');
    });
  }

  toggle(): void {
    const next: Theme = this.theme() === 'dark' ? 'light' : 'dark';
    this.apply(next);
    try {
      localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // Storage can be blocked (private mode); the choice then lasts for this visit only.
    }
  }

  private apply(theme: Theme): void {
    this.theme.set(theme);
    document.documentElement.dataset['theme'] = theme;
  }

  private saved(): string | null {
    try {
      return localStorage.getItem(STORAGE_KEY);
    } catch {
      return null;
    }
  }
}

import { Component, inject, Injectable, signal } from '@angular/core';

export interface Toast {
  message: string;
  action?: { label: string; run: () => void };
}

/** One short message at a time at the bottom of the page, with an optional action such as Undo. */
@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly current = signal<Toast | null>(null);
  private timer?: ReturnType<typeof setTimeout>;

  show(toast: Toast, ms = 6000): void {
    clearTimeout(this.timer);
    this.current.set(toast);
    this.timer = setTimeout(() => this.current.set(null), ms);
  }

  dismiss(): void {
    clearTimeout(this.timer);
    this.current.set(null);
  }
}

@Component({
  selector: 'app-toast',
  template: `
    <div class="toast-region" aria-live="polite">
      @if (toasts.current(); as t) {
        <div class="toast" role="status">
          <span>{{ t.message }}</span>
          @if (t.action; as a) {
            <button type="button" class="toast-action" (click)="run(a.run)">{{ a.label }}</button>
          }
          <button type="button" class="toast-close" (click)="toasts.dismiss()" aria-label="Dismiss">×</button>
        </div>
      }
    </div>
  `,
})
export class ToastComponent {
  protected readonly toasts = inject(ToastService);

  protected run(action: () => void): void {
    this.toasts.dismiss();
    action();
  }
}

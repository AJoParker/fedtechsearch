import { Component, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AlertsService } from '../../alerts.service';

/** The unsubscribe link in every alert email. A button, so link scanners can't unsubscribe people. */
@Component({
  selector: 'app-alert-unsubscribe-page',
  imports: [RouterLink],
  template: `
    <main class="job-page prose">
      <h2>Unsubscribe</h2>
      @if (done()) {
        <p class="tf-message" role="status">You're unsubscribed. You won't get any more alert emails.</p>
        <p class="fine">Changed your mind? Confirm a new alert from any search. To remove your address entirely, use "Delete all my data" on <a routerLink="/alerts">your alerts</a>.</p>
      } @else {
        <p>Stop all FedTech Search alert emails to this address?</p>
        <button type="button" class="primary" (click)="unsubscribe()" [disabled]="busy()">Unsubscribe from all alerts</button>
        @if (error()) { <p class="tf-error" role="alert">{{ error() }}</p> }
      }
    </main>
  `,
})
export class AlertUnsubscribePage {
  private readonly alerts = inject(AlertsService);

  readonly token = input<string>();
  protected readonly done = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected unsubscribe(): void {
    this.busy.set(true);
    this.alerts.unsubscribe(this.token() ?? '').subscribe({
      next: () => this.done.set(true),
      error: () => {
        this.busy.set(false);
        this.error.set("This unsubscribe link isn't valid. Sign in on the alerts page to manage or delete your alerts.");
      },
    });
  }
}

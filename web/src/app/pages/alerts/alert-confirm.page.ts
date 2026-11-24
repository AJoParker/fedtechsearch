import { Component, inject, input, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AlertsService } from '../../alerts.service';

/** Landing page for the confirm link in the sign-up email. */
@Component({
  selector: 'app-alert-confirm-page',
  imports: [RouterLink],
  template: `
    <main class="job-page prose">
      <h2>Confirming your alert</h2>
      @if (error()) {
        <p class="tf-error" role="alert">{{ error() }}</p>
        <p><a routerLink="/alerts">Go to your alerts</a> · <a routerLink="/">Back to search</a></p>
      } @else {
        <p class="fine">One moment...</p>
      }
    </main>
  `,
})
export class AlertConfirmPage implements OnInit {
  private readonly alerts = inject(AlertsService);
  private readonly router = inject(Router);

  readonly token = input<string>();
  protected readonly error = signal('');

  ngOnInit(): void {
    const token = this.token();
    if (!token) {
      this.error.set('This confirmation link is incomplete.');
      return;
    }
    this.alerts.confirm(token).subscribe({
      next: () => this.router.navigate(['/alerts'], { replaceUrl: true }),
      error: () => this.error.set('This confirmation link has expired or was already used. If you already confirmed, your alert is active.'),
    });
  }
}

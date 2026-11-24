import { Component, inject, input, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { AlertFrequency, AlertsService, AlertsView, SavedAlert } from '../../alerts.service';

/** Manage email alerts. Signed in by a confirm or sign-in link; otherwise offers to email one. */
@Component({
  selector: 'app-alerts-page',
  imports: [DatePipe, RouterLink],
  template: `
    <main class="job-page alerts-page">
      <h2>Job alerts</h2>

      @if (!alerts.session()) {
        <p>Get an email when new federal tech jobs match a search. To manage your alerts, we'll email you a sign-in link.</p>
        @if (linkSent()) {
          <p class="tf-message" role="status">If that address has alerts, a sign-in link is on its way. It works once, for one hour.</p>
        } @else {
          <form class="admin-key" (submit)="sendLink($event, email.value)">
            <label>
              <span>Email</span>
              <input #email type="email" autocomplete="email" required />
            </label>
            <button type="submit" class="primary">Email me a sign-in link</button>
          </form>
        }
        @if (error()) { <p class="tf-error" role="alert">{{ error() }}</p> }
        <p class="fine">To create an alert, search for jobs and choose <strong>Email me new matches</strong>. <a routerLink="/privacy">Privacy</a></p>
      } @else if (view(); as v) {
        <p class="fine">Signed in as {{ v.email }} · <button type="button" class="link" (click)="signOut()">Sign out</button></p>
        @if (v.unsubscribed) {
          <p class="hidden-banner" role="status">You unsubscribed from all alerts. Confirm a new alert to start getting emails again.</p>
        }

        @for (a of v.searches; track a.id) {
          <article class="card alert-card" [class.paused]="a.paused">
            <div class="alert-row">
              <label class="alert-name">
                <span class="visually-hidden">Alert name</span>
                <input type="text" maxlength="80" [value]="a.name" (change)="rename(a, $any($event.target).value)" />
              </label>
              <select [value]="a.frequency" (change)="setFrequency(a, $any($event.target).value)" aria-label="How often">
                <option value="Daily" [selected]="a.frequency === 'Daily'">Daily</option>
                <option value="Weekly" [selected]="a.frequency === 'Weekly'">Weekly</option>
              </select>
            </div>
            <p class="fine">
              @if (!a.confirmed) { <strong>Waiting for you to confirm by email.</strong> · }
              @else if (a.paused) { <strong>Paused.</strong> · }
              @if (a.lastNotifiedAt) { Checked {{ a.lastNotifiedAt | date: 'MMM d, h:mm a' }} · }
              Daily emails go out at 7 AM Eastern; weekly ones on Mondays.
            </p>
            <div class="tf-actions">
              <a [routerLink]="['/']" [queryParams]="params(a)">Open search</a>
              @if (a.confirmed) {
                <button type="button" class="link" (click)="togglePause(a)">{{ a.paused ? 'Resume' : 'Pause' }}</button>
              }
              <button type="button" class="link danger" (click)="remove(a)">Delete</button>
            </div>
          </article>
        } @empty {
          <div class="empty">No alerts yet. Search for jobs and choose <strong>Email me new matches</strong>.</div>
        }

        <section class="danger-zone">
          <h3>Delete all my data</h3>
          <p class="fine">Removes your email address, every alert, and every link we've sent. This can't be undone.</p>
          <button type="button" class="link danger" (click)="deleteEverything()">Delete everything</button>
        </section>
      } @else if (error()) {
        <p class="tf-error" role="alert">{{ error() }}</p>
      } @else {
        <p class="fine">Loading...</p>
      }
    </main>
  `,
})
export class AlertsPage implements OnInit {
  protected readonly alerts = inject(AlertsService);
  private readonly router = inject(Router);

  /** A sign-in link's token, bound from ?token=. */
  readonly token = input<string>();

  protected readonly view = signal<AlertsView | null>(null);
  protected readonly error = signal('');
  protected readonly linkSent = signal(false);

  ngOnInit(): void {
    const token = this.token();
    if (token) {
      this.alerts.signIn(token).subscribe({
        next: () => {
          this.router.navigate(['/alerts'], { replaceUrl: true }); // drop the token from the address bar
          this.load();
        },
        error: () => this.error.set('This sign-in link has expired or was already used. Request a new one below.'),
      });
    } else if (this.alerts.session()) {
      this.load();
    }
  }

  protected sendLink(event: Event, email: string): void {
    event.preventDefault();
    this.error.set('');
    this.alerts.requestLink(email).subscribe({
      next: () => this.linkSent.set(true),
      error: (e) => this.error.set(e.status === 429 ? 'Too many requests from your network. Try again later.' : 'Could not send the link.'),
    });
  }

  protected params(a: SavedAlert): Record<string, string | string[]> {
    const out: Record<string, string | string[]> = {};
    new URLSearchParams(a.queryString).forEach((value, key) => {
      const existing = out[key];
      out[key] = existing === undefined ? value : [...(Array.isArray(existing) ? existing : [existing]), value];
    });
    return out;
  }

  protected rename(a: SavedAlert, name: string): void {
    if (name.trim() && name.trim() !== a.name) this.patch(a, { name: name.trim() });
  }

  protected setFrequency(a: SavedAlert, frequency: AlertFrequency): void {
    this.patch(a, { frequency });
  }

  protected togglePause(a: SavedAlert): void {
    this.patch(a, { paused: !a.paused });
  }

  protected remove(a: SavedAlert): void {
    if (!confirm(`Delete the alert "${a.name}"?`)) return;
    this.alerts.remove(a.id).subscribe({ next: () => this.load(), error: () => this.fail() });
  }

  protected deleteEverything(): void {
    if (!confirm('Delete your email address and all your alerts? This cannot be undone.')) return;
    this.alerts.deleteEverything().subscribe({
      next: () => {
        this.view.set(null);
        this.error.set('');
      },
      error: () => this.fail(),
    });
  }

  protected signOut(): void {
    this.alerts.signOut();
    this.view.set(null);
  }

  private patch(a: SavedAlert, change: { name?: string; frequency?: AlertFrequency; paused?: boolean }): void {
    this.alerts.update(a.id, change).subscribe({ next: () => this.load(), error: () => this.fail() });
  }

  private load(): void {
    this.alerts.list().subscribe({
      next: (v) => this.view.set(v),
      error: (e) => {
        if (e.status === 401) {
          this.alerts.signOut(); // session expired
          this.view.set(null);
        } else this.fail();
      },
    });
  }

  private fail(): void {
    this.error.set('Something went wrong. Please try again.');
  }
}

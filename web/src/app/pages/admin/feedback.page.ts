import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { usajobsLink } from '../../job-format';
import { JobsService } from '../../jobs.service';
import { FeedbackSummary } from '../../models';

const KEY_STORAGE = 'fedtech-admin-key';

const FIELD_LABELS: Record<string, string> = {
  roleFamily: 'Role', tech: 'Tech', workingTitle: 'Working title',
  seniority: 'Seniority', clearance: 'Clearance', workArrangement: 'Work arrangement',
};

/** Grouped "wrong tag" reports: the worklist for fixing taxonomy.json patterns. */
@Component({
  selector: 'app-feedback-admin-page',
  imports: [DatePipe, RouterLink],
  template: `
    <main class="job-page admin">
      <h2>Tag reports</h2>
      <p class="fine">Most-reported first. Fix the pattern in <code>taxonomy.json</code>, bump its version, and re-ingest.</p>

      <form class="admin-key" (submit)="load($event, key.value, days.value)">
        <label>
          <span>Admin key</span>
          <input #key type="password" autocomplete="off" [value]="adminKey()" />
        </label>
        <label>
          <span>Period</span>
          <select #days [value]="period()">
            <option value="30">Last 30 days</option>
            <option value="90">Last 90 days</option>
            <option value="365">Last year</option>
          </select>
        </label>
        <button type="submit" class="primary">Show reports</button>
      </form>

      @if (error()) { <p class="tf-error" aria-live="polite">{{ error() }}</p> }
      @if (loading()) { <p class="fine">Loading...</p> }

      @if (summary(); as s) {
        <p class="total">{{ s.total }} {{ s.total === 1 ? 'report' : 'reports' }} in {{ s.groups.length }} groups</p>
        @for (g of s.groups; track $index) {
          <details class="card report">
            <summary>
              <span class="badge role">{{ label(g.field) }}</span>
              <strong>{{ g.currentValue ?? '(missing)' }}</strong>
              <span aria-hidden="true">→</span>
              <span>{{ g.suggestedValue ?? (g.currentValue ? '(remove)' : '?') }}</span>
              <span class="report-count">{{ g.count }}</span>
              <span class="fine">last {{ g.lastAt | date: 'MMM d' }}</span>
            </summary>
            <ul>
              @for (e of g.examples; track $index) {
                <li>
                  <a [routerLink]="['/jobs', e.jobId]">{{ e.workingTitle }}</a>
                  <span class="fine">{{ e.officialTitle }} · {{ e.createdAt | date: 'MMM d' }}</span>
                  @if (usajobsLink(e.url); as link) { · <a [href]="link" target="_blank" rel="noopener">USAJOBS ↗</a> }
                  @if (e.note) { <p class="report-note">“{{ e.note }}”</p> }
                </li>
              }
            </ul>
          </details>
        } @empty {
          <div class="empty">No reports in this period.</div>
        }
      }
    </main>
  `,
})
export class FeedbackAdminPage {
  private readonly jobs = inject(JobsService);

  protected readonly adminKey = signal(this.storedKey());
  protected readonly period = signal('90');
  protected readonly summary = signal<FeedbackSummary | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly usajobsLink = usajobsLink;

  constructor() {
    if (this.adminKey()) this.fetch();
  }

  protected label(field: string): string {
    return FIELD_LABELS[field] ?? field;
  }

  protected load(event: Event, key: string, days: string): void {
    event.preventDefault();
    this.adminKey.set(key.trim());
    this.period.set(days);
    try {
      sessionStorage.setItem(KEY_STORAGE, this.adminKey());
    } catch {
      // Storage blocked: the key lasts until the page is closed.
    }
    this.fetch();
  }

  private fetch(): void {
    this.loading.set(true);
    this.error.set('');
    this.jobs.feedbackSummary(this.adminKey(), Number(this.period())).subscribe({
      next: (s) => {
        this.summary.set(s);
        this.loading.set(false);
      },
      error: (e: HttpErrorResponse) => {
        this.summary.set(null);
        this.loading.set(false);
        this.error.set(
          e.status === 401 ? 'That key is not right.'
          : e.status === 404 ? 'Admin is turned off. Set ADMIN_API_KEY for the API and restart it.'
          : 'Could not load reports.',
        );
      },
    });
  }

  /** Kept per tab only (sessionStorage), so closing the tab forgets it. */
  private storedKey(): string {
    try {
      return sessionStorage.getItem(KEY_STORAGE) ?? '';
    } catch {
      return '';
    }
  }
}

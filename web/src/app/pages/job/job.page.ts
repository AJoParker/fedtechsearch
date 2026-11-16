import { Component, inject, input, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { toObservable } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, of, switchMap, tap } from 'rxjs';
import { JobDetailComponent } from '../../job-detail';
import { grade, seniorityLabel, usajobsLink } from '../../job-format';
import { JobsService } from '../../jobs.service';
import { JobDetail } from '../../models';

@Component({
  selector: 'app-job-page',
  imports: [CurrencyPipe, DatePipe, RouterLink, JobDetailComponent],
  template: `
    <main class="job-page">
      <p><a routerLink="/">← Back to search</a></p>
      @if (job(); as job) {
        <article class="card open">
          <div class="card-main">
            <div class="card-top">
              <div>
                <h2>{{ job.workingTitle }}</h2>
                <p class="official">{{ job.officialTitle }}</p>
              </div>
              <div class="pay">
                @if (job.salaryMin) {
                  <strong>{{ job.salaryMin | currency: 'USD' : 'symbol' : '1.0-0' }}–{{ job.salaryMax | currency: 'USD' : 'symbol' : '1.0-0' }}</strong>
                }
                @if (job.lowGrade) { <span>{{ grade(job) }}</span> }
              </div>
            </div>
            <p class="meta">
              <span>{{ job.agency }}</span>
              <span>{{ job.locationDisplay }}</span>
              @if (job.closeDate) { <span>Closes {{ job.closeDate | date: 'MMM d' }}</span> }
            </p>
            <div class="badges">
              <span class="badge role">{{ job.roleFamily }}</span>
              <span class="badge" [class.good]="job.workArrangement !== 'On-site'">{{ job.workArrangement }}</span>
              @if (job.clearance !== 'None') { <span class="badge warn">{{ job.clearance }}</span> }
              @if (job.seniority.length) { <span class="badge">{{ seniorityLabel(job) }}</span> }
              @if (job.directHire) { <span class="badge good">Direct hire</span> }
            </div>
            @if (job.tech.length) {
              <div class="chips">
                @for (t of job.tech; track t) { <span class="chip">{{ t }}</span> }
              </div>
            }
          </div>
          @if (usajobsLink(job.url); as link) {
            <div class="card-actions">
              <a class="apply" [href]="link" target="_blank" rel="noopener">Apply on USAJOBS ↗</a>
            </div>
          }
          <div class="detail"><app-job-detail [job]="job" /></div>
        </article>
      } @else if (notFound()) {
        <div class="empty">This job isn't in FedTech Search. It may have closed.</div>
      } @else {
        <p class="fine">Loading...</p>
      }
    </main>
  `,
})
export class JobPage {
  private readonly jobs = inject(JobsService);

  /** Bound from the :id route parameter. */
  readonly id = input.required<string>();

  protected readonly job = signal<JobDetail | null>(null);
  protected readonly notFound = signal(false);
  protected readonly grade = grade;
  protected readonly seniorityLabel = seniorityLabel;
  protected readonly usajobsLink = usajobsLink;

  constructor() {
    toObservable(this.id)
      .pipe(
        tap(() => {
          this.job.set(null);
          this.notFound.set(false);
        }),
        switchMap((id) => this.jobs.get(id).pipe(catchError(() => of(null)))),
      )
      .subscribe((job) => (job ? this.job.set(job) : this.notFound.set(true)));
  }
}

import { Component, input } from '@angular/core';
import { JobDetail } from './models';
import { TagFeedbackComponent } from './tag-feedback';

/** Summary, duties and qualifications. Shared by the expanded search card and the job page. */
@Component({
  selector: 'app-job-detail',
  imports: [TagFeedbackComponent],
  template: `
    @let d = job();
    @if (d.summary) { <p>{{ d.summary }}</p> }
    @if (d.duties.length) {
      <h3>Duties</h3>
      <ul>
        @for (duty of d.duties; track $index) { <li>{{ duty }}</li> }
      </ul>
    }
    @if (d.qualifications) {
      <h3>Qualifications</h3>
      <p>{{ d.qualifications }}</p>
    }
    <p class="fine">
      Announcement {{ d.announcementNumber }} · Series {{ d.series.join(', ') }}
      @if (d.specialties.length) { · Specialty {{ d.specialties.join(', ') }} }
      · Title from {{ d.workingTitleSource }}
    </p>
    <p class="fine">You'll sign in to USAJOBS with login.gov, pick your resume, then finish on the agency's application site.</p>
    <app-tag-feedback [job]="d" />
  `,
})
export class JobDetailComponent {
  readonly job = input.required<JobDetail>();
}

import { Component, computed, input } from '@angular/core';
import { JobDetail } from './models';
import { QualificationsComponent } from './qualifications';
import { TagFeedbackComponent } from './tag-feedback';

/** Summary, duties and qualifications. Shared by the expanded search card and the job page. */
@Component({
  selector: 'app-job-detail',
  imports: [TagFeedbackComponent, QualificationsComponent],
  template: `
    @let d = job();
    @if (d.summary) { <p>{{ d.summary }}</p> }
    @if (d.duties.length) {
      <h3>Duties</h3>
      @if (dutyParagraph(); as paragraph) {
        <div class="duties-card"><p>{{ paragraph }}</p></div>
      } @else {
        <ul>
          @for (duty of d.duties; track $index) { <li>{{ duty }}</li> }
        </ul>
      }
    }
    <app-qualifications [job]="d" />
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

  /**
   * Some postings put all duties in one long paragraph. A lone bullet reads badly, so a single
   * duty with several sentences (or over 160 characters) is shown as a paragraph in its own card.
   */
  protected readonly dutyParagraph = computed(() => {
    const duties = this.job().duties;
    if (duties.length !== 1) return null;
    const text = duties[0].trim();
    const sentences = text.split(/(?<=[.!?])\s+(?=[A-Z])/).length;
    return sentences > 1 || text.length > 160 ? text : null;
  });
}

import { Component, computed, input, signal } from '@angular/core';
import { JobDetail } from './models';

/**
 * Breaks the original text into paragraphs at section markers, without changing a word.
 * Used when the posting couldn't be parsed and for "Show original text".
 */
const PARAGRAPH_BREAK =
  /(?=\bOR\b\s+Education\b)|(?=\b[Ee]ducation\s*:)|(?=\bSpecialized [Ee]xperience\b)|(?=\(\d\)\s+[A-Z])|(?=\bBASIC QUALIFICATION)|(?=\bMinimum Qualifications\b)|(?=\bTo qualify (?:at|for) the GS-)/;

/** "Qualifications for GS-11": structured, verbatim requirements with the original text one click away. */
@Component({
  selector: 'app-qualifications',
  template: `
    @let q = info();
    @if (q?.parsed) {
      <section class="quals" [attr.aria-labelledby]="'quals-title-' + job().id">
        <div class="quals-head">
          <h3 [id]="'quals-title-' + job().id">Qualifications for {{ grades() }}</h3>
          @if (q!.whoMayApply; as who) {
            <span class="badge good">{{ who.toLowerCase().startsWith('open to') ? who : 'Open to ' + who }}</span>
          }
        </div>

        @if (q!.technicalQualifications?.length) {
          <div class="quals-path">
            <h5>Technical qualifications (TQs)</h5>
            <ol class="quals-tqs">
              @for (t of q!.technicalQualifications; track t.label) {
                <li>
                  <span class="quals-tq-label">{{ t.label }}</span>
                  @if (t.title) { <strong>{{ t.title }}</strong> }
                  <p>{{ t.text }}</p>
                </li>
              }
            </ol>
          </div>
        }

        @for (b of q!.blocks; track $index) {
          @if (q!.blocks.length > 1) { <h4 class="quals-grade">{{ b.grade }}</h4> }
          @if (b.education) { <p class="quals-ways">You qualify in one of two ways</p> }
          <div class="quals-paths" [class.two]="!!b.education">
            <div class="quals-path">
              <h5>{{ b.education ? 'Experience' : 'Experience you need' }}</h5>
              <p class="quals-label">
                {{ b.experienceLabel ?? 'Specialized experience' }}@if (b.equivalentGrade) {, equivalent to {{ b.equivalentGrade }}},
                including:
              </p>
              <ul>
                @for (d of b.experienceDuties; track $index) { <li>{{ d }}</li> }
              </ul>
            </div>
            @if (b.education; as e) {
              <div class="quals-path">
                <h5>Education</h5>
                <p>{{ e.requirement }}@if (e.fields.length) { in:}</p>
                @if (e.fields.length) {
                  <ul>
                    @for (f of e.fields; track $index) { <li>{{ f }}</li> }
                  </ul>
                }
                @for (a of e.alternatives; track $index) { <p class="fine">Or: {{ a }}</p> }
              </div>
            }
          </div>
        }

        @if (q!.notes.length) {
          <ul class="quals-notes">
            @for (n of q!.notes; track $index) { <li>{{ n }}</li> }
          </ul>
        }

        @if (q!.executiveCoreQualifications?.length) {
          <div class="quals-comps">
            <p class="control-label">Also required: Executive Core Qualifications (ECQs)</p>
            <div class="chips">
              @for (c of q!.executiveCoreQualifications; track c) { <span class="chip">{{ c }}</span> }
            </div>
          </div>
        }

        @if (q!.standardCompetencies.length) {
          <div class="quals-comps">
            <p class="control-label">Also required (standard for every IT Specialist job)</p>
            <div class="chips">
              @for (c of q!.standardCompetencies; track c) { <span class="chip">{{ c }}</span> }
            </div>
          </div>
        }

        <p class="quals-tip">
          <strong>Tip:</strong>
          @if (q!.technicalQualifications?.length && !q!.blocks.length) {
            your resume should show accomplishments that meet each qualification above, in your own words.
          } @else {
            your resume should show the experience above in your own words, with dates and hours per week.
          }
        </p>

        <button type="button" class="link" (click)="showOriginal.set(!showOriginal())" [attr.aria-expanded]="showOriginal()"
                [attr.aria-controls]="'quals-original-' + job().id">
          {{ showOriginal() ? 'Hide original text' : 'Show original text' }}
        </button>
        @if (showOriginal()) {
          <div [id]="'quals-original-' + job().id" class="quals-original">
            @for (p of paragraphs(); track $index) { <p>{{ p }}</p> }
          </div>
        }
      </section>
    } @else if (job().qualifications) {
      <h3>Qualifications</h3>
      @for (p of paragraphs(); track $index) { <p>{{ p }}</p> }
    }
  `,
})
export class QualificationsComponent {
  readonly job = input.required<JobDetail>();

  protected readonly showOriginal = signal(false);
  protected readonly info = computed(() => this.job().qualificationInfo ?? null);
  /** "GS-11", "GS-14 or GS-15", or the executive pay plan ("SES", "ST") for TQ-style postings. */
  protected readonly grades = computed(() => {
    const fromBlocks = (this.info()?.blocks ?? []).map((b) => b.grade).filter((g): g is string => !!g).join(' or ');
    if (fromBlocks) return fromBlocks;
    const plan = this.job().payPlan;
    return plan === 'ES' ? 'SES' : plan || 'this job';
  });
  protected readonly paragraphs = computed(() =>
    this.job().qualifications.split(PARAGRAPH_BREAK).map((p) => p.trim()).filter((p) => p.length > 0),
  );
}

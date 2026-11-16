import { Component, computed, ElementRef, inject, input, signal, viewChild } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { HiddenJobsStore } from './hidden-jobs.store';
import { JobsService } from './jobs.service';
import { MetaService } from './meta.service';
import { ToastService } from './toast';
import { JobDetail, TagField } from './models';

const MISSING = '__missing__';
const REMOVE = '__remove__';
const NOTE_MAX = 500;

const FIELDS: { key: TagField; label: string }[] = [
  { key: 'roleFamily', label: 'Role' },
  { key: 'tech', label: 'Tech stack' },
  { key: 'workingTitle', label: 'Working title' },
  { key: 'seniority', label: 'Seniority' },
  { key: 'clearance', label: 'Clearance' },
  { key: 'workArrangement', label: 'Work arrangement' },
];

/** "Wrong tag?" link that opens a compact report form. Reports tune taxonomy.json. */
@Component({
  selector: 'app-tag-feedback',
  template: `
    @if (!open()) {
      <button type="button" class="link" (click)="start()" [attr.aria-expanded]="false">Wrong tag?</button>
    } @else {
      <form class="tag-feedback" (submit)="submit($event)" aria-labelledby="tf-title">
        @if (status() === 'done') {
          <p class="tf-message" tabindex="-1" #message aria-live="polite">Thanks, reported. Hide this post?</p>
          <div class="tf-actions">
            <button type="button" class="primary" (click)="hidePost()">Yes, hide it</button>
            <button type="button" class="link" (click)="close()">No thanks</button>
          </div>
        } @else {
          <p id="tf-title" class="control-label">Report a wrong tag</p>
          <div class="tf-row">
            <label>
              <span>Which tag</span>
              <select [value]="field()" (change)="setField($any($event.target).value)">
                @for (f of fields; track f.key) { <option [value]="f.key" [selected]="f.key === field()">{{ f.label }}</option> }
              </select>
            </label>
            <label>
              <span>Looks wrong</span>
              <select [value]="current()" (change)="setCurrent($any($event.target).value)">
                <option value="" disabled [selected]="!current()">Pick one</option>
                @for (v of currentOptions(); track v) { <option [value]="v" [selected]="v === current()">{{ v }}</option> }
                @if (field() === 'tech') {
                  <option [value]="missing" [selected]="current() === missing">Something is missing</option>
                }
              </select>
            </label>
            <label>
              <span>{{ current() === missing ? 'Missing technology' : 'Should be (optional)' }}</span>
              @if (field() === 'workingTitle') {
                <input type="text" maxlength="80" [value]="suggested()" (input)="suggested.set($any($event.target).value)"
                       placeholder="e.g. Data Engineer" />
              } @else {
                <select [value]="suggested()" (change)="suggested.set($any($event.target).value)">
                  @if (current() !== missing) {
                    <option value="" [selected]="!suggested()">Not sure</option>
                    @if (field() === 'tech') { <option [value]="remove" [selected]="suggested() === remove">Remove this tag</option> }
                  } @else {
                    <option value="" disabled [selected]="!suggested()">Pick one</option>
                  }
                  @for (v of suggestionOptions(); track v) { <option [value]="v" [selected]="v === suggested()">{{ v }}</option> }
                </select>
              }
            </label>
          </div>
          <label class="tf-note">
            <span>Note (optional)</span>
            <textarea rows="2" [maxLength]="noteMax" [value]="note()" (input)="note.set($any($event.target).value)"
                      placeholder="What in the posting shows it?"></textarea>
            <span class="fine">{{ note().length }}/{{ noteMax }}</span>
          </label>
          @if (error()) { <p class="tf-error" aria-live="polite">{{ error() }}</p> }
          <div class="tf-actions">
            <button type="submit" class="primary" [disabled]="!canSubmit() || status() === 'sending'">
              {{ status() === 'sending' ? 'Sending...' : 'Send report' }}
            </button>
            <button type="button" class="link" (click)="close()">Cancel</button>
          </div>
        }
      </form>
    }
  `,
})
export class TagFeedbackComponent {
  private readonly jobs = inject(JobsService);
  private readonly meta = inject(MetaService).meta;
  private readonly hiddenJobs = inject(HiddenJobsStore);
  private readonly toasts = inject(ToastService);
  private readonly message = viewChild<ElementRef<HTMLElement>>('message');

  readonly job = input.required<JobDetail>();

  protected readonly fields = FIELDS;
  protected readonly missing = MISSING;
  protected readonly remove = REMOVE;
  protected readonly noteMax = NOTE_MAX;

  protected readonly open = signal(false);
  protected readonly field = signal<TagField>('tech');
  protected readonly current = signal('');
  protected readonly suggested = signal('');
  protected readonly note = signal('');
  protected readonly status = signal<'idle' | 'sending' | 'done'>('idle');
  protected readonly error = signal('');

  /** What the job shows for the chosen field. */
  protected readonly currentOptions = computed(() => {
    const j = this.job();
    switch (this.field()) {
      case 'roleFamily': return [j.roleFamily];
      case 'tech': return j.tech;
      case 'workingTitle': return [j.workingTitle];
      case 'seniority': return j.seniority;
      case 'clearance': return [j.clearance];
      case 'workArrangement': return [j.workArrangement];
    }
  });

  /** Known taxonomy values to suggest instead (minus what's already shown, for tech). */
  protected readonly suggestionOptions = computed(() => {
    const m = this.meta();
    if (!m) return [];
    switch (this.field()) {
      case 'roleFamily': return m.roleFamilies.filter((v) => v !== this.current());
      case 'tech': return m.tech.filter((v) => !this.job().tech.includes(v));
      case 'seniority': return m.seniority.filter((v) => v !== this.current());
      case 'clearance': return m.clearance.filter((v) => v !== this.current());
      case 'workArrangement': return m.work.filter((v) => v !== this.current());
      default: return [];
    }
  });

  protected readonly canSubmit = computed(() =>
    this.current() === MISSING ? !!this.suggested() && this.suggested() !== REMOVE : !!this.current(),
  );

  protected start(): void {
    this.open.set(true);
    this.status.set('idle');
    this.error.set('');
    this.setField(this.job().tech.length ? 'tech' : 'roleFamily');
  }

  protected close(): void {
    this.open.set(false);
    this.note.set('');
  }

  /** Hides the post in this browser; search results reload without it. Undo brings it back. */
  protected async hidePost(): Promise<void> {
    const job = this.job();
    await this.hiddenJobs.hide(job);
    this.close();
    this.toasts.show({ message: 'Post hidden', action: { label: 'Undo', run: () => void this.hiddenJobs.unhide(job.id) } });
  }

  protected setField(field: TagField): void {
    this.field.set(field);
    const options = this.currentOptions();
    this.current.set(options.length === 1 ? options[0] : '');
    this.suggested.set('');
    this.error.set('');
  }

  protected setCurrent(value: string): void {
    this.current.set(value);
    this.suggested.set('');
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (!this.canSubmit()) return;
    const suggested = this.suggested().trim();
    this.status.set('sending');
    this.error.set('');
    this.jobs
      .feedback(this.job().id, {
        field: this.field(),
        currentValue: this.current() === MISSING ? null : this.current(),
        suggestedValue: suggested && suggested !== REMOVE ? suggested : null,
        note: this.note().trim() || null,
      })
      .subscribe({
        next: () => {
          this.status.set('done');
          this.note.set('');
          setTimeout(() => this.message()?.nativeElement.focus());
        },
        error: (e: HttpErrorResponse) => {
          this.status.set('idle');
          this.error.set(
            e.status === 429
              ? 'Too many reports from your network. Please try again later.'
              : (e.error?.error ?? 'Could not send the report. Please try again.'),
          );
        },
      });
  }
}

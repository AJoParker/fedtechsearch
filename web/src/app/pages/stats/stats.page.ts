import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Params, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { StatSeries, StatsResponse, StatsService, StatValue } from '../../stats.service';

/** A trend line needs at least a week of daily snapshots to say anything. */
const MIN_TREND_POINTS = 7;

interface BarRow {
  label: string;
  /** Number shown at the end of the row. */
  value: string;
  /** Extra text after the number, e.g. "12 of 48". */
  detail?: string;
  /** Bar length, 0-100. */
  pct: number;
  /** Stacked instead: fully remote, then telework eligible (each 0-100 of the track). */
  split?: { remote: number; telework: number };
  params: Params;
  change: number | null;
  spark: string | null;
}

interface Section {
  title: string;
  note: string;
  rows: BarRow[];
  /** Rows carry "x of y" detail, so the number column is wider. */
  detail?: boolean;
  /** Shows the remote/telework legend. */
  split?: boolean;
  /** Has sparklines and week-over-week changes (once a week of history exists). */
  trends?: boolean;
}

/** Public stats from the daily snapshots: what tech is in demand, who is hiring, how much is remote. */
@Component({
  selector: 'app-stats-page',
  imports: [RouterLink, DatePipe, DecimalPipe],
  template: `
    <main class="job-page stats-page">
      <h2>Federal tech hiring, by the numbers</h2>

      @if (error()) {
        <div class="error" role="alert">Couldn't load the stats. Please try again later.</div>
      } @else if (!data()) {
        <p class="fine">Loading...</p>
      } @else if (!data()!.date) {
        <div class="empty">Stats appear after the first data load.</div>
      } @else {
        <p class="fine">
          Open postings on USAJOBS that FedTech Search tracks, as of {{ data()!.date | date: 'MMMM d, y' }}.
          Select a row to see those jobs.
        </p>

        <div class="stat-tiles">
          <a class="stat-tile" routerLink="/">
            <span class="stat-number">{{ total().active | number }}</span>
            <span class="stat-label">open tech postings</span>
          </a>
          <a class="stat-tile" routerLink="/" [queryParams]="{ postedWithinDays: 7 }">
            <span class="stat-number">{{ total().new | number }}</span>
            <span class="stat-label">posted in the last 7 days</span>
          </a>
          <a class="stat-tile" routerLink="/" [queryParams]="{ work: flexibleWork }">
            <span class="stat-number">{{ flexibleShare() }}%</span>
            <span class="stat-label">remote or telework eligible ({{ total().remote + total().telework | number }})</span>
          </a>
        </div>

        @if (!hasTrends()) {
          <p class="fine">Week-over-week changes and trend lines appear after a week of daily snapshots.</p>
        }

        <div class="stat-sections">
          @for (section of sections(); track section.title) {
            <section class="card stat-section">
              <h3>{{ section.title }}</h3>
              <p class="fine">{{ section.note }}</p>
              @if (section.split) {
                <p class="legend" aria-hidden="true">
                  <span><span class="swatch remote"></span>Fully remote</span>
                  <span><span class="swatch telework"></span>Telework eligible</span>
                </p>
              }
              @if (section.rows.length) {
                <ol class="bars" [class.with-detail]="section.detail" [class.trends]="section.trends && hasTrends()">
                  @for (row of section.rows; track row.label) {
                    <li>
                      <a class="bar-row" routerLink="/" [queryParams]="row.params">
                        <span class="bar-label">{{ row.label }}</span>
                        <span class="bar-track" aria-hidden="true">
                          @if (row.split; as split) {
                            <span class="bar-fill remote" [style.width.%]="split.remote"></span>
                            <span class="bar-fill telework" [style.width.%]="split.telework"></span>
                          } @else {
                            <span class="bar-fill" [style.width.%]="row.pct"></span>
                          }
                        </span>
                        <span class="bar-value">
                          {{ row.value }}
                          @if (row.detail) { <span class="bar-detail">{{ row.detail }}</span> }
                        </span>
                        @if (section.trends && hasTrends()) {
                          <!-- Always both cells once trends exist, so every row's columns line up -->
                          <span class="spark-cell" aria-hidden="true">
                            @if (row.spark) {
                              <svg class="spark" viewBox="0 0 60 18" width="60" height="18" focusable="false">
                                <polyline [attr.points]="row.spark" />
                              </svg>
                            }
                          </span>
                          <span class="bar-change" [class.up]="(row.change ?? 0) > 0" [class.down]="(row.change ?? 0) < 0">
                            @if (row.change !== null) {
                              {{ row.change > 0 ? '+' : '' }}{{ row.change }}<span class="visually-hidden"> vs last week</span>
                            }
                          </span>
                        }
                      </a>
                    </li>
                  }
                </ol>
              } @else {
                <p class="fine">Nothing to show yet.</p>
              }
            </section>
          }
        </div>
      }
    </main>
  `,
})
export class StatsPage {
  private readonly stats = inject(StatsService);

  private readonly tech = signal<StatsResponse | null>(null);
  private readonly agency = signal<StatsResponse | null>(null);
  private readonly roles = signal<StatsResponse | null>(null);
  protected readonly error = signal(false);
  protected readonly flexibleWork = ['Remote', 'Telework eligible'];

  /** Any response carries the snapshot date and site-wide totals. */
  protected readonly data = computed(() => this.roles());
  protected readonly total = computed(() => this.roles()?.total ?? { active: 0, new: 0, remote: 0, telework: 0 });
  protected readonly flexibleShare = computed(() => percent(this.total().remote + this.total().telework, this.total().active));
  protected readonly hasTrends = computed(() =>
    [this.tech(), this.agency(), this.roles()].some((r) => r?.top.some((v) => v.change7d !== null)),
  );

  protected readonly sections = computed((): Section[] => {
    const tech = this.tech();
    const agency = this.agency();
    const roles = this.roles();
    if (!tech || !agency || !roles) return [];

    const byNew = roles.top.filter((v) => v.new > 0).sort((a, b) => b.new - a.new || a.value.localeCompare(b.value));
    const maxNew = Math.max(1, ...byNew.map((v) => v.new));
    const flexible = (v: StatValue) => (v.remote + v.telework) / v.active;
    const byFlexible = [...roles.top].filter((v) => v.active > 0).sort((a, b) => flexible(b) - flexible(a) || b.active - a.active);

    return [
      {
        title: 'Top tech in federal postings',
        note: 'Open postings that mention each technology.',
        trends: true,
        rows: countRows(tech, (v) => ({ tech: v.value })),
      },
      {
        title: 'Agencies hiring the most in tech',
        note: 'Open tech postings per agency.',
        trends: true,
        rows: countRows(agency, (v) => ({ agency: v.value })),
      },
      {
        title: 'Remote and telework by role',
        note: 'Share of open postings in each role that are fully remote or telework eligible.',
        detail: true,
        split: true,
        rows: byFlexible.map((v) => ({
          label: v.value,
          value: `${percent(v.remote + v.telework, v.active)}%`,
          detail: `${v.remote + v.telework} of ${v.active}`,
          pct: flexible(v) * 100,
          split: { remote: (v.remote / v.active) * 100, telework: (v.telework / v.active) * 100 },
          params: { roleFamily: v.value, work: this.flexibleWork },
          change: null,
          spark: null,
        })),
      },
      {
        title: 'New postings this week',
        note: 'Postings opened in the last 7 days, by role.',
        rows: byNew.map((v) => ({
          label: v.value,
          value: v.new.toLocaleString(),
          pct: (v.new / maxNew) * 100,
          params: { roleFamily: v.value, postedWithinDays: 7 },
          change: null,
          spark: null,
        })),
      },
    ];
  });

  constructor() {
    forkJoin({
      tech: this.stats.get('tech', 15),
      agency: this.stats.get('agency', 10),
      roles: this.stats.get('roleFamily', 25),
    }).subscribe({
      next: (r) => {
        this.tech.set(r.tech);
        this.agency.set(r.agency);
        this.roles.set(r.roles);
      },
      error: () => this.error.set(true),
    });
  }
}

function percent(part: number, whole: number): number {
  return whole > 0 ? Math.round((part / whole) * 100) : 0;
}

/** Bars sized against the section's largest count, with trend data when there's enough of it. */
function countRows(response: StatsResponse, params: (v: StatValue) => Params): BarRow[] {
  const max = Math.max(1, ...response.top.map((v) => v.active));
  return response.top.map((v) => ({
    label: v.value,
    value: v.active.toLocaleString(),
    pct: (v.active / max) * 100,
    params: params(v),
    change: v.change7d,
    spark: sparkline(response.series.find((s) => s.value === v.value)),
  }));
}

/** SVG polyline points for a 60x18 sparkline, or null with too little history. */
function sparkline(series: StatSeries | undefined): string | null {
  const points = series?.points ?? [];
  if (points.length < MIN_TREND_POINTS) return null;
  const values = points.map((p) => p.active);
  const min = Math.min(...values);
  const range = Math.max(1, Math.max(...values) - min);
  return values
    .map((v, i) => `${((i / (values.length - 1)) * 58 + 1).toFixed(1)},${(17 - ((v - min) / range) * 16).toFixed(1)}`)
    .join(' ');
}

import { Component, computed, DestroyRef, ElementRef, inject, signal, viewChild } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpParams } from '@angular/common/http';
import { ActivatedRoute, ParamMap, Params, Router, RouterLink } from '@angular/router';
import { toObservable } from '@angular/core/rxjs-interop';
import { combineLatest, filter, Subscription } from 'rxjs';
import { HiddenJobsStore } from '../../hidden-jobs.store';
import { JobDetailComponent } from '../../job-detail';
import { grade, seniorityLabel, usajobsLink } from '../../job-format';
import { JobsService } from '../../jobs.service';
import {
  FacetValue,
  FILTER_KEYS,
  FILTER_SECTIONS,
  FilterControl,
  FilterSection,
  DEFAULT_RADIUS,
  HIRING_PATH_SHORT,
  RADIUS_OPTIONS,
  JobDetail,
  JobListItem,
  JobSearchResponse,
} from '../../models';

/** Filter values by URL parameter. Single-value controls hold a one-item list. */
type Filters = Record<string, string[]>;

const PAGE_SIZE = 20;
const COLLAPSED_COUNT = 12;
const DAY_MS = 86_400_000;
/** Most recently hidden posts sent with a search; keeps the request URL well under server limits. */
const MAX_HIDDEN_SENT = 300;

@Component({
  selector: 'app-search-page',
  imports: [CurrencyPipe, DatePipe, RouterLink, JobDetailComponent],
  templateUrl: './search.page.html',
})
export class SearchPage {
  private readonly jobs = inject(JobsService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private searchSub?: Subscription;
  private detailSub?: Subscription;
  private readonly filtersDialog = viewChild.required<ElementRef<HTMLDialogElement>>('filtersDialog');
  private readonly hiddenDialog = viewChild.required<ElementRef<HTMLDialogElement>>('hiddenDialog');
  protected readonly hiddenJobs = inject(HiddenJobsStore);
  private readonly filtersPane = viewChild.required<ElementRef<HTMLElement>>('filtersPane');
  private readonly filtersNav = viewChild.required<ElementRef<HTMLElement>>('filtersNav');
  /** While the dialog is open, the first change adds a history entry and later ones replace it. */
  private pushedSinceOpen = false;

  protected readonly sections = FILTER_SECTIONS;
  protected readonly collapsedCount = COLLAPSED_COUNT;
  protected readonly grade = grade;
  protected readonly seniorityLabel = seniorityLabel;
  protected readonly usajobsLink = usajobsLink;

  protected readonly q = signal('');
  protected readonly location = signal('');
  protected readonly includeRemote = signal(true);
  /** Miles around a city; 0 = exact city. Default 25 stays out of the URL. */
  protected readonly radius = signal(DEFAULT_RADIUS);
  protected readonly radiusOptions = RADIUS_OPTIONS;
  protected readonly filters = signal<Filters>({});
  protected readonly sort = signal('newest');
  protected readonly page = signal(1);

  protected readonly response = signal<JobSearchResponse | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly openId = signal<string | null>(null);
  protected readonly detail = signal<JobDetail | null>(null);

  // Dialog-only UI state
  protected readonly filterSearch = signal('');
  protected readonly activeSection = signal(FILTER_SECTIONS[0].id);
  protected readonly find = signal<Record<string, string>>({});
  protected readonly expanded = signal<Record<string, boolean>>({});

  protected readonly items = computed(() => this.response()?.items ?? []);
  protected readonly total = computed(() => this.response()?.total ?? 0);
  protected readonly facets = computed(() => this.response()?.facets ?? {});
  protected readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / PAGE_SIZE)));
  protected readonly locationMode = computed(() => this.response()?.location?.mode ?? null);
  /** City searches get the "Within" select; state searches don't. */
  protected readonly isCitySearch = computed(() => this.locationMode() === 'radius' || this.locationMode() === 'text');
  /** A radius was asked for, but we have no coordinates for this city. */
  protected readonly noMapData = computed(() => this.locationMode() === 'text' && this.radius() > 0);
  /** "nearest" only means something with a radius; show the default otherwise. */
  protected readonly effectiveSort = computed(() =>
    this.sort() === 'nearest' && this.locationMode() !== 'radius' ? (this.q() ? 'relevance' : 'newest') : this.sort(),
  );

  /** Sections in nav order, grouped under their headings. */
  protected readonly groups = computed(() => {
    const visible = this.sections.filter((s) => this.sectionMatches(s));
    const groups: { name: string; sections: FilterSection[] }[] = [];
    for (const s of visible) {
      const g = groups.find((x) => x.name === s.group) ?? groups[groups.push({ name: s.group, sections: [] }) - 1];
      g.sections.push(s);
    }
    return groups;
  });

  protected readonly activeChips = computed(() => {
    const chips: { key: string; value: string; label: string }[] = [];
    if (this.location()) {
      const miles = this.locationMode() === 'radius' ? ` · ${this.radius()} mi` : '';
      chips.push({ key: 'location', value: this.location(), label: this.location() + miles });
    }
    for (const section of this.sections)
      for (const control of section.controls)
        for (const value of this.filters()[control.key] ?? [])
          chips.push({ key: control.key, value, label: this.chipLabel(control, value) });
    return chips;
  });
  protected readonly activeCount = computed(() => this.activeChips().length + (this.q() ? 1 : 0));

  /** Filters set inside the dialog. Query and location live in the header, so they don't count. */
  protected readonly dialogFilterCount = computed(() =>
    Object.values(this.filters()).reduce((n, v) => n + v.length, 0),
  );

  /** "Now" for card badges, captured per response so one render agrees with itself. */
  private readonly now = computed(() => (this.response(), Date.now()));

  constructor() {
    // The URL is the single source of truth: every change navigates, and this reloads.
    // Back/forward and shared links go through the same path. Hidden posts (kept in this
    // browser) also trigger a reload, and the first search waits until they've loaded.
    const sub = combineLatest([
      this.route.queryParamMap,
      toObservable(this.hiddenJobs.ids).pipe(filter((ids) => ids !== null)),
    ]).subscribe(([params]) => {
      this.readUrl(params);
      this.load();
    });
    inject(DestroyRef).onDestroy(() => {
      sub.unsubscribe();
      this.searchSub?.unsubscribe();
      this.detailSub?.unsubscribe();
    });
  }

  // ---- filters dialog --------------------------------------------------------------

  protected openFilters(sectionId = FILTER_SECTIONS[0].id): void {
    this.pushedSinceOpen = false;
    this.filterSearch.set('');
    this.filtersDialog().nativeElement.showModal();
    // Wait a frame so the dialog has laid out before scrolling the pane.
    requestAnimationFrame(() => this.goToSection(sectionId, 'instant'));
  }

  protected closeFilters(): void {
    this.filtersDialog().nativeElement.close();
  }

  /** Clicks on the dialog element itself (not its content) land on the backdrop. */
  protected onDialogClick(event: MouseEvent): void {
    if (event.target === this.filtersDialog().nativeElement) this.closeFilters();
  }

  protected onDialogClosed(): void {
    this.pushedSinceOpen = false;
  }

  protected goToSection(id: string, behavior: ScrollBehavior = 'smooth'): void {
    this.setActiveSection(id, true);
    const pane = this.filtersPane().nativeElement;
    const target = pane.querySelector<HTMLElement>(`#fs-${id}`);
    if (target) pane.scrollTo({ top: target.offsetTop - pane.offsetTop, behavior });
  }

  /** Keeps the nav highlight on the section at the top of the pane while scrolling. */
  protected onPaneScroll(): void {
    const pane = this.filtersPane().nativeElement;
    const top = pane.scrollTop + 24;
    let current = this.activeSection();
    for (const el of Array.from(pane.querySelectorAll<HTMLElement>('section[data-section]'))) {
      if (el.offsetTop - pane.offsetTop <= top) current = el.dataset['section']!;
    }
    if (pane.scrollTop + pane.clientHeight >= pane.scrollHeight - 2) {
      const all = pane.querySelectorAll<HTMLElement>('section[data-section]');
      current = all[all.length - 1]?.dataset['section'] ?? current;
    }
    this.setActiveSection(current);
  }

  /** Highlights a nav item and keeps it visible (the nav is a sideways strip on phones). */
  private setActiveSection(id: string, force = false): void {
    if (this.activeSection() === id && !force) return;
    this.activeSection.set(id);
    requestAnimationFrame(() =>
      this.filtersNav().nativeElement.querySelector('.nav-item.active')?.scrollIntoView({ block: 'nearest', inline: 'nearest' }),
    );
  }

  protected sectionCount(section: FilterSection): number {
    return section.controls.reduce((n, c) => n + (this.filters()[c.key]?.length ?? 0), 0);
  }

  /** The search box shows a section if its name, a control name or any option matches. */
  protected sectionMatches(section: FilterSection): boolean {
    const needle = this.filterSearch().trim().toLowerCase();
    if (!needle) return true;
    if (section.label.toLowerCase().includes(needle)) return true;
    return section.controls.some((c) => this.controlVisible(c, section));
  }

  private controlMatches(control: FilterControl, needle: string): boolean {
    if (control.label.toLowerCase().includes(needle)) return true;
    return (control.type === 'choice' || control.type === 'select') && control.options.some((o) => o.label.toLowerCase().includes(needle));
  }

  /** During a filter search, show only the controls that match (or all, if the section name matches). */
  protected controlVisible(control: FilterControl, section: FilterSection): boolean {
    const needle = this.filterSearch().trim().toLowerCase();
    if (!needle || section.label.toLowerCase().includes(needle)) return true;
    return this.controlMatches(control, needle) || this.pillValues(control, section).length > 0;
  }

  // ---- control helpers ---------------------------------------------------------------

  protected values(key: string): string[] {
    return this.filters()[key] ?? [];
  }

  protected isOn(key: string, value: string): boolean {
    return this.values(key).includes(value);
  }

  /** Facet values for a pills control, filtered by the find box and the dialog search. */
  protected pillValues(control: FilterControl, section: FilterSection): FacetValue[] {
    if (control.type !== 'pills') return [];
    const facet = this.facets()[control.facet ?? control.key] ?? [];
    // Keep selected values visible even when the current results have none left.
    const selected = this.values(control.key).filter((s) => !facet.some((v) => v.value === s));
    let values = [...selected.map((value) => ({ value, count: 0 })), ...facet];

    const search = this.filterSearch().trim().toLowerCase();
    const sectionHit = !search || section.label.toLowerCase().includes(search) || control.label.toLowerCase().includes(search);
    const find = (this.find()[control.key] ?? '').trim().toLowerCase();
    if (!sectionHit) values = values.filter((v) => v.value.toLowerCase().includes(search));
    if (find) values = values.filter((v) => v.value.toLowerCase().includes(find));
    return values;
  }

  protected visiblePills(control: FilterControl, section: FilterSection): FacetValue[] {
    const values = this.pillValues(control, section);
    if (this.expanded()[control.key] || this.find()[control.key] || this.filterSearch()) return values;
    return values.filter((v, i) => i < COLLAPSED_COUNT || this.isOn(control.key, v.value));
  }

  protected facetCount(control: FilterControl, value: string): number | null {
    const facet = 'facet' in control && control.facet ? control.facet : control.type === 'flag' ? 'flags' : control.key;
    return this.facets()[facet]?.find((f) => f.value === value)?.count ?? null;
  }

  protected setFind(key: string, value: string): void {
    this.find.update((f) => ({ ...f, [key]: value }));
  }

  protected toggleExpanded(key: string): void {
    this.expanded.update((e) => ({ ...e, [key]: !e[key] }));
  }

  /** Multi-select: add or remove a value. Single-select (choice/flag): pick it or clear it. */
  protected toggle(key: string, value: string, single = false): void {
    this.filters.update((f) => {
      const current = f[key] ?? [];
      const next = current.includes(value) ? current.filter((v) => v !== value) : single ? [value] : [...current, value];
      const copy = { ...f, [key]: next };
      if (!next.length) delete copy[key];
      return copy;
    });
    this.changed();
  }

  protected setValue(key: string, value: string): void {
    this.filters.update((f) => {
      const copy = { ...f };
      if (value.trim()) copy[key] = [value.trim()];
      else delete copy[key];
      return copy;
    });
    this.changed();
  }

  protected removeChip(key: string, value: string): void {
    if (key === 'location') {
      this.location.set('');
      this.includeRemote.set(true);
      this.radius.set(DEFAULT_RADIUS);
      this.changed();
    } else this.toggle(key, value);
  }

  protected clearAll(): void {
    this.filters.set({});
    this.q.set('');
    this.location.set('');
    this.includeRemote.set(true);
    this.radius.set(DEFAULT_RADIUS);
    this.find.set({});
    this.changed();
  }

  protected setIncludeRemote(value: boolean): void {
    this.includeRemote.set(value);
    this.changed();
  }

  protected setRadius(value: string): void {
    this.radius.set(Number(value));
    this.changed();
  }

  protected radiusLabel(miles: number): string {
    return miles === 0 ? 'Exact city' : `${miles} mi`;
  }

  protected distanceLabel(job: JobListItem): string | null {
    const miles = job.distanceMiles;
    if (miles === null || miles === undefined) return null;
    return miles < 1 ? 'under 1 mi away' : `${Math.round(miles)} mi away`;
  }

  protected setSort(value: string): void {
    this.sort.set(value);
    this.changed();
  }

  protected goTo(page: number): void {
    this.page.set(page);
    this.navigate();
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  private chipLabel(control: FilterControl, value: string): string {
    switch (control.type) {
      case 'flag':
        return control.label;
      case 'choice':
      case 'select': {
        const option = control.options.find((o) => o.value === value)?.label ?? value;
        if (control.key === 'minGrade') return `${option} or above`;
        if (control.key === 'maxGrade') return `${option} or below`;
        if (control.key === 'minPromotionPotential') return `Grows to ${option}`;
        return control.type === 'choice' && control.key !== 'supervisory' ? `${control.label}: ${option}` : option;
      }
      case 'text':
        return control.exclude ? `Not: "${value}"` : value;
      default:
        return control.exclude ? `Not: ${value}` : value;
    }
  }

  // ---- hidden posts ------------------------------------------------------------------

  protected openHidden(): void {
    this.hiddenDialog().nativeElement.showModal();
  }

  protected closeHidden(): void {
    this.hiddenDialog().nativeElement.close();
  }

  protected onHiddenDialogClick(event: MouseEvent): void {
    if (event.target === this.hiddenDialog().nativeElement) this.closeHidden();
  }

  /** Closes the list once the last hidden post is restored. */
  protected async unhide(id: string): Promise<void> {
    await this.hiddenJobs.unhide(id);
    if (!this.hiddenJobs.hidden().length) this.closeHidden();
  }

  protected async unhideAll(): Promise<void> {
    await this.hiddenJobs.unhideAll();
    this.closeHidden();
  }

  // ---- card helpers -------------------------------------------------------------------

  protected isNew(job: JobListItem): boolean {
    const opened = Date.parse(job.openDate ?? job.firstSeen);
    return this.now() - opened < 2 * DAY_MS;
  }

  /** Days until close when it is 5 or fewer, else null. */
  protected closesInDays(job: JobListItem): number | null {
    if (!job.closeDate) return null;
    const days = Math.ceil((Date.parse(job.closeDate) - this.now()) / DAY_MS);
    return days >= 0 && days <= 5 ? days : null;
  }

  protected openTo(job: JobListItem): string {
    return job.hiringPaths.map((p) => HIRING_PATH_SHORT[p] ?? p.toLowerCase()).join(', ');
  }

  protected toggleDetail(job: JobListItem): void {
    if (this.openId() === job.id) {
      this.openId.set(null);
      return;
    }
    this.openId.set(job.id);
    this.detail.set(null);
    this.detailSub?.unsubscribe();
    this.detailSub = this.jobs.get(job.id).subscribe({
      next: (d) => this.detail.set(d),
      error: () => this.detail.set(null),
    });
  }

  // ---- loading and URL state -------------------------------------------------------

  private changed(): void {
    this.page.set(1);
    this.navigate();
  }

  private buildParams(): HttpParams {
    let p = new HttpParams();
    if (this.q()) p = p.set('q', this.q());
    if (this.location()) p = p.set('location', this.location());
    if (this.location() && !this.includeRemote()) p = p.set('includeRemote', 'false');
    if (this.location() && this.radius() !== DEFAULT_RADIUS) p = p.set('radius', this.radius());
    for (const key of FILTER_KEYS) for (const v of this.filters()[key] ?? []) p = p.append(key, v);
    const defaultSort = this.q() ? 'relevance' : 'newest';
    if (this.sort() !== defaultSort && !(this.sort() === 'relevance' && !this.q())) p = p.set('sort', this.sort());
    if (this.page() > 1) p = p.set('page', this.page());
    return p;
  }

  private navigate(): void {
    const params = this.buildParams();
    const queryParams: Params = {};
    for (const key of params.keys()) queryParams[key] = params.getAll(key);
    const open = this.filtersDialog().nativeElement.open;
    const replaceUrl = open && this.pushedSinceOpen;
    if (open) this.pushedSinceOpen = true;
    this.router.navigate([], { relativeTo: this.route, queryParams, replaceUrl });
  }

  private load(): void {
    this.loading.set(true);
    this.error.set('');
    this.openId.set(null);
    this.searchSub?.unsubscribe();
    // Hidden IDs go to the API only, never into the shareable URL (buildParams).
    let params = this.buildParams().set('pageSize', PAGE_SIZE);
    for (const id of (this.hiddenJobs.ids() ?? []).slice(0, MAX_HIDDEN_SENT)) params = params.append('hide', id);
    this.searchSub = this.jobs.search(params).subscribe({
      next: (r) => {
        this.response.set(r);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not reach the search API. Is it running?');
        this.loading.set(false);
      },
    });
  }

  /** Restores state from the address bar so searches can be bookmarked and shared. */
  private readUrl(url: ParamMap): void {
    const filters: Filters = {};
    for (const key of FILTER_KEYS) {
      const values = url.getAll(key);
      if (values.length) filters[key] = values;
    }
    // Older links used an openToPublic flag; it is a hiring path now.
    if (url.get('openToPublic') === 'true' && !filters['hiringPath']?.includes('Open to the public'))
      filters['hiringPath'] = [...(filters['hiringPath'] ?? []), 'Open to the public'];
    this.filters.set(filters);
    this.q.set(url.get('q') ?? '');
    this.location.set(url.get('location') ?? '');
    this.includeRemote.set(url.get('includeRemote') !== 'false');
    const radius = Number(url.get('radius'));
    this.radius.set(url.has('radius') && RADIUS_OPTIONS.includes(radius) ? radius : DEFAULT_RADIUS);
    this.sort.set(url.get('sort') ?? (this.q() ? 'relevance' : 'newest'));
    this.page.set(Number(url.get('page')) || 1);
  }
}

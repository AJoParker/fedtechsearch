import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Params, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink],
  template: `
    <header class="top">
      <a class="brand" routerLink="/">
        <span class="logo" aria-hidden="true">◆</span>
        <div>
          <h1>FedTech Search</h1>
          <p class="tag">Federal tech jobs by what the job actually is</p>
        </div>
      </a>
      <form class="search" (submit)="submitSearch($event, box.value, where.value)">
        <input #box type="search" [value]="query()['q'] ?? ''" placeholder="Search duties, tools, agencies (e.g. kubernetes azure)" aria-label="Search jobs" />
        <input #where class="where" type="search" [value]="query()['location'] ?? ''" placeholder="City, state (e.g. Houston, TX)" aria-label="Location" />
        <button type="submit">Search</button>
      </form>
    </header>

    <router-outlet />

    <footer class="foot"><a routerLink="/privacy">Privacy</a></footer>
  `,
})
export class App {
  private readonly router = inject(Router);

  /** Query params of the current page, so the header box shows the active search. */
  protected readonly query = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map(() => this.router.parseUrl(this.router.url).queryParams as Params),
    ),
    { initialValue: {} as Params },
  );

  /** Searching keeps the current filters on the search page; from any other page it starts fresh. */
  protected submitSearch(event: Event, q: string, location: string): void {
    event.preventDefault();
    const onSearch = this.router.parseUrl(this.router.url).root.children['primary'] === undefined;
    const base: Params = onSearch ? { ...this.query() } : {};
    delete base['sort'];
    delete base['page'];
    if (location.trim() !== (base['location'] ?? '')) delete base['includeRemote'];
    this.router.navigate(['/'], {
      queryParams: { ...base, q: q.trim() || null, location: location.trim() || null },
    });
  }
}

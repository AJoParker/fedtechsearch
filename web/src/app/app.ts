import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Params, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { ThemeService } from './theme';
import { ToastComponent } from './toast';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, ToastComponent],
  template: `
    <header class="top">
      <a class="brand" routerLink="/">
        <span class="logo" aria-hidden="true">★</span>
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
      <a class="header-link" routerLink="/stats">Stats</a>
      <a class="header-link" routerLink="/alerts">Alerts</a>
      <button type="button" class="theme-toggle" (click)="theme.toggle()"
              [attr.aria-label]="theme.theme() === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'"
              [attr.title]="theme.theme() === 'dark' ? 'Light theme' : 'Dark theme'">
        @if (theme.theme() === 'dark') {
          <svg aria-hidden="true" viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
            <circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/>
          </svg>
        } @else {
          <svg aria-hidden="true" viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round">
            <path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8Z"/>
          </svg>
        }
      </button>
    </header>

    <router-outlet />

    <footer class="foot"><a routerLink="/privacy">Privacy</a></footer>
    <app-toast />
  `,
})
export class App {
  private readonly router = inject(Router);
  protected readonly theme = inject(ThemeService);

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
    if (location.trim() !== (base['location'] ?? '')) {
      // A new place starts from the defaults (remote included, 25 mi).
      delete base['includeRemote'];
      delete base['radius'];
    }
    this.router.navigate(['/'], {
      queryParams: { ...base, q: q.trim() || null, location: location.trim() || null },
    });
  }
}

import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

export type AlertFrequency = 'Daily' | 'Weekly';

export interface SavedAlert {
  id: string;
  name: string;
  queryString: string;
  frequency: AlertFrequency;
  confirmed: boolean;
  paused: boolean;
  createdAt: string;
  lastNotifiedAt: string | null;
}

export interface AlertsView {
  email: string;
  unsubscribed: boolean;
  searches: SavedAlert[];
}

const SESSION_KEY = 'fedtech-alerts-session';

/** Email alerts. The session (from a confirm or sign-in link) lives in sessionStorage, per tab. */
@Injectable({ providedIn: 'root' })
export class AlertsService {
  private readonly http = inject(HttpClient);
  readonly session = signal<string | null>(this.read());

  subscribe(email: string, queryString: string, name: string, frequency: AlertFrequency): Observable<unknown> {
    return this.http.post('/api/alerts', { email, queryString, name, frequency });
  }

  requestLink(email: string): Observable<unknown> {
    return this.http.post('/api/alerts/manage-link', { email });
  }

  confirm(token: string): Observable<{ session: string }> {
    return this.http.post<{ session: string }>('/api/alerts/confirm', { token }).pipe(tap((r) => this.store(r.session)));
  }

  signIn(token: string): Observable<{ session: string }> {
    return this.http.post<{ session: string }>('/api/alerts/session', { token }).pipe(tap((r) => this.store(r.session)));
  }

  unsubscribe(token: string): Observable<unknown> {
    return this.http.post(`/api/alerts/unsubscribe?token=${encodeURIComponent(token)}`, null);
  }

  list(): Observable<AlertsView> {
    return this.http.get<AlertsView>('/api/alerts', { headers: this.auth() });
  }

  update(id: string, patch: { name?: string; frequency?: AlertFrequency; paused?: boolean }): Observable<SavedAlert> {
    return this.http.patch<SavedAlert>(`/api/alerts/searches/${id}`, patch, { headers: this.auth() });
  }

  remove(id: string): Observable<unknown> {
    return this.http.delete(`/api/alerts/searches/${id}`, { headers: this.auth() });
  }

  deleteEverything(): Observable<unknown> {
    return this.http.delete('/api/alerts/me', { headers: this.auth() }).pipe(tap(() => this.store(null)));
  }

  signOut(): void {
    this.store(null);
  }

  private auth(): HttpHeaders {
    return new HttpHeaders({ Authorization: `Bearer ${this.session() ?? ''}` });
  }

  private store(value: string | null): void {
    this.session.set(value);
    try {
      if (value) sessionStorage.setItem(SESSION_KEY, value);
      else sessionStorage.removeItem(SESSION_KEY);
    } catch {
      // Storage blocked: the session lasts until the page is closed.
    }
  }

  private read(): string | null {
    try {
      return sessionStorage.getItem(SESSION_KEY);
    } catch {
      return null;
    }
  }
}

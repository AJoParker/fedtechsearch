import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, map, of, shareReplay } from 'rxjs';

/** The taxonomy lists from /api/meta, loaded once. */
export interface Meta {
  roleFamilies: string[];
  tech: string[];
  seniority: string[];
  clearance: string[];
  work: string[];
}

interface MetaResponse {
  roleFamilies: { name: string }[];
  tech: { category: string; names: string[] }[];
  seniority: string[];
  clearance: string[];
  work: string[];
}

@Injectable({ providedIn: 'root' })
export class MetaService {
  private readonly http = inject(HttpClient);

  private readonly meta$ = this.http.get<MetaResponse>('/api/meta').pipe(
    map(
      (m): Meta => ({
        roleFamilies: m.roleFamilies.map((f) => f.name),
        tech: m.tech.flatMap((c) => c.names).sort((a, b) => a.localeCompare(b)),
        seniority: m.seniority,
        clearance: m.clearance,
        work: m.work,
      }),
    ),
    catchError(() => of(null)),
    shareReplay(1),
  );

  /** Null until loaded, or if the API is unreachable. */
  readonly meta = toSignal(this.meta$, { initialValue: null });
}

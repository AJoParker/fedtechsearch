import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type StatsDimension = 'tech' | 'roleFamily' | 'agency' | 'state' | 'seniority' | 'clearance' | 'work';

export interface StatCounts {
  active: number;
  new: number;
  remote: number;
  /** Telework eligible (not fully remote). */
  telework: number;
}

export interface StatValue extends StatCounts {
  value: string;
  /** Active now minus a week ago; null until a week of snapshots exists. */
  change7d: number | null;
}

export interface StatSeries {
  value: string;
  points: { date: string; active: number }[];
}

export interface StatsResponse {
  dimension: StatsDimension;
  /** Latest snapshot date (yyyy-mm-dd), or null before the first data load. */
  date: string | null;
  days: number;
  total: StatCounts;
  top: StatValue[];
  series: StatSeries[];
}

/** Daily counts behind the /stats page (GET /api/stats, cached for an hour by the API). */
@Injectable({ providedIn: 'root' })
export class StatsService {
  private readonly http = inject(HttpClient);

  get(dimension: StatsDimension, top: number, days = 90): Observable<StatsResponse> {
    return this.http.get<StatsResponse>('/api/stats', { params: { dimension, top, days } });
  }
}

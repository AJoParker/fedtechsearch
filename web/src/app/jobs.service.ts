import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { JobDetail, JobSearchResponse } from './models';

@Injectable({ providedIn: 'root' })
export class JobsService {
  private readonly http = inject(HttpClient);

  search(params: HttpParams): Observable<JobSearchResponse> {
    return this.http.get<JobSearchResponse>('/api/jobs', { params });
  }

  get(id: string): Observable<JobDetail> {
    return this.http.get<JobDetail>(`/api/jobs/${encodeURIComponent(id)}`);
  }
}

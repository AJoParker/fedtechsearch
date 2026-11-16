import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { FeedbackSummary, JobDetail, JobSearchResponse, TagFeedbackInput } from './models';

@Injectable({ providedIn: 'root' })
export class JobsService {
  private readonly http = inject(HttpClient);

  search(params: HttpParams): Observable<JobSearchResponse> {
    return this.http.get<JobSearchResponse>('/api/jobs', { params });
  }

  get(id: string): Observable<JobDetail> {
    return this.http.get<JobDetail>(`/api/jobs/${encodeURIComponent(id)}`);
  }

  /** "Wrong tag?" report. 204 on success; 400 carries { error }; 429 means rate limited. */
  feedback(id: string, body: TagFeedbackInput): Observable<void> {
    return this.http.post<void>(`/api/jobs/${encodeURIComponent(id)}/feedback`, body);
  }

  feedbackSummary(adminKey: string, days: number): Observable<FeedbackSummary> {
    return this.http.get<FeedbackSummary>('/api/admin/feedback/summary', {
      params: { days },
      headers: { 'X-Admin-Key': adminKey },
    });
  }
}

import { JobListItem } from './models';

export function grade(job: JobListItem): string {
  const range = job.highGrade && job.highGrade !== job.lowGrade ? `${job.lowGrade}–${job.highGrade}` : `${job.lowGrade}`;
  return `${job.payPlan ?? ''}-${range}`;
}

export function seniorityLabel(job: JobListItem): string {
  const s = job.seniority;
  return s.length === 1 ? s[0] : `${s[0]} → ${s[s.length - 1]}`;
}

/** Only link to real USAJOBS pages, normalized to https without the :443 the API includes. */
export function usajobsLink(raw: string | null | undefined): string | null {
  if (!raw) return null;
  try {
    const url = new URL(raw);
    if (url.hostname !== 'usajobs.gov' && !url.hostname.endsWith('.usajobs.gov')) return null;
    url.protocol = 'https:';
    url.port = '';
    return url.toString();
  } catch {
    return null;
  }
}

export interface FacetValue {
  value: string;
  count: number;
}

export interface JobListItem {
  id: string;
  workingTitle: string;
  officialTitle: string;
  roleFamily: string;
  agency: string;
  department: string;
  locationDisplay: string;
  tech: string[];
  seniority: string[];
  specialties: string[];
  workArrangement: string;
  clearance: string;
  payPlan: string | null;
  lowGrade: number | null;
  highGrade: number | null;
  salaryMin: number | null;
  salaryMax: number | null;
  openDate: string | null;
  closeDate: string | null;
  directHire: boolean;
  openToPublic: boolean;
  supervisory: boolean;
  mayCloseEarly: boolean;
  hiringPaths: string[];
  promotionPotential: number | null;
  firstSeen: string;
  url: string;
  snippet: string;
}

export interface JobSearchResponse {
  total: number;
  page: number;
  pageSize: number;
  items: JobListItem[];
  facets: Record<string, FacetValue[]>;
}

export interface JobDetail extends JobListItem {
  announcementNumber: string;
  applyUrl: string | null;
  subAgency: string | null;
  locations: string[];
  series: string[];
  hiringPaths: string[];
  summary: string;
  duties: string[];
  qualifications: string;
  workingTitleSource: string;
  roleConfidence: number;
}

export interface Option {
  value: string;
  label: string;
}

/**
 * One control in a filter section. `key` is the URL / API query parameter.
 * - pills:  multi-select from a facet (`facet` defaults to `key`); `searchable` adds a find box
 * - flag:   one boolean pill (key=true), counted from the `flags` facet
 * - choice: single-select pills from fixed options; clicking the active one clears it
 * - select: a native select with fixed options
 * - text:   free text
 */
export type FilterControl =
  | { type: 'pills'; key: string; label: string; facet?: string; searchable?: boolean; note?: string; exclude?: boolean }
  | { type: 'flag'; key: string; label: string; flag: string }
  | { type: 'choice'; key: string; label: string; options: Option[]; facet?: string; note?: string }
  | { type: 'select'; key: string; label: string; options: Option[] }
  | { type: 'text'; key: string; label: string; placeholder: string; exclude?: boolean };

export interface FilterSection {
  id: string;
  group: string;
  label: string;
  hint?: string;
  controls: FilterControl[];
}

const salaryOptions: Option[] = [60000, 80000, 100000, 120000, 140000, 160000, 180000].map((v) => ({
  value: String(v),
  label: `$${v.toLocaleString()}+`,
}));
const gradeOptions: Option[] = [5, 7, 9, 11, 12, 13, 14, 15].map((g) => ({ value: String(g), label: `GS-${g}` }));

/**
 * Every filter the search page offers. The chip row above the results, the dialog's
 * section nav and the "Search filters" box all read from this list. Add a filter here.
 */
export const FILTER_SECTIONS: FilterSection[] = [
  {
    id: 'date',
    group: 'Result filters',
    label: 'Date posted',
    controls: [
      {
        type: 'choice',
        key: 'postedWithinDays',
        label: 'Posted',
        options: [
          { value: '1', label: 'Past 24 hours' },
          { value: '3', label: 'Past 3 days' },
          { value: '7', label: 'Past week' },
          { value: '14', label: 'Past 2 weeks' },
        ],
      },
      {
        type: 'choice',
        key: 'closingWithinDays',
        label: 'Closing',
        options: [
          { value: '3', label: 'Within 3 days' },
          { value: '7', label: 'Within a week' },
        ],
      },
      { type: 'flag', key: 'mayCloseEarly', label: 'May close early', flag: 'May close early' },
    ],
  },
  {
    id: 'apply',
    group: 'Result filters',
    label: 'Who can apply',
    hint: 'Hiring paths from the announcement. Schedule A is a non-competitive path for people with disabilities.',
    controls: [
      { type: 'pills', key: 'hiringPath', label: 'Open to' },
      { type: 'flag', key: 'directHire', label: 'Direct hire', flag: 'Direct hire' },
    ],
  },
  {
    id: 'exclude',
    group: 'Result filters',
    label: 'Exclude',
    hint: 'Hide jobs you never want to see.',
    controls: [
      { type: 'text', key: 'excludeKeyword', label: 'Words', placeholder: 'e.g. help desk', exclude: true },
      { type: 'pills', key: 'excludeAgency', label: 'Agencies', facet: 'agency', searchable: true, exclude: true },
      { type: 'pills', key: 'excludeTech', label: 'Tech', facet: 'tech', searchable: true, exclude: true },
    ],
  },
  {
    id: 'role',
    group: 'Role',
    label: 'Role & specialty',
    controls: [
      { type: 'pills', key: 'roleFamily', label: 'Role' },
      { type: 'pills', key: 'specialty', label: 'Federal specialty', searchable: true },
      { type: 'pills', key: 'series', label: 'Series' },
    ],
  },
  {
    id: 'tech',
    group: 'Role',
    label: 'Tech stack',
    controls: [{ type: 'pills', key: 'tech', label: 'Technologies', searchable: true, note: 'Jobs must mention all selected' }],
  },
  {
    id: 'experience',
    group: 'Role',
    label: 'Experience',
    controls: [
      { type: 'pills', key: 'seniority', label: 'Seniority', note: 'Showing every level. Select one or more to narrow.' },
      {
        type: 'choice',
        key: 'supervisory',
        label: 'Role type',
        facet: 'roleType',
        note: 'Whether the role manages people or contributes hands-on.',
        options: [
          { value: 'false', label: 'Individual contributor' },
          { value: 'true', label: 'People manager' },
        ],
      },
      {
        type: 'choice',
        key: 'minPromotionPotential',
        label: 'Promotion potential',
        note: 'The highest grade the job can grow into without reapplying.',
        options: ['12', '13', '14', '15'].map((g) => ({ value: g, label: g === '15' ? 'GS-15' : `GS-${g}+` })),
      },
    ],
  },
  {
    id: 'pay',
    group: 'Pay',
    label: 'Salary & grade',
    controls: [
      { type: 'select', key: 'minSalary', label: 'Minimum salary', options: salaryOptions },
      { type: 'select', key: 'minGrade', label: 'Grade from', options: gradeOptions },
      { type: 'select', key: 'maxGrade', label: 'Grade to', options: gradeOptions },
    ],
  },
  {
    id: 'work',
    group: 'Work',
    label: 'Work & location',
    controls: [
      { type: 'pills', key: 'work', label: 'Work arrangement' },
      { type: 'pills', key: 'state', label: 'State', searchable: true },
    ],
  },
  {
    id: 'commitment',
    group: 'Work',
    label: 'Commitment',
    controls: [
      { type: 'pills', key: 'schedule', label: 'Schedule' },
      { type: 'pills', key: 'appointment', label: 'Appointment' },
      { type: 'pills', key: 'travel', label: 'Travel' },
    ],
  },
  {
    id: 'org',
    group: 'Organization',
    label: 'Agency & department',
    controls: [
      { type: 'pills', key: 'department', label: 'Department', searchable: true },
      { type: 'pills', key: 'agency', label: 'Agency', searchable: true },
    ],
  },
  {
    id: 'clearance',
    group: 'Organization',
    label: 'Security clearance',
    controls: [{ type: 'pills', key: 'clearance', label: 'Clearance' }],
  },
];

/** Every URL parameter the filter sections own. */
export const FILTER_KEYS: string[] = FILTER_SECTIONS.flatMap((s) => s.controls.map((c) => c.key));

/** Short wording for the "Open to" line on job cards. */
export const HIRING_PATH_SHORT: Record<string, string> = {
  'Open to the public': 'the public',
  Veterans: 'veterans',
  'Military spouses': 'military spouses',
  'Individuals with disabilities (Schedule A)': 'Schedule A',
  'Federal employees': 'federal employees',
  Students: 'students',
  'Recent graduates': 'recent graduates',
  'National Guard and Reserves': 'Guard and Reserves',
  'Native Americans': 'Native Americans',
  'Peace Corps and AmeriCorps alumni': 'Peace Corps/AmeriCorps',
};

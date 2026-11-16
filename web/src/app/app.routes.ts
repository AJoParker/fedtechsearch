import { Routes } from '@angular/router';
import { JobPage } from './pages/job/job.page';
import { PrivacyPage } from './pages/privacy/privacy.page';
import { SearchPage } from './pages/search/search.page';

// /alerts and /stats arrive with their roadmap phases (4 and 5).
export const routes: Routes = [
  { path: '', component: SearchPage, title: 'FedTech Search' },
  { path: 'jobs/:id', component: JobPage, title: 'Job · FedTech Search' },
  { path: 'privacy', component: PrivacyPage, title: 'Privacy · FedTech Search' },
  { path: '**', redirectTo: '' },
];

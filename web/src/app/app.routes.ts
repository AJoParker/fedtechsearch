import { Routes } from '@angular/router';
import { FeedbackAdminPage } from './pages/admin/feedback.page';
import { AlertConfirmPage } from './pages/alerts/alert-confirm.page';
import { AlertUnsubscribePage } from './pages/alerts/alert-unsubscribe.page';
import { AlertsPage } from './pages/alerts/alerts.page';
import { JobPage } from './pages/job/job.page';
import { PrivacyPage } from './pages/privacy/privacy.page';
import { SearchPage } from './pages/search/search.page';

// /stats arrives with roadmap phase 5.
export const routes: Routes = [
  { path: '', component: SearchPage, title: 'FedTech Search' },
  { path: 'jobs/:id', component: JobPage, title: 'Job · FedTech Search' },
  { path: 'privacy', component: PrivacyPage, title: 'Privacy · FedTech Search' },
  { path: 'alerts', component: AlertsPage, title: 'Job alerts · FedTech Search' },
  { path: 'alerts/confirm', component: AlertConfirmPage, title: 'Confirm alert · FedTech Search' },
  { path: 'alerts/unsubscribe', component: AlertUnsubscribePage, title: 'Unsubscribe · FedTech Search' },
  // Not linked from the public UI; the API rejects requests without the admin key.
  { path: 'admin/feedback', component: FeedbackAdminPage, title: 'Tag reports · FedTech Search' },
  { path: '**', redirectTo: '' },
];

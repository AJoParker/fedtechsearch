import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-privacy-page',
  imports: [RouterLink],
  template: `
    <main class="job-page prose">
      <h2>Privacy</h2>
      <p>FedTech Search shows public job announcements from USAJOBS. It doesn't ask for or store any personal information today.</p>
      <p>When email alerts launch, this page will explain what is stored, why, and how to delete it.</p>
      <p><a routerLink="/">← Back to search</a></p>
    </main>
  `,
})
export class PrivacyPage {}

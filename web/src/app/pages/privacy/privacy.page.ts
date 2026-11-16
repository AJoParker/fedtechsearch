import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-privacy-page',
  imports: [RouterLink],
  template: `
    <main class="job-page prose">
      <h2>Privacy</h2>
      <p>FedTech Search shows public job announcements from USAJOBS. It doesn't ask for or store any personal information today.</p>
      <p>When you report a wrong tag, we store the job, the tag, your suggestion and your note. To stop abuse we also keep a
        one-way hash of your network address combined with the date. It changes every day, can't be turned back into the
        address, and no IP address is stored.</p>
      <p>Posts you hide are saved only in this browser (in its local database). Their job IDs are sent along with your
        searches so those posts can be left out of the results; the server doesn't store them. Clearing this site's data
        in your browser removes them.</p>
      <p>When email alerts launch, this page will explain what is stored, why, and how to delete it.</p>
      <p><a routerLink="/">← Back to search</a></p>
    </main>
  `,
})
export class PrivacyPage {}

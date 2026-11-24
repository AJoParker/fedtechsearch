import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-privacy-page',
  imports: [RouterLink],
  template: `
    <main class="job-page prose">
      <h2>Privacy</h2>
      <p>FedTech Search shows public job announcements from USAJOBS. You can search without an account and without giving us anything.</p>

      <h3>Email alerts</h3>
      <p>If you ask for alerts, we store <strong>your email address</strong>, the searches you save (their name, filters and how often to email you), and when we last emailed you. That's the only personal information we keep, and we use it only to send the alerts you asked for.</p>
      <ul>
        <li>Nothing is sent until you confirm by clicking the link in the first email.</li>
        <li>Links in our emails carry a random code. We store only a one-way fingerprint (hash) of each code, and they expire.</li>
        <li>Every alert email has a one-click unsubscribe link.</li>
        <li>On <a routerLink="/alerts">your alerts page</a> you can pause, change or delete alerts, or use <strong>Delete all my data</strong> to remove your address and everything tied to it.</li>
        <li>We never log, sell or share your email address. Emails are delivered by our email provider (Amazon SES in production) only to send your alerts.</li>
      </ul>

      <h3>Hidden posts</h3>
      <p>Posts you hide are saved only in this browser (in its local database). Their job IDs are sent with your searches so those posts can be left out of the results; the server doesn't store them. Alerts can't see them, so a hidden post can still appear in an alert email. Clearing this site's data in your browser removes them.</p>

      <h3>Tag reports</h3>
      <p>When you report a wrong tag, we store the job, the tag, your suggestion and your note. To stop abuse we also keep a one-way hash of your network address combined with the date. It changes every day, can't be turned back into the address, and no IP address is stored.</p>

      <p><a routerLink="/">← Back to search</a></p>
    </main>
  `,
})
export class PrivacyPage {}

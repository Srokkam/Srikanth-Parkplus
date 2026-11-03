import { Injectable, signal } from '@angular/core';

/** Feeds the page's polite live region, so screen readers hear results of actions that don't move focus. */
@Injectable({ providedIn: 'root' })
export class Announcer {
  readonly message = signal('');

  announce(text: string): void {
    // Clearing first makes a repeated message be read again.
    this.message.set('');
    queueMicrotask(() => this.message.set(text));
  }
}

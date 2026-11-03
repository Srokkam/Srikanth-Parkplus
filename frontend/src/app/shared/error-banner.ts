import { Component, computed, input, output } from '@angular/core';
import { ApiError } from '../core/api/api-error';

/** One place that shows a failed action: what happened, field problems, and a retry when retrying makes sense. */
@Component({
  selector: 'app-error-banner',
  template: `
    @if (error(); as e) {
      <div class="banner banner-error" role="alert">
        <p class="banner-title">{{ e.message }}</p>
        @if (fieldMessages().length > 0) {
          <ul>
            @for (message of fieldMessages(); track message) {
              <li>{{ message }}</li>
            }
          </ul>
        }
        @if (e.retryAfterSeconds) {
          <p>Please wait {{ e.retryAfterSeconds }} second(s) before trying again.</p>
        }
        @if (e.retryable) {
          <button type="button" (click)="retry.emit()">Try again</button>
        }
      </div>
    }
  `,
})
export class ErrorBanner {
  readonly error = input<ApiError | null>(null);
  readonly retry = output<void>();

  protected readonly fieldMessages = computed(() => Object.values(this.error()?.fieldErrors ?? {}).flat());
}

import { Component, computed, input } from '@angular/core';
import { StockStatus } from '../core/models';

@Component({
  selector: 'app-stock-badge',
  template: `<span class="badge" [class]="'badge-' + status()">{{ label() }}</span>`,
})
export class StockBadge {
  readonly status = input.required<StockStatus>();
  readonly available = input<number | null>(null);

  protected readonly label = computed(() => {
    switch (this.status()) {
      case 'out-of-stock':
        return 'Out of stock';
      case 'low-stock':
        return `Only ${this.available() ?? 'a few'} left`;
      default:
        return 'In stock';
    }
  });
}

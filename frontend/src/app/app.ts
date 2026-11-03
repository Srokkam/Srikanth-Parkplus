import { Component, ElementRef, inject, viewChild } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { Announcer } from './core/state/announcer';
import { CartStore } from './core/state/cart-store';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <a class="skip-link" href="#main" (click)="focusMain($event)">Skip to content</a>

    <header class="site-header">
      <a routerLink="/products" class="brand">IPL Fan Store</a>
      <nav aria-label="Main">
        <a routerLink="/products" routerLinkActive="active">Products</a>
        <a routerLink="/cart" routerLinkActive="active">
          Cart
          @if (cart.count() > 0) {
            <span class="count" aria-hidden="true">{{ cart.count() }}</span>
            <span class="sr-only">({{ cart.count() }} items)</span>
          }
        </a>
        <a routerLink="/orders" routerLinkActive="active">Orders</a>
        <a routerLink="/admin/products/new" routerLinkActive="active">Add product</a>
      </nav>
    </header>

    <main id="main" #main tabindex="-1">
      <router-outlet />
    </main>

    <div class="sr-only" role="status" aria-live="polite">{{ announcer.message() }}</div>
  `,
})
export class App {
  protected readonly cart = inject(CartStore);
  protected readonly announcer = inject(Announcer);
  private readonly main = viewChild.required<ElementRef<HTMLElement>>('main');

  constructor() {
    // The cart badge should be right on first paint, and focus should move to the new page after navigation.
    void this.cart.load();

    inject(Router)
      .events.pipe(filter((e) => e instanceof NavigationEnd))
      .subscribe(() => this.main().nativeElement.focus({ preventScroll: false }));
  }

  protected focusMain(event: Event): void {
    event.preventDefault();
    this.main().nativeElement.focus();
  }
}

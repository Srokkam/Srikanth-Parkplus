import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'products' },
  {
    path: 'products',
    title: 'Products · IPL Fan Store',
    loadComponent: () => import('./features/products/product-list').then((m) => m.ProductList),
  },
  {
    path: 'products/:franchiseId/:productId',
    title: 'Product · IPL Fan Store',
    loadComponent: () => import('./features/products/product-detail').then((m) => m.ProductDetail),
  },
  {
    path: 'cart',
    title: 'Cart · IPL Fan Store',
    loadComponent: () => import('./features/cart/cart-page').then((m) => m.CartPage),
  },
  {
    path: 'checkout',
    title: 'Checkout · IPL Fan Store',
    loadComponent: () => import('./features/cart/checkout-page').then((m) => m.CheckoutPage),
  },
  {
    path: 'orders',
    title: 'Orders · IPL Fan Store',
    loadComponent: () => import('./features/orders/orders-page').then((m) => m.OrdersPage),
  },
  {
    path: 'orders/:orderId',
    title: 'Order · IPL Fan Store',
    loadComponent: () => import('./features/orders/order-detail').then((m) => m.OrderDetail),
  },
  {
    path: 'admin/products/new',
    title: 'Add product · IPL Fan Store',
    loadComponent: () => import('./features/admin/product-form').then((m) => m.ProductForm),
  },
  {
    path: 'admin/products/:franchiseId/:productId',
    title: 'Manage product · IPL Fan Store',
    loadComponent: () => import('./features/admin/admin-product').then((m) => m.AdminProductPage),
  },
  { path: '**', redirectTo: 'products' },
];

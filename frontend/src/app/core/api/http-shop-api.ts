import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { AdminProduct, AuditEntry, AuditField, Cart, Franchise, NewProduct, Order, OrderQuery, Page, PageQuery, PlacedOrder, Product, ProductQuery } from '../models';
import { APP_CONFIG } from './app-config';
import { ShopApi } from './shop-api';

/** Talks to the ASP.NET Core API under /api/v1. Errors are turned into ApiError by the problem interceptor. */
@Injectable()
export class HttpShopApi extends ShopApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(APP_CONFIG).apiBaseUrl}/v1`;

  listFranchises(): Observable<Franchise[]> {
    return this.http.get<Franchise[]>(`${this.base}/franchises`);
  }

  searchProducts(query: ProductQuery): Observable<Page<Product>> {
    let params = new HttpParams();
    if (query.text) params = params.set('q', query.text);
    if (query.type) params = params.set('type', query.type);
    if (query.franchise) params = params.set('franchise', query.franchise);
    if (query.pageSize) params = params.set('pageSize', query.pageSize);
    if (query.continuationToken) params = params.set('continuationToken', query.continuationToken);
    return this.http.get<Page<Product>>(`${this.base}/products`, { params });
  }

  getProduct(franchiseId: string, productId: string): Observable<Product> {
    return this.http.get<Product>(`${this.base}/products/${enc(franchiseId)}/${enc(productId)}`);
  }

  getCart(): Observable<Cart> {
    return this.http.get<Cart>(`${this.base}/cart`);
  }

  addToCart(key: string, franchiseId: string, productId: string, quantity: number): Observable<Cart> {
    return this.http.post<Cart>(`${this.base}/cart/items`, { franchiseId, productId, quantity }, withKey(key));
  }

  setQuantity(key: string, productId: string, quantity: number): Observable<Cart> {
    return this.http.put<Cart>(`${this.base}/cart/items/${enc(productId)}`, { quantity }, withKey(key));
  }

  removeFromCart(key: string, productId: string): Observable<Cart> {
    return this.http.delete<Cart>(`${this.base}/cart/items/${enc(productId)}`, withKey(key));
  }

  acceptChanges(key: string, expectedTotalPaise: number): Observable<Cart> {
    return this.http.post<Cart>(`${this.base}/cart/accept-changes`, { expectedTotalPaise }, withKey(key));
  }

  placeOrder(orderId: string, acceptedTotalPaise: number): Observable<PlacedOrder> {
    return this.http
      .put<Order>(`${this.base}/orders/${enc(orderId)}`, { acceptedTotalPaise }, { observe: 'response' })
      .pipe(map((response: HttpResponse<Order>) => ({ order: response.body as Order, created: response.status === 201 })));
  }

  listOrders(query: OrderQuery): Observable<Page<Order>> {
    let params = new HttpParams();
    if (query.from) params = params.set('from', query.from);
    if (query.to) params = params.set('to', query.to);
    if (query.pageSize) params = params.set('pageSize', query.pageSize);
    if (query.continuationToken) params = params.set('continuationToken', query.continuationToken);
    return this.http.get<Page<Order>>(`${this.base}/orders`, { params });
  }

  getOrder(orderId: string): Observable<Order> {
    return this.http.get<Order>(`${this.base}/orders/${enc(orderId)}`);
  }

  createProduct(product: NewProduct): Observable<Product> {
    return this.http.post<Product>(`${this.base}/admin/products`, product);
  }

  getAdminProduct(franchiseId: string, productId: string): Observable<AdminProduct> {
    return this.http.get<AdminProduct>(this.adminUrl(franchiseId, productId));
  }

  changePrice(franchiseId: string, productId: string, pricePaise: number, reason: string, eTag: string): Observable<void> {
    return this.http
      .put(`${this.adminUrl(franchiseId, productId)}/price`, { pricePaise, reason }, { headers: { 'If-Match': eTag } })
      .pipe(map(() => undefined));
  }

  setStock(franchiseId: string, productId: string, available: number, reason: string, eTag: string): Observable<void> {
    return this.http
      .put(`${this.adminUrl(franchiseId, productId)}/stock`, { available, reason }, { headers: { 'If-Match': eTag } })
      .pipe(map(() => undefined));
  }

  listAudit(franchiseId: string, productId: string, field: AuditField, query: PageQuery): Observable<Page<AuditEntry>> {
    let params = new HttpParams().set('field', field);
    if (query.pageSize) params = params.set('pageSize', query.pageSize);
    if (query.continuationToken) params = params.set('continuationToken', query.continuationToken);
    return this.http.get<Page<AuditEntry>>(`${this.adminUrl(franchiseId, productId)}/audit`, { params });
  }

  private adminUrl(franchiseId: string, productId: string): string {
    return `${this.base}/admin/products/${enc(franchiseId)}/${enc(productId)}`;
  }
}

const enc = encodeURIComponent;

function withKey(key: string) {
  return { headers: { 'Idempotency-Key': key } };
}

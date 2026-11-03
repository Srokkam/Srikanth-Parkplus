import { Observable } from 'rxjs';
import { AdminProduct, AuditEntry, AuditField, Cart, Franchise, NewProduct, Order, OrderQuery, Page, PageQuery, PlacedOrder, Product, ProductQuery } from '../models';

/**
 * The only thing the UI knows about the backend (implemented over HTTP by HttpShopApi).
 * Every cart change takes an idempotency key and every order an order id, both created by the caller.
 */
export abstract class ShopApi {
  abstract listFranchises(): Observable<Franchise[]>;
  abstract searchProducts(query: ProductQuery): Observable<Page<Product>>;
  abstract getProduct(franchiseId: string, productId: string): Observable<Product>;

  abstract getCart(): Observable<Cart>;
  abstract addToCart(key: string, franchiseId: string, productId: string, quantity: number): Observable<Cart>;
  abstract setQuantity(key: string, productId: string, quantity: number): Observable<Cart>;
  abstract removeFromCart(key: string, productId: string): Observable<Cart>;
  abstract acceptChanges(key: string, expectedTotalPaise: number): Observable<Cart>;

  abstract placeOrder(orderId: string, acceptedTotalPaise: number): Observable<PlacedOrder>;
  abstract listOrders(query: OrderQuery): Observable<Page<Order>>;
  abstract getOrder(orderId: string): Observable<Order>;

  /** Admin only. The id is chosen by the caller, so repeating the call answers 409 product-exists instead of duplicating. */
  abstract createProduct(product: NewProduct): Observable<Product>;

  /** Admin only: current price and stock, each with the version (ETag) an edit must send back. */
  abstract getAdminProduct(franchiseId: string, productId: string): Observable<AdminProduct>;

  /** Admin only. Fails with 412 when someone changed the price since eTag was read; nothing is overwritten. */
  abstract changePrice(franchiseId: string, productId: string, pricePaise: number, reason: string, eTag: string): Observable<void>;

  /** Admin only. Fails with 412 when someone changed the stock since eTag was read; nothing is overwritten. */
  abstract setStock(franchiseId: string, productId: string, available: number, reason: string, eTag: string): Observable<void>;

  /** Admin only: the audit trail for one field, newest first. */
  abstract listAudit(franchiseId: string, productId: string, field: AuditField, query: PageQuery): Observable<Page<AuditEntry>>;
}

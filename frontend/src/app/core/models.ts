export type StockStatus = 'in-stock' | 'low-stock' | 'out-of-stock';
export type LineStock = 'ok' | 'insufficient' | 'unavailable';

export const PRODUCT_TYPES = ['jersey', 'cap', 'flag', 'signed-photo', 'other'] as const;
export type ProductType = (typeof PRODUCT_TYPES)[number];

/** A franchise is a code used by products in the catalogue, e.g. "csk". The API derives the list from the products. */
export interface Franchise {
  code: string;
}

/** Money is always integer paise, exactly as the API sends it. */
export interface Product {
  id: string;
  franchiseId: string;
  name: string;
  type: ProductType;
  description: string;
  pricePaise: number;
  limitedEdition: boolean;
  maxPerCustomer: number | null;
  available: number;
  stockStatus: StockStatus;
}

/** What an admin sends to create a product. Money is integer paise; maxPerCustomer only for limited editions. */
export interface NewProduct {
  franchiseId: string;
  id: string;
  type: ProductType;
  name: string;
  description: string;
  pricePaise: number;
  limitedEdition: boolean;
  maxPerCustomer: number | null;
  initialStock: number;
}

export interface Page<T> {
  items: T[];
  continuationToken: string | null;
  totalCount: number | null;
}

export interface ProductQuery {
  text?: string;
  type?: string;
  franchise?: string;
  pageSize?: number;
  continuationToken?: string | null;
}

export interface PageQuery {
  pageSize?: number;
  continuationToken?: string | null;
}

/** from is inclusive and to exclusive, both UTC ISO instants. */
export interface OrderQuery extends PageQuery {
  from?: string;
  to?: string;
}

export interface CartLine {
  productId: string;
  franchiseId: string;
  name: string;
  quantity: number;
  acceptedUnitPricePaise: number;
  currentUnitPricePaise: number;
  lineTotalPaise: number;
  priceChanged: boolean;
  stock: LineStock;
  available: number;
}

export interface Cart {
  lines: CartLine[];
  subtotalPaise: number;
  acceptedSubtotalPaise: number;
  shippingPaise: number;
  totalPaise: number;
  requiresAcceptance: boolean;
  hasBlockingIssues: boolean;
}

export interface OrderLine {
  productId: string;
  franchiseId: string;
  name: string;
  unitPricePaise: number;
  quantity: number;
  lineTotalPaise: number;
}

export interface Order {
  id: string;
  userId: string;
  createdUtc: string;
  lines: OrderLine[];
  shippingPaise: number;
  subtotalPaise: number;
  totalPaise: number;
  /** Added to v1 later, so optional: an older API may not send it, and the UI then sums the lines. */
  itemCount?: number;
}

/** A product as an admin edits it: each half carries its own version for If-Match. */
export interface AdminProduct {
  id: string;
  franchiseId: string;
  name: string;
  pricePaise: number;
  available: number;
  priceETag: string;
  stockETag: string;
}

export type AuditField = 'price' | 'stock';

/** One recorded change. For price the values are paise; for stock they are units. */
export interface AuditEntry {
  id: string;
  field: AuditField;
  oldValue: number | null;
  newValue: number | null;
  delta: number | null;
  reason: string;
  actor: string;
  tsUtc: string;
}

export interface PlacedOrder {
  order: Order;
  /** false when the same order id was sent before and the original order came back. */
  created: boolean;
}

export interface FailedLine {
  productId: string;
  requested: number;
  available: number;
  reason: 'out-of-stock' | 'insufficient-stock' | 'product-unavailable' | 'limit-exceeded';
}

export const EMPTY_CART: Cart = {
  lines: [],
  subtotalPaise: 0,
  acceptedSubtotalPaise: 0,
  shippingPaise: 0,
  totalPaise: 0,
  requiresAcceptance: false,
  hasBlockingIssues: false,
};

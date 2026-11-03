# IPL Fan Store (Angular front end)

Angular 21, standalone components, signals, no UI library.

## Run

```bash
npm install
npm start          # http://localhost:4200
npm test           # Vitest, 35 tests
npm run build
```

## In-memory API now, real backend later

Every screen talks to one abstract class, `ShopApi` (`src/app/core/api/shop-api.ts`).

| Implementation | When |
| --- | --- |
| `MockShopApi` | default. Same limits, status codes and error payloads as the backend, all in memory |
| `HttpShopApi` | calls `/api/v1/...`; `ng serve` proxies `/api` to `http://localhost:5220` (`proxy.conf.json`) |

To switch, set `useMockApi: false` in `src/app/app.config.ts`. While the mock is on, a "Demo controls" panel at the
bottom of the page raises a price, sells out stock, fails the next request, or loses the next response, so the
conflict / price-changed / retry screens can be seen without a backend.

`http-shop-api.spec.ts` pins the contract (URLs, headers, problem+json mapping) and `mock-shop-api.spec.ts` pins the
rules. Change the backend contract in both or neither.

## Behaviour worth knowing

- **Idempotency:** the browser creates one `Idempotency-Key` per user intent (cart) and one order id per checkout attempt.
  They are kept across failed attempts that might have been applied (network error, 429, 5xx) and dropped after a final
  answer. The checkout order id also survives a page reload (sessionStorage).
- **Double submit:** a second click while a request is in flight is ignored, and the buttons are disabled.
- **Errors:** every failure is an `ApiError` (status, code, message, field errors, extension members such as
  `failedLines`), shown by one `<app-error-banner>` with `role="alert"` and a retry when retrying makes sense.
- **Money:** integer paise end to end, formatted only by `formatPaise` (`₹1,999.00`).
- **Accessibility:** labelled controls, skip link, focus moves to the page on navigation, polite live region for
  results, table headers with scopes, visible focus.

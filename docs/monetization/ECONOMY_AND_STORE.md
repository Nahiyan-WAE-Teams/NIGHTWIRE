# NIGHTWIRE monetization and player-first economy

Status: product and integration baseline. Store products, server receipt verification, ad-network credentials, and live purchase flows are not configured yet.

## Player experience rules

1. The main mystery, required conversations, evidence, calls, and episode completion remain playable without payment.
2. Paid products unlock optional extras; they never sell a required clue or force a player to pay to continue the central story.
3. Every paid item has a clear price and exact contents before purchase. No randomized paid loot boxes.
4. A rewarded ad is always optional, clearly labelled, and only grants the reward after the ad provider confirms completion. No forced interstitials during messages, calls, cutscenes, or puzzles.
5. Puzzle retries and basic hints are available without payment. Optional ad rewards may grant an extra hint, never a mandatory solution.
6. Purchases and earned currency are separate balances. A network failure must not silently consume currency or grant a purchase.
7. Restore purchases and pending-purchase handling are required before production.

## Initial product catalogue

Use store-managed product IDs; do not hardcode local currency prices because Google Play supplies localized price strings.

| Product ID | Type | Benefit |
|---|---|---|
| `nightwire_episode_001_pass` | Non-consumable | Optional bonus media, alternate dialogue, and cosmetic extras for Episode 001 |
| `nightwire_signal_bundle_small` | Consumable | Optional Signal Credits |
| `nightwire_signal_bundle_medium` | Consumable | Optional Signal Credits |
| `nightwire_signal_bundle_large` | Consumable | Optional Signal Credits |

Episode access itself remains free in this first release. Do not publish the Episode Pass until its optional contents are authored, the store listing is configured, and purchase verification is working.

## Earnable currency

Signal Credits can be earned from first-time achievements, episode completion, optional daily check-ins, and approved rewarded-ad completion. Keep rewards modest and predictable. Avoid daily streak penalties and avoid making a player log in every day to finish the story.

Use an append-only economy ledger on the trusted service for production. The client UI may display a cached balance, but must not be the authority for purchased credits or entitlement grants.

## Technical integration

- Unity In-App Purchasing package: `com.unity.purchasing@5.5.0`, the released package listed for Unity 6000.3.
- `NightwireIapService` uses the IAP 5.5 `PurchasingController`, a local `CatalogProvider`, and the four declared NIGHTWIRE product IDs. It fetches store metadata and localized prices; it does not hardcode regional price strings.
- Purchase initiation is gated on an injected `INightwirePurchaseFulfillment` implementation. The provider must verify the pending order with a trusted backend, apply the entitlement/credit ledger idempotently, and return success only after durable fulfillment. The client confirms the store order only after that success. Without the provider, purchase buttons must remain unavailable and no local grant is permitted.
- Configure products in Unity IAP / store dashboards and use the localized store price returned by the store.
- Verify purchase receipts server-side before granting entitlements or consumable credits. Process transaction IDs idempotently.
- Acknowledge/consume purchases only after verification and successful entitlement processing, following the store's current requirements.
- Support pending, cancelled, failed, restored, refunded, and duplicate transaction states.
- Rewarded ads must use a provider adapter with server-side or signed completion validation; do not trust a client callback alone for valuable currency.
- Do not add location permissions or collect precise location for ad targeting. Disable unnecessary ad SDK data collection.

## Release gate

The Unity package dependency is only the SDK baseline. A live monetization launch is blocked until all of the following exist and are tested:

- Google Play product records and localized prices.
- Verified package name and signed application build.
- Store receipt verification endpoint and secure credentials.
- Idempotent entitlement/credit ledger and refund/revocation handling.
- Restore purchases and pending transaction tests, including backend entitlement reconciliation for previously confirmed orders.
- Rewarded-ad provider, consent/privacy configuration, and completion verification (if ads are enabled).
- Internal Play testing with successful, cancelled, pending, refunded, and offline purchase scenarios.

Do not show fake purchase success or grant premium access from a local button click.

# Saturday Pulse — Monetization & Paywall Implementation Plan

## Feature Tier Split

### Free Tier — Core Engagement Loop

These features keep users coming back and build the audience you monetize. No friction on any of these.

- **Scores & Schedule** — The fundamental hook. Game results, live scores, upcoming matchups.
- **Live Rankings** — Power ratings and conference standings. The credibility layer that sells premium.
- **Team Pages** — Team details, history, schedule, record.
- **Following / Favorites** — Star teams and games. Personalization drives retention; keep it free.
- **Conference Navigation** — Week/conference filter, default settings. UX scaffolding, not a revenue surface.
- **Rivalries** — Engaging casual content, no gate.

### Premium Tier — Analytical Depth (~$2.99–4.99/mo or seasonal pass)

- **Projected Scores & Margins** — Flagship premium feature. The `( )` parenthetical placeholder already built in UI is the ideal tease.
- **Over/Under Projections** — Natural companion to projected margins; bundle together.
- **Vegas Odds Comparison** — Model spread vs. Vegas line. `Lines` table already populated. Strongest single premium selling point.
- **Season Arc Charts** — Syncfusion dual-axis chart with Seed/Trend/Pedigree tiers. Sophisticated enough to justify the unlock.
- **Seed / Trend / Pedigree Breakdown** — Composite rating is free; component breakdown is premium.
- **Historical Projections** — 61-year backfill (1965–2025) is a unique differentiator. "What would the model have projected for the 2006 BCS title game?"
- **CFP Playoff Bracket Projections** — High-engagement postseason window. Projected outcomes per round with model confidence.

### Borderline / Consider Later

- **Poll vs. Model Divergence** — AP/Coaches Poll vs. power rating delta. Could serve as a free hook or premium feature.
- **Expanded Game Panel** — Free tier shows actual scores; premium reveals projections inline. Clean, fair gate.

---

## Implementation Plan

### Phase 1 — Auth & Identity Foundation
*Pre-requisite for all gating. Nothing else ships until this is done.*

#### 1.1 ASP.NET Core Identity + JWT Bearer
- Add `Microsoft.AspNetCore.Identity.EntityFrameworkCore` to the API project
- Extend `DbContext` with `IdentityDbContext<ApplicationUser>`
- Add `ApplicationUser` with a `IsPremium` property or use role-based claims
- Configure JWT Bearer authentication middleware
- Add `/auth/register` and `/auth/login` endpoints returning signed JWT
- Define `"Premium"` role; assign on successful subscription validation (Phase 3)

#### 1.2 Token Storage in MAUI
- Use `SecureStorage.SetAsync` / `GetAsync` for JWT and refresh token
- Create an `AuthService` that attaches `Authorization: Bearer {token}` to all API calls via `HttpClient` handler
- Handle 401 responses — prompt re-login or attempt silent refresh

#### 1.3 Role-Based Feature Flags
- Define a `UserContext` service on the client that exposes `bool IsPremium`
- Parse the JWT claim on login and cache the result
- Use this throughout the ViewModels to conditionally show/hide premium surfaces
- **Important:** gate at the API level too — never rely solely on UI gating

---

### Phase 2 — Paywall UI & Soft Gates
*Can be built in parallel with Phase 1 client work. API gating requires Phase 1 complete.*

#### 2.1 Projection Tease in Scores Panel (Expanded Game Panel)
- When `IsPremium == false`, render projected values as `( ? )` or a blurred/locked indicator
- Tapping the locked value navigates to the upgrade screen (2.2)
- The `( )` placeholder format already in the UI is the right pattern — users see the slot, want the number

#### 2.2 Upgrade / Paywall Screen
- Reusable modal or dedicated `PremiumUpgradePage`
- List premium features with brief descriptions
- Display price and billing cadence
- Primary CTA: "Unlock Premium" → triggers IAP flow (Phase 3)
- Secondary: "Maybe later" dismiss
- Accessible from any locked surface and from Settings

#### 2.3 API Endpoint Gating
- Decorate projection endpoints with `[Authorize(Roles = "Premium")]`
- Return `403 Forbidden` for free users — do not return empty or zeroed data
- Free users should never be able to retrieve premium data by inspecting API responses
- Endpoints to gate: projected scores/margins, O/U, Vegas comparison, component ratings, historical projections

---

### Phase 3 — In-App Purchase Integration
*Longest lead time. Apple review can take 1–2 weeks. Start early.*

#### 3.1 Plugin.InAppBilling Setup (MAUI)
- Add `Plugin.InAppBilling` NuGet package
- Define subscription product IDs:
  - Monthly: `com.yourapp.saturdaypulse.premium.monthly`
  - Seasonal pass (optional): `com.yourapp.saturdaypulse.premium.season`
- Configure products in App Store Connect (iOS) and Google Play Console (Android)
- Implement `PurchaseService` wrapping `InAppBilling.Current.PurchaseAsync`

#### 3.2 Server-Side Receipt Validation
- **Never trust the client** — always validate receipts on the backend before granting Premium
- iOS: call Apple's `/verifyReceipt` endpoint (or StoreKit 2 transaction API)
- Android: call Google Play Developer API `purchases.subscriptions.get`
- On successful validation: assign `"Premium"` role to the user in Identity, issue new JWT with updated claims
- Store subscription metadata: `SubscriptionId`, `ExpiresAt`, `Platform` on `ApplicationUser`

#### 3.3 Renewal & Cancellation Webhooks
- Register webhook endpoint: `/webhooks/apple` and `/webhooks/google`
- Apple: handle `SUBSCRIBED`, `DID_RENEW`, `EXPIRED`, `GRACE_PERIOD_STARTED` notification types
- Google: handle `SUBSCRIPTION_RENEWED`, `SUBSCRIPTION_CANCELED`, `SUBSCRIPTION_EXPIRED`
- On expiry/cancellation: remove `"Premium"` role, issue updated JWT on next login
- On renewal: extend `ExpiresAt`, no action needed if role already assigned

#### 3.4 Seasonal Pass Product (Optional)
- One-time non-consumable IAP covering bowl + playoff window
- Lower price point (~$0.99–1.99) vs. full subscription
- Expires at a fixed date (e.g. January 20 post-CFP championship)
- Same server-side validation flow, different expiry logic

---

### Phase 4 — Premium Feature Rollout
*Data infrastructure is largely in place. Mostly UI surface work.*

#### 4.1 Vegas Odds Comparison UI
- `Lines` table already populated via CFBD API
- Surface model projected spread vs. Vegas line in the expanded game panel
- Show delta: model vs. Vegas, highlight divergences above a threshold
- Premium-only; free users see the label but not the values

#### 4.2 Seed / Trend / Pedigree Component Breakdown
- `SeedRating`, `TrendRating`, `PedigreeRating` columns exist on `TeamRecords`
- Add breakdown section to team or rankings detail view
- Composite rating visible to all; the three components are premium
- Consider a small bar chart showing relative contribution of each tier

#### 4.3 Historical Projection Explorer
- Allow premium users to select any year (1965–2025) and browse projected outcomes
- Reuses existing backfilled `Projections` table data
- Filter by team, conference, or week
- Surface on a dedicated tab or as a filter mode on the existing Projections page

#### 4.4 CFP Playoff Bracket Projections
- Postseason `PlayoffDays` collection already wired in `ProjectionsViewModel`
- Add bracket-style layout for playoff rounds with projected scores and model confidence
- Update as rounds are played; re-project remaining matchups
- High engagement window — consider a "share bracket" feature as a free viral hook

---

## Key Decisions to Make Early

**Anonymous free users vs. required signup**
Requiring an account even for free users simplifies role management and gives you an email list, but adds top-of-funnel friction. Recommended: allow anonymous browsing with a prompt to create an account when a user tries to follow a team or access any premium surface.

**Pricing structure**
- Monthly subscription: $2.99–$4.99/mo
- Seasonal pass: $0.99–$1.99 one-time (bowl + playoff window only)
- Consider a free trial period (7 days) to reduce subscription hesitation

**Grace period handling**
Define behavior when a subscription lapses: immediate downgrade vs. a 3–7 day grace period. Apple and Google both have grace period concepts built into their billing systems.

---

## Architecture Notes

- All premium enforcement must live at the **API layer**. Client-side gating is UX only.
- The existing `Unit of Work / Repository` pattern means a `SubscriptionRepository` fits naturally.
- `ProjectionCacheService` (Scoped) is already correct for per-request premium checks.
- Azure App Service supports environment-variable injection for Apple/Google webhook secrets — do not hardcode in `appsettings.json`.


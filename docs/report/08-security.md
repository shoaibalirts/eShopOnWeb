> **DRAFT.** AI-drafted from my code on 18/11-2026. I review and edit it before I submit.

# Security

## What I protect

Inventory has one dangerous endpoint: `PUT /api/inventory/{itemId}/stock`, where an administrator sets the stock level. Anyone who could call it could set all stock to 0 and stop all sales. Reading stock (`GET`) is public information on the product page, so it needs no token.

I check security **at the gateway**, not in Inventory. Inventory has no host port in Docker, so the only way in is through the gateway. This keeps Inventory simple: it never has to deal with tokens.

## Reusing the monolith's token

I don't have a new identity service. The monolith already issues JSON Web Tokens in `IdentityTokenClaimService` (Infrastructure). It signs them with HMAC-SHA256 and the key `AuthorizationConstants.JWT_SECRET_KEY`, and puts the user name and roles in them. The gateway validates the same tokens with the same key (`Jwt:SigningKey`), in `src/Gateway/Program.cs`:

- `ValidateIssuerSigningKey = true`: the signature must match the key. A token signed with another key gets 401.
- `ValidateLifetime = true`: an expired token gets 401.
- `ValidateIssuer = false` and `ValidateAudience = false`: the monolith's tokens have no issuer or audience, so I can't check them. This is a weakness, see below.
- `RoleClaimType = "role"` and `MapInboundClaims = false`.

The last setting cost me time. By default, the JWT handler renames the `role` claim to a long Microsoft URI. My policy then looked for `role` and found nothing, so even the admin got 403. With `MapInboundClaims = false` the claims keep their names from the token, and the admin got 200.

The route `inventory-restock` uses the policy `admin`, which needs an authenticated user with the role `Administrators`. YARP runs this check before it forwards anything.

## Tests

`tests/Gateway.Tests/RestockAuthorization.cs` starts the real gateway with `WebApplicationFactory` and points the Inventory cluster at a closed port. A request the gateway lets through therefore gets 502 (Inventory unreachable), and a request it stops gets 401 or 403. So the status code shows exactly what the gateway decided.

| Test | Result |
|---|---|
| `RejectsRequestWithoutToken` | 401 |
| `RejectsTokenSignedWithAnotherKey` | 401 |
| `RejectsExpiredToken` | 401 |
| `ForbidsUserWithoutAdministratorsRole` | 403 |
| `ForwardsAdministratorToInventory` | 502, so it was let through |
| `ReadingStockNeedsNoToken` | 502, so it was let through |

I also tested it live in Docker. No token gave 401, `demouser` gave 403, `admin` gave 200, a negative quantity gave 400 from Inventory's own check, and `POST` gave 405.

## Rate limiting

A client that floods the inventory routes could overload Inventory and its database. `AddRateLimiter` in the gateway uses a sliding window per client IP: 100 requests per 10 seconds (`RateLimiting` in appsettings), counted in 5 segments of 2 seconds. Request 101 gets `429 Too Many Requests` with a `Retry-After: 10` header, before the request reaches Inventory. Only the inventory routes have the policy, so browsing the shop is not limited.

`UseRateLimiter()` comes before `UseAuthentication()`, so a flood is stopped before the gateway spends time checking signatures.

The tests in `InventoryRateLimit.cs` use a limit of 3 to keep them fast. The 4th call gets 429 with `Retry-After`, and the shop pages are not limited. Live in Docker, 110 calls gave 100 × 200 and 10 × 429.

I first used a fixed window. During Checkpoint 2 on 18/11, 110 quick calls in a row all got 200. A fixed window resets its counter on a timer, so a burst that crosses a reset is split over two windows, and up to twice the limit gets through. A sliding window looks at the last 10 seconds at any moment, so this gap is gone. After the change, three runs of 110 calls in a row each gave 100 × 200 and 10 × 429.

## Weaknesses

- **The signing key is shared and weak.** The monolith's key is in its source code, and the gateway needs the same key. Anyone with the key can make admin tokens. In production the key would come from a secret store, or better, the monolith would sign with a private key (RS256) and the gateway would only have the public key. In Docker I can at least override it with `JWT_SIGNING_KEY`.
- **No issuer or audience.** A token made for another app with the same key would be accepted.
- **HTTP inside Docker.** The traffic between the containers is not encrypted. That is fine on one Docker network, but not across machines.
- **Rate limit per IP.** Many users behind one IP share one limit, and an attacker with many IPs gets many limits. All of the monolith's own calls to Inventory also come from one IP.
- **No check inside Inventory.** If someone got onto the Docker network, they could call Inventory directly without a token. Defence in depth would also validate the token in Inventory.

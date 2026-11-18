> **DRAFT.** AI-drafted from my code on 18/11-2026. I review and edit it before I submit.

# Strangler implementation and gateway

## The idea

The Strangler Pattern replaces a system step by step, while the old system keeps running. A facade sits in front of it. At first everything goes to the old system. Then one route at a time is sent to new code, until the old system has nothing left to do. My facade is a YARP reverse proxy in `src/Gateway`. My first "strangled" piece is stock: `/api/inventory/*` goes to the new Inventory service, and everything else still goes to the monolith.

## The routes

The routes are in `src/Gateway/appsettings.json`, not in code. Adding the next service later only means adding a route and a cluster.

| Route | Match | Goes to | Extra |
|---|---|---|---|
| `inventory-restock` | `PUT /api/inventory/{itemId}/stock` | Inventory | JWT, role `Administrators`, rate limit |
| `inventory` | `GET /api/inventory/{**rest}` (Order 100) | Inventory | rate limit |
| `monolith` | `{**catch-all}` (Order 1000) | Monolith | keeps the original `Host` header |

YARP picks the most specific match, and `Order` decides ties: a lower number wins. The catch-all has `Order` 1000, so it only gets what no other route takes. That is the whole Strangler idea in one line of config. When I move the next piece out of the monolith, I add a route with a lower number.

The `inventory` route only allows `GET`. Without that, a `POST` or `DELETE` on `/api/inventory/...` would skip the restock route (which needs a token) and reach Inventory unchecked. Now it falls through to the monolith, which has no such page, so the client gets 405 or 404. I tested this: `POST` returned 405.

The monolith route uses the `RequestHeaderOriginalHost` transform. Without it, the monolith would see `eshopwebmvc:8080` as the host. Sign-in cookies and redirects after login then point at the wrong address, so login through the gateway would break.

## One entry point

In `docker-compose.override.yml` only the gateway has a host port (5000). The monolith and Inventory have none, so a browser can't skip the gateway. The shop, sign-in and checkout all work through `http://localhost:5000`. The monolith also calls Inventory through the gateway (`Inventory__BaseUrl=http://gateway:8080/`), so the rate limit and the load balancing apply to it too.

## Two replicas

In Docker, the `inventory` cluster has two destinations, `inventory-api` and `inventory-api-2` (`appsettings.Docker.json`). `LoadBalancingPolicy: RoundRobin` sends every second call to each. Each replica adds an `X-Served-By` header with its host name, so I can see this. Six GETs were answered by inventory-2, inventory-1, inventory-2, inventory-1, and so on.

The cluster also has an active health check: every 5 seconds the gateway calls `/health` on each replica. When I stopped replica 2, the first few calls got 502 (the gateway hadn't noticed yet). After one health check round, all calls went to replica 1 with 200. When I started replica 2 again, it was used again automatically.

Both replicas also consume the same queue `inventory.order-created`. That is the Competing Consumers pattern: RabbitMQ gives each message to only one of them. When I published six orders straight into the queue, both replicas handled some. Because the reservation is idempotent and protected by `rowversion`, two replicas can't reserve the same stock twice.

`inventory-api-2` waits until `inventory-api` is healthy before it starts. Both run `MigrateAsync` at startup, and two migrations at the same time on an empty database could clash.

## Trade-offs

- **The gateway is a single point of failure.** If it is down, the whole shop is down. In production I would run two gateways behind a cloud load balancer.
- **An extra network hop.** Every request now goes through one more process. For a shop this is a few milliseconds.
- **The monolith calls Inventory through the gateway.** This keeps one place for limits and load balancing, but it also means internal calls count towards the per-IP rate limit. All of the monolith's calls come from one IP.
- **Routes in config.** Changing a route is easy, but a typo in JSON is only found at runtime. My gateway tests start the real gateway with the real config, which catches some of this.

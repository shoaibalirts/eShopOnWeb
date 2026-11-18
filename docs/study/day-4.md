# Day 4 – Saturday's plan, done on Wednesday 18/11: CI/CD, README, diagrams, Checkpoint 2

## What was built

| Commit | What |
|---|---|
| `ae6ec9a` | `.github/workflows/ci-cd.yml` (test → images to GHCR → deploy + smoke test), `scripts/smoke-test.sh`, the monolith's `/health` fixed, `/health/monolith` and `/health/inventory` in the gateway, Inventory Swagger at `/api/inventory/swagger`, `IMAGE_TAG` in compose |
| `fd5f34a` | New `README.md` |
| `ff962a8` | Final diagrams, with Mermaid sources in `architecture/diagrams/` |
| `93180ae` | Rate limit changed from a fixed to a sliding window (found in Checkpoint 2) |
| `b62c6cd` | 7 `WebApplicationFactory` tests of the Inventory API |
| `04f5220` | `docs/checkpoint-2.md` |

The pipeline:

```
push ─► build-and-test ─► images ─────────────────► deploy (main only)
        restore, build      build 3 images            pull sha-<commit> images
        5 test projects     push to GHCR (main only)  docker compose up gateway
        upload .trx         sha-<commit> + latest     smoke-test.sh: 3 × /health + Confirmed + Cancelled
                                                       logs on failure, always down -v
```

## Patterns and ideas

| Idea | Where | In one sentence |
|---|---|---|
| Pipeline as code | `ci-cd.yml` | The build steps are versioned with the code and run the same way every time. |
| Fail fast / quality gate | `needs:` between jobs | A failing test stops the pipeline before an image is pushed. |
| Immutable image tags | `sha-<commit>` + `IMAGE_TAG` | Deploy exactly the image that was tested, not "latest". |
| Least privilege | `permissions: packages: write` only in `images` | Each job gets only the rights it needs. |
| Multi-stage Dockerfile | `src/*/Dockerfile` | Build with the SDK, run on the small runtime image. |
| Smoke test | `scripts/smoke-test.sh` | A short end-to-end check that the deployed system really works. |
| Health endpoint per service | `/health`, `/health/monolith`, `/health/inventory` | Machines (Docker, YARP, CI) can ask each service if it is ready. |
| Sliding window | gateway rate limiter | Counts the last N seconds at any moment, so a burst can't slip between two windows. |

## Key code lines

**`.github/workflows/ci-cd.yml`**
```yaml
deploy:
  needs: images
  if: github.event_name == 'push' && github.ref == 'refs/heads/main'
```
`needs` makes the chain: test → images → deploy. If a test fails, the later jobs are skipped.

```yaml
push: ${{ github.event_name == 'push' && github.ref == 'refs/heads/main' }}
```
Images are built on every branch (to catch broken Dockerfiles), but only pushed from `main`.

```bash
echo "DOCKER_REGISTRY=ghcr.io/${GITHUB_REPOSITORY_OWNER,,}/" >> "$GITHUB_ENV"
echo "IMAGE_TAG=sha-${GITHUB_SHA::7}" >> "$GITHUB_ENV"
```
`,,` lowercases the owner (GHCR names must be lowercase). `::7` takes the first 7 characters of the commit id, the same short id `docker/metadata-action` uses in the tag.

**`docker-compose.yml`**
```yaml
image: ${DOCKER_REGISTRY-}gateway:${IMAGE_TAG:-latest}
```
Locally both variables are empty, so it is `gateway:latest`, built on my machine. In CI it becomes `ghcr.io/shoaibalirts/gateway:sha-ab12cd3`.

**`scripts/smoke-test.sh`**
```bash
for _ in $(seq 1 15); do
  read -r id status < <(my_orders | tail -1)
  [ "$status" != Pending ] && break
  sleep 2
done
```
The answer from Inventory is asynchronous, so the test waits for the status to leave `Pending` (eventual consistency) instead of checking right after checkout.

**`src/Web/Extensions/WebApplicationExtensions.cs`**
```csharp
Predicate = check => !check.Tags.Contains("homePageHealthCheck") && !check.Tags.Contains("apiHealthCheck"),
```
`/health` skips the two old checks that call outside addresses, and runs `self`, `catalog_db` and `identity_db`.

**`src/Gateway/appsettings.json`**
```json
"inventory-health": { "ClusterId": "inventory", "Match": { "Path": "/health/inventory" }, "Transforms": [ { "PathSet": "/health" } ] }
```
The gateway forwards `/health/inventory` to Inventory's `/health`. No code, only a route.

**`src/Gateway/Program.cs`**
```csharp
RateLimitPartition.GetSlidingWindowLimiter(..., new SlidingWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window, SegmentsPerWindow = 5, QueueLimit = 0 })
```
100 per 10 s, counted in 5 segments of 2 s. A fixed window let 110 × 200 through when the burst crossed a reset.

**`tests/Inventory.Tests/Api/InventoryApiFactory.cs`**
```csharp
services.AddDbContext<InventoryDbContext>(options => options.UseInMemoryDatabase(_databaseName));
services.Remove(services.Single(d => d.ImplementationType == typeof(OrderCreatedConsumer)));
```
The real API in memory, with only the database and the RabbitMQ consumer swapped out.

## What I learned today

- **A clean reset is `docker compose down -v`.** SQL Server has no volume, but RabbitMQ and Seq have anonymous volumes. Everything is seeded again on startup.
- **The monolith's `/health` was always `Unhealthy` in Docker.** Nobody had noticed, because nothing used it. CI was the first real user.
- **A fixed window is not a strict limit.** Checkpoint 2 found it; the unit tests didn't, because they use a 60-second window.
- **A clean clone is a real test.** It proves that the build doesn't depend on files that are only on my machine.

## Exam questions with ideal answers

### Microservices vs monolith

**1. Why does each service need its own health endpoint, and who uses them in my system?**
In a monolith there is one process: it runs or it doesn't. With several services, the system can be half-working, so each part must be able to say "I'm ready". Docker Compose uses Inventory's health check, so replica 2 waits until replica 1 has run its migration. YARP calls `/health` on each replica every 5 seconds and stops sending traffic to a dead one. The CI deploy job checks `/health`, `/health/monolith` and `/health/inventory` before the smoke test orders anything. The monolith's `/health` checks its two databases. Inventory's checks `InventoryDb`.

### Security

**2. How does my pipeline push to GHCR without any secrets, and what is "least privilege" here?**
It uses the `GITHUB_TOKEN`, which GitHub creates for every run and which expires when the run ends. The workflow's default permission is `contents: read`. Only the `images` job asks for `packages: write`, and only `deploy` asks for `packages: read`. So a compromised test step can't push images. The demo passwords in the compose file are a weakness. In production they would be GitHub secrets or come from a secret store.

### Testing + message transformation

**3. What does the smoke test check, and why is it more than "the containers started"?**
It waits for 200 from the three health endpoints. Then it logs in as `demouser`, places an order for 1 × item 1 and one for 4 × item 2, through the gateway, with real anti-forgery tokens. It polls My Orders until the status leaves `Pending`, and expects `Confirmed` and `Cancelled`. That covers the gateway, the monolith, RabbitMQ in both directions, both Inventory replicas and both databases. Containers can be "up" while the saga is broken, for example with a wrong queue binding, and only a real order shows that.

### Build and deploy

**4. Walk through the pipeline. What happens when a test fails, and how do I know the deploy runs the tested images?**
`build-and-test` restores, builds in Release and runs five test projects. `images` needs it, so a failing test means no image is built or pushed, and `deploy` needs `images`, so it is skipped too. `images` builds three images on every branch, and on `main` pushes them to GHCR as `sha-<commit>` and `latest`. `deploy` sets `IMAGE_TAG=sha-<commit>`. My compose file uses `${DOCKER_REGISTRY-}<name>:${IMAGE_TAG:-latest}`, so `docker compose up --no-build` pulls exactly the images from this commit. Then the smoke test runs, and `down -v` cleans up.

### Message channels + API gateway

**5. In Checkpoint 2, 110 calls in a row all got 200 although the limit is 100 per 10 seconds. Why, and how did I fix it?**
.NET's fixed window limiter resets its counter on a timer, not when the first request arrives. My 110 calls took about 4.4 seconds and crossed a reset, so maybe 60 landed in one window and 50 in the next, and neither passed 100. At a boundary a fixed window can let almost twice the limit through. I switched to a sliding window with 5 segments of 2 seconds. It counts the requests of the last 10 seconds at any moment. After the change, three runs of 110 calls each gave 100 × 200 and 10 × 429. My gateway test didn't catch it, because it uses a 60-second window.

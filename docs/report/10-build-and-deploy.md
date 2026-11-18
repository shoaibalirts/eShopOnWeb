> **DRAFT.** AI-drafted from my code on 18/11-2026. I review and edit it before I submit.

# Build and deploy

## Containers

Every part of the system runs in a container, started by `docker-compose.yml` and `docker-compose.override.yml`:

| Service | Image | Host port |
|---|---|---|
| `gateway` | built from `src/Gateway/Dockerfile` | 5000, the only way into the shop |
| `eshopwebmvc` | built from `src/Web/Dockerfile` | none |
| `inventory-api`, `inventory-api-2` | the same image, built from `src/Inventory.Api/Dockerfile` | none |
| `eshoppublicapi` | built from `src/PublicApi/Dockerfile` | 5200 (eShopOnWeb's admin API) |
| `sqlserver` | `mcr.microsoft.com/mssql/server:2022-latest` | 1434, for my own tools |
| `rabbitmq` | `rabbitmq:4.3-management` | 5672, 15672 |
| `seq` | `datalust/seq:2026.1` | 5341 |

My Dockerfiles use two stages. The `sdk` image restores and publishes, and only the published output is copied into the small `aspnet` runtime image. I copy the `.csproj` files and `Directory.Packages.props` before the source code, so Docker can reuse the restore layer when only code changes.

Start order matters, so I use `depends_on` with conditions. SQL Server and RabbitMQ have health checks (`sqlcmd` and `rabbitmq-diagnostics ping`), and the services wait until they are `service_healthy`. Replica 2 waits until replica 1 is healthy, so the two EF migrations don't run at the same time. The Inventory image has no `curl`, so its health check opens a TCP connection with bash: `exec 3<>/dev/tcp/127.0.0.1/8080`.

Configuration comes from environment variables in the override file, for example `ConnectionStrings__InventoryDb`, `RabbitMq__Password` and `Jwt__SigningKey`. The double underscore is how ASP.NET Core reads nested settings from the environment. The two settings I use for demos can be overridden from the shell: `STOCK_CHECK_ENABLED` (the rollback switch) and `JWT_SIGNING_KEY`.

The databases have no volume, so `docker compose down -v` gives a clean start. On startup the monolith seeds the catalog and the users, and Inventory's migration seeds the stock.

## The pipeline

`.github/workflows/ci-cd.yml` has three jobs. Each job `needs` the one before it, so a failure stops everything after it.

**1. build-and-test** runs on every push and pull request. It restores and builds the whole solution in Release, then runs five test projects: `UnitTests`, `IntegrationTests`, `FunctionalTests`, `Inventory.Tests` and `Gateway.Tests`. The `.trx` results are uploaded as an artifact, also when a test fails, so I can see which one.

I left two projects out on purpose. `EndToEndTests` are eShopOnWeb's Playwright tests. They start the shop on HTTPS and drive a real browser, so they need a developer certificate and installed browsers. They failed on my own machine for the same reason. `PublicApiIntegrationTests` contains no runnable tests, which was already the case before my changes.

**2. images** builds the three images I changed: `eshopwebmvc`, `gateway` and `inventory-api`. It builds them on every branch, so a broken Dockerfile is found early, but it only pushes from `main`. The images go to GitHub Container Registry as `ghcr.io/shoaibalirts/<image>`, with two tags: `sha-<commit>` and `latest`. The job logs in with the built-in `GITHUB_TOKEN` and asks for `packages: write` only in this job. So there are no secrets to manage. The build uses the GitHub Actions cache, so unchanged layers are not rebuilt.

**3. deploy** runs only on `main`. It deploys to the runner itself: a throw-away test environment, but the same commands would work on a server.

1. It sets `DOCKER_REGISTRY=ghcr.io/shoaibalirts/` and `IMAGE_TAG=sha-<commit>`. My compose file uses these variables in the image names (`${DOCKER_REGISTRY-}gateway:${IMAGE_TAG:-latest}`), so the job runs **exactly the images that were just built and tested**, not whatever `latest` happens to be.
2. `docker compose pull` and `docker compose up -d --no-build gateway`. Starting the gateway starts everything it depends on.
3. `bash scripts/smoke-test.sh` waits until `/health`, `/health/monolith` and `/health/inventory` answer 200. Then it places two real orders through the gateway: 1 × item 1 must become `Confirmed`, and 4 × item 2 (only 3 in stock) must become `Cancelled`. So the deploy check tests the whole saga across the gateway, the monolith, RabbitMQ and both Inventory replicas, not only that the containers started.
4. On failure it prints the container status and logs, and it always ends with `docker compose down -v`.

## Making the services checkable

The deploy check needed a few changes, and they also improved the system:

- The monolith already had `/health`, but in Docker it always answered `Unhealthy`. Two of eShopOnWeb's old checks call the shop's own outside address and the PublicApi on `localhost:5200`, and neither exists inside a container. They keep their own endpoints, and `/health` now checks the app itself and its two databases (`CatalogDb` and `Identity`).
- The gateway has two new routes, `/health/monolith` and `/health/inventory`. With a `PathSet` transform they forward to each service's `/health`. So all three health endpoints can be checked from outside, while the services still have no host ports.
- Inventory's Swagger moved to `/api/inventory/swagger`, under the gateway's existing route. Its OpenAPI document now says the server is `/`, so "Try it out" calls the gateway instead of the address inside Docker.

## How I tested the pipeline before pushing

- `actionlint` (in Docker) found no errors in the workflow.
- I rehearsed the deploy job locally: `docker compose down -v`, `docker compose up -d --no-build gateway`, then the smoke test. It passed in about 11 seconds.
- I cloned the repository into an empty folder and ran `docker compose up -d --build` and the smoke test there. This proves that nothing depends on files that only exist on my machine.
- The first run on GitHub ([run 44323361311](https://github.com/shoaibalirts/eShopOnWeb/actions/runs/44323361311), commit `8dfc3bc`) was green in all five jobs: 104 tests passed, the three images were pushed to GHCR, and the smoke test on the runner confirmed one order and cancelled the other.
- **[CHECK]** A branch with a deliberately failing test, to show that `images` and `deploy` don't run.

## Trade-offs and future work

- **The deploy target is the runner.** It proves the images work together, but nothing keeps running afterwards. A real deploy would run the same compose commands on a server over SSH, or use Kubernetes.
- **No versioned releases.** Every push to `main` gets `latest`. Rolling back means deploying an older `sha-` tag by hand. The feature flag is the faster rollback for the Inventory behaviour.
- **Demo secrets in the compose file.** The SQL password, the RabbitMQ password and the JWT key are demo values in the repository. In production they would come from GitHub secrets or a secret store.
- **No browser tests in CI.** The smoke test covers the order flow, but nobody clicks through the pages in CI.
- **Images for PublicApi are not pushed.** I didn't change PublicApi, so the deploy job doesn't need it.

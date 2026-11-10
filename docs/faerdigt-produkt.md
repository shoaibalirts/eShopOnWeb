# Hvad er et færdigt produkt?

Denne tabel oversætter hvert krav fra [opgavebeskrivelsen](opgavebeskrivelse.pdf) til et konkret kriterium, som kan tjekkes ved aflevering søndag den 22/11-2026.
Jeg arbejder alene, og min underviser Jakob bekræftede den 6/11-2026, at jeg bygger **én** microservice, `Inventory`, ved siden af eShopOnWeb-monolitten.
Arbejdet bag hvert kriterium er planlagt i [PLAN.md](../architecture/PLAN.md), og filer og tests nævnt her oprettes undervejs.

## Kravspecifikation → konkret kriterium

| Krav | Hvordan ser vi, at det er opfyldt? |
|---|---|
| **1. Identificér og afgræns services** (min. 3 microservices, fx katalog, ordre, betaling; for mig: 1, bekræftet af Jakob 6/11-2026) | 1 service i `src/Inventory.Api` med sin egen database og `Dockerfile`. Afsnittet "Why Inventory" i [02-target-architecture.md](../architecture/02-target-architecture.md) begrunder valget med min matrix, og [00-business-story.md](../architecture/00-business-story.md) viser alle 5 bounded contexts og hvorfor de øvrige 4 bliver i monolitten. |
| **2. Strangler Pattern implementering** — funktionalitet flyttes gradvist uden driftsafbrydelse | `src/Gateway` (YARP) ruter `/api/inventory/*` til `Inventory` og alt andet (catch-all) til monolitten. Med `StockCheck:Enabled=false` virker butikken som i dag, og med `true` går hver ordre gennem lagertjekket. |
| **3. Lagerstyringssystem** implementeret som selvstændig microservice | `Inventory` kører i sin egen container med databasen `InventoryDb`. Efter en ordre på 2 stk. er beholdningen 2 lavere i `GET /api/inventory/{itemId}`. Unit tests i `Inventory.Tests` viser, at en reservation er alle linjer eller ingen, aldrig går under nul, og kun sker én gang pr. `OrderId`. |
| **4. API Gateway** — samlet indgangspunkt med autentificering, load balancing og rate-limiting | `PUT /api/inventory/{itemId}/stock` uden gyldig JWT fra monolitten giver `401`. En test viser `429`, når rate limit overskrides. Der kører 2 `Inventory`-replikaer, og YARP fordeler kald mellem dem (round-robin). |
| **5. Asynkron beskedbaseret kommunikation** (fx RabbitMQ, Azure Service Bus, Kafka) | RabbitMQ kører i `docker-compose.yml` med topic exchange `eshop.events` og routing keys `order.created`, `stock.reserved` og `stock.rejected`. Køen `inventory.order-created` er bundet til `order.created`, og beskeder bekræftes manuelt (manual ack). En ordre med nok lager bliver `Confirmed`, og en ordre på mere end beholdningen bliver `Cancelled`. |
| **6. Datahåndtering og dataseparation** — egen database pr. microservice | `Inventory` har sin egen forbindelsesstreng til `InventoryDb` og ingen `ProjectReference` til monolittens projekter. Monolitten læser aldrig `InventoryDb` direkte, kun via gatewayen eller beskeder. |
| **7. Fejlhåndtering og overvågning** (fx Circuit Breaker, logging, ELK eller Prometheus/Grafana) | Produktsiden kalder `GET /api/inventory/{id}` gennem gatewayen med en Polly circuit breaker. Når `Inventory` stoppes, viser siden "availability unknown" i stedet for HTTP 500. Serilog sender logs fra monolitten, gatewayen og `Inventory` til Seq. Alle tre svarer på `/health`. |
| **8. CI/CD pipeline** — automatisk build, test og deployment, containerisering (fx Docker) | GitHub Actions bygger, tester, laver Docker-images og pusher dem til GHCR. Et deploy-job på runneren starter systemet og tjekker, at `/health` svarer `200`. Én grøn kørsel på `main` er linket i `README.md`, og en fejlende test stopper pipelinen. |
| **9. Dokumentation** af arkitekturbeslutninger, API'er og microservices (fx i README) | `README.md` beskriver, hvordan systemet køres, API'erne og mine arkitekturbeslutninger. Der er et diagram over målarkitekturen med én service og et sekvensdiagram over ordre-sagaen. `Inventory` viser sit API på `/swagger`. |

Tabellen indeholder kun mine **Must**-punkter fra [PLAN.md](../architecture/PLAN.md), så en planlagt nedskæring aldrig får et kriterium til at fejle.

## Ekstra bevis, hvis tiden rækker (Should og Could)

Disse punkter styrker kravene, men afleveringen består uden dem. Hvis de bliver skåret, beskriver jeg dem i rapporten som fremtidigt arbejde.

| Styrker krav | Ekstra bevis |
|---|---|
| 3 | En samtidighedstest viser, at kun én af to reservationer af den sidste enhed lykkes (`rowversion`). |
| 5 | Fejlende beskeder ender i en dead-letter exchange efter en retry-grænse. Tests viser, at en dublet ikke reserverer to gange, og at en poison message ender i dead-letter. `scripts/test-messaging.ps1` kører hele flowet. |
| 6 | RabbitMQ- og databaseadgangskoder kommer fra miljøvariabler. |
| 7 | Et `CorrelationId` (= `OrderId`) for én ordre kan søges frem i Seq i logs fra alle tre. |
| 8 | En `WebApplicationFactory`-integrationstest af API'et. |

## Sluttjek

Fra en ren klon: `docker compose up` → afgiv en ordre i butikken → ordrens status skifter fra `Pending` til `Confirmed` under My Orders.

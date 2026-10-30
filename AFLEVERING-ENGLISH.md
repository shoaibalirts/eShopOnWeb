# Hand-in: eShopOnWeb from monolith to microservices

## 1. Git link

https://github.com/shoaibalirts/eShopOnWeb

## 2. Diagrams

| Diagram | Explanation |
|---|---|
| [00-context-map.png](architecture/diagrams/00-context-map.png) | The shop's business areas and the information that passes between them. |
| [01-browse-catalog.png](architecture/diagrams/01-browse-catalog.png) | How the monolith shows the product list today, including its cache. |
| [01-basket.png](architecture/diagrams/01-basket.png) | How the monolith adds items, shows the basket, changes quantities and merges baskets at sign-in. |
| [01-checkout-place-order.png](architecture/diagrams/01-checkout-place-order.png) | How the monolith turns a basket into an order today, in separate saves with no shared transaction. |
| [02-target-architecture.png](architecture/diagrams/02-target-architecture.png) | My target microservice architecture during migration, with HTTP calls and events labelled. |

| Document | Explanation |
|---|---|
| [00-business-story.md](architecture/00-business-story.md) | Why the shop should migrate, plus the bounded contexts and the context map. |
| [01-monolith-flows.md](architecture/01-monolith-flows.md) | The three key monolith flows as sequence diagrams, explained in plain English. |
| [02-target-architecture.md](architecture/02-target-architecture.md) | My services, events, differences from the instructors' proposal and the Strangler order. |
| [PLAN.md](architecture/PLAN.md) | My tasks, three-point estimates and weekly schedule up to the deadline. |
| [faerdigt-produkt.md](docs/faerdigt-produkt.md) | One checkable acceptance criterion for each requirement. |

## 3. Architecture

I split the monolith into **Catalog, Basket and Ordering**, and I add a new **Inventory** service. Each service has its own SQL Server database. A YARP gateway is the single entry point and handles JWT authentication, load balancing and rate limiting. Services communicate through HTTP and through events on RabbitMQ. The monolith keeps the pages and sign-in.

My design keeps the main ideas from the instructors' eShopOnContainers proposal: a gateway, one service per business area, a database per service and an event bus. It differs in these ways:

| Instructors' proposal | My design | Why |
|---|---|---|
| Mobile, MVC and SPA clients | MVC web app and Blazor admin | These are the clients eShopOnWeb already has. |
| Two gateways | One gateway | I have no mobile client, so a second gateway adds no value. |
| Identity as its own service | Identity stays in the monolith | Moving sign-in is risky, and the assignment doesn't require it. |
| No Inventory service | A new Inventory service | The assignment requires it. |
| Basket in Redis | Basket in SQL Server | Anonymous baskets are kept for a long time, so they are lasting data rather than a cache. |
| Ordering API + BackgroundTasks | One Ordering service | Events drive my checkout steps, so I don't need a second process. |
| RabbitMQ or Azure Service Bus | RabbitMQ only | It runs in Docker with no cloud account. |
| Web Status, Seq and Webhooks | Seq only | Seq covers central logging, and no requirement needs the other two. |
| A finished system | The monolith remains during migration | The Strangler Pattern keeps the shop running while I move one part at a time. |

## 4. Plan with estimates

The plan targets the semester deadline of **23/11-2026** with **~130 hours**: 113.0 expected hours plus a 15% buffer of 17.0 hours.

| Service / area | Expected hours (E) |
|---|---:|
| Catalog | 19.1 |
| Basket | 19.8 |
| Ordering | 23.4 |
| Inventory | 14.9 |
| Gateway | 11.8 |
| Shared setup | 8.7 |
| CI/CD | 6.6 |
| Documentation | 8.7 |
| **Total expected** | **113.0** |
| Buffer (15%) | 17.0 |
| **Total with buffer** | **130.0** |

- Full plan: [PLAN.md](architecture/PLAN.md)
- Acceptance criteria for each requirement: [faerdigt-produkt.md](docs/faerdigt-produkt.md)

## 5. What I did in the project week

**Work:** I analysed the monolith and traced how its code handles browsing, the basket and checkout. I wrote a business story explaining why the shop should migrate, and I divided the shop into bounded contexts with a context map. I documented the three key flows as sequence diagrams. From this I designed a target architecture and made a plan with three-point estimates, sized for one developer and the time I have.

**What I learned:**
- The sequence diagrams made the hidden coupling visible. Catalog, Basket and Ordering share one database, and the page header reads the basket table on every page.
- Service boundaries should come from business areas and real dependencies, not only from the technical layers in the code.
- The Strangler Pattern sets the order of the work: start with the safest part (Catalog) and finish with the most tangled (Ordering).
- My first plan came to 354 hours, far more than I have. Linking every task to a requirement showed me what I could postpone without missing a requirement.

**Next steps:**
1. Put the YARP gateway in front of the monolith, with all traffic still going to the monolith.
2. Set up RabbitMQ, Seq and the CI pipeline.
3. Extract Catalog as the first service.
4. Check three questions with my instructor: building Inventory from scratch, what counts as automatic deployment, and whether Inventory counts towards the minimum of 3 microservices.

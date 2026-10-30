# Aflevering: eShopOnWeb fra monolit til microservices

## 1. Git-link

https://github.com/shoaibalirts/eShopOnWeb

## 2. Diagrammer

| Diagram | Forklaring |
|---|---|
| [00-context-map.png](architecture/diagrams/00-context-map.png) | Butikkens forretningsområder og de oplysninger, der går mellem dem. |
| [01-browse-catalog.png](architecture/diagrams/01-browse-catalog.png) | Hvordan monolitten viser produktlisten i dag, inklusive dens cache. |
| [01-basket.png](architecture/diagrams/01-basket.png) | Hvordan monolitten lægger varer i kurven, viser kurven, ændrer antal og samler kurve ved login. |
| [01-checkout-place-order.png](architecture/diagrams/01-checkout-place-order.png) | Hvordan monolitten i dag gør en kurv til en ordre, i separate gemninger uden fælles transaktion. |
| [02-target-architecture.png](architecture/diagrams/02-target-architecture.png) | Min microservice-målarkitektur under migreringen, hvor HTTP-kald og hændelser er markeret. |

| Dokument | Forklaring |
|---|---|
| [00-business-story.md](architecture/00-business-story.md) | Hvorfor butikken skal migreres, samt bounded contexts og context map. |
| [01-monolith-flows.md](architecture/01-monolith-flows.md) | Monolittens tre vigtigste flows som sekvensdiagrammer, forklaret i et enkelt sprog. |
| [02-target-architecture.md](architecture/02-target-architecture.md) | Mine services, hændelser, forskelle fra undervisernes forslag og rækkefølgen i Strangler Pattern. |
| [PLAN.md](architecture/PLAN.md) | Mine opgaver, trepunktsestimater og ugeplan frem til afleveringsfristen. |
| [faerdigt-produkt.md](docs/faerdigt-produkt.md) | Ét konkret acceptkriterium, der kan tjekkes, for hvert krav. |

## 3. Arkitektur

Jeg deler monolitten op i **Catalog, Basket og Ordering** og tilføjer en ny **Inventory**-service. Hver service har sin egen SQL Server-database. En YARP-gateway er det eneste indgangspunkt og håndterer JWT-autentificering, load balancing og rate limiting. Services kommunikerer via HTTP og via hændelser på RabbitMQ. Monolitten beholder siderne og login.

Mit design bevarer hovedidéerne fra undervisernes eShopOnContainers-forslag: en gateway, én service pr. forretningsområde, en database pr. service og en event bus. Det adskiller sig på disse punkter:

| Undervisernes forslag | Mit design | Hvorfor |
|---|---|---|
| Mobil-, MVC- og SPA-klienter | MVC-webapp og Blazor admin | Det er de klienter, eShopOnWeb allerede har. |
| To gateways | Én gateway | Jeg har ingen mobilklient, så en ekstra gateway giver ingen værdi. |
| Identity som egen service | Identity bliver i monolitten | Det er risikabelt at flytte login, og opgaven kræver det ikke. |
| Ingen Inventory-service | En ny Inventory-service | Opgaven kræver den. |
| Basket i Redis | Basket i SQL Server | Anonyme kurve gemmes længe, så de er varige data og ikke en cache. |
| Ordering API + BackgroundTasks | Én Ordering-service | Hændelser styrer mine checkout-trin, så jeg behøver ikke en ekstra proces. |
| RabbitMQ eller Azure Service Bus | Kun RabbitMQ | Den kører i Docker uden en cloud-konto. |
| Web Status, Seq og Webhooks | Kun Seq | Seq dækker central logning, og intet krav har brug for de to andre. |
| Et færdigt system | Monolitten er der under migreringen | Strangler Pattern holder butikken kørende, mens jeg flytter én del ad gangen. |

## 4. Plan med estimater

Planen sigter mod semesterfristen **23/11-2026** med **~130 timer**: 113,0 forventede timer plus en buffer på 15 % svarende til 17,0 timer.

| Service / område | Forventede timer (E) |
|---|---:|
| Catalog | 19,1 |
| Basket | 19,8 |
| Ordering | 23,4 |
| Inventory | 14,9 |
| Gateway | 11,8 |
| Fælles opsætning | 8,7 |
| CI/CD | 6,6 |
| Dokumentation | 8,7 |
| **Forventet i alt** | **113,0** |
| Buffer (15 %) | 17,0 |
| **I alt med buffer** | **130,0** |

- Hele planen: [PLAN.md](architecture/PLAN.md)
- Acceptkriterier for hvert krav: [faerdigt-produkt.md](docs/faerdigt-produkt.md)

## 5. Hvad jeg har lavet i projektugen

**Arbejde:** Jeg analyserede monolitten og fulgte, hvordan koden håndterer visning af produkter, kurven og checkout. Jeg skrev en business story om, hvorfor butikken skal migreres, og delte butikken op i bounded contexts med et context map. Jeg dokumenterede de tre vigtigste flows som sekvensdiagrammer. Ud fra det designede jeg en målarkitektur og lavede en plan med trepunktsestimater, tilpasset én udvikler og den tid, jeg har.

**Hvad jeg har lært:**
- Sekvensdiagrammerne gjorde den skjulte kobling synlig. Catalog, Basket og Ordering deler én database, og sidehovedet læser kurvetabellen på hver side.
- Servicegrænser bør komme fra forretningsområder og reelle afhængigheder, ikke kun fra de tekniske lag i koden.
- Strangler Pattern bestemmer rækkefølgen i arbejdet: start med den sikreste del (Catalog), og slut med den mest sammenfiltrede (Ordering).
- Min første plan var på 354 timer, langt mere end jeg har. Ved at knytte hver opgave til et krav kunne jeg se, hvad jeg kunne udskyde uden at misse et krav.

**Næste skridt:**
1. Sætte YARP-gatewayen foran monolitten, mens al trafik stadig går til monolitten.
2. Sætte RabbitMQ, Seq og CI-pipelinen op.
3. Udskille Catalog som den første service.
4. Afklare tre spørgsmål med min underviser: at jeg bygger Inventory fra bunden, hvad der tæller som automatisk deployment, og om Inventory tæller med i minimum 3 microservices.

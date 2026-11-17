> **DRAFT.** AI-drafted from my code and docs on 17/11-2026. I must rewrite it in my own words before I submit.

# Why Inventory

## The problem

eShopOnWeb sells products without checking stock. When a customer checks out, `OrderService.CreateOrderAsync` in `src/ApplicationCore/Services/OrderService.cs` builds the order from the basket and saves it. Nothing in that flow asks whether the products are on the shelf. On a busy sale day, the shop can sell more mugs than it has.

This is a real business gap, and it is also requirement 3 in the assignment: a stock management system built as its own microservice.

## How I chose

I work alone, and my instructor Jakob confirmed on 6/11-2026 that I build one microservice beside the monolith. So I had to pick the one service that gives the most value for the least risk.

In `architecture/00-business-story.md` I split the shop into five bounded contexts: Catalog, Basket, Ordering, Identity and Inventory. I then compared them on the same five questions. The full matrix is in `architecture/02-target-architecture.md`.

| Context | Required by the assignment? | Risk if it breaks | Tangled today | Business value | Fits one person? |
|---|---|---|---|---|---|
| Catalog | No | Low | Medium | High | Medium |
| Basket | No | Medium | High | Medium | Medium |
| Ordering | No | High | Highest | High | No |
| Identity | No | Highest | Low | Low | No |
| **Inventory** | **Yes** | **Low** | **None** | **High** | **Yes** |

Inventory is the only row with no bad answer.

- **Required:** it is the only context the assignment names as its own service.
- **Low risk:** it is new code. If it fails, the shop still works, and the feature flag `StockCheck:Enabled` turns the stock check off.
- **Not tangled:** no existing code depends on it, so I don't have to cut it out of the monolith first.
- **High value:** it stops overselling.
- **Small data:** one stock number per product, so I can build it properly with messaging, resilience and tests in the time I have.

## Why not the others

**Ordering** has the most value, but it is the most tangled. One checkout touches Basket, Catalog and Ordering in one flow and one database. Moving it alone is too big and too risky for one person.

**Identity** has its own database already, but if it breaks, nobody can sign in. It also needs careful security work.

**Basket** writes on every click and reads the catalog for prices, so it is tangled with Catalog.

**Catalog** would be the best choice if I had to *extract* existing code. It is mostly reading, it gets the most traffic, and it is the safest to move. It is my next step after this project.

## What this means for the Strangler Pattern

Inventory is not extracted from the monolith. It grows beside it. That is still the Strangler Pattern: new functionality goes into a new service behind a gateway, and the monolith gets smaller in responsibility over time. Because Inventory is new, I can show the full pattern (gateway, messages, rollback) without breaking a feature that already works.

The trade-off is that I don't show the hardest part of a real migration: moving existing code and data out of a running system. I describe how I would do that for Catalog in the future work section.

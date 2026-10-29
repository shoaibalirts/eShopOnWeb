# As-is: the three key monolith flows

These diagrams show how the **current monolith** (`src/Web`) handles the three core shopping flows.
They are the baseline for the Strangler Pattern migration: every arrow that crosses a future
service boundary is a coupling point that has to become an HTTP call or a message.

Participants map to real classes:

| Participant | Code |
|---|---|
| Razor Page | `src/Web/Pages/Index.cshtml.cs`, `src/Web/Pages/Basket/Index.cshtml.cs`, `src/Web/Pages/Basket/Checkout.cshtml.cs` |
| ViewModel services | `src/Web/Services/CachedCatalogViewModelService.cs`, `CatalogViewModelService.cs`, `BasketViewModelService.cs` |
| Domain services | `src/ApplicationCore/Services/BasketService.cs`, `OrderService.cs` |
| Repository | `src/Infrastructure/Data/EfRepository.cs` (generic `IRepository<T>`) |
| CatalogDB | **one** SQL Server database (`CatalogContext`) holding catalog, baskets **and** orders |
| IdentityDB | separate `AppIdentityDbContext` (users/roles) |

---

## 1. Browse catalog

`GET /` (optionally `?pageId=n&BrandFilterApplied=x&TypesFilterApplied=y`)

```mermaid
sequenceDiagram
    autonumber
    actor U as Browser
    participant P as Pages/Index (IndexModel)
    participant C as CachedCatalogViewModelService
    participant M as IMemoryCache
    participant S as CatalogViewModelService
    participant R as IRepository (CatalogItem/Brand/Type)
    participant DB as CatalogDB (SQL)

    U->>P: GET /?pageId&brand&type
    P->>C: GetCatalogItems(page, ITEMS_PER_PAGE, brandId, typeId)
    C->>M: GetOrCreateAsync(cacheKey)
    alt cache hit
        M-->>C: CatalogIndexViewModel
    else cache miss
        M->>S: GetCatalogItems(...)
        S->>R: ListAsync(CatalogFilterPaginatedSpecification)
        R->>DB: SELECT CatalogItems ... OFFSET/FETCH
        DB-->>R: items on page
        S->>R: CountAsync(CatalogFilterSpecification)
        R->>DB: SELECT COUNT(*)
        DB-->>R: total
        S->>R: ListAsync() brands
        R->>DB: SELECT CatalogBrands
        S->>R: ListAsync() types
        R->>DB: SELECT CatalogTypes
        S->>S: map to CatalogIndexViewModel<br/>(UriComposer builds picture URLs, pagination)
        S-->>M: view model (sliding expiration)
        M-->>C: view model
    end
    C-->>P: CatalogIndexViewModel
    P-->>U: HTML (product grid + filters)
    Note over U,P: Layout also renders the Basket view component,<br/>which calls BasketQueryService.CountTotalBasketItems<br/>→ SUM over Baskets in the same CatalogDB
```

**Observations for the migration**
- Pure read flow, no writes — the safest first candidate to strangle into a **Catalog service**.
- Cache lives in-process (`IMemoryCache`); it moves with the service (or becomes gateway/HTTP caching).
- The basket counter in the header reads the Basket table on *every* page — a hidden Catalog↔Basket coupling.

---

## 2. Basket (view / add item / update quantities)

`GET /Basket`, `POST /Basket` (add from product card), `POST /Basket?handler=Update`

```mermaid
sequenceDiagram
    autonumber
    actor U as Browser
    participant P as Pages/Basket/Index (IndexModel)
    participant BV as BasketViewModelService
    participant BS as BasketService
    participant RB as IRepository (Basket)
    participant RC as IRepository (CatalogItem)
    participant DB as CatalogDB (SQL)

    U->>P: POST /Basket (CatalogItemViewModel.Id)
    P->>RC: GetByIdAsync(productId)
    RC->>DB: SELECT CatalogItem
    DB-->>RC: item (incl. Price)
    alt item not found
        P-->>U: 302 → /Index
    end
    P->>P: GetOrSetBasketCookieAndUserName()<br/>authenticated → User.Identity.Name<br/>anonymous → GUID in "eShop" cookie (set if missing)
    P->>BS: AddItemToBasket(buyerId, itemId, item.Price)
    BS->>RB: FirstOrDefaultAsync(BasketWithItemsSpecification(buyerId))
    RB->>DB: SELECT Basket + BasketItems
    opt no basket yet
        BS->>RB: AddAsync(new Basket(buyerId))
        RB->>DB: INSERT Basket
    end
    BS->>BS: basket.AddItem(itemId, price, qty)<br/>(price snapshot stored in BasketItem)
    BS->>RB: UpdateAsync(basket)
    RB->>DB: INSERT/UPDATE BasketItems
    P-->>U: 302 → /Basket (PRG)

    U->>P: GET /Basket
    P->>BV: GetOrCreateBasketForUser(buyerId)
    BV->>RB: FirstOrDefaultAsync(BasketWithItemsSpecification)
    RB->>DB: SELECT Basket + Items
    BV->>RC: ListAsync(CatalogItemsSpecification(ids))
    RC->>DB: SELECT CatalogItems WHERE Id IN (...)
    BV->>BV: join basket items with catalog name + picture
    BV-->>P: BasketViewModel
    P-->>U: HTML basket

    U->>P: POST /Basket?handler=Update (items[].Quantity)
    P->>BV: GetOrCreateBasketForUser(buyerId)
    P->>BS: SetQuantities(basketId, {itemId: qty})
    BS->>RB: load, SetQuantity, RemoveEmptyItems, UpdateAsync
    RB->>DB: UPDATE/DELETE BasketItems
    P->>BV: Map(basket) (re-reads CatalogItems)
    P-->>U: HTML basket

    Note over U,DB: On login, Login.cshtml.cs calls BasketService.TransferBasketAsync(anonymousId, userName):<br/>merge anonymous basket into user basket, delete anonymous basket, delete cookie
```

**Observations for the migration**
- Basket reads `CatalogItem` directly from the shared DB twice (price at add-time, name/picture at view-time).
  In a **Basket service** this becomes a call to the Catalog API (or a local read-model fed by `ProductPriceChanged` events).
- Buyer identity is either a username or an anonymous GUID cookie — the gateway/Basket service must keep supporting both.
- Login triggers a basket merge — an Identity → Basket dependency to preserve.

---

## 3. Checkout / place order

`GET /Basket/Checkout` then `POST /Basket/Checkout` (requires `[Authorize]`)

```mermaid
sequenceDiagram
    autonumber
    actor U as Browser (signed in)
    participant P as Pages/Basket/Checkout (CheckoutModel)
    participant BV as BasketViewModelService
    participant BS as BasketService
    participant OS as OrderService
    participant R as IRepository (Basket/CatalogItem/Order)
    participant DB as CatalogDB (SQL)
    participant Med as IMediator (in-process)
    participant H as OrderCreatedHandler
    participant E as IEmailSender

    U->>P: POST /Basket/Checkout (items[].Quantity)
    P->>BV: GetOrCreateBasketForUser(User.Identity.Name)
    BV->>R: Basket + CatalogItems
    R->>DB: SELECT
    P->>BS: SetQuantities(basketId, quantities)
    BS->>R: UpdateAsync(basket)
    R->>DB: UPDATE BasketItems
    P->>OS: CreateOrderAsync(basketId, hard-coded Address)
    OS->>R: FirstOrDefaultAsync(BasketWithItemsSpecification)
    R->>DB: SELECT Basket + Items
    alt basket empty
        OS-->>P: EmptyBasketOnCheckoutException
        P-->>U: 302 → /Basket
    end
    OS->>R: ListAsync(CatalogItemsSpecification(ids))
    R->>DB: SELECT CatalogItems
    OS->>OS: build OrderItems<br/>(CatalogItemOrdered snapshot: id, name, picture,<br/>unit price from BasketItem)
    OS->>R: AddAsync(new Order(buyerId, address, items))
    R->>DB: INSERT Order + OrderItems
    OS->>Med: Publish(OrderCreatedEvent)
    Med->>H: Handle(OrderCreatedEvent)
    H->>E: SendEmailAsync("Order Created")
    P->>BS: DeleteBasketAsync(basketId)
    BS->>R: DeleteAsync(basket)
    R->>DB: DELETE Basket
    P-->>U: 302 → /Basket/Success
```

**Observations for the migration**
- `OrderService` reaches into **three aggregates** (Basket, CatalogItem, Order) in one database — the main thing to untangle.
- No **stock check / reservation** and no **payment** — this is exactly where the new Inventory and Payment services plug in.
- The three writes (update quantities → insert order → delete basket) are **not in one transaction**; a crash between
  them leaves an order *and* a basket. In the microservice design this becomes an explicit saga with compensations.
- `OrderCreatedEvent` is already a domain event published in-process via Mediator — the natural seam to replace with a
  RabbitMQ integration event (`OrderCreated`) consumed by Inventory, Payment, Basket and Notification.
- Shipping address is hard-coded (`123 Main St.`).

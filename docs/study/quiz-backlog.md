# Quiz backlog

Questions I skipped and will answer after submission. The ideal answers are in the `day-N.md` file named in each heading. I answer first, then compare.

## Day 1 – Wednesday 18/11 (answers in [day-1.md](day-1.md))

1. **(Microservices vs monolith)** Why does checkout send `order.created` as a message instead of calling Inventory over HTTP? What is the price of that choice?
2. **(Testing)** How do I know the all-or-none rule works? What do my tests *not* prove?
3. **(Message channels)** What is the difference between `eshop.events` and `inventory.order-created`? What happens to orders placed while Inventory is down?
4. **(Message channels)** In `OrderCreatedConsumer`, why do I ack after publishing the result, and not before? What does "at-least-once" mean here?
5. **(Message routing)** The monolith's queue is bound with `stock.*`. How does that one binding receive both answers? What would I do if a new Shipping service also needed `order.created`?

# Quiz backlog

Questions I skipped and will answer after submission. The ideal answers are in the `day-N.md` file named in each heading. I answer first, then compare.

## Day 1 – Wednesday 18/11 (answers in [day-1.md](day-1.md))

1. **(Microservices vs monolith)** Why does checkout send `order.created` as a message instead of calling Inventory over HTTP? What is the price of that choice?
2. **(Testing)** How do I know the all-or-none rule works? What do my tests *not* prove?
3. **(Message channels)** What is the difference between `eshop.events` and `inventory.order-created`? What happens to orders placed while Inventory is down?
4. **(Message channels)** In `OrderCreatedConsumer`, why do I ack after publishing the result, and not before? What does "at-least-once" mean here?
5. **(Message routing)** The monolith's queue is bound with `stock.*`. How does that one binding receive both answers? What would I do if a new Shipping service also needed `order.created`?

## Day 2 – Thursday's plan, done 18/11 (answers in [day-2.md](day-2.md))

1. **(Microservices vs monolith)** What does `StockCheck:Enabled` do, and why is it part of the Strangler Pattern?
2. **(Testing + message transformation)** What does `IntegrationEventPublisher.Translate` do, and why not send the whole `Order`?
3. **(Build and deploy)** Why did the monolith not start in Docker at first, and how did I find and fix it?
4. **(Message channels)** What happens if RabbitMQ is down when a customer checks out, and what would fix it properly?
5. **(Domain decomposition + message routing)** My saga is "choreographed". What does that mean, and what is missing compared to a full saga?

## Day 3 – Friday's plan, done 18/11 (answers in [day-3.md](day-3.md))

1. **(Microservices vs monolith)** How is my gateway a Strangler Fig, and what would the next step look like?
2. **(Security)** How does the gateway decide that a restock request is allowed? Why there and not in Inventory?
3. **(Testing)** How did I test the circuit breaker, both in a unit test and live?
4. **(Build and deploy)** How do the two Inventory replicas work in Docker Compose, and what happens when one dies?
5. **(Message channels)** What happens to an `order.created` message that keeps failing, and why didn't I use RabbitMQ's own delivery limit?
6. **(Bonus: message routing)** How can I follow one order across the gateway, the monolith, RabbitMQ and Inventory in Seq?

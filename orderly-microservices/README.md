# Orderly Microservices

A multi-tenant restaurant management system built on .NET 10, structured as a set of independently deployable microservices behind a YARP API gateway.

**Architecture & patterns:** CQRS with MediatR, Carter minimal APIs, and Domain-Driven Design (Ordering aggregates).

**Data:** Marten (document store) and EF Core (relational) over PostgreSQL (Catalog, Basket, Identity, Kitchen) and SQL Server (Ordering).

**Messaging:** MassTransit over RabbitMQ with a transactional outbox pattern for reliable integration-event publishing, plus SignalR for real-time kitchen ticket broadcasting.

**Cross-cutting:** Redis distributed cache, a Discount service exposed over gRPC, and ASP.NET Core Identity with JWT bearer auth and role-based access control (RBAC).

## Port strategy

Ports are listed as http/https

| Service          | Local env   | Docker env  | Docker inside |
|------------------|-------------|-------------|---------------|
| Catalog API      | 5000 - 5050 | 6000 - 6060 | 8080 - 8081   |
| Basket API       | 5001 - 5051 | 6001 - 6061 | 8080 - 8081   |
| Discount gRPC    | 5002 - 5052 | 6002 - 6062 | 8080 - 8081   |
| Ordering API     | 5003 - 5053 | 6003 - 6063 | 8080 - 8081   |
| Kitchen API      | 5005 - 5055 | 6005 - 6065 | 8080 - 8081   |
| Yarp API Gateway | 5004 - 5054 | 6004 - 6064 | 8080 - 8081   |
| Identity API     | 5007 - 5057 | 6007 - 6067 | 8080 - 8081   |

## Running the Application

There are two primary ways to run this solution: locally (via Visual Studio / .NET CLI) or fully containerized via Docker.

### Option 1: Running with Docker (Recommended)

Running the project via Docker is the simplest method as it automatically provisions the necessary backing services (PostgreSQL, endpoints, etc.) alongside the APIs.

1. Ensure you have [Docker Desktop](https://www.docker.com/products/docker-desktop/) installed and running.
2. Open a terminal at the root of the project (where the `docker-compose.yml` file is located).
3. Run the following command to build and attach to the containers:

```bash
docker-compose up -d --build
```

To view the logs or stop the environment:

- **View logs:** `docker-compose logs -f`
- **Stop containers:** `docker-compose down`
- **Wipe database volumes (if you need a fresh start):** `docker-compose down -v`

#### Environment variables

All credentials are externalized. Out of the box, `docker-compose up` uses the
dev defaults baked into `docker-compose.override.yml`. To override any
credential (e.g. a stronger DB password for a shared environment), copy
`.env.example` to `.env` in this same directory (next to
`docker-compose.override.yml`) and edit it. Docker Compose auto-loads that
`.env` when you run `docker-compose up` from here. The `.env` file is
git-ignored — do not commit real secrets.

Variables: `POSTGRES_USER`, `POSTGRES_PASSWORD`, `ACCEPT_EULA`,
`SA_PASSWORD`, `RABBITMQ_DEFAULT_USER`, `RABBITMQ_DEFAULT_PASS`,
`REDIS_PASSWORD`,
`ASPNETCORE_Kestrel__Certificates__Default__Password` (cert).

### Option 2: Running Locally (Visual Studio or .NET CLI)

If you prefer to run the .NET processes locally on your machine for easier debugging:

#### Prerequisites for Local Execution

You still need the backing services.

To debug the code locally, you still need the background services (PostgreSQL, Redis, etc.) active. A highly recommended hybrid approach is to spin up **only the backing services** using Docker Compose:

```bash
docker-compose up catalogdb basketdb identitydb kitchendb orderdb distributedcache messagebroker -d
```

*(With this, your databases run via Docker and their ports are mapped automatically, allowing you to debug the .NET APIs natively).*

When you're finished, tear them down with:

```bash
docker-compose down
```

Or ensure you have local instances of the backing services running on the mapped ports:

| Service             | Engine        | Port(s)         |
|---------------------|---------------|-----------------|
| CatalogDB           | PostgreSQL    | `5433`          |
| BasketDB            | PostgreSQL    | `5434`          |
| IdentityDB          | PostgreSQL    | `5435`          |
| KitchenDB           | PostgreSQL    | `5436`          |
| OrderDB             | SQL Server    | `1433`          |
| Distributed cache   | Redis         | `6379`          |
| Message broker      | RabbitMQ      | `5672` (mgmt UI `15672`) |

> **Note:** Ordering uses SQL Server; all other relational stores are PostgreSQL.

#### Using Visual Studio

1. Open the solution file `orderly-microservices.slnx` in Visual Studio.
2. Ensure Docker Desktop is running if you are using the `.dcproj` as your startup project.
3. Configure your Startup Projects. You can configure it to start multiple API services simultaneously (e.g., `Catalog.API` & `Basket.API`).
4. Press `F5` or click **Start**.

#### Using .NET CLI

1. Open a terminal.
2. Run each service individually from its respective directory:

```bash
# Run the Catalog API
cd Services/Catalog/Catalog.API
dotnet run

# Run the Basket API
cd ../../Basket/Basket.API
dotnet run

# Run the Ordering API
cd ../../Ordering/Ordering.API
dotnet run

# Run the Identity API
cd ../../Identity/Identity.API
dotnet run

# Run the Kitchen API
cd ../../Kitchen/Kitchen.API
dotnet run

# Run the Discount gRPC service
cd ../../Discount/Discount.Grpc
dotnet run

# Run the YARP API Gateway
cd ../../../ApiGateway/YarpApiGateway
dotnet run
```

## Useful Docker CLI Commands

While the background services are running via Docker, you can execute commands directly on the containers to inspect databases or test caches.

### PostgreSQL (Catalog, Basket, Identity & Kitchen Databases)

To open the Postgres interactive terminal (`psql`) inside the `catalogdb` container:

```bash
docker exec -it catalogdb psql -U postgres
```

*(Replace `catalogdb` with `basketdb`, `identitydb`, or `kitchendb` to inspect the other databases)*

**Common `psql` Commands:**

- `\l` : List all databases
- `\c Catalogdb` : Connect to a specific database (e.g., connected to Catalogdb)
- `\dt` : List all tables in the current database
- `SELECT * FROM "Restaurants";` : Execute a SQL query (remember the `;` at the end)
- `\q` : Quit the terminal

### SQL Server (Ordering Database)

The Ordering service is backed by SQL Server (`orderdb`). To open an interactive `sqlcmd` session inside the container:

```bash
docker exec -it orderdb /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -C -d OrderDb
```

*(`-C` trusts the container's self-signed dev certificate. On older images the tools live under `/opt/mssql-tools/bin/sqlcmd`.)*

**Common `sqlcmd` Commands** (each batch is executed with `GO` on its own line):

- `SELECT name FROM sys.databases;` then `GO` : List all databases
- `SELECT * FROM sys.tables;` then `GO` : List all tables in the current database
- `SELECT TOP 10 * FROM Orders;` then `GO` : Query the Orders table
- `:exit` : Quit the terminal

### Redis (Distributed Cache)

To open the Redis interactive terminal on the distributed cache container (Redis is password-protected — the dev password is `redisdev`):

```bash
docker exec -it distributedcache redis-cli -a redisdev
```

**Common `redis-cli` Commands:**

- `PING` : Test the connection (expects `PONG`)
- `KEYS *` : List all keys currently stored
- `GET "basket:user_id:rest_id"` : Get the value stored under a specific key
- `FLUSHALL` : Clear everything in the cache universally
- `exit` : Quit the terminal

### RabbitMQ (Message Broker)

Integration events flow between services over RabbitMQ (via MassTransit and the transactional outbox). The broker runs in the `messagebroker` container using the `rabbitmq:3-management` image.

- **Management UI:** [http://localhost:15672](http://localhost:15672) — log in with the RabbitMQ credentials (`guest`/`guest` by default). Use it to inspect exchanges, queues, and message rates.
- **AMQP endpoint:** `localhost:5672` (used by the services).

To open the RabbitMQ admin CLI inside the container:

```bash
docker exec -it messagebroker rabbitmqctl list_queues name messages consumers
```

## Testing

The solution ships xUnit test suites that spin up real backing services via [Testcontainers](https://testcontainers.com/) (PostgreSQL / SQL Server and RabbitMQ), so Docker Desktop must be running to execute them.

Test projects: `Catalog.API.Tests`, `Kitchen.API.Tests`, `Identity.API.Tests`, and the Ordering suites (`Ordering.API.Tests`, `Ordering.Application.Tests`, `Ordering.Domain.Tests`, `Ordering.Infrastructure.Tests`).

Run the full suite from the solution root:

```bash
dotnet test
```

Or run a single project:

```bash
dotnet test Services/Kitchen/Kitchen.API.Tests
```

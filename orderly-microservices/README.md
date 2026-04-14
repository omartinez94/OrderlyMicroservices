# Orderly Microservices

A multi-tenant restaurant management system built with modern .NET utilizing the CQRS pattern, MediatR, Carter, and Marten (PostgreSQL).

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

### Option 2: Running Locally (Visual Studio or .NET CLI)

If you prefer to run the .NET processes locally on your machine for easier debugging:

#### Prerequisites for Local Execution

You still need the backing services.

To debug the code locally, you still need the background services (PostgreSQL, Redis, etc.) active. A highly recommended hybrid approach is to spin up **only the backing services** using Docker Compose:

```bash
docker-compose up catalogdb basketdb distributedcache -d
```

*(With this, your databases run via Docker and their ports are mapped automatically, allowing you to debug the .NET APIs natively).*

When you're finished, tear them down with:

```bash
docker-compose down
```

Or ensure you have local instances of PostgreSQL and Redis running on the mapped ports (`5433` for CatalogDB, `5434` for BasketDB, and `6379` for Redis).

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
```

## Useful Docker CLI Commands

While the background services are running via Docker, you can execute commands directly on the containers to inspect databases or test caches.

### PostgreSQL (Catalog & Basket Databases)

To open the Postgres interactive terminal (`psql`) inside the `catalogdb` container:

```bash
docker exec -it catalogdb psql -U postgres
```

*(Replace `catalogdb` with `basketdb` to inspect the basket database)*

**Common `psql` Commands:**

- `\l` : List all databases
- `\c Catalogdb` : Connect to a specific database (e.g., connected to Catalogdb)
- `\dt` : List all tables in the current database
- `SELECT * FROM "Restaurants";` : Execute a SQL query (remember the `;` at the end)
- `\q` : Quit the terminal

### Redis (Distributed Cache)

To open the Redis interactive terminal on the distributed cache container:

```bash
docker exec -it distributedcache redis-cli
```

**Common `redis-cli` Commands:**

- `PING` : Test the connection (expects `PONG`)
- `KEYS *` : List all keys currently stored
- `GET "basket:user_id:rest_id"` : Get the value stored under a specific key
- `FLUSHALL` : Clear everything in the cache universally
- `exit` : Quit the terminal

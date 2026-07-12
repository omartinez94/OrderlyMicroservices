# Dev MCP Server — Implementation Plan

> Scope: Plan for building `Orderly.DevMCP.Server`, a local-only Node.js service that implements the Model Context Protocol (MCP). This server acts as an "AI Developer Gateway," connecting AI coding assistants directly to the OrderlyMicroservices backend during the development of frontend clients (Web CRM and Mobile App).

---

## 0. Skill & documentation conventions

### 0.1 Skill mandate — `mcp-developer` (Node.js)
> **All implementation work on this plan MUST use modern Node.js and the official MCP SDKs.**
>
> The server will be built using TypeScript and the `@modelcontextprotocol/sdk`. It will connect to the local instances of the microservices' databases (PostgreSQL/Marten, Redis, SQLite) and/or their APIs.

### 0.2 Code-quality guard rails
- **TypeScript Strict Mode**: The project must use strict TypeScript configurations to ensure type safety, especially when defining tool schemas.
- **Local Development Only**: This server is explicitly for development environments. It will NOT be deployed to production.
- **Zod for Schemas**: Use `zod` alongside the MCP SDK to validate inputs and strongly type tool arguments.

---

## 1. Context

The `OrderlyMicroservices` solution provides a robust backend (Catalog, Basket, Ordering) built in .NET. However, building frontend clients (a Web CRM for restaurant management and a Mobile App) requires constant context-switching to understand API contracts, setup test data, and debug backend states.

Currently, there is no unified way for an AI assistant to interact directly with the running backend services to accelerate frontend development.

---

## 2. Goal

Build `Orderly.DevMCP.Server`, an MCP server that exposes the following capabilities to AI assistants:

1.  **API Discovery**: Tools to read OpenAPI/Swagger definitions from the running microservices.
2.  **Data Seeding**: Tools to inject test data directly into the databases (e.g., seeding a restaurant menu, creating fake orders).
3.  **State Inspection**: Tools to query the current state of a basket, an order, or ingredient availability.
4.  **Log Tracing**: Tools to pull recent application logs from Docker containers or local log files for quick debugging.

By exposing these tools, the AI can independently setup scenarios, verify contracts, and troubleshoot issues while writing frontend code.

---

## 3. Out of scope

-   **Production Deployment**: This is a DevEx (Developer Experience) tool only. It will not be packaged into the production Docker Compose setup.
-   **Direct Production Database Access**: The server should be configured to connect ONLY to local development databases (`localhost:5432`, `localhost:6379`).
-   **Replacing Backend Logic**: The MCP server should not implement business logic. It should either call the APIs or directly query/mutate the database strictly for seeding/inspection purposes.

---

## 4. Tech decisions

| Decision | Choice | Reason |
| :--- | :--- | :--- |
| **Runtime** | **Node.js (TypeScript)** | Best ecosystem for MCP tooling via the official `@modelcontextprotocol/sdk`. Fast to write and iterate. |
| **Database Access** | `pg` (PostgreSQL) + `ioredis` | To connect directly to the local Catalog/Basket databases for rapid seeding and inspection. |
| **HTTP Client** | Native `fetch` | To make requests to the running .NET APIs (e.g., to fetch Swagger schemas). |
| **Tool Input Validation** | `zod` | Standard schema definition library used in MCP TypeScript examples. |
| **Transport** | **HTTP / SSE (Server-Sent Events)** | Because the backend runs on a local server (`192.168.1.65`), the MCP server will expose an HTTP SSE endpoint (e.g., `http://192.168.1.65:8080/sse`) rather than `stdio`. This allows AI clients on other network machines to connect seamlessly. |

---

## 5. Folder layout

The server will live in a new directory at the root of the solution, distinct from the .NET microservices.

```text
OrderlyMicroservices/
  Orderly.DevMCP.Server/
    package.json
    tsconfig.json
    src/
      index.ts                 -- MCP Server initialization
      config/                  -- DB connection strings (defaulting to localhost)
      tools/
        api-discovery.ts       -- Tools for reading swagger docs
        data-seeding.ts        -- Tools for inserting mock data
        state-inspection.ts    -- Tools for querying current DB/Redis state
        log-tracing.ts         -- Tools for reading local container logs
      db/
        postgres-client.ts     -- Connection to local Marten/EF databases
        redis-client.ts        -- Connection to local Redis cache
```

---

## 6. Initial Tool Specification

The MCP server will register the following tools on startup:

### 6.1 API Discovery Tools
*   `get_api_schema(serviceName)`: Fetches and parses the swagger.json for a given service (`Catalog`, `Basket`, `Ordering`). Returns the endpoint paths and payload schemas.

### 6.2 Data Seeding Tools
*   `seed_test_menu(restaurantId)`: Injects a standard set of categories, items, and ingredients into the Catalog DB for a specific restaurant.
*   `create_mock_order(restaurantId, status)`: Creates a fake order in the Ordering DB/Basket DB to allow the UI to test different order states (e.g., Pending, Completed).

### 6.3 State Inspection Tools
*   `inspect_basket(basketId)`: Directly queries Redis to return the current state of a user's basket.
*   `get_recent_logs(serviceName)`: Fetches the last 50 log lines from a specific service's docker container for instant debugging.

---

## 7. Cross-Repository AI Communication

Since the frontend clients (Web CRM, Mobile App) live in separate repositories and run on developer workstations, while the backend and MCP server run on a local network server (e.g., `192.168.1.65`), communication flows over HTTP:

1.  **Server Startup**: The Node.js DevMCP server exposes an HTTP SSE endpoint (e.g., `http://192.168.1.65:8080/sse`).
2.  **Frontend AI Configuration**: When an AI agent (like Claude Desktop or Cursor) is opened in the Web CRM or Mobile App repository, it is configured with an `sse` MCP connection:
    ```json
    {
      "mcpServers": {
        "orderly-backend": {
          "type": "sse",
          "url": "http://192.168.1.65:8080/sse"
        }
      }
    }
    ```
3.  **Result**: The AI working on the frontend can securely request backend tools over the local network without needing access to the backend source code or direct database credentials.

---

## 8. Next Steps

1.  Initialize the Node.js project in `Orderly.DevMCP.Server`.
2.  Install `@modelcontextprotocol/sdk` (including the express/SSE transport packages), `zod`, `pg`, and `ioredis`.
3.  Implement the skeleton `index.ts` that starts the HTTP SSE server.
4.  Implement the first tool: `get_api_schema`.
5.  Test the connection locally using an MCP inspector or Claude Desktop configured for the SSE endpoint.

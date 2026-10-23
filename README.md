# AI Maintenance Agent

An AI-assisted maintenance request management API built with ASP.NET Core, PostgreSQL, Entity Framework Core, and Google Gemini.

The project explores how AI function calling can be integrated into a backend application while maintaining human oversight, data consistency, and reliable error handling.

## Overview

Users can describe property maintenance issues in natural language. Google Gemini interprets the request and proposes structured maintenance details through function calling.

Instead of immediately creating a maintenance request, the system stores a pending approval. A maintenance request is created only after explicit approval.

This design demonstrates how AI-generated actions can be controlled through backend validation, human approval, and database safeguards.

## Key Features

- **AI Function Calling:** Uses Google Gemini to interpret natural-language maintenance requests and propose structured actions.
- **Human-in-the-Loop Approval:** Requires explicit approval before creating maintenance requests.
- **Idempotency:** Prevents duplicate approval creation when the same request is retried with the same idempotency key.
- **Conflict Detection:** Rejects reuse of an idempotency key with different request content.
- **Concurrency Control:** Uses conditional atomic database updates to prevent duplicate approval processing.
- **Transactional Consistency:** Groups approval updates and maintenance request creation within a PostgreSQL transaction.
- **Error Handling:** Uses centralised exception handling, structured logging, and fallback responses for failures after approval creation.
- **Automated Testing:** Includes unit tests and HTTP integration tests using xUnit, WebApplicationFactory, and PostgreSQL Testcontainers.

## Tech Stack

| Area | Technologies |
|---|---|
| Backend | C#, ASP.NET Core Web API |
| Database | PostgreSQL |
| ORM | Entity Framework Core |
| AI Integration | Google Gemini API |
| Containers | Docker, Docker Compose |
| Testing | xUnit, WebApplicationFactory, Testcontainers |
| Observability | ASP.NET Core logging, structured logs |
| Version Control | Git, GitHub |

## Getting Started

### Prerequisites

- .NET SDK compatible with the project
- Docker Desktop
- PostgreSQL (provided through Docker Compose)
- Google Gemini API key

### 1. Clone the Repository

```bash
git clone https://github.com/YOUR_USERNAME/ai-maintenance-agent.git
cd ai-maintenance-agent
```

### 2. Configure PostgreSQL

Create a `.env` file in the repository root using `.env.example` as a template.

```dotenv
POSTGRES_PASSWORD=your-local-database-password
```

The `.env` file is excluded from Git and is used by Docker Compose to configure the PostgreSQL container.

Start PostgreSQL:

```bash
docker compose up -d
```

PostgreSQL is available locally on port `5433`.

**Note:** If the PostgreSQL volume already exists, changing `POSTGRES_PASSWORD` in `.env` will not update the existing database user's password. Update the PostgreSQL role password separately.

### 3. Configure ASP.NET Core User Secrets

Sensitive application settings are managed using ASP.NET Core User Secrets during local development.

Initialise User Secrets:

```bash
dotnet user-secrets init --project backend/MaintenanceAgent.Api
```

If User Secrets have already been initialised, this step can be skipped.

Set the PostgreSQL connection string:

```bash
dotnet user-secrets set "ConnectionStrings:MaintenanceDb" "Host=127.0.0.1;Port=5433;Database=maintenance_db;Username=maintenance_user;Password=YOUR_PASSWORD" --project backend/MaintenanceAgent.Api
```

Replace `YOUR_PASSWORD` with the same password configured in the Docker Compose `.env` file.

Configure the Gemini API key using the configuration key expected by `GeminiAgentService`:

```bash
dotnet user-secrets set "Gemini:ApiKey" "YOUR_GEMINI_API_KEY" --project backend/MaintenanceAgent.Api
```

This example assumes the application reads `Gemini:ApiKey`. If the service uses a different configuration key, use that key instead.

**Security:** Avoid exposing secrets in terminal output, command history, screenshots, or logs. ASP.NET Core User Secrets are intended for local development and are not encrypted.

### 4. Apply Database Migrations

```bash
dotnet ef database update --project backend/MaintenanceAgent.Api --startup-project backend/MaintenanceAgent.Api
```

Ensure the application is running with the `Development` environment so User Secrets are loaded.

### 5. Run the API

```bash
dotnet run --project backend/MaintenanceAgent.Api
```

Open the root endpoint at the address displayed in the terminal, for example:

```text
http://localhost:5247/
```

Expected response:

```json
{
  "application": "AI Maintenance Agent",
  "status": "Running"
}
```

This endpoint confirms that the API is running. It does not verify database connectivity.

### 6. Run Automated Tests

```bash
dotnet test backend/MaintenanceAgent.Api.Tests/MaintenanceAgent.Api.Tests.csproj
```

The integration tests use PostgreSQL Testcontainers and their own test database configuration. They do not require the local development database connection string to be valid.

Docker must be available for Testcontainers-based tests.

### Configuration and Security

The project separates general application settings from sensitive credentials.

| Configuration source | Purpose |
|---|---|
| `appsettings.json` | Non-sensitive application configuration |
| `.env` | Local PostgreSQL container password used by Docker Compose |
| ASP.NET Core User Secrets | Local API database connection string and Gemini API key |
| Environment variables / managed secrets | Production configuration |

The application retrieves the database connection string through ASP.NET Core's configuration system:

```csharp
builder.Configuration.GetConnectionString("MaintenanceDb")
```

No database passwords or API keys should be committed to the repository.

For production deployments, use environment variables or a managed secrets service instead of ASP.NET Core User Secrets.

## How It Works

1. A user submits a maintenance issue in natural language.
2. The backend validates the request and checks the property and idempotency key.
3. Gemini analyses the issue and may propose a `CreateMaintenanceRequest` function call.
4. The backend stores the proposed action as a `Pending` approval.
5. The user can approve or reject the proposal.
6. Approval creates a maintenance request within a database transaction.
7. Repeated or conflicting operations are handled through idempotency and concurrency controls.

**The AI proposes actions; the backend controls execution.**

## Architecture

The application separates AI interpretation, business operations, and data persistence.

- **AgentController:** Receives natural-language requests.
- **GeminiAgentService:** Integrates with Gemini and processes function calls.
- **MaintenanceApprovalsController:** Manages approval and rejection.
- **MaintenanceDbContext:** Handles PostgreSQL persistence through EF Core.
- **GlobalExceptionHandler:** Provides centralised handling for unexpected exceptions.

```mermaid
flowchart TD
    U["User / API Client"] --> A["AgentController"]
    A --> S["GeminiAgentService"]
    S --> G["Google Gemini API"]
    G -->|"Function Call Proposal"| S
    S --> DB[("PostgreSQL")]
    DB --> P["Pending Approval"]
    U --> C["MaintenanceApprovalsController"]
    C -->|"Approve / Reject"| DB
    C -->|"Approved"| M["MaintenanceRequest"]
    M --> DB
  ```

## API Endpoints

| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/agent/process` | Process a natural-language maintenance request |
| GET | `/api/maintenance-approvals/{id}` | Retrieve an approval |
| POST | `/api/maintenance-approvals/{id}/approve` | Approve a pending request |
| POST | `/api/maintenance-approvals/{id}/reject` | Reject a pending request |

### Examples
- POST /api/agent/process
  Content-Type: application/json 
  { 
    "propertyId": 1,
    "message": "There is water leaking from the bathroom ceiling.",
    "idempotencyKey": "maintenance-req-001"
  }
- POST /api/maintenance-approvals/1/approve
- POST /api/maintenance-approvals/1/reject

## Backend Configuration

The application is built with ASP.NET Core Web API and uses Entity Framework Core with PostgreSQL for data persistence.

Google Gemini is integrated through a typed `HttpClient`, while centralised exception handling provides consistent error responses.

### Application Setup

The application registers the following services:

- ASP.NET Core controllers for HTTP endpoints.
- An HTTP client for Google Gemini integration.
- Entity Framework Core with the Npgsql PostgreSQL provider.
- Centralised exception handling and Problem Details support.

The API uses ASP.NET Core's built-in dependency injection to manage these services.

## Reliability and Data Integrity

### Idempotency

The API uses an idempotency key and request-content hash to identify repeated requests.

- Same key and same content: return the existing approval.
- Same key and different content: return HTTP 409 Conflict.
- A database unique constraint protects against concurrent creation using the same key.

### Concurrency Control

Approval processing uses an atomic conditional update that changes an approval from `Pending` to `Approved` only when its current status is `Pending`.

This ensures that only one competing approval operation can successfully perform the state transition.

### Transactions

Approval state changes and maintenance request creation are performed within a PostgreSQL transaction.

If an operation fails before commit, the transaction is rolled back to preserve consistency.

### Human Oversight

AI-generated function calls do not directly execute maintenance operations. Proposed actions remain pending until approved.

## Testing

The project includes automated tests covering:

- Gemini function-call processing
- Idempotency reuse and conflict detection
- HTTP 400, 404, and 409 responses
- Concurrent approval requests
- Database transaction rollback
- Approval and rejection workflows
- Gemini API failures and fallback handling

Integration tests use PostgreSQL Testcontainers to exercise the application against a real PostgreSQL database.

## Project Purpose

This project was developed to explore practical AI-assisted backend engineering, with an emphasis on reliable API design, database integrity, safe AI tool execution, and automated testing.

It combines experience in enterprise application development with modern .NET, PostgreSQL, containerisation, and AI integration.
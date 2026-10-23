# Backend API

This folder contains the ASP.NET Core Web API and backend test projects for AI Maintenance Agent.

## Projects

- MaintenanceAgent.Api: application API
- MaintenanceAgent.Api.Tests: backend automated test suite

## API Highlights

- Agent processing endpoint with idempotency support.
- Approval workflow endpoints for approve/reject/list.
- Property endpoints for creating and reading property records.
- Maintenance request endpoints for reading and creating records.

## Local Run

From repository root:

```bash
dotnet run --project backend/MaintenanceAgent.Api
```

Default local URL from launch settings: http://localhost:5247

## Required Local Configuration

- ConnectionStrings:MaintenanceDb
- Gemini:ApiKey
- Gemini:Model

Use user secrets for local development to avoid committing secrets.

## Database and Migrations

Apply migrations:

```bash
dotnet ef database update --project backend/MaintenanceAgent.Api --startup-project backend/MaintenanceAgent.Api
```

## Test Commands

Run all backend tests:

```bash
dotnet test backend/MaintenanceAgent.Api.Tests/MaintenanceAgent.Api.Tests.csproj
```

## Reliability Mechanisms Implemented

- Idempotency key validation and request hash comparison.
- Unique index on approval idempotency key.
- Conditional status transitions for concurrency-safe approval handling.
- Transaction-wrapped approval execution when creating maintenance requests.
- PostgreSQL integration tests with Testcontainers.

For architecture and full project setup, see ../README.md.
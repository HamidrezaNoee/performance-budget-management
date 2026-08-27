# ADR-001: Modular Monolith with Clean Architecture

Status: Accepted

## Context

PBM must start with roughly five users but scale toward hundreds, support multiple licensed companies, SQL Server/on-premise deployment, rich multidimensional budgeting, actuals, KPI, forecasting, reporting, future workflow/BPMN and AI integrations. The business model is still evolving, so a premature microservice split would increase operational cost and make domain changes slower.

## Decision

Use an ASP.NET Core 10 modular monolith with Clean Architecture boundaries:

- `PBM.Domain`: enterprise rules and entities.
- `PBM.Application`: use-case contracts, safe formula engine and application abstractions.
- `PBM.Infrastructure`: SQL Server/EF Core persistence and service implementations.
- `PBM.Api`: versioned HTTP API, authentication, exception handling and composition root.
- `PBM.Web`: React/TypeScript Persian-first RTL user interface.

Modules will communicate through application contracts. When a bounded context becomes independently scalable or operationally separate, it can be extracted behind the same contracts.

## Consequences

- Faster first production release and simpler on-premise deployment.
- Clean path to future services for AI/forecasting, notification, workflow and data integration.
- Database boundaries and module ownership must remain disciplined to avoid a big-ball-of-mud monolith.

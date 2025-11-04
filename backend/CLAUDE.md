# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is an ASP.NET Core 9.0 backend application for managing Stripe subscriptions. The project follows Clean Architecture principles with a layered structure organized around Domain-Driven Design concepts.

## Solution Structure

The solution is organized into three main layers:

**0. BuildingBlocks** (Cross-cutting concerns)
- `BuildingBlocks.SharedKernel`: Base entity classes, value objects, and domain primitives
- `BuildingBlocks.Utils`: Utility extensions (hashing, string manipulation, date/time helpers)

**1. Core** (Domain Layer)
- `A2I.Core`: Domain entities (Customer, Plan, Subscription, Invoice), domain errors, and business rules
  - Uses FluentResults for error handling
  - Contains no infrastructure dependencies

**2. Application** (Application Layer)
- `A2I.Application`: Application services and abstractions
  - Business logic orchestration for subscriptions, customers, and invoices
  - Defines interfaces for Stripe services and caching
  - Contains DTOs and API models

**3. Infrastructure** (Infrastructure Layer)
- `A2I.Infrastructure.Database`: EF Core DbContext, migrations, and entity configurations
  - Uses PostgreSQL with Npgsql
  - Includes soft-delete and audit trail support via `ISoftDelete` and `IAuditableEntity` interfaces
- `A2I.Infrastructure.Identity`: ASP.NET Core Identity with JWT authentication
  - Separate schema ("identity") for user management
  - Custom JWT service with key rotation (Quartz job runs at 2 AM every 30 days)
  - JWKS endpoint for public key distribution
- `A2I.Infrastructure`: Stripe service implementations
- `A2I.Infrastructure.Caching`: Redis-based caching service

**Presentation**
- `A2I.WebAPI`: Minimal API endpoints, middleware, and application bootstrapping
  - Uses endpoint groups organized by feature (Auth, Subscriptions, Customers, Invoices)
  - No traditional controllers; endpoints are defined in `Endpoints/*` folders

## Database Architecture

The application uses **two separate PostgreSQL databases**:

1. **Application Database** (`ApplicationDbContext`):
   - Default schema: `public`
   - Tables: customers, plans, subscriptions, invoices, stripe_webhook_events
   - Includes automatic soft-delete query filters
   - Audit fields (CreatedAt, UpdatedAt) auto-populated via `SaveChangesAsync` override

2. **Identity Database** (`AppIdentityDbContext`):
   - Schema: `identity`
   - Tables: users, roles, user_roles, user_claims, etc.
   - Managed by ASP.NET Core Identity

Both contexts use snake_case naming convention via `UseSnakeCaseNamingConvention()`.

## Development Commands

### Build and Run
```bash
# Build the entire solution
dotnet build

# Run the WebAPI project
dotnet run --project src/A2I.WebAPI/A2I.WebAPI.csproj

# Watch mode (auto-restart on changes)
dotnet watch --project src/A2I.WebAPI/A2I.WebAPI.csproj
```

### Database Migrations

**Application Database:**
```bash
# Add new migration
dotnet ef migrations add <MigrationName> --project src/A2I.Infrastructure.Database --startup-project src/A2I.WebAPI --context ApplicationDbContext

# Update database
dotnet ef database update --project src/A2I.Infrastructure.Database --startup-project src/A2I.WebAPI --context ApplicationDbContext

# Remove last migration
dotnet ef migrations remove --project src/A2I.Infrastructure.Database --startup-project src/A2I.WebAPI --context ApplicationDbContext
```

**Identity Database:**
```bash
# Add new migration
dotnet ef migrations add <MigrationName> --project src/A2I.Infrastructure.Identity --startup-project src/A2I.WebAPI --context AppIdentityDbContext

# Update database
dotnet ef database update --project src/A2I.Infrastructure.Identity --startup-project src/A2I.WebAPI --context AppIdentityDbContext

# Remove last migration
dotnet ef migrations remove --project src/A2I.Infrastructure.Identity --startup-project src/A2I.WebAPI --context AppIdentityDbContext
```

### Clean and Restore
```bash
# Clean build artifacts
dotnet clean

# Restore NuGet packages
dotnet restore
```

## Key Architectural Patterns

### Minimal API Endpoints
Endpoints are organized by feature in `A2I.WebAPI/Endpoints/`:
- Each feature has its own folder (e.g., `Subscriptions/`, `Customers/`)
- Endpoints are registered via extension methods on `RouteGroupBuilder`
- Example: `SubscriptionEndpoints.MapSubscriptionEndpoints()` registers all subscription routes

### Dependency Injection Registration
Service registration is centralized in `A2I.WebAPI/Extensions/ServiceCollectionExtensions.cs`:
- `AddDatabaseServices()`: Configures EF Core with connection pooling
- `AddIdentityServices()`: Sets up JWT authentication with key rotation
- `AddStripeServices()`: Registers Stripe services and webhook handlers
- `AddBackgroundJobServices()`: Configures Quartz for scheduled jobs
- `AddCacheService()`: Configures Redis caching

### Error Handling
- Domain layer uses `FluentResults.Result<T>` for error handling
- API responses use `ApiResponse<T>` wrapper with consistent error codes
- Error codes are centralized in `A2I.Application.Common.ApiErrorCodes`
- Global exception middleware in `GlobalExceptionMiddleware.cs`

### Background Jobs
- **Hangfire**: Used for webhook processing (queues: "stripe-webhooks", "emails", "default")
  - Dashboard available at `/hangfire` (development)
  - PostgreSQL storage with 15-second polling interval
- **Quartz**: Used for scheduled jobs like JWT key rotation
  - `KeyRotationJob` runs at 2 AM every 30 days

### Webhook Event Processing
Stripe webhooks are handled asynchronously:
1. Events stored in `StripeWebhookEvent` table for idempotency
2. Dispatched to specific handlers via `IWebhookEventDispatcher`
3. Handlers implement `IWebhookEventHandler` (e.g., `CheckoutSessionCompletedHandler`)

### Caching
- Redis-based caching via `ICacheService` abstraction
- Configuration: `localhost:6379`, 30-minute default expiration
- Used for frequently accessed data and rate limiting

## Important Conventions

- **Entity IDs**: Auto-generated GUIDs via `IdGenHelper.NewGuidId()` in `SaveChangesAsync`
- **Soft Delete**: Entities implementing `ISoftDelete` are automatically filtered from queries
- **Audit Trails**: Entities implementing `IAuditableEntity` get automatic timestamp management
- **Snake Case**: Database uses snake_case; C# uses PascalCase
- **API Versioning**: All business endpoints under `/api/v1/`
- **Rate Limiting**: Fixed and sliding window limiters configured in `Program.cs`

## Configuration

Key configuration sections in `appsettings.json`:
- `Database`: Connection string and pooling settings
- `Stripe`: API keys and webhook secrets (not committed to repo)
- `Logging`: Log levels for EF Core, Hangfire, and application

## Security

- JWT tokens signed with RSA keys stored in `src/A2I.WebAPI/keys/` (not committed)
- Keys rotated automatically every 30 days
- JWKS endpoint at `/.well-known/jwks.json` for token verification
- Password requirements: 6+ chars, upper/lower case, digits
- Account lockout: 5 failed attempts = 5-minute lockout

## Development Notes

- Development environment enables Scalar API documentation at `/scalar/v1`
- OpenAPI spec available at `/openapi/v1.json`
- Health check endpoint at `/health` (excluded from API docs)
- Test endpoints available only in Development mode at `/api/v1/test`
- Weather forecast demo endpoint at `/weatherforecast` (remove in production)

## Dependencies

Key external packages:
- Stripe.net v49.0.0 (Stripe API client)
- EF Core 9.0 with Npgsql for PostgreSQL
- Hangfire with PostgreSQL storage
- Quartz.NET for scheduled jobs
- StackExchange.Redis for caching
- ASP.NET Core Identity
- Scalar for API documentation
# Project Instructions

## Current project

- This repository contains an ASP.NET Core Web API solution at `wis-fel-api/wis-fel-api.slnx`. The app is `wis-fel-api/fel-api` and targets .NET 10.
- The app registers controllers, OpenAPI, EF Core SQL Server, local JWT auth for platform/Agents, external JWT validation for clients, rate limiting, and domain services. Worker/auth endpoints are under `fel-api/Controllers`; Agent, subscription, catalog, identity, and document entities are under `fel-api/Domain`.
- `/auth/login` and `/auth/refresh` are for platform administrators only. `GET /auth/me` accepts the configured external client JWT scheme. `/agent/handshake` validates a provisioned Agent secret and issues worker tokens; `/agent/token/refresh`, `/agent/heartbeat`, and `/agent/documents` are worker routes. `/platform/agents` provisions/revokes Agents and requires a platform-admin JWT.
- `ClientJwt:Authority` and `ClientJwt:Audience` must be configured for the portal's identity provider before client `/auth/me` tokens can validate. Client JWT `iss`+`sub` must map to `ExternalUserIdentities`.
- JWT signing key is required via User Secrets in Development or deployment secret configuration in Production. Bootstrap the first platform administrator with `dotnet run --project wis-fel-api/fel-api/fel-api.csproj --launch-profile http -- --bootstrap-platform-admin <email>`; the password is prompted without echo. There is no public admin-registration route.
- The physical document table is `Documents` and delivery history is `DocumentDeliveryAttempts`. No certifier request mappers are registered yet: `/agent/documents` stores the incoming JSON but returns `503` without sending it until a mapper exists for the configured certifier, country, and format.
- OpenAPI UI and its document are enabled only in Development. Preserve that boundary unless the deployment requirements explicitly change.

## Product direction and open requirements

The API receives JSON submissions from worker services running on multiple PCs. It uses portal-managed tenant, company, country, and certifier settings to select the certifier-specific JSON or XML request and send it to a configured certifier endpoint. The agreed submission flow is synchronous.

- Treat the incoming payload shape as undecided. Do not invent a final schema, required fields, version policy, or unknown-field behavior; make those decisions explicit and confirm them before they constrain clients.
- Client and tenant are the same concept. Each company belongs to one tenant, has one country, and its branches inherit that country.
- A user identity is globally unique by normalized email and may have platform-administrator membership and tenant memberships. Tenant roles grant access to explicitly assigned companies; preserve tenant boundaries in queries and foreign keys.
- Subscription plans define maximum companies, branches (total per tenant), users, and a configurable pre-expiration grace period. Subscription state is derived from subscription dates and plan settings; reject resource creation when its quota is reached or the subscription is suspended.
- A company has at most one certifier assignment. Certifier configuration is unique by certifier and country because the same certifier can behave differently by country.
- Persist incoming document payloads and outbound delivery attempts in SQL Server without inventing payload fields or status semantics. The exact payload contract and retention policy remain undecided; do not add automatic document deletion until retention is defined.
- The documents endpoint accepts optional `Idempotency-Key`; absent keys are generated server-side. A repeated supplied key in the same tenant returns `409`; it does not replay a prior response. Do not promise ordering, retry, deduplication, or delivery guarantees beyond implemented and tested behavior.
- Agents are provisioned per company and tenant. `MaxAgents` is a distinct subscription-plan quota. Handshake/document access is allowed in `Active` and `Grace`, and denied for suspended subscriptions or inactive Agents.
- SQL Server is the configured persistence provider, and the initial database is `FEL_API`. Do not assume credential storage for certifier integrations beyond the established database/configuration boundaries.

## Implementation guidance

- Keep HTTP controllers focused on transport concerns. Put validation, routing decisions, country/certifier mappings, and outbound delivery behind testable application services.
- Keep tenant ownership explicit in relationships and queries. Use composite foreign keys where needed so roles, company grants, branches, and submissions cannot cross tenants.
- Keep certifier- and country-specific request construction isolated so adding a format or integration does not spread conditional logic through controllers. Serialize to the exact JSON/XML contract required by the selected certifier.
- Resolve the destination and format from validated portal-managed configuration; do not let worker payloads select arbitrary outbound URLs or override trusted integration settings.
- Worker JWT authentication, hashed Agent credentials/refresh tokens, and configurable rate-limit policies are implemented. Continue to treat client identity-provider setup, outbound DNS/private-network allow-listing, retry safety, idempotency replay semantics, retention, and sensitive-data handling as requirements to define; never log credentials, certificates, or full submitted documents by default.
- Add focused tests for request validation, configuration-based routing, JSON/XML mapping, and delivery failure behavior as those capabilities are introduced. Do not claim delivery guarantees that tests and implementation do not establish.
- Follow the existing ASP.NET Core and nullable-reference-type conventions. Keep package versions centrally managed in `wis-fel-api/Directory.Packages.props`.
- Configure SQL Server through `ConnectionStrings:FelApi`; use User Secrets for local Development and environment/deployment secret configuration for Production. Never commit connection credentials.
- JWT settings live under `Jwt`; keep `Jwt:SigningKey` out of tracked settings. Rate-limit defaults live under `RateLimiting` in `appsettings.json` and can be overridden per environment. Tune initial values against real traffic before production.
- Create or update the database only through EF Core migrations. Do not call `EnsureCreated` or apply migrations automatically at application startup. Keep the initial model free of placeholder business entities; EF's `__EFMigrationsHistory` table is expected.
- Run production migrations as an explicit deployment step using an identity authorized to create/update the database, rather than granting those permissions to the API's normal runtime identity.

## Build and run

Run from the repository root:

```sh
dotnet restore wis-fel-api/wis-fel-api.slnx
dotnet tool restore
dotnet build wis-fel-api/wis-fel-api.slnx
dotnet run --project wis-fel-api/fel-api/fel-api.csproj --launch-profile http
```

Add and apply migrations from the repository root, targeting the API project:

```sh
dotnet tool run dotnet-ef migrations add <MigrationName> --project wis-fel-api/fel-api/fel-api.csproj --startup-project wis-fel-api/fel-api/fel-api.csproj
dotnet tool run dotnet-ef database update --project wis-fel-api/fel-api/fel-api.csproj --startup-project wis-fel-api/fel-api/fel-api.csproj
```

Run focused tests with `dotnet test wis-fel-api/wis-fel-api.slnx`. The test project is `wis-fel-api/fel-api.Tests`.
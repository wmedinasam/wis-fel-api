# Project Instructions

## Current project

- This repository contains an ASP.NET Core Web API solution at `wis-fel-api/wis-fel-api.slnx`. The app is `wis-fel-api/fel-api` and targets .NET 10.
- The app registers controllers, OpenAPI, and an EF Core SQL Server `FelApiDbContext`. There are no implemented endpoints, business services, business entities, authentication schemes, or certifier integrations yet.
- OpenAPI UI and its document are enabled only in Development. Preserve that boundary unless the deployment requirements explicitly change.

## Product direction and open requirements

The intended API receives JSON submissions from worker services running on multiple PCs. It will use country and certifier settings managed through a web portal to build the certifier-specific JSON or XML request and send it to a configured certifier endpoint.

- Treat the incoming payload shape as undecided. Do not invent a final schema, required fields, version policy, or unknown-field behavior; make those decisions explicit and confirm them before they constrain clients.
- Do not assume whether submission is synchronous or queued, or promise ordering, retry, deduplication, or delivery guarantees without an agreed requirement.
- SQL Server is the configured persistence provider, and the initial database is `FEL_API`. Do not assume a tenancy model, portal-configuration schema, or credential-storage mechanism beyond the established connection-string configuration.

## Implementation guidance

- Keep HTTP controllers focused on transport concerns. Put validation, routing decisions, country/certifier mappings, and outbound delivery behind testable application services.
- Keep certifier- and country-specific request construction isolated so adding a format or integration does not spread conditional logic through controllers. Serialize to the exact JSON/XML contract required by the selected certifier.
- Resolve the destination and format from validated portal-managed configuration; do not let worker payloads select arbitrary outbound URLs or override trusted integration settings.
- Treat worker authentication, outbound endpoint validation, timeouts, retry safety, idempotency, and sensitive-data handling as requirements to define and implement, not as existing capabilities. Never log credentials, certificates, or full submitted documents by default.
- Add focused tests for request validation, configuration-based routing, JSON/XML mapping, and delivery failure behavior as those capabilities are introduced. Do not claim delivery guarantees that tests and implementation do not establish.
- Follow the existing ASP.NET Core and nullable-reference-type conventions. Keep package versions centrally managed in `wis-fel-api/Directory.Packages.props`.
- Configure SQL Server through `ConnectionStrings:FelApi`; use User Secrets for local Development and environment/deployment secret configuration for Production. Never commit connection credentials.
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

There is currently no test project. When one is added, run its focused tests and the solution build for changes affecting shared behavior.
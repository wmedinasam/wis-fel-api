# Project Instructions

## Current project

- This repository contains an ASP.NET Core Web API solution at `wis-fel-api/wis-fel-api.slnx`. The app is `wis-fel-api/fel-api` and targets .NET 10.
- The current app only registers controllers and OpenAPI; there are no implemented endpoints, business services, persistence, authentication schemes, or certifier integrations yet.
- OpenAPI UI and its document are enabled only in Development. Preserve that boundary unless the deployment requirements explicitly change.

## Product direction and open requirements

The intended API receives JSON submissions from worker services running on multiple PCs. It will use country and certifier settings managed through a web portal to build the certifier-specific JSON or XML request and send it to a configured certifier endpoint.

- Treat the incoming payload shape as undecided. Do not invent a final schema, required fields, version policy, or unknown-field behavior; make those decisions explicit and confirm them before they constrain clients.
- Do not assume whether submission is synchronous or queued, or promise ordering, retry, deduplication, or delivery guarantees without an agreed requirement.
- Do not assume a database, tenancy model, configuration ownership model, or credential-storage mechanism that the repository does not yet implement.

## Implementation guidance

- Keep HTTP controllers focused on transport concerns. Put validation, routing decisions, country/certifier mappings, and outbound delivery behind testable application services.
- Keep certifier- and country-specific request construction isolated so adding a format or integration does not spread conditional logic through controllers. Serialize to the exact JSON/XML contract required by the selected certifier.
- Resolve the destination and format from validated portal-managed configuration; do not let worker payloads select arbitrary outbound URLs or override trusted integration settings.
- Treat worker authentication, outbound endpoint validation, timeouts, retry safety, idempotency, and sensitive-data handling as requirements to define and implement, not as existing capabilities. Never log credentials, certificates, or full submitted documents by default.
- Add focused tests for request validation, configuration-based routing, JSON/XML mapping, and delivery failure behavior as those capabilities are introduced. Do not claim delivery guarantees that tests and implementation do not establish.
- Follow the existing ASP.NET Core and nullable-reference-type conventions. Keep package versions centrally managed in `wis-fel-api/Directory.Packages.props`.

## Build and run

Run from the repository root:

```sh
dotnet restore wis-fel-api/wis-fel-api.slnx
dotnet build wis-fel-api/wis-fel-api.slnx
dotnet run --project wis-fel-api/fel-api/fel-api.csproj --launch-profile http
```

There is currently no test project. When one is added, run its focused tests and the solution build for changes affecting shared behavior.
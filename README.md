# FEL API: Plataforma de Firma Electrónica

API multi-tenant para la plataforma de facturación electrónica (FEL). Gestiona la autenticación de administradores de plataforma, el registro y la licencia de los **agentes** (workers locales que envían documentos) y la recepción de documentos que se reenvían a los certificadores.

> Estado: en desarrollo. Parte del diseño está descrito en [`AGENTS.md`](AGENTS.md) y todavía no está implementada. Ver [Estado actual y limitaciones](#estado-actual-y-limitaciones).

---

## Tabla de contenidos

- [Tecnologías](#tecnologías)
- [Arquitectura](#arquitectura)
- [Modelo de dominio](#modelo-de-dominio)
- [Autenticación y autorización](#autenticación-y-autorización)
- [Requisitos previos](#requisitos-previos)
- [Inicio rápido](#inicio-rápido)
- [Probar en Scalar](#probar-en-scalar)
- [Endpoints y ejemplos](#endpoints-y-ejemplos)
- [Configuración](#configuración)
- [Migraciones de base de datos](#migraciones-de-base-de-datos)
- [Tests](#tests)
- [Estado actual y limitaciones](#estado-actual-y-limitaciones)
- [Contribuir](#contribuir)

---

## Tecnologías

| Componente | Tecnología / versión |
|---|---|
| Framework | .NET 10 (`net10.0`), ASP.NET Core Web API con controllers |
| ORM | Entity Framework Core 10.0.12 |
| Base de datos | SQL Server (`Microsoft.EntityFrameworkCore.SqlServer`) |
| Autenticación | `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12 (HS256) |
| Hash de contraseñas | `PasswordHasher<UserAccount>` de `Microsoft.AspNetCore.Identity` |
| Documentación API | `Microsoft.AspNetCore.OpenApi` 10.0.12 + `Scalar.AspNetCore` 2.18.0 |
| Rate limiting | `Microsoft.AspNetCore.RateLimiting` (ventanas fijas) |
| Tests | xUnit 2.9.3, `Microsoft.AspNetCore.Mvc.Testing` 10.0.12, EF Core InMemory |

Gestión central de versiones de paquetes con `wis-fel-api/Directory.Packages.props`.

---

## Arquitectura

### Estructura de la solución

```
wis-fel-api/                      Raíz del repositorio
├── README.md
├── AGENTS.md                     Plan de diseño de la plataforma
├── .config/dotnet-tools.json     dotnet-ef 10.0.12 (herramienta local)
└── wis-fel-api/                  Solución
    ├── wis-fel-api.slnx
    ├── Directory.Packages.props  Versiones centrales de paquetes
    ├── fel-api/                  Web API (namespace fel_api)
    │   ├── Program.cs            DI, autenticación, rate limiting, pipeline y modo bootstrap
    │   ├── Controllers/          AuthController, AgentController, PlatformAgentsController
    │   ├── Auth/                 Emisión de JWT, opciones JWT, hashing de tokens
    │   ├── RateLimiting/         Opciones de rate limiting
    │   ├── Services/
    │   │   ├── Auth/             Login, refresh tokens, perfil cliente, bootstrap admin
    │   │   ├── Agents/           Provisionamiento, handshake, heartbeat, licencia
    │   │   ├── Catalog/          Resolución de configuración de certificador
    │   │   ├── Documents/        Envío de documentos al certificador
    │   │   └── Subscriptions/    Validación de cuotas del plan
    │   ├── Domain/               Entidades y reglas (Agents, Audit, Catalog, Documents,
    │   │                         Identity, Subscriptions, Tenants)
    │   ├── Data/                 FelApiDbContext y migraciones
    │   └── Properties/launchSettings.json
    └── fel-api.Tests/            Tests xUnit
```

### Flujo por capas

```
 HTTP ──► Controllers ──► Services ──► Domain / EF Core ──► SQL Server
           (validación    (reglas de     (entidades,
            de entrada,    negocio,       DbContext)
            políticas)     licencia,
                           cuotas,
                           tokens)
                              │
                              └──► Certificador externo (HttpClient "Certifier")
```

### Flujo de un documento

```
Agente (worker)                 FEL API                              Certificador
     │ POST /agent/documents        │                                     │
     │ (JWT de agente)              │ 1. Valida agente y licencia         │
     │─────────────────────────────►│ 2. Verifica la sucursal             │
     │                              │ 3. Idempotencia (Idempotency-Key)   │
     │                              │ 4. Resuelve certificador de empresa │
     │                              │ 5. Guarda DocumentSubmission        │
     │                              │ 6. Mapper ──► POST al certificador ►│
     │                              │ 7. Guarda DocumentDeliveryAttempt   │
     │◄─────────────────────────────│ 200 / 502 / 504 / 503 / 409         │
```

El paso 6 requiere un `ICertifierRequestMapper` registrado. Hoy no hay ninguno, así que el documento se guarda y la respuesta es 503 (ver [Estado actual](#estado-actual-y-limitaciones)).

---

## Modelo de dominio

Los datos se aíslan por tenant en la capa de aplicación, usando las columnas `tenant_id` y `company_id` y los claims del token. No hay Row Level Security: la base es SQL Server.

- **Tenant**: cliente de la plataforma. Tiene empresas, usuarios, roles y una suscripción.
- **Company**: empresa del tenant con `CountryCode`, `TaxId`, `CurrencyCode` y `Timezone`. El nombre es único por tenant.
- **Branch**: sucursal de una empresa. El nombre es único por empresa.
- **Identidad**:
  - `UserAccount`, `ExternalUserIdentity` (issuer + subject de un IdP externo), `PlatformAdministrator`.
  - `TenantMembership`, `TenantRole`, `TenantUserRole`, `TenantRoleCompany`: roles por tenant, opcionalmente limitados a ciertas empresas.
  - `ActivationCode`: modelado pero sin uso todavía.
- **Suscripciones**:
  - `SubscriptionPlan` define `BillingCycle` (Monthly, Annual, Lifetime), límites (`MaxCompanies`, `MaxBranches`, `MaxUsers`, `MaxAgents`), `GracePeriodDays` y `AllowedCountries`.
  - `TenantSubscription` tiene `ExpiresAtUtc` e `IsCurrent`. Hay un índice único filtrado por `TenantId`.
  - Estados calculados por `SubscriptionStateCalculator`:
    - `Active`: antes de `ExpiresAtUtc - GracePeriodDays`.
    - `Grace`: desde ese punto hasta la expiración.
    - `Suspended`: en o después de la expiración.
- **Agentes**:
  - `Agent` tiene `CredentialHash`, datos de la máquina, `LastHandshakeAtUtc`, `LastHeartbeatAtUtc` y `WorkerVersion`.
  - `PendingAgentCommand` guarda comandos con lease (`LeaseExpiresAtUtc`) y `AcknowledgedAtUtc`. Todavía no hay código que los cree.
  - `RefreshToken` guarda solo el hash del token, con `FamilyId` y `ReplacedByTokenHash`.
- **Catálogo de certificadores**:
  - `Certifier` (por ejemplo `INFILE`) y `CertifierCountryConfiguration` (país, `EndpointUrl` HTTPS, formato Json o Xml).
  - `CompanyCertifierAssignment`: una asignación por empresa, con credenciales cifradas y `UseProduction`.
- **Documentos**:
  - `DocumentSubmission` guarda el payload original, la `IdempotencyKey` (única por tenant) y los ids de agente, sucursal y empresa.
  - `DocumentDeliveryAttempt` registra la petición, la respuesta, el código HTTP y si fue exitosa. No hay reintentos.
- **AuditLog**: modelado, pero nadie escribe en él todavía.

---

## Autenticación y autorización

Hay dos esquemas JWT:

| Esquema | Uso | Emisor / audiencia |
|---|---|---|
| `LocalJwt` | Tokens emitidos por esta API (HS256) | `Jwt:Issuer` / `PlatformAudience` o `AgentAudience` |
| `ClientJwt` | IdP externo, solo para `GET /auth/me` | `ClientJwt:Authority` / `ClientJwt:Audience` |

Políticas:

- **`PlatformAdministrator`**: `LocalJwt` con `principal_type=platform_admin`.
- **`Agent`**: `LocalJwt` con `principal_type=agent` y los claims `agent_id`, `tenant_id`, `company_id`.

El tenant y la empresa siempre salen del token, nunca del cuerpo de la petición.

### Vida de los tokens

- Access token: 15 minutos (plataforma y agente).
- Refresh token: 7 días (plataforma) y 30 días (agente).
- Cada refresh revoca el token anterior. Si se reutiliza un refresh token ya rotado, se revoca toda la familia.
- Un token de plataforma no sirve en endpoints de agente, y viceversa.
- Al refrescar un agente se vuelve a validar la licencia del tenant.

### Secretos

- Los secretos de agente son 32 bytes aleatorios en Base64Url. Se guarda el hash SHA-256 (`Auth/TokenHashing.cs`) y la comparación es de tiempo constante.
- El secreto se muestra una sola vez, al crear el agente.

### Licencia

`AgentLicenseValidator` exige una suscripción `IsCurrent` con plan activo y estado distinto de `Suspended`. Si no se cumple, los endpoints de agente responden 403.

### Rate limiting

Ventanas fijas, cola 0, respuesta 429 con `Retry-After`. Particiones por IP (login y refresh de plataforma, handshake y refresh de agente), por `sub` (`/auth/me`), por `agent_id` (heartbeat y documentos) y por admin (gestión de agentes).

---

## Requisitos previos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server (local, Docker o remoto)
- `openssl` para generar la clave de firma
- Opcional: `dotnet-ef` (se instala con `dotnet tool restore`)

---

## Inicio rápido

```bash
# 1. Clonar el repositorio
git clone <url-del-repositorio> wis-fel-api
cd wis-fel-api

# 2. Restaurar herramientas locales (dotnet-ef)
dotnet tool restore

# 3. Configurar secretos (no van en appsettings.json)
dotnet user-secrets set "ConnectionStrings:FelApi" \
  "Server=localhost;Database=FelApi;User Id=sa;Password=<tu-password>;TrustServerCertificate=True" \
  --project wis-fel-api/fel-api

dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 64)" \
  --project wis-fel-api/fel-api

# 4. Crear la base de datos y aplicar migraciones
dotnet ef database update --project wis-fel-api/fel-api

# 5. Crear el primer administrador de plataforma (pide la contraseña sin eco)
dotnet run --project wis-fel-api/fel-api -- --bootstrap-platform-admin admin@ejemplo.com

# 6. Levantar la API
dotnet run --project wis-fel-api/fel-api
```

Notas:

- `Jwt:SigningKey` debe ser base64 de al menos 32 bytes. Si falta, la aplicación no arranca.
- El bootstrap requiere una contraseña de al menos 12 caracteres y solo funciona si no existe ningún administrador de plataforma. Termina sin levantar el servidor web.
- Las variables de entorno sirven igual que los user-secrets, usando `__` como separador: `ConnectionStrings__FelApi`, `Jwt__SigningKey`.

Con el perfil `http` la API queda en `http://localhost:5070`. El perfil `https` usa `https://localhost:7168`.

---

## Probar en Scalar

Scalar solo está disponible en el entorno `Development`, que es el valor de `launchSettings.json`.

1. Levanta la API con `dotnet run --project wis-fel-api/fel-api`.
2. Abre `http://localhost:5070/scalar/v1`. El documento OpenAPI está en `http://localhost:5070/openapi/v1.json`.
3. Para endpoints protegidos:
   1. Ejecuta `POST /auth/login` con tu administrador de plataforma.
   2. Copia `accessToken` de la respuesta.
   3. En Scalar, configura la autenticación Bearer con ese token.

> El documento OpenAPI actual no declara un esquema de seguridad Bearer. Por eso Scalar no aplica el token automáticamente; hay que pegarlo a mano en cada petición protegida.

---

## Endpoints y ejemplos

Las rutas no tienen prefijo `/api`. Los errores siguen el formato `ProblemDetails`.

### Autenticación de plataforma: `/auth`

**Login** (anónimo, `platform-login`, 10 peticiones por minuto por IP):

```bash
curl -X POST http://localhost:5070/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@ejemplo.com","password":"<contraseña-de-12-o-más>"}'
```

Respuesta 200:

```json
{
  "accessToken": "eyJhbGciOi...",
  "tokenType": "Bearer",
  "accessTokenExpiresAtUtc": "2026-10-19T12:15:00Z",
  "refreshToken": "x7Qk...",
  "refreshTokenExpiresAtUtc": "2026-10-26T12:00:00Z"
}
```

**Refresh** (anónimo, `platform-refresh`):

```bash
curl -X POST http://localhost:5070/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"<refreshToken>"}'
```

**Perfil** (`ClientJwt`, requiere `ClientJwt:Authority` y `ClientJwt:Audience`):

```bash
curl http://localhost:5070/auth/me -H "Authorization: Bearer <token-del-idp>"
```

Devuelve `{id, email, displayName, tenants:[{id, name, roles:[{id, name, companies:[{id, name}]}]}]}`.

### Gestión de agentes (plataforma): `/platform/agents`

Requiere política `PlatformAdministrator`.

**Crear agente**:

```bash
curl -X POST http://localhost:5070/platform/agents \
  -H "Authorization: Bearer <accessToken>" \
  -H "Content-Type: application/json" \
  -d '{"tenantId":"<guid>","companyId":"<guid>","name":"Caja Central"}'
```

Respuesta 201: `{"agentId":"<guid>","secret":"<secreto-una-sola-vez>"}`.

Errores: 409 si se alcanza `Plan.MaxAgents`, si la licencia no es válida o si el nombre ya existe en la empresa. 400 si la empresa no pertenece al tenant.

**Revocar agente**:

```bash
curl -X POST http://localhost:5070/platform/agents/<agentId>/revoke \
  -H "Authorization: Bearer <accessToken>"
```

Responde 204, o 404 si no existe. Marca el agente como inactivo y revoca todos sus refresh tokens.

### Agente (worker): `/agent`

**Handshake** (anónimo, `agent-handshake`, 10 por minuto por IP):

```bash
curl -X POST http://localhost:5070/agent/handshake \
  -H "Content-Type: application/json" \
  -d '{"agentId":"<guid>","secret":"<secreto>","workerVersion":"1.0.0"}'
```

Respuesta 200 con `accessToken`, `refreshToken`, `licenseState` (`Active`, `Grace` o `Suspended`), `handshakenAtUtc`, `tenantId` y `companyId`. 401 si las credenciales no son válidas, 403 si la licencia no es válida.

**Refresh de token de agente** (anónimo, `agent-refresh`):

```bash
curl -X POST http://localhost:5070/agent/token/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"<refreshToken>"}'
```

**Heartbeat** (`Agent`, `agent-heartbeat`):

```bash
curl -X POST http://localhost:5070/agent/heartbeat \
  -H "Authorization: Bearer <accessToken-agente>" \
  -H "Content-Type: application/json" \
  -d '{"workerVersion":"1.0.0","acknowledgedCommandIds":[]}'
```

Respuesta 200: `{serverTimeUtc, pendingCommands:[{id, type, payload, leaseExpiresAtUtc}]}`.

**Enviar documento** (`Agent`, `agent-documents`):

```bash
curl -X POST http://localhost:5070/agent/documents \
  -H "Authorization: Bearer <accessToken-agente>" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: factura-000123" \
  -H "X-Branch-Id: <guid-sucursal>" \
  -d '{"...": "payload del documento"}'
```

- `Idempotency-Key` es opcional (máximo 200 caracteres). Si falta, la API genera uno.
- `X-Branch-Id` es opcional y debe pertenecer a la empresa del agente.

| Código | Significado |
|---|---|
| 200 | El certificador respondió con éxito. Cuerpo: `{documentId, delivered:true}` |
| 502 | El certificador devolvió error o la llamada falló |
| 504 | Tiempo de espera agotado con el certificador |
| 503 | No hay mapper registrado. El documento se guardó pero no se envió |
| 409 | `Idempotency-Key` duplicada dentro del tenant |
| 400 | La sucursal no pertenece a la empresa del agente |
| 401 / 403 | Agente desconocido o licencia no válida |

### Flujo completo de ejemplo

1. `POST /auth/login` como administrador de plataforma. Obtienes `accessToken`.
2. `POST /platform/agents` con `tenantId`, `companyId` y `name`. Guardas `agentId` y `secret`.
3. `POST /agent/handshake` con `agentId` y `secret`. Guardas los tokens del agente.
4. `POST /agent/heartbeat` periódicamente, para recibir comandos pendientes.
5. `POST /agent/documents` para enviar un documento.

---

## Configuración

### `appsettings.json`

| Sección | Clave | Valor por defecto |
|---|---|---|
| `Jwt` | `Issuer` | `wis-fel-api` |
| `Jwt` | `PlatformAudience` | `wis-fel-platform` |
| `Jwt` | `AgentAudience` | `wis-fel-agent` |
| `Jwt` | `PlatformTokenMinutes` / `AgentTokenMinutes` | `15` / `15` |
| `Jwt` | `PlatformRefreshTokenDays` / `AgentRefreshTokenDays` | `7` / `30` |
| `ClientJwt` | `Authority`, `Audience` | vacíos (`/auth/me` no funciona) |
| `ClientJwt` | `RequireHttpsMetadata` | `true` |
| `AgentHeartbeat` | `CommandLeaseSeconds` | `30` |
| `AgentHeartbeat` | `MaxCommandsPerHeartbeat` | `20` |
| `RateLimiting` | cada política: `PermitLimit`, `WindowSeconds` | ver tabla abajo |
| `AllowedHosts` | | `*` |

Políticas de rate limiting (`PermitLimit` / `WindowSeconds`):

| Política | Límite |
|---|---|
| `PlatformLogin` | 10 / 60 s |
| `PlatformRefresh` | 20 / 60 s |
| `AgentHandshake` | 10 / 60 s |
| `AgentRefresh` | 20 / 60 s |
| `ClientMe` | 60 / 60 s |
| `AgentHeartbeat` | 120 / 60 s |
| `AgentDocuments` | 60 / 60 s |
| `PlatformAgentManagement` | 30 / 60 s |

### Secretos requeridos (no van en el repositorio)

| Clave | Obligatoria | Notas |
|---|---|---|
| `ConnectionStrings:FelApi` | Sí | Cadena de conexión a SQL Server |
| `Jwt:SigningKey` | Sí | Base64 de al menos 32 bytes |
| `ClientJwt:Authority` / `Audience` | Solo para `/auth/me` | URL del IdP y audiencia |

### Pipeline HTTP

`UseHttpsRedirection` → `UseRouting` → `UseAuthentication` → `UseRateLimiter` → `UseAuthorization` → `MapControllers`.

Scalar y OpenAPI solo se mapean en `Development`.

---

## Migraciones de base de datos

Las migraciones están en `wis-fel-api/fel-api/Data/Migrations/`. Desde la raíz del repositorio:

```bash
# Aplicar migraciones pendientes
dotnet ef database update --project wis-fel-api/fel-api

# Crear una nueva migración
dotnet ef migrations add <Nombre> --project wis-fel-api/fel-api --output-dir Data/Migrations
```

El proyecto de inicio es el mismo `fel-api`, así que la cadena de conexión debe estar en user-secrets.

---

## Tests

```bash
dotnet test wis-fel-api/wis-fel-api.slnx
```

Los tests usan EF Core InMemory, así que no necesitan SQL Server. `ApiFactory` levanta la API en memoria con una clave de prueba y un límite de login de 2 por minuto.

Cobertura actual (17 tests):

- `AgentApiTests` (8): login solo para administradores, `/auth/me`, rate limit de login, handshake y heartbeat con comando, rotación de refresh tokens y rechazo de reuso, provisionamiento y revocación, documentos guardados y cerrados cuando no hay mapper.
- `SubscriptionStateCalculatorTests` (3), `TenantQuotaValidatorTests` (4), `CertifierConfigurationResolverTests` (1), `ClientProfileServiceTests` (1).

---

## Estado actual y limitaciones

- No hay endpoints CRUD para tenants, empresas, sucursales, planes, suscripciones, certificadores, usuarios ni códigos de activación. Esos datos se cargan directamente en la base.
- No hay ninguna implementación de `ICertifierRequestMapper` registrada en DI. Todos los envíos de documentos responden 503 y solo se guarda el payload.
- Para agregar un certificador o país, implementa `ICertifierRequestMapper` (`CanMap` y `Map`) y regístralo con `AddScoped<ICertifierRequestMapper, TuMapper>()`. El servicio usa `SingleOrDefault`, así que solo puede haber uno que aplique.
- `AuditLog` y `ActivationCode` están modelados, pero no se usan.
- Los comandos de agente (`PendingAgentCommand`) existen, pero nada los crea todavía.
- Sin Dockerfile, sin docker-compose y sin CI.
- `AGENTS.md` describe PostgreSQL, RS256 y ASP.NET Identity como parte del diseño. La implementación actual usa SQL Server y HS256 con un hasher propio.

---

## Contribuir

- Convenciones: los datos de tenant y empresa siempre salen del token. Las nuevas entidades van en `Domain/` y su configuración en `FelApiDbContext`.
- Antes de proponer cambios, ejecuta `dotnet test wis-fel-api/wis-fel-api.slnx`.
- Los commits y los push los hace el responsable del repositorio de forma manual.
- Plan de diseño completo: [`AGENTS.md`](AGENTS.md).

# Plan: Plataforma de Facturación Electrónica Centralizada Multi País

## 1. Objetivo

Plataforma centralizada de licencias y administración para un servicio de facturación electrónica multi país. Los clientes ejecutan un **Worker** local que certifica documentos con el certificador de cada país; un **Portal** web centraliza licencias, planes, empresas y usuarios.

## 2. Principios

- Tecnologías ejecutables y desarrollables desde cualquier S.O. (Windows, Linux, macOS).
- Licencias centralizadas.
- Aislamiento total por tenant (cliente).
- Agregar un país o certificador nuevo sin tocar el núcleo.

## 3. Stack

| Componente | Tecnología |
|---|---|
| Frontend (portal cliente y admin) | React |
| API | ASP.NET Core (.NET 10) |
| Base de datos central | SQL Server (implementado; el plan original decía PostgreSQL) |
| Worker cliente | .NET 10 Worker Service (C#) |
| Cola local del worker | La define el repo del worker (aquí §3 decía SQL Server y §10.3 dice SQLite) |
| Identidad | JWT HS256 propio + `PasswordHasher` para admins; IdP externo (`ClientJwt`) para usuarios de tenant |
| Resiliencia | Polly |

## 4. Repositorios

- **Repo portal web:** React + API. (este REPO)
- **Repo worker service:** Worker .NET 10.
- **Recomendado:** repo/paquete tercero de **contratos compartidos** (DTOs, modelo canónico `Documento` aún no definida la arquitectura).

## 5. Arquitectura general

```
[Portal Cliente]   [Portal Admin]
        \             /
         [ API ASP.NET Core ] ---- [ SQL Server ]
                |      ▲
  licencia,     |      | estado de documentos (solo metadatos)
  comandos      |      | POST /agent/documents/status
  (heartbeat)   v      |
        [ Worker (por tenant) ]
          |  cola local
          |  providers por país/certificador
          v
   [ Certificadores: INFILE GT / SV / RD, otros ]
```

- **La API nunca contacta a los certificadores.** Licencias, planes, clientes, empresas, sucursales y agentes viven en la API. La certificación y los reintentos viven en el worker.
- El reenvío manual se ordena desde el portal, a través de la API. La API crea un comando `ResendDocument` que el worker recibe en el heartbeat y ejecuta.

## 6. Modelo de datos (alto nivel)

- `tenants` (cliente) → `companies` (empresa) → `branches` (sucursal).
- `plans`: `tipo` (mensual / anual / lifetime), `max_empresas`, `max_sucursales_por_empresa`, opcional `paises_permitidos`.
- `subscriptions`: tenant, plan, fechas, estado.
- `companies`: `pais`, `certificador_id`, identificación fiscal (NIT, etc.), moneda, huso horario.
- `certificadores`: catálogo (INFILE, otros).
- `company_certifier_config`: credenciales cifradas, URLs, modo pruebas/producción.
- `users`, `roles`, `user_roles`.
- `instances` (workers activos), `activation_codes`.
- `audit_log`.
- `company_id` en documentos, series y correlativos.
- `documents`: **solo metadatos** reportados por el worker.
  - Campos: `idempotency_key` (única por tenant), tipo, serie, número, estado, número de autorización del certificador, código y mensaje de error, intentos, fechas de emisión, certificación y último reporte, y `resend_requested_at`.
  - El XML o JSON firmado no se guarda en la central.
- `pending_agent_commands`: comandos para el worker con lease, entregados en el heartbeat (por ejemplo `ResendDocument`).

## 7. Multi tenant

- `tenant_id` en todas las tablas + **Row Level Security** de PostgreSQL.
- El `tenant_id` se toma siempre del token, nunca del request.

## 8. Planes y licenciamiento

### 8.1 Planes

- Mensual, anual o lifetime (lifetime sin vencimiento del plan).
- Cada plan incluye un número de empresas y de sucursales por empresa.
  - Ejemplo: plan básico = 1 empresa, 1 sucursal.
- **Upgrade:** se cambia el plan en `subscriptions`; los límites aplican de inmediato.
- Prorrateo solo para mensual/anual (pagos: fuera de alcance por ahora).
- La API rechaza crear empresas o sucursales sobre el límite.

### 8.2 Activación del worker

1. El cliente crea la empresa/sucursal en el portal.
2. El portal genera un **código de activación** de un solo uso con vencimiento corto.
3. El worker lo recibe en el primer arranque (archivo de configuración o variable de entorno).
4. El worker envía el código + **huella de máquina** (hostname, ID de máquina, SO).
5. La API valida, registra la instancia y responde con JWT de licencia, refresh token y configuración inicial.
6. El worker guarda los tokens cifrados (DPAPI en Windows, Keychain en macOS, archivo con clave derivada en Linux).

### 8.3 JWT de licencia

- Firma **RS256**: clave privada en la API, clave pública en el worker.
- Claims: `tenant_id`, `plan`, `max_empresas`, `max_sucursales`, `vence`, `instance_id`.
- Vigencia corta (24 a 72 h), renovada automáticamente con el refresh token.
- Lifetime: el plan no vence, pero el JWT se sigue renovando.

### 8.4 Renovación y gracia

- Sin conexión, el worker opera dentro de un **periodo de gracia** (ej. 1 día).
- Pasada la gracia, se bloquea la certificación y se avisa.
- Plan vencido o suspendido: la API deja de renovar.

### 8.5 Control y revocación

- El plan limita el número de instancias activas.
- Los límites de empresas/sucursales se validan en la API y en el worker (según JWT).
- Upgrade: el siguiente JWT trae los nuevos límites, o se fuerza vía SignalR.
- Admin puede revocar una instancia o cambiar su huella (migración de equipo).
- Heartbeat periódico (estado y versión del worker).
- Si dos equipos usan el mismo `instance_id`, la API lo detecta y bloquea.
- Auditoría de activaciones y renovaciones.

## 9. Portales

### 9.1 Portal cliente

- Gestión de sus empresas, sucursales, país y certificador.
- Usuarios y roles propios.
- Solo ve su tenant.

### 9.2 Portal administrador

- Ver y editar información de todos los clientes, empresas, planes, instancias y usuarios.
- Suplantación de tenant con registro de auditoría.
- 2FA obligatorio.

### 9.3 Roles

- **Plataforma:** `SuperAdmin`, `Soporte` (solo lectura).
- **Cliente:** `Owner`, `Admin`, `Usuario`, `SoloLectura`.

### 9.4 API

- Una sola API con `/platform/*` (admin, JWT propio) y `/tenant/{tenantId}/*` (usuarios del IdP). El `tenantId` de la ruta solo se acepta con una membresía activa.
- Los dos portales ven el estado de los documentos y pueden ordenar el reenvío manual: `/platform/documents` y `/tenant/{tenantId}/documents`.
- Dos apps React (rutas separadas o dos builds).

## 10. Worker

### 10.1 Multi país y multi certificador

- País y certificador se definen a nivel de **empresa**, no de tenant.
- Un cliente con central en GT puede tener empresas en SV o RD, cada una con un certificador distinto.
- Un worker por tenant atiende varias empresas de distintos países.
- Por documento: lee `company_id` → resuelve `país + certificador` → elige el provider.

### 10.2 Diseño (Strategy + Factory)

- **Modelo canónico:** `Documento` interno, neutral al país y al certificador.
- `ICertificadorProvider.CertificarAsync(Documento)`.
- Implementaciones por certificador y país: `InfileGtProvider` (XML), `InfileSvProvider` (JSON), `InfileRdProvider`, etc.
- `IDocumentSerializer` separado: XML con `XDocument`/`XmlWriter` (GT), JSON con `System.Text.Json` (otros países).
- `IProviderFactory` con clave `país + certificador` vía keyed services de .NET 10.
- **Estrategia fiscal por país** (aparte del certificador): impuestos (IVA/ITBIS), tipos de documento, numeración, moneda y huso horario.
- Agregar un país = una clase nueva, sin tocar el núcleo.
- Credenciales por empresa descargadas de la API y guardadas cifradas, nunca en texto plano.

### 10.3 Cola local (outbox)

- SQLite (multiplataforma).
- Estados: `Pendiente → Procesando → Certificado | Error | Reintentar`.
- Cola con `company_id` para aislar errores y reintentos por empresa.
- Flujo: documento entra a la cola → certificación → resultado guardado local → sincronización con la API.

### 10.4 Comunicación con la API

- **Pull:** configuración y licencia cada X minutos (HTTPS + JWT). El heartbeat (`POST /agent/heartbeat`) entrega comandos pendientes, como `ResendDocument` con payload `{documentId, idempotencyKey}`. El worker hace ack con `acknowledgedCommandIds`.
- **Push:** resultados y estado en lotes de hasta 100 a `POST /agent/documents/status`, con upsert por `idempotencyKey`. Se reporta cada cambio de estado, no solo el final.
- Opcional: SignalR para avisos en tiempo real, con polling como respaldo.
- **Idempotencia:** `idempotency_key` por documento.

### 10.5 Fallos, timeouts y conexión

- Timeout corto por petición (15 a 30 s) con `HttpClient` + Polly.
- Reintentos con backoff exponencial y jitter (5 s, 15 s, 1 min, 5 min...).
- Circuit breaker tras varios fallos consecutivos.
- **Modo offline:** el worker sigue certificando con el certificador (no depende de la API); resultados pendientes quedan en SQLite y se sincronizan al volver la conexión.
- Licencia: periodo de gracia sin conexión.
- Timeout con el certificador: antes de reintentar, consultar si el documento ya fue certificado para evitar duplicados.
- **Dead-letter:** tras N intentos pasa a `Error` y se alerta en el portal.
- **Reenvío manual:** el portal lo ordena y el worker lo ejecuta con el documento de su cola local. Antes de reenviar consulta al certificador si el documento ya fue certificado. Luego reporta el nuevo estado; un estado final limpia la marca de reenvío pendiente en la API.
- **Heartbeat** para ver en el admin qué workers están desconectados.

## 11. Seguridad

- HTTPS siempre; certificate pinning opcional.
- Validación real de licencia en la API (ofuscar el worker aporta poco).
- 2FA para admins.
- Credenciales de certificadores cifradas.
- Auditoría completa.

## 12. Fuera de alcance por ahora

- Pagos y pasarela (Stripe u otra).

## 13. Siguientes pasos

1. Modelo de datos completo (DDL PostgreSQL + RLS).
2. Estructura de los repos y proyecto de contratos compartidos.
3. Esqueleto de la API (identidad, tenants, planes, activación).
4. Esqueleto del Worker (cola SQLite, providers, activación).
5. Primer provider: INFILE GT (XML).
6. Portal admin y portal cliente (React).
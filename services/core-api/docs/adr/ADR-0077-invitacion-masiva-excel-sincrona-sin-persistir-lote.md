# ADR-0077: Invitación masiva de usuarios por Excel — proceso síncrono acotado, sin persistir el lote, RBAC en un servicio compartido

**Fecha**: 2026-11-09
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana — regla FLIT 15), PO Jorman, architecture-agent (propuesta)
**Tags**: arquitectura, backend, frontend, seguridad, rbac, usuarios, invitaciones, excel
**Feature**: ADO #14420 — [USUARIOS] Invitar usuarios de forma masiva mediante archivo Excel
**Supersedes**: ninguno. No contradice ADR-0023 (roles híbridos por tenant), ADR-0048 (invitación cancelada) ni HU #11580 (código único de conflicto de correo).

## Contexto

Hoy se invita de a uno: `InviteUserModal` → `POST /security/invitations` (SuperAdmin y AdminCompany), `POST /admin/ot/users/invite` (OT) y `POST /admin/companies/{head}/children/{child}/invitations` (cabeza de grupo). Los tres endpoints repiten a mano la regla de alcance de roles y delegan en `CreateInvitationHandler`, que **no** valida RBAC: solo comprueba que el rol exista, esté activo y sea global o del tenant (`RoleExistsInTenantAsync`), que no haya invitación pendiente y que el correo no tenga cuenta. Además `/security/invitations` para no-SuperAdmin solo bloquea el rol SuperAdmin; el filtrado de roles de admin de producto y del tipo de entidad descansa en la UI y en `GET /security/roles`.

Restricciones del repo que condicionan la decisión:
1. nginx corta a los 60 s (`proxy_read_timeout`, ver `docker-compose.prod.yml`). `CreateInvitationHandler` envía el correo en línea.
2. `DocumentFormat.OpenXml` ya está en `Flit.Infrastructure` (patrón SAX en `BulkTramitesXlsxParser` / `StandaloneDocumentXlsxParser`), con tope de 50 filas en carga masiva de trámites y 100 en documentos standalone.
3. No hay colas externas (ADR-0024-telemetria; ADR-0059-estandar-procesos-automaticos-periodicos). Un motor de lotes persistido ya existe como patrón (ADR-0070-motor-lotes) pero es pesado.
4. Auditoría administrativa: `AdminAuditFilter` + `AdminAuditEntry` (con `NewValue`) ya soportan una fila por operación.
5. El frontend no tiene librería de Excel; solo `FormData` hacia el backend (patrón `bulk-tramites-client.ts`).

Requerimiento acordado con el PO: Perfil + destino en pantalla, Excel con nombre/correo/rol (un rol por fila), plantilla con catálogo de roles del contexto, RBAC estricto en UI + plantilla + backend, reutilizar `CreateInvitationHandler`, validación por fila sin tumbar el lote, reporte de errores, auditoría. Fuera de v1: perfil FLIT, multi-compañía por archivo, roles legados, edición masiva.

## Decisión

Implementar la invitación masiva como **operación síncrona acotada (≤ 50 filas, configurable)** con **dos fases sin estado** (`dryRun=true` para validar y previsualizar; `dryRun=false` para ejecutar, revalidando todo), **sin persistir el lote ni sus filas**, con la **regla de alcance de roles extraída a un servicio compartido** (`IInvitationScopePolicy`) que usan la plantilla, la validación y el commit, y **una fila de auditoría resumen por carga** (conteos en `NewValue`). Cada fila válida se crea llamando a `CreateInvitationHandler` en su propio scope DI.

## Alternativas consideradas

### Opción 1: Síncrona acotada, dos fases (dryRun/commit), sin persistir lote (elegida)

**Pros:** - Cero tablas y cero migraciones; sin datos personales retenidos fuera de `user_invitations` (Habeas Data más simple). - Reutiliza 100 % `CreateInvitationHandler` y el correo de activación. - Reutiliza parser SAX y patrón plantilla/lote de `BulkTramites`. - Idempotente por naturaleza: reenviar el mismo archivo devuelve `EMAIL_ALREADY_IN_USE` en las filas ya invitadas. - Esfuerzo bajo-medio; entregable en un solo PR de Feature.
**Cons:** - Tope de filas por archivo (50) por el límite de 60 s de nginx y el envío de correo en línea. - Si el cliente pierde la conexión el reporte se pierde (las invitaciones sí quedan; se ven en Usuarios → pendientes). - El reporte de errores no se puede recuperar después (solo en la respuesta). - Sin historial navegable de cargas más allá de la auditoría.
**Esfuerzo:** M
**Riesgos:** Latencia real del SMTP en PDN (a medir). Un archivo > 50 filas obliga a partirlo; si el PO necesita cientos de filas, escalar a la Opción 2.

### Opción 2: Lote persistido + procesamiento en segundo plano (patrón ADR-0070 / `StandaloneDocumentBatch`)

**Pros:** - Sin límite práctico de filas ni de timeout; reanudable tras reinicio. - Reporte de errores persistente y descargable luego; historial de cargas. - Permite throttling de correo.
**Cons:** - Dos tablas nuevas con PII (correo, nombre) que hay que cifrar/purgar (Habeas Data). - Worker (`BackgroundService`, `SKIP LOCKED`), polling en UI, estados intermedios, cancelación. - Más superficie de pruebas y de seguridad (autorización del worker sin JWT: el RBAC debe re-evaluarse o congelarse al encolar). - Mucho más código para decenas de filas.
**Esfuerzo:** L
**Riesgos:** El worker corre sin JWT: hay que persistir el snapshot de permisos del invitador o revalidarlos, o se abre una escalada de privilegios diferida.

### Opción 3: Orquestación en el cliente (parsear el Excel en el navegador y llamar N veces al endpoint individual)

**Pros:** - Cero backend nuevo; RBAC lo imponen los endpoints existentes. - Sin problema de timeout (una petición por fila).
**Cons:** - Requiere nueva dependencia de Excel en el frontend (ADR obligatorio por regla del agente) y generar la plantilla en el navegador. - Cerrar la pestaña deja el lote a medias; sin auditoría resumen; el endpoint OT no tiene `AdminAuditFilter`. - La validación RBAC en backend sigue siendo la débil del endpoint individual (ver Contexto) y el catálogo de la plantilla dependería del cliente. - 50-100 llamadas HTTP encadenadas, sin control de concurrencia ni protección contra doble clic.
**Esfuerzo:** M
**Riesgos:** Incumple el requisito de «validar en UI, plantilla Y backend» con una sola fuente de verdad; auditoría dispersa.

## Tradeoff aceptado

Se acepta un tope de filas y un reporte efímero a cambio de no crear persistencia con PII, no abrir un worker sin JWT y entregar en un solo Feature. El PO declaró volúmenes de «decenas» de correos (onboarding de Renting Colombia). Si el volumen real supera el tope, la Opción 2 es el siguiente paso natural y reutiliza el servicio de alcance y el parser definidos aquí.

Se descarta la Opción 3 porque separa la fuente de verdad del RBAC (cliente vs. servidor) y deja la auditoría y el reporte sin dueño.

## Consecuencias

### Lo que se gana
- Una sola implementación de la regla «qué roles puede asignar quien invita» (`IInvitationScopePolicy`), usada por plantilla, validación y commit; los endpoints individuales pueden migrar a ella después (deuda explícita, no en v1).
- Sin migraciones ni tablas nuevas; despliegue sin ventana de BD.
- Auditoría trazable: una fila `users/bulk_invite` por carga + `invited_by` en cada `user_invitations`.

### Lo que se pierde
- Sin historial/recuperación del reporte por fila.
- Tope de 50 filas por archivo en v1.
- La regla de roles queda duplicada temporalmente (servicio nuevo + endpoints individuales) hasta migrar; se mitiga con pruebas de paridad.

### Cambios operacionales
- Infra: confirmar `proxy_read_timeout` y `client_max_body_size` para las rutas nuevas (hoy 60 s por defecto; el tope de filas se calibra contra eso).
- Configuración nueva: `BulkInvitations:MaxRows` (50), `MaxFileBytes` (1 MB), `MaxParallelism` (4), `CommitDeadlineSeconds` (50).
- Nuevo valor de vocabulario de auditoría: `AuditVocabulary.Operations.BulkInvite = "bulk_invite"`.

## ADRs relacionados

- ADR-0023 / ADR-0023-enmienda-roles-hibridos-por-tenant — catálogo global + roles propios por tenant (fuente del catálogo de la plantilla).
- ADR-0048 — invitación cancelada como estado vivo reversible (una fila cancelada no cuenta como pendiente).
- ADR-0044 — envío real vs. buzón de control por despliegue (QA: el correo no sale a destinatarios reales fuera de PDN).
- ADR-0070-motor-lotes-descarga-masiva-consolidados — patrón de lotes persistidos (Opción 2, descartada para v1).
- ADR-0057-jerarquia-de-clientes-alcance-tipado-fail-closed — alcance fail-closed (extensión futura a cabeza de grupo).

## Notas para agentes

- **Backend Agent**: crear `IInvitationScopePolicy` + `BulkInvitationService` en `Flit.Modules.Security.Application`; no copiar la lógica de los endpoints. Cada fila = `CreateInvitationHandler` en un scope DI propio (`IServiceScopeFactory`) para aislar el `DbContext` ante una violación de unicidad. El commit no usa `RequestAborted`: usa un deadline propio.
- **Database Agent**: sin DDL en v1. Verificar que `uq_users_email` y `uq_user_invitations_tenant_email_pending` cubren la carrera entre filas.
- **Frontend Agent**: `BulkInviteModal` compartido (inyecta cliente, patrón `submitInvitation`); reporte de errores en CSV generado en cliente con escape de fórmulas; no añadir librería de Excel.
- **QA Agent**: matriz RBAC (SuperAdmin, AdminCompany, ot_admin, Gestor OT) × (rol permitido / SuperAdmin / admin OT / rol de otro tipo de entidad) en UI, plantilla y API; reenvío del mismo archivo; archivo manipulado.
- **Security Agent**: revisar indistinguibilidad de correo (`EMAIL_ALREADY_IN_USE`), inyección de fórmulas en exportes, zip-bomb/tamaño, enumeración vía `dryRun` (límite de tasa), y que el servicio de alcance se invoque SIEMPRE antes del handler.
- **Infra Agent**: ajustar timeouts/tamaño de cuerpo en nginx para las rutas `/invitations/bulk*`.

## Referencias externas

- OWASP — CSV/Formula Injection.
- ECMA-376 (Office Open XML) / Open XML SDK — lectura SAX.

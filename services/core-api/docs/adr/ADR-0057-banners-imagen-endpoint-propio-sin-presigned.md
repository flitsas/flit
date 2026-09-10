
# ADR-0057: Imágenes de banners promocionales servidas por endpoint propio, sin exponer presigned URLs de S3

**Fecha**: 2026-09-09
**Status**: Aceptado (2026-09-09, por Willyn Londoño Calle)
**Deciders**: Willyn Londoño Calle (Líder Técnico), Architecture Agent
**Tags**: arquitectura, backend, seguridad, modulo-admin, modulo-banners

## Contexto

El Feature #12236 (Epic #12231) pide un módulo administrable de banners promocionales
(nombre, imagen, enlace URL opcional, período de vigencia, activar/desactivar) **globales**
(no por tenant), conectados a dos carruseles ya existentes en frontend (`Dashboard.tsx` del
gestor y `OtDashboard.tsx` del OT) que hoy están hardcodeados. Un banner debe verse **siempre**,
para **todos** los usuarios, en un carrusel que puede permanecer abierto horas.

El único mecanismo de storage existente para binarios (`IAttachmentStorage` →
`FileManagerAttachmentStorage`, sobre el file-manager interno `BackCrudFileManager · api/v1/files`)
no expone URL pública ni permanente: `GetPresignedViewUrlAsync` firma un GET de S3 con TTL ≈ 10-15 min
(`FileManagerOptions.PreviewUrlTtlMinutes`, ADR-0029), pensado para que un usuario autorizado vea un
PDF puntualmente — no para un `<img>` de larga vida en un carrusel. Además, `OpenReadAsync` bufferiza
el binario completo en memoria (`FileManagerAttachmentStorage.cs:96-118`), y `Flit.Admin.Application`
no puede referenciar `Flit.Tramites.Application` (restricción de compilación C6, ya resuelta en
ADR-0056 con el patrón de puerto acotado `IStandaloneDocumentStorage`).

## Decisión

Declarar un puerto acotado `IBannerImageStorage` en `Flit.Admin.Application` (mismo patrón que
`IStandaloneDocumentStorage`, delegando en `IAttachmentStorage` con el `tenantId` reemplazado por una
clave de agrupación fija, p. ej. un GUID constante `BannerStorageGroupId`, ya que los banners no
tienen tenant) y exponer un **endpoint propio de streaming** — `GET /public/banners/{id}/image` —
que hace `OpenReadAsync` y transmite los bytes en la respuesta con `Cache-Control` y `ETag` (hash
SHA-256 ya calculado por `SaveAsync`). El `<img src>` del frontend apunta siempre a ese endpoint,
nunca a una URL de S3: no hay presigned URL que expire de cara al navegador.

## Alternativas consideradas

### Opción 1: Puerto acotado + endpoint propio de streaming *(elegida)*

`IBannerImageStorage.OpenReadAsync` delega en `IAttachmentStorage.OpenReadAsync` (mismo patrón que
`StandaloneDocumentStorage`). El endpoint `GET /public/banners/{id}/image`:
- Responde con `Content-Type` del binario, `Cache-Control: public, max-age=86400`
  (24h) y `ETag: "<sha256>"` (el hash ya se calcula y persiste en `SaveAsync`).
- Soporta `If-None-Match` → `304 Not Modified` sin recalcular ni releer S3 (el hash cambia si el
  admin reemplaza la imagen, por lo que el navegador la vuelve a pedir automáticamente).
- Se declara **anónimo** (`[AllowAnonymous]`, mismo patrón que `Endpoints/Public/*` ya existente en
  `Flit.Api`), porque el binario servido es contenido de marketing global sin PII (Ley 1581 no aplica:
  no hay dato personal en una imagen de banner) y porque un `<img src>` de HTML **no puede** adjuntar
  el header `Authorization: Bearer …` que usa el resto de la API (el JWT vive en cookie no-httpOnly +
  `localStorage`, leído por `frontend/lib/api/client.ts`, y se inyecta manualmente como header en cada
  `fetch`). Forzar auth en este endpoint obligaría a fetch+blob-URL en el frontend solo para pintar un
  `<img>`, complejidad que no se justifica para un asset ya global y no sensible. Las operaciones de
  escritura (crear/editar/borrar banner, subir imagen) **sí** quedan detrás del permiso admin
  correspondiente (`banners.manage` o equivalente, HU4).

**Pros:**
- Elimina por completo el problema de expiración: el navegador nunca ve una URL de S3.
- Reutiliza el 100% del patrón ya aceptado en ADR-0056 (puerto acotado sobre `IAttachmentStorage`);
  cero cambios en `Flit.Tramites.Application` ni en `IAttachmentStorage`.
- Cache HTTP estándar (`ETag` + `Cache-Control`) resuelve tráfico repetido sin infraestructura nueva:
  el navegador solo repite el `GET` cuando el hash cambia.
- Compila con la restricción C6 sin necesidad de refactors ajenos.

**Cons:**
- `OpenReadAsync` bufferiza el binario completo en memoria por request (no hay streaming real desde
  S3); aceptable si se acota el tamaño de imagen en la validación de subida (HU2, p. ej. ≤ 2 MB), pero
  es deuda si en el futuro se permiten banners de video o imágenes pesadas.
- Endpoint anónimo amplía superficie sin autenticación (mitigado: solo lectura de un asset no
  sensible; ninguna operación de escritura es anónima).
- Indirección extra (puerto + adaptador + endpoint) respecto a servir directo desde S3.

**Esfuerzo:** S
**Riesgos:** ninguno de PII; riesgo operativo bajo (carga del binario en memoria del proceso de
`core-api` en cada miss de caché — mitigado por `Cache-Control` de 24h y por el tamaño acotado de
imagen).

### Opción 2: Presigned URLs con refresco periódico en frontend

El `<img src>` apunta a la presigned URL de `GetPresignedViewUrlAsync` (TTL ≈ 10-15 min); un
`setInterval`/`useEffect` en `Dashboard.tsx` y `OtDashboard.tsx` la renueva antes de expirar mientras
el carrusel esté montado.

**Pros:**
- Cero endpoint nuevo de streaming; reusa `GetPresignedViewUrlAsync` tal cual existe hoy.
- Los bytes de la imagen nunca pasan por `core-api` (los sirve S3 directo).

**Cons:**
- Complejidad de refresco duplicada en **dos** componentes de frontend (gestor + OT), con el riesgo
  de que un carrusel abierto más tiempo del previsto muestre un `<img>` roto si el timer falla o la
  pestaña queda en background (los navegadores limitan timers en tabs no visibles).
- Cada refresco es una llamada de red adicional a `core-api` → file-manager → S3, por cada banner
  visible, cada pocos minutos, multiplicado por cada usuario con el dashboard abierto — carga
  innecesaria en el file-manager interno para un asset que no cambia.
- No hay `Cache-Control` real posible: la URL rota, así que el navegador no puede cachear entre
  refrescos.
- El TTL de 10-15 min es una decisión operativa del file-manager (`FileManagerOptions.PreviewUrlTtlMinutes`),
  fuera del control de FLIT; un cambio ahí (a la baja) rompe el refresco sin que el equipo lo note.

**Esfuerzo:** M (lógica de refresco duplicada en dos componentes + manejo de fallos)
**Riesgos:** UX (parpadeo/rotura de imagen si el refresco falla), acoplamiento a un TTL externo no
gobernado por FLIT.

### Opción 3: Bucket S3 público aparte, fuera del file-manager

Provisionar un bucket S3 nuevo (o un bucket/prefix con política pública) independiente del
file-manager interno, opcionalmente detrás de un CDN, y subir ahí las imágenes de banner desde el
backend con un cliente S3 directo.

**Pros:**
- URL verdaderamente permanente y cacheable sin lógica de servidor por request; el CDN absorbe toda
  la carga de lectura.
- Sin límite de tamaño de imagen impuesto por la memoria del proceso de `core-api`.

**Cons:**
- **Dependencia nueva** (cliente S3 directo + bucket/política de acceso público + posible CDN) que
  requiere justificación explícita y trabajo de `infra-agent` (regla innegociable #3: ninguna
  dependencia nueva sin ADR que la justifique).
- Bypasea por completo el único mecanismo de storage ya establecido y auditado
  (`BackCrudFileManager`), duplicando el patrón de reciclaje/borrado que ese sistema ya resuelve (hoy
  el borrado es no-op delegado al ciclo de vida del file-manager; un bucket aparte obliga a
  reimplementar esa política).
- Expone objetos públicos de S3 directamente a Internet, fuera del dominio de `core-api`: rompe la
  invariante actual del repo de que **todo** binario pasa por el file-manager interno.
- Introduce una segunda vía de almacenamiento de archivos en el sistema, con su propio ciclo de
  vida, credenciales y observabilidad — sobre-ingeniería para un asset de bajo volumen (banners
  administrados manualmente, no attachments de trámite).

**Esfuerzo:** L (requiere infra-agent: bucket, política, credenciales, y frontend apuntando a un
dominio nuevo)
**Riesgos:** deuda operativa (segundo sistema de storage que mantener), superficie de exposición
pública mayor de la necesaria, tiempo de entrega mayor al del resto del Feature (S/M).

## Tradeoff aceptado

Se acepta **bufferizar el binario en memoria por request** (deuda ya presente en
`FileManagerAttachmentStorage.OpenReadAsync`, no nueva) y **anonimizar la lectura del endpoint de
imagen** a cambio de eliminar por completo el problema de expiración sin tocar infraestructura ni
introducir dependencias nuevas. La Opción 2 se descarta porque traslada la complejidad al frontend
duplicada en dos componentes y no resuelve cacheo; la Opción 3 se descarta porque agrega una
dependencia de infraestructura no justificada por el volumen/naturaleza del asset (regla #3) y
bypasea el único mecanismo de storage ya auditado del repo.

## Consecuencias

### Lo que se gana

- Ningún carrusel se rompe por expiración de URL, sin lógica de refresco en frontend.
- Cero cambios en `IAttachmentStorage`, `FileManagerAttachmentStorage` ni en el pipeline de trámites.
- Cacheo HTTP estándar (`ETag`/`Cache-Control`) reduce tráfico repetido sin infraestructura extra.
- Compila bajo la restricción C6 reusando el patrón ya aceptado en ADR-0056.

### Lo que se pierde

- El binario se bufferiza completo en memoria por miss de caché (aceptable con límite de tamaño de
  imagen impuesto en la validación de subida, HU2).
- Un endpoint de lectura sin autenticación (mitigado: sin PII, sin escritura anónima).

### Cambios operacionales

- Nuevo puerto `IBannerImageStorage` en `Flit.Admin.Application/Banners/Ports/` + adaptador en
  `Flit.Infrastructure/Storage/BannerImageStorage.cs`, calcados de `IStandaloneDocumentStorage` /
  `StandaloneDocumentStorage`.
- Nuevo endpoint `GET /public/banners/{id}/image` en `Flit.Api/Endpoints/Public/` (mismo namespace
  que `PublicPortalEndpoints.cs`), `[AllowAnonymous]`, con `ETag`/`If-None-Match`/`Cache-Control`.
- Validación de tamaño/tipo de imagen en la subida (HU2, backend CRUD) — límite recomendado a acordar
  con Backend Agent (p. ej. ≤ 2 MB, `image/png`/`image/jpeg`/`image/webp`).
- El contrato OpenAPI de banners (HU3, endpoint de consumo) expone el `id` del banner; el frontend
  arma la URL de imagen como `/public/banners/{id}/image` sin necesitar el storage path opaco.

## ADRs relacionados

- `ADR-0056-generacion-documental-standalone` — origen inmediato del patrón de puerto acotado sobre
  `IAttachmentStorage` que este ADR reutiliza para banners.
- `ADR-0029-preview-presigned-get-inline` — mecanismo de presigned view (TTL ≈ 10-15 min) que este
  ADR decide **no** usar para banners por su ciclo de vida de carrusel de larga duración.
- `ADR-0058-banners-tabla-global-sin-tenant-excepcion` — decisión de modelo de datos complementaria
  del mismo Feature #12236 (tabla `admin.banners` global).

## Notas para agentes

- **Backend Agent**: declarar `IBannerImageStorage` en `Flit.Admin.Application`, implementar en
  `Flit.Infrastructure` delegando en `IAttachmentStorage` (grupo fijo, no `tenantId`). No tocar
  `IAttachmentStorage` ni `FileManagerAttachmentStorage`. El endpoint de imagen va en
  `Endpoints/Public/`, `[AllowAnonymous]`, con `ETag` = SHA-256 persistido. Las operaciones de
  CRUD (crear/editar/borrar/activar-desactivar) permanecen autenticadas y detrás del permiso admin
  correspondiente (HU4) — la anonimidad es **solo** del endpoint de lectura del binario.
- **Frontend Agent**: `<img src>` apunta directo a `/public/banners/{id}/image`; no implementar lógica
  de refresco de URL ni fetch+blob. No enviar `Authorization` en esa petición (el endpoint es
  anónimo); sí seguir usando el cliente autenticado normal para el CRUD de administración de banners.
- **QA Agent**: validar `304` con `If-None-Match` tras no-cambio, y `200` con nuevo `ETag` tras
  reemplazar la imagen de un banner. Verificar que un carrusel abierto > 15 min sigue mostrando la
  imagen sin refresco manual.
- **Security Agent**: confirmar que el endpoint anónimo **solo** sirve el binario (GET), sin exponer
  metadata sensible en headers ni permitir enumeración útil más allá de ver una imagen de marketing;
  ninguna ruta de escritura debe quedar `[AllowAnonymous]`.
- **Infra Agent**: sin infraestructura nueva; el endpoint corre dentro del proceso existente de
  `core-api`. Sin bucket ni CDN nuevos.

## Referencias externas

- Ninguna (decisión interna de arquitectura sobre infraestructura ya existente).

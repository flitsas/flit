# ADR-0081: La marca del portal Persona Natural se guarda por organismo de tránsito, en tablas propias

**Fecha**: 2026-11-05
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana, regla FLIT 15), Product Owner (Épica #14297).
**Tags**: arquitectura, backend, datos, frontend, persona-natural, marca, modulo-admin
**Épica / Features**: #14297 · Feature #14374 (HU #14375, #14376, #14378) · sección pendiente del Feature #14380 (dominio cruzado, HU #14381)
**Base de código**: rama `feature/AB-14374-marca-portal-ot`; `origin/develop` verificado hasta `c94183722` (PR #776, dominios de red por producto).
**Relacionado**: [ADR-0060-marca-blanca-identidad-dominio-y-tema-de-correo] (Propuesto), [ADR-0080-departamentos-impuesto-por-tabla] (misma excepción de catálogo global)
**Diseño**: `docs/design/EPICA-14297-contrato-portal-ot.md`

> **Este ADR documenta una decisión ya implementada.** Queda en `Propuesto` hasta que el Líder Técnico humano la revise. La sección «Pendiente: dominio cruzado» NO es una decisión tomada: plantea alternativas para #14381.

## Contexto

Cada organismo de tránsito (OT) debe poder mostrar en su portal Persona Natural su propio nombre, colores, logo y banner, con borrador y publicación. Los cambios deben verse de inmediato.

Marca blanca (ADR-0060) ya resolvió un problema parecido para redes: `admin.tenant_brandings` (borrador y publicado en `jsonb`, versión publicada), `admin.tenant_brand_logos` y `admin.tenant_domains`. Esas tres tablas llevan el disparador `identity.trg_require_marca_blanca_head()`, que exige `tenant_type = 'MARCA_BLANCA'` y `is_group_parent` al insertar o actualizar. Allí el dueño de la marca es un tenant. Aquí es una fila de `catalogs.transit_offices`, que no es un tenant `MARCA_BLANCA`. La marca debe llegar al portal por el código del organismo, a través de la fachada gRPC de `core-api` y el BFF de Persona Natural.

## Alternativas consideradas

### Opción 1: Tablas propias por `transit_office_id`, globales, sin RLS (elegida, implementada)

DDL `180-HU14375-portal-marca-organismo.sql`: `admin.transit_office_portal_brandings` (una fila por organismo, `draft` y `published` en `jsonb`, `published_version`) y `admin.transit_office_portal_brand_images` (una fila por versión de logo o banner).

**Pros:**
- Marca blanca no cambia: ni sus tablas, ni sus disparadores, ni su comportamiento. Riesgo cero de regresión en FLIT 2 y en las redes.
- El invariante de marca blanca (solo una cabeza `MARCA_BLANCA` puede tener marca) sigue siendo fail-closed en BD.
- El dueño del dato es el organismo, con FK `RESTRICT` a `catalogs.transit_offices`. No hay tenant ficticio que crear.
- Reutiliza el patrón ya probado (borrador/publicado en `jsonb`, `published_version`, `row_version`) y el cálculo de contraste (`WcagContrast`).

**Contras:**
- Dos tablas más con forma casi igual a las de marca blanca: duplicación de modelo y de repositorio.
- El `jsonb` no tiene CHECK de forma; la valida la aplicación.
- Hay dos caminos de marca. El contraste comparte cálculo (`WcagContrast`), pero la imagen tiene reglas propias en `PortalMarcaReglas`: admite WebP y no exige dimensiones mínimas, a diferencia de `BrandAssetValidator`. Un cambio en las reglas de imagen de marca blanca no llega solo al portal.

**Esfuerzo:** M. **Riesgos:** deriva entre los dos modelos de marca con el tiempo.

### Opción 2: Reusar `admin.tenant_brandings` y quitar o relajar los disparadores

Aflojar `trg_require_marca_blanca_head()` para aceptar también organismos, o crear un tenant por organismo.

**Pros:**
- Una sola estructura de marca, un solo repositorio, un solo lugar para las reglas.
- Menos DDL nuevo.

**Contras:**
- Quita la garantía fail-closed de ADR-0060: ya no sería imposible una marca en una hija o en una Concesión si falla la aplicación.
- Toca tablas de marca blanca con regla propia (`published_version`, auditoría en `tenant_config_audit_logs`, lectura con `JOIN identity.tenants`). Cambia un comportamiento que debe quedar intacto.
- `tenant_brandings.tenant_id` apunta a `identity.tenants`. Obliga a crear un tenant por organismo o a hacer la columna polimórfica.
- Los resolutores de marca blanca comprueban el tipo en lectura; habría que revisarlos todos.

**Esfuerzo:** M. **Riesgos:** regresión en marca blanca y en login por dominio.

### Opción 3: Marca como columnas o `jsonb` dentro de `catalogs.transit_offices`

**Pros:**
- Cero tablas nuevas; un solo `SELECT`.

**Contras:**
- Mezcla un catálogo con datos publicados por el organismo, con borrador, versión y quién publicó.
- No resuelve el versionado inmutable de imágenes.
- Hace caliente una tabla de catálogo y la deja con escritores de rol de organismo.

**Esfuerzo:** S. **Riesgos:** auditoría y concurrencia pobres.

## Decisión

Se adopta la **Opción 1**.

### 1. Tablas propias, globales por organismo, sin `tenant_id` ni RLS

Las dos tablas son globales: sin `tenant_id` y sin RLS. Solo contienen nombre y colores de marca e imágenes de marca, sin PII. Las administra el `ot_admin` de ese organismo o el SuperAdmin, y el **alcance lo impone el caso de uso**: el `ot_admin` trabaja con el organismo de su token e ignora `transitOfficeId`; el SuperAdmin lo envía; otro organismo responde 403. El repositorio usa SQL parametrizado, no entidades EF, y no comparte nada con marca blanca. La auditoría técnica es `trg_audit_log` más `row_version` en `transit_office_portal_brandings`, y `trg_audit_log` en las imágenes.

### 2. Excepciones de convención de datos

| Excepción | Tabla | Motivo |
|---|---|---|
| Sin `deleted_at` ni `deleted_by` | las dos | La marca no se borra: **retirar** deja `published` en NULL y conserva el borrador. Las imágenes son versiones inmutables que se conservan porque una URL publicada no debe romperse. |
| Sin `updated_at`, `updated_by` ni `row_version` | `transit_office_portal_brand_images` | Cada imagen es una versión inmutable: no se actualiza, solo se agrega otra (`version` crece por organismo y tipo). La concurrencia se resuelve en `transit_office_portal_brandings.row_version`. |
| `id` de imagen sin `DEFAULT uuidv7()` | `transit_office_portal_brand_images` | Lo genera la aplicación (`Guid.CreateVersion7()`), porque el id forma parte de la ruta de almacenamiento y de la URL pública y se necesita antes del INSERT. Mismo criterio que ADR-0080. |

`transit_office_portal_brandings` sí conserva `created_*`, `updated_*` y `row_version`. `published_by` queda sin FK a propósito: el rastro sobrevive al usuario.

### 3. Cambios inmediatos, sin caché por tiempo

**Decisión del usuario.** Marca, precios y listados del portal no usan caché por tiempo en `core-api`, BFF ni front (`PortalMarcaRepository` lee siempre de la base; el BFF de Persona Natural no guarda nada en caché). Lo publicado se ve en la siguiente carga.

La inmediatez se consigue sin romper el almacenamiento en caché de las imágenes: cada logo o banner tiene **id propio por versión**, y la ruta pública (`/api/v1/persona-natural/marca/imagenes/{id}`) responde `Cache-Control: public, max-age=31536000, immutable` y `ETag` con el sha256. Una imagen nueva es otra URL, así que nunca se ve una anterior. La referencia a esa URL sí llega fresca con cada carga de la marca.

**Consecuencia aceptada:** una imagen **retirada** o reemplazada deja de estar referenciada por la marca, pero quien ya tenga su URL puede seguir sirviéndola desde el caché del navegador o de un CDN hasta un año. La ruta pública solo entrega imágenes publicadas (`ImagenEstaPublicadaAsync`), por lo que un caché frío no la recupera tras el retiro; un caché caliente sí. No se ofrece purga.

### 4. Publicación sin moderación (riesgo conocido, sin resolver)

Un organismo puede publicar el nombre y el logo que quiera: no hay revisión previa ni aprobación de FLIT. Las validaciones técnicas (formato real por firma binaria, 512 KB logo y 2 MB banner, contraste 4,5:1) no revisan el contenido. Se deja **para decisión del PO**: moderación previa, lista de verificación posterior, o aceptar el riesgo. Mitigaciones existentes: el SuperAdmin puede retirar la marca; el borrador nunca sale por la ruta pública; hay rastro de quién publicó (`published_by`).

## Consecuencias

- Marca y precios del portal del OT se pueden entregar sin tocar marca blanca.
- Hay dos modelos de marca con la misma forma. Si se quiere unificar más adelante, la forma `jsonb` (`schemaVersion: 1`) está alineada a propósito para facilitar la migración.
- Ante código inválido, organismo sin marca o falla, el portal recibe 200 con `origen: "flit"`. No hay filtración de datos internos.
- Las imágenes antiguas se conservan en almacenamiento. No hay limpieza; crece con cada reemplazo. Pendiente de definir política de retención.

## Pendiente: dominio cruzado (Feature #14380, HU #14381)

**No es una decisión.** El Feature pide que el dominio propio del portal del organismo funcione con el **mismo tipo de configuración que marca blanca**. Eso significa reutilizar `admin.tenant_domains`, `DomainStateMachine` (pending, verified, active, failed; comprobación TXT; gracia) y el sello `X-Flit-Domain` del Gateway, con un `DomainContext`.

### Qué hay hoy en el código (develop `c94183722`)

- `admin.tenant_domains.tenant_id` es `NOT NULL` con FK a `identity.tenants`. El disparador `tr_tenant_domains_marca_blanca` (`BEFORE INSERT OR UPDATE OF tenant_id, host`) exige cabeza `MARCA_BLANCA`.
- Desde DDL 122 hay `purpose` (`'HUB'` o el código de un producto) y el índice único parcial es `(tenant_id, purpose) WHERE deleted_at IS NULL`. Un disparador exige que `purpose <> 'HUB'` exista en `platform.products`.
- `admin.v_active_network_domains` solo expone dominios `active` de tenants `MARCA_BLANCA`, `is_group_parent` y activos. `ListProvisioningAsync` (alimenta `/internal/domains/provisioning`, es decir, qué publica el borde) también filtra por cabezas `MARCA_BLANCA`.
- Novedad de develop: la activación de una red es un paso aparte del SuperAdmin (`ActivateNetworkHandler`). El certificado emitido ya no pasa el dominio a `active`; `DomainStateMachine.DecideCertificate` solo registra fechas. `NetworkActivationStatus` calcula lo pendiente por red a partir de `TenantProductFlags` (hub más un dominio por producto habilitado). La activación corta el acceso de los usuarios por el hub de FLIT, supuesto que no aplica a un portal público de Persona Natural.
- El Gateway sella `X-Flit-Domain` con el `Host` real; la API lo resuelve a `DomainContext` (`Flit` o `Network(headTenantId)`). `Domains:Reserved` rechaza dominios de FLIT (`*.flitsas.online`, `*.flitsas.com`).

### Qué falta decidir antes de implementar #14381

1. **A qué apunta un dominio de organismo.** `tenant_id` es obligatorio y el disparador impide que sea un organismo. Hay que elegir cómo se expresa «este dominio es del organismo X».
2. **Qué es `purpose` para un organismo.** Hoy un propósito no-HUB debe ser un código de `platform.products`. El portal Persona Natural no es un producto de red ni sigue la regla de activación por producto.
3. **Si la activación manual de red aplica.** `NetworkActivationStatus` y `ActivateNetworkHandler` asumen hub más productos de un tenant; un organismo no tiene `TenantProductFlags`.
4. **Qué devuelve el `DomainContext`** para un dominio de organismo (un tipo nuevo, o reutilizar `Network` con un identificador distinto), y cómo lo consumen el BFF de Persona Natural y `app/o/[codigo]/page.tsx`.
5. **Qué ve el borde.** `ListProvisioningAsync` debe incluir o no estos dominios; si no, no se emite certificado.
6. **Convivencia con `Domains:Reserved`** y unicidad global de `host`.

### Alternativas para decidir (sin recomendación vinculante)

**A. `admin.tenant_domains` con un destino alterno al tenant.** Hacer `tenant_id` nullable, añadir `transit_office_id` (FK a `catalogs.transit_offices`), `CHECK` de exactamente uno de los dos, y ajustar el disparador y los índices únicos parciales. Reutiliza tabla, máquina de estados, job y sello al máximo.
- Pros: un solo ciclo de dominios, un solo `uq_tenant_domains_host`, un solo job de comprobación.
- Contras: relaja una tabla de marca blanca (mismo problema que la Opción 2 de este ADR); toca `ITenantDomainRepository`, la vista y el provisioning; riesgo de regresión en las redes.
- Esfuerzo: M a L.

**B. Tabla hermana `admin.transit_office_domains`, reutilizando `DomainStateMachine` y el sello.** La máquina de estados ya es una clase estática de decisiones (`DomainStateMachine`); el repositorio y el job se parametrizan por tabla.
- Pros: marca blanca no cambia; coherente con la decisión 1 de este ADR; el acoplamiento es por la lógica, no por la tabla.
- Contras: otro job y otro endpoint interno de provisioning, o refactor para recorrer ambas tablas; hay que garantizar la unicidad de `host` entre dos tablas (índice cruzado o validación de aplicación).
- Esfuerzo: M.

**C. Un tenant `MARCA_BLANCA` por organismo.** Cada organismo con dominio propio se modela como cabeza de una red de una sola empresa, y el portal resuelve el organismo por esa cabeza.
- Pros: no cambia nada en dominios; usa todo lo existente.
- Contras: se crea un tenant artificial con efectos en alcance, usuarios y facturación; acopla organismos a la jerarquía de clientes; hay que mapear tenant a organismo (no se verificó que exista ese vínculo: la entidad `TransitOffice` no expone un `tenant_id`); arrastra el paso de activación de red de develop.
- Esfuerzo: S a M en dominios, L en consecuencias.

**Propuesta de implementación (2026-11-06), en `Propuesto`, a confirmar por el Líder Técnico:** alternativa **B**. Es la única que no toca las tablas ni los disparadores de marca blanca. Puntos: (1) `admin.transit_office_domains`; (2) sin `purpose`; (3) no aplica la activación manual de red: con TXT correcto y certificado emitido, el dominio pasa a activo; (4) `DomainContext` con resultado nuevo `Portal(transitOfficeId)`; (5) el provisioning incluye estos dominios sin cambiar lo que entrega de marca blanca; (6) `Domains:Reserved` aplica igual y la unicidad de `host` entre las dos tablas se garantiza en base de datos. Contrato: `docs/design/EPICA-14297-contrato-resto.md`.

## Archivos relevantes

- `services/core-api/src/Flit.Infrastructure/Persistence/Sql/Ddl/180-HU14375-portal-marca-organismo.sql`
- `services/core-api/src/Flit.Infrastructure/PortalMarca/PortalMarcaRepository.cs`
- `services/core-personanatural/src/Flit.PersonaNatural.Api/Punto/MarcaPunto.cs`
- `docs/design/EPICA-14297-contrato-portal-ot.md`

## Validación pendiente

- Líder Técnico: aceptar o rechazar este ADR y decidir la sección de dominio cruzado.
- Product Owner: moderación de nombre y logo; retención de imágenes antiguas.

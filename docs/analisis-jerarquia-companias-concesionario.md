# Épica #12235 — Tipo de cliente CONCESIONARIO y gestión de clientes hijos

> Generado: 2026-09-09 · rama `develop` @ `1480beb4` · **estado: análisis previo, nada implementado**
> · Alineado a la épica **#12235** (`[CONCESION] - Tipo de cliente Concesión y gestión de clientes hijos`),
> Sprint 6, `New`. **La épica no se modifica desde aquí**; este documento es el análisis técnico que la acompaña.
>
> Producido con el panel `/refine-requirement`: brief de hechos verificados en código → `po-agent` (Modo A)
> y `architecture-agent` en ronda ciega → `tech-lead-agent` como crítica cruzada → reconciliación, más las
> decisiones del PO humano recogidas en §8.

---

## 1. El alcance, tal como queda

Un cliente `tenant_type='CONCESIONARIO'` actúa como **entidad padre**: crea clientes hijos, gestiona su
configuración y sus usuarios, opera sus propios trámites como cualquier cliente, y ve de forma
**consolidada y en sólo lectura** los trámites y estadísticas de su red.

Dos formas de cabeza de grupo, **ambas en alcance**:

- **Concesión-compañía:** se le configuran los **Organismos de Tránsito que maneja**, y sus hijos solo
  pueden radicar en esa lista.
- **Concesión-OT:** un Organismo de Tránsito actúa como cabeza de grupo, y sus hijos radican
  **únicamente a ese OT**.

**Restricción dura: sin afectar el comportamiento actual del aplicativo.**

---

## 2. Respuesta corta

**Cuatro bloques, ~109 puntos firmes y 110–125 realistas.**

Lo que hay que entender antes de estimar: el trabajo no está donde parece. La relación padre-hija es
barata. Lo caro es que la visibilidad consolidada obliga a ampliar el mecanismo más frágil del
sistema, y que la verificación de "sin afectar el comportamiento actual" exige una infraestructura de
pruebas que este repositorio no tiene.

| Lo que dice la épica | Lo que es en el código |
|---|---|
| "Crear un nuevo tipo de cliente Concesión" · "Actualmente FLIT solo cuenta con un tipo de cliente estándar" | **Inexacto.** Hoy existen **tres** tipos y `CONCESIONARIO` **ya está en el CHECK de la base de datos**. No falta el tipo: falta el comportamiento. Por decisión del PO **no se crea ningún valor nuevo** (§8.1) |
| "El cliente Concesión tiene acceso a los trámites de todos sus hijos" | Ampliar el único mecanismo que separa hoy a una compañía de otra: ~159 filtros `Where` escritos a mano sobre un contrato donde **`null` significa "todos los tenants"** |
| "Estadísticas agregadas" y "reportes consolidados" | La analítica es **SQL crudo cross-schema** con esa misma semántica `null = global`, donde ningún filtro de EF protege. Es el punto de mayor riesgo del bloque B |
| "Sin afectar el comportamiento actual" | Verificable sólo con pruebas de integración contra Postgres real, **que este repo no tiene** |

---

## 3. Punto de partida verificado

Todo lo de esta tabla fue leído en código el 2026-09-09, no inferido.

| # | Hecho | Por qué importa aquí |
|---|---|---|
| 1 | `identity.tenants` **no tiene ninguna columna de jerarquía**. No existe concepto padre/hija en todo el repo | Se parte de cero |
| 2 | `tenant_type` **no tiene comportamiento de negocio**: sólo etiquetas de frontend y el CHECK `ck_tenants_tenant_type IN ('RENTING','CONCESIONARIO','FLIT')`. Únicas dos lecturas en runtime: `CompanyWriteRepository.cs:130` y `AdminAuditLogRepository.cs:87` (esta última con **otro** vocabulario: `COMPANY`/`TRANSIT_OFFICE`) | `CONCESIONARIO` ya existe como valor; lo que se construye es su primera regla de negocio |
| 3 | Los tenants OT viven en `identity.tenants` con `tenant_type='RENTING'` **forzado** (`TransitOfficeTenantWriteRepository.cs:83-87`). La verdad de "es un OT" es la fila en `admin.transit_office_profiles` | Un OT **no puede** marcarse CONCESIONARIO sin romper ese invariante → §5.4 |
| 4 | El JWT emite **un solo** claim `tenant_id`. El Gateway **no valida la firma en ningún ambiente** (`Flit.Gateway/Program.cs:64-68`) | Descarta meter la lista de hijas en el token |
| 5 | `TenantEnforcementMiddleware` sobrescribe el tenant desde el JWT y deja `(Guid? tenantId, bool isSuperAdmin)` en `HttpContext.Items`. **`null` = todos los tenants** | Es el contrato que hay que ampliar sin romperlo |
| 6 | Los listados hacen `if (tenantId is {} tid) query = query.Where(...)`. Un `null` que se cuele **no falla: devuelve datos ajenos con apariencia correcta**. `PlateHistoryScope.cs` deja constancia de que "ese error ya se cometió dos veces" | El modo de fallo es silencioso |
| 7 | El **RLS es decorativo**: ~61 `ENABLE ROW LEVEL SECURITY`, ~65 policies, **cero `FORCE ROW LEVEL SECURITY`**, y la app conecta como owner ⇒ Postgres la exime | No hay red debajo del código de aplicación |
| 8 | `POST /admin/companies` y `GET /admin/companies/index` son **SuperAdmin-only**. `CompanyOwnTenantFilter` valida **el primer `Guid` posicional** del handler | Las rutas nuevas con dos `Guid` no quedan cubiertas |
| 9 | En `POST /security/invitations`, si el invitante no es SuperAdmin el backend **fuerza `targetTenantId` = su propio tenant** | Guardarraíl anti-escalada que la épica **obliga a perforar** |
| 10 | `admin.tenant_transit_office_grants` (UNIQUE tenant+OT) ya define **a qué OT puede radicar una compañía**, y desde la HU #11228 el propio AdminCompany los administra sobre su tenant. `GET /api/v1/tramites/transit-offices` lo consume en el wizard | Media épica de "configuración de OT" **ya existe**; lo nuevo es acotar a los hijos |
| 11 | `TryResolveTenantId` está **duplicado como static local en 13+ archivos de endpoints** | No hay punto único de resolución de tenant: es la puerta trasera del diseño |
| 12 | **No hay Testcontainers ni Respawn** en ningún `.csproj`. 115 archivos de test usan `UseInMemoryDatabase`; 49 usan `WebApplicationFactory` | CHECK, triggers y SQL crudo **no son testeables hoy**. Una suite anti-fuga sobre InMemory pasa en verde sin probar nada |

---

## 4. Cómo se corta el trabajo

Cuatro bloques. La épica #12235 es el paraguas; estos son sus Features.

| Bloque | Qué entrega | Depende de |
|---|---|---|
| **F0 · Fundación** | Modelo de datos, invariantes, alcance tipado, punto único de resolución de tenant, infraestructura de pruebas, kill switches, ADR. **Sale a producción con la jerarquía vacía: cero cambio observable** | — |
| **A · Administración delegada** | La Concesión crea hijos, gestiona su configuración y sus usuarios | F0 |
| **C · Organismos de tránsito** | Lista de OT de la Concesión heredada por los hijos **y** OT como cabeza de grupo | F0 (**no** de A) |
| **B · Visibilidad consolidada** | Trámites en sólo lectura, estadísticas agregadas y reportes de la red | F0 + A |

La fundación va aparte a propósito. Si viviera dentro de A, sería camino crítico de todo, impediría el
paralelismo con C, y mezclaría dos riesgos de naturaleza distinta —escalada de privilegios en A, fuga
de datos en la fundación— en un mismo entregable. Cuando haya que recortar por tiempo se recorta del
bloque sobrecargado, y lo primero que cae de un bloque sobrecargado son las pruebas. Que es lo único
que aquí no se puede recortar.

F0 es verificable sin negocio: su criterio es *"un tenant sin jerarquía responde exactamente igual que
antes"*. Y dos de sus historias —punto único de resolución de tenant e infraestructura de pruebas— son
mejora neta del repositorio **aunque la épica se cancele**.

---

## 5. Diseño

### 5.1 La regla que ordena todo

> **La degradación aceptable es "la Concesión no ve a sus hijos".
> La degradación inaceptable es "alguien ve datos de otra compañía".**
> Todo default se elige en esa dirección.

### 5.2 Modelo de datos

`identity.tenants` gana `parent_tenant_id uuid NULL` (FK **`ON DELETE RESTRICT`**) e
`is_group_parent boolean NOT NULL DEFAULT false`. **Profundidad máxima 2**, forzada por la base de
datos: un hijo **no puede** crear hijos (§8.1).

Se descartaron la tabla puente con vigencia (un `valid_to` mal puesto deja visible a un ex-hijo —
*fuga con reloj*) y la closure table (sobre-diseño sobre la parte más frágil del sistema).

Sin backfill: los defaults reproducen exactamente el mundo actual.

**El `tenant_type` no gana valores nuevos.** Los hijos nacen `CONCESIONARIO`, que ya existe. La
capacidad de ser cabeza de grupo vive en su **propia columna**, no en el tipo — es lo que permite que
un OT sea Concesión sin dejar de ser `RENTING` (§5.4).

### 5.3 El alcance de lectura — el punto que decide el bloque B

Lo que **no** se hace, y por qué:

- **No se mete la lista de hijos en el JWT.** El Gateway no valida la firma, así que un claim con los
  tenants legibles es entregarle el alcance al atacante. Y el token es una foto del login:
  desvincular un hijo no surtiría efecto hasta que expirara — una fuga con temporizador.
- **No se amplía la semántica del `Guid?` actual, ni se tocan los ~159 filtros manuales.**
- **No se introduce filtro global de consulta.** Es la respuesta correcta a largo plazo, pero
  cambiaría los ~159 filtros de golpe. Va al Feature de endurecimiento (§10.2).

Lo que **sí** se hace: un canal nuevo al lado, explícito y *fail-closed*.

- Objeto de valor **`TenantScope`** sellado, con tres fábricas: `All()` (**`internal`**, sólo el
  middleware tras confirmar SuperAdmin), `Single(Guid)` y `Group(padre, hijos)`. Miembros
  `WriteTenantId`, `ReadTenantIds`, `CanRead`, `CanWrite`.
- Resolver que consulta **la base de datos**, nunca el header, el body ni el token. Si falla, cae a
  `Single(propio)` — **nunca a `All`**. Sin caché en v1.
- El middleware añade el canal nuevo y **conserva `tramites.tenantId` e `isSuperAdmin` con los mismos
  valores de hoy**. Para una Concesión, su propio id: nunca `null`, nunca el de un hijo.
- La lectura ampliada vive en **rutas nuevas**, no en parámetros añadidos a las existentes: así los
  endpoints actuales quedan literalmente intactos, la superficie ancha es enumerable con un `grep`, y
  admite policy, auditoría y límite de tasa propios.

De ahí sale la propiedad más valiosa del diseño: **cualquier consumidor que ignore el canal nuevo se
comporta idéntico a hoy. El olvido produce "la Concesión no ve a sus hijos", jamás una fuga.**

Dos reglas de code-review que hay que memorizar:

- **Conjunto de lectura vacío ⇒ `WHERE 1=0`.** Nunca "vacío ⇒ no filtro". Es la regla espejo del
  `null = todos`.
- **Toda escritura valida `CanWrite`, nunca `CanRead`.**

### 5.4 Los dos modelos de Organismo de Tránsito

> **2026-09-10 (D15):** el Modelo 2 queda **fuera**. Se conserva el texto por trazabilidad.

Ambos en alcance (§8.1), y comparten el mismo mecanismo: la lista efectiva de OT del hijo sale de su
padre, no de él mismo.

**Modelo 1 · Concesión-compañía con lista de OT.** Es el de la épica. Buena noticia: **media ya
existe**. `admin.tenant_transit_office_grants` es exactamente "los OT que maneja una compañía", el
AdminCompany ya los administra sobre su tenant desde la HU #11228, y el wizard ya se alimenta de ahí.
Lo nuevo es que **los hijos queden acotados a la lista del padre** y no puedan editarla.

**Modelo 2 · Concesión-OT.** Un tenant OT actúa como cabeza de grupo. **No se cambia su
`tenant_type`**: sigue `RENTING`, porque mutarlo rompería el invariante que hoy sostiene el módulo OT
y ensuciaría la detección de cambios de `CompanyWriteRepository.cs:130`. Su capacidad de ser padre
vive en la columna dedicada. Sus hijos reciben un grant automático **a ese OT y sólo a ése**.

**En ambos modelos, dos capas de defensa, no una:** grant marcado como gobernado por el sistema e
inmutable desde la UI del hijo, **y** aserción en el create del trámite. La segunda es necesaria
porque el SuperAdmin puede editar grants y porque hay datos que se manipulan fuera de la aplicación.

**Criterio que aporta la épica y se adopta:** quitar un OT de la configuración **no afecta los
trámites ya creados** en ese OT.

**Nota operativa:** una Concesión-OT verá los trámites de sus hijos por **dos caminos** —el módulo OT
como organismo receptor, y el alcance de grupo como cabeza de red—. QA debe verificar que coinciden y
que el de grupo no le muestra trámites que sus hijos radicaron a otro OT (no debería existir por la
capa 1, pero el test debe existir).

### 5.5 Sólo lectura sobre trámites, escritura sobre configuración

La distinción es del PO (§8.1) y hay que redactarla sin ambigüedad, porque la épica no la trae:

| Sobre los **trámites** del hijo | Sobre la **configuración** del hijo |
|---|---|
| **Sólo lectura.** No crea, no edita, no avanza estado, no firma, no anula, no reasigna, no carga ni elimina anexos | **Escritura.** La Concesión gestiona la configuración de sus hijos y sus usuarios |

**Los artefactos documentales quedan fuera** (§8.1): la Concesión ve trámites y estadísticas, no
descarga FUR, mandatos, compraventas, certificados de identidad ni anexos de sus hijos.

Eso tiene una consecuencia técnica afortunada que conviene conocer, porque explica por qué la
decisión abarata tanto el bloque:

> El FUR, el mandato, la compraventa, la solicitud virtual y el certificado de Kyverum **no tienen
> endpoint propio: son adjuntos** (`FurCommand.cs:31-34`, `AttachmentsCommand.cs:548-552`). Y
> `preview-url` devuelve una **URL prefirmada de S3 que es un portador anónimo**: sin tenant ni usuario
> en la firma, **sin revocación**, y con un `expiresAt` que la API **calcula localmente** porque el
> file-manager no devuelve el vencimiento real (`FileManagerAttachmentStorage.cs:120-147`). Además, las
> descargas de anexos y del FUR **no dejan hoy ningún rastro de auditoría**.
>
> Es decir: abrir artefactos habría abierto todos los documentos generados a la vez, por un canal sin
> revocación ni traza. Dejarlos fuera evita construir granularidad, auditoría de descarga y alineación
> de TTL que hoy no existen.

**Sigue haciendo falta** auditoría de los accesos consolidados (quién de la Concesión consultó qué
trámite de qué hijo y cuándo) — más ligera que la de descarga, pero necesaria para responder un
reclamo.

### 5.6 Autorización

Se descartaron el rol global nuevo (un usuario tiene **un** rol ⇒ habría que duplicar todos los
permisos de AdminCompany ⇒ deriva garantizada) y el permiso sobre AdminCompany (los permisos cuelgan
del rol global ⇒ se lo concedería a **todos** los AdminCompany del sistema).

Se adopta **capacidad derivada del dato**: policy nueva que exige AdminCompany **y** ser cabeza de
grupo **y** que el objetivo sea propio o hijo. `AdminCompanyPolicy` y `CompanyOwnTenantFilter` no se
tocan.

> **Consecuencia pendiente de decidir:** con esto, **todo** usuario AdminCompany de la Concesión podrá
> crear hijos, gestionar su configuración e invitar administradores. → **D3** (§8.2).

---

## 6. La restricción "sin afectar el comportamiento actual"

| Capa | Garantía |
|---|---|
| DDL | Columnas nullable con default; sin backfill; toda fila existente queda hoja sin padre |
| `tenant_type` | **No se toca.** Ni el CHECK, ni ningún valor nuevo, ni ninguna fila, ni sus dos lectores en runtime |
| JWT | Sin claims nuevos. El emisor de tokens no se modifica ⇒ ningún consumidor cambia |
| Middleware | Conserva los valores actuales; toda ruta nueva entra en su lista blanca, con test que lo verifica |
| Autorización | Policies y filtros existentes intactos; lo nuevo vive en rutas nuevas con policy nueva |
| Endpoints existentes | **No se les añaden parámetros.** La lectura ampliada va en rutas nuevas (§5.3) ⇒ el contrato OpenAPI actual no cambia y no se regeneran tipos del frontend |
| Repositorios | Los ~159 filtros manuales, intactos; sólo se añaden sobrecargas |
| Invitación existente | `POST /security/invitations` **no se modifica**: su forzado sigue siendo la garantía para todos los demás |
| SuperAdmin | Sin cambios de ningún tipo (§8.1, D6/D7) |
| Frontend | Todo condicionado a "es cabeza de grupo"; en falso, misma UI y mismas llamadas |

**Dos kill switches independientes**, uno por dato y otro por configuración, en columnas distintas —
la palanca de emergencia no puede ser el mismo bit que el invariante estructural protege.

**Y la parte incómoda:** verificar todo esto exige pruebas contra Postgres real —CHECK, triggers, SQL
crudo de analítica, aislamiento entre tenants— y **este repo no las tiene**. Una suite anti-fuga
contra EF InMemory pasa en verde porque cada test siembra su propia base: no prueba nada. Levantar esa
infraestructura es **prerequisito**, y es la historia más grande de F0.

---

## 7. Trazabilidad con la épica #12235

Los 18 criterios de la épica, mapeados. Ninguno queda huérfano.

| Criterio de la épica | Bloque | Observación |
|---|---|---|
| Existe un nuevo tipo de cliente "Concesión" diferenciado del estándar | F0 | **Se interpreta como "el tipo `CONCESIONARIO` adquiere comportamiento"**, no como valor nuevo (§8.1). La premisa de la épica —"FLIT solo cuenta con un tipo estándar"— es inexacta |
| Al crear un cliente se puede seleccionar el tipo "Concesión" | — | **Ya existe** hoy en el alta de compañías |
| Un cliente tipo Concesión puede operar trámites directamente | — | **Ya existe.** Ser cabeza de grupo no le quita nada |
| Al crear una Concesión se configuran los OT que maneja | C | Sobre `tenant_transit_office_grants`, que ya existe |
| Los hijos solo pueden crear trámites en los OT configurados | C | Lo genuinamente nuevo. Doble defensa (§5.4) |
| La Concesión puede agregar OT en cualquier momento desde su panel | C | **Ya existe** para el propio tenant (HU #11228) |
| Quitar un OT no afecta los trámites ya creados | C | Criterio aportado por la épica, adoptado |
| Los OT disponibles son los registrados en la plataforma | — | **Ya existe** (`catalogs.transit_offices`) |
| La Concesión puede crear hijos desde su panel | A | Perfora el guardarraíl de alta SuperAdmin-only |
| Los hijos quedan asociados exclusivamente a esa Concesión | F0 | Invariante en base de datos |
| Un hijo no puede pertenecer a más de una Concesión | F0 | Un solo `parent_tenant_id` |
| La Concesión puede gestionar la configuración de sus hijos | A | **Escritura**, a diferencia de los trámites (§5.5) |
| La Concesión crea y gestiona usuarios propios y de sus hijos | A | Perfora el forzado de tenant destino en la invitación |
| Acceso a los trámites de todos sus hijos en vista consolidada | B | **En sólo lectura** — la épica no lo dice, el PO sí (§8.1) |
| Acceso a estadísticas agregadas de sus hijos | B | El punto de mayor riesgo: SQL crudo (§10.3) |
| Los datos corresponden únicamente a su propia red | F0 + B | Es la regla anti-fuga; se verifica con la suite negativa |
| Reportes que consoliden la información de sus hijos | B | — |
| Reportes filtrables por hijo o agregados | B | — |

**Presente en la épica pero fuera de alcance:** "marca blanca" aparece en la descripción, no se
desarrolla en ningún criterio y **no entra todavía** por decisión del PO (§8.1).

**Ausente en la épica y necesario:** la fundación completa (F0, 28 SP). Es normal que una épica de
negocio no la contenga, pero significa que **quien la estime sin este documento la va a subestimar de
forma seria**.

---

## 8. Decisiones

### 8.1 Resueltas por el PO humano — 2026-09-09

| | Decisión |
|---|---|
| **S1** | **Los hijos son tenants completos con su propio NIT.** No son sucursales dentro del mismo tenant |
| **D2** | ~~Los artefactos quedan fuera~~ — **revocada para Marca Blanca el 2026-09-10** (`Requerimiento_Concesion_Marca_Blanca.md` RF-MB-05 / CA-MB-03; HU #12410). Para Concesión sigue pendiente (pendiente 13 del requerimiento). Texto original: **Los artefactos quedan fuera.** Sólo **trámites y estadísticas**; no se descargan documentos ni anexos de los hijos. *(Revierte una decisión previa del mismo día que sí habilitaba la descarga; se deja constancia porque cambió el alcance y el estimado del bloque B)* |
| **D6** | **El SuperAdmin conserva íntegras sus capacidades actuales**: gestiona la configuración de los hijos como la de cualquier compañía y sigue viendo todos los trámites y registros. No es capacidad nueva: es **restricción de no regresión**, y como tal se redacta y va a la suite de paridad |
| **D7** | **El SuperAdmin sigue sin poder descargar contenido de otras compañías**, tal como funciona hoy. Cero trabajo, y preserva el comportamiento actual |
| **D9** | ~~Van los dos modelos de OT (§5.4)~~ — **sustituida por D15 el 2026-09-10** |
| **D10** | ~~**No se crea ningún tipo nuevo.** Se usa `CONCESIONARIO`, que ya existe en base de datos. El CHECK no se toca~~ — **revocada por D19 el 2026-09-10 (tarde)** |
| **D11** | **Sólo lectura sobre los trámites de los hijos; escritura sobre su configuración** (§5.5) |
| **D12** | **Un hijo no puede crear hijos.** Profundidad 2, forzada por base de datos |
| **D13** | **"Marca blanca" no entra todavía.** Fuera de alcance |

**Resueltas por el PO humano — 2026-09-10**

| | Decisión |
|---|---|
| **D14** | **Los OT de una Concesión los asocia el SuperAdmin al momento de crear la empresa como Concesión.** Ni la Concesión ni sus hijos editan esa lista (rechazo en servidor: #12346 AC7; UI en solo lectura y selección en el flujo de alta: #12357 AC6/AC7). Las compañías sin jerarquía conservan la autogestión de #11228 (AC4 de #12346) |
| **D17** | **Marca Blanca tiene política de OT por exclusión** (todos los operables − bloqueos de la cabeza; RF-MB-03) y la Concesión por inclusión. La clase ~~vive en `identity.tenants.group_kind`~~ es el `tenant_type` de la cabeza (D19; HU #12406, F0). Bloqueos: HU #12407/#12408 (Feature C) |
| **D18** | **Los correos de una red Marca Blanca llevan tema de marca** (nombre, logo, colores, estructura fija, respaldo FLIT; RF-MB-07). Sin editor libre. Feature #12405 |
| **D19** | **La clase de la cabeza es un valor de `tenant_type`.** El catálogo pasa a `RENTING · CONCESIONARIO · FLIT · CONCESION · MARCA_BLANCA`; no existe `group_kind`; `is_group_parent` queda acoplado al tipo por CHECK (`is_group_parent = tenant_type IN (CONCESION, MARCA_BLANCA)`); el trigger rechaza el cambio de tipo hacia/desde/entre clases de cabeza con hijos vigentes. «Marcar cabeza» = fijar el tipo (alta o edición, SuperAdmin). El select «Tipo de compañía» muestra los 5 valores; `CONCESIONARIO` se etiqueta «Concesionario de vehículos». Los hijos eligen entre `RENTING` y `CONCESIONARIO`. Motivo: una sola pregunta al crear la compañía; se pierde solo el eje comercial de la cabeza para filtros/analítica. Revoca D10; revisión registrada en ADR-0057 |
| **D15** | **El OT ya no es cabeza de grupo.** Se retira el Modelo 2 de §5.4; solo queda el Modelo 1 (Concesión-compañía con lista de OT heredada). #12349 y #12352 en `Removed`; la cobertura anti-fuga de #12352 (AC3-AC6) la lleva #12322. Feature #12256 pasa de 24 a 18 SP |

### 8.2 Pendientes

| | Decisión | Por qué importa |
|---|---|---|
| **D1** | **¿Bajo qué figura jurídica accede la Concesión a los datos personales que aparecen en los trámites de sus hijos?** | Ley 1581: el titular autorizó el tratamiento al hijo, no al padre. Con los artefactos fuera (D2) la exposición baja mucho —ya no hay cédulas ni firmas descargables—, pero **los listados y las estadísticas siguen mostrando datos de personas** (nombres de comprador y vendedor, placa). Ya **no bloquea estimar**, pero sí hace falta una posición antes de exponer el bloque B en producción |
| **D3** | ¿Todo AdminCompany de la Concesión puede crear hijos, gestionar su configuración e invitar administradores, o hace falta distinguir "administrador del holding"? | Recomendación: empezar sin distinguir; el rol nuevo encarece y obliga a migrar usuarios |
| **D4** | ¿Plazo máximo aceptable entre desvincular un hijo y que la Concesión deje de verlo? | Sin caché es inmediato. Hace falta el número para fijarlo como criterio |
| **D5** | ¿Cuántas Concesiones y de qué tamaño en los primeros 6 meses? | Una con 5 hijos y una con 400 son problemas distintos |
| **D16** | Tras desvincular una hija, ¿la ex-cabeza sigue viendo los trámites radicados mientras estaba vinculada? | El trámite guarda `parent_tenant_id_at_creation` (HU #12406) para poder decidirlo después; #12355 AC3 hoy dice que deja de verlos |

---

## 9. Descomposición y esfuerzo

Fibonacci (1, 2, 3, 5, 8).

**F0 · Fundación — 28 SP**

| Historia | Tipo | SP |
|---|---|---|
| Punto único de resolución de tenant (elimina los 13 duplicados) + test de arquitectura | BACKEND | 3 |
| Infraestructura de pruebas de integración con Postgres real | BACKEND | 8 |
| DDL: jerarquía, FK RESTRICT, CHECK, trigger bidireccional, índice parcial | DATABASE | 5 |
| `TenantScope` + resolver sin caché + integración en middleware conservando valores | BACKEND | 5 |
| Suite de paridad + suite negativa de fuga (padre/hijo/**hermano↔hermano**/ajeno) | BACKEND | 5 |
| ADR + kill switches + auditoría de vínculo y desvínculo | BACKEND | 2 |

**A · Administración delegada — 26 SP**

| Historia | Tipo | SP |
|---|---|---|
| Resolución del tenant destino de la invitación, 3 ramas exhaustivas + lista blanca de rol por id | BACKEND | 5 |
| Endpoints de hijos con validación explícita de parentesco + policy de cabeza de grupo | BACKEND | 5 |
| Gestión de la cabeza de grupo por SuperAdmin; **adopción SuperAdmin-only** | BACKEND | 3 |
| Gestión de la **configuración** de los hijos por la Concesión | BACKEND | 5 |
| UI de red: listado de hijos, alta, edición, desactivación, invitar administradores | FRONTEND | 5 |
| UI SuperAdmin: marcar cabeza de grupo, vincular y desvincular | FRONTEND | 3 |

**C · Organismos de tránsito — 23 SP**

| Historia | Tipo | SP |
|---|---|---|
| Grant gobernado por el sistema, inmutable desde la UI del hijo | DATABASE | 2 |
| Lista de OT de la Concesión heredada y acotada a los hijos | BACKEND | 5 |
| Aserción en el create del trámite + errores de OT no permitido | BACKEND | 5 |
| Habilitar un OT como cabeza de grupo sin tocar su `tenant_type` | BACKEND | 3 |
| Quitar un OT sin afectar trámites ya creados | BACKEND | 3 |
| Wizard: OT acotados para el hijo, no editables | FRONTEND | 2 |
| Conciliación de los dos caminos de visibilidad de la Concesión-OT | BACKEND | 3 |

**B · Visibilidad consolidada — 32 SP** (+3 sujetos a D1)

| Historia | Tipo | SP |
|---|---|---|
| Lectura consolidada de trámites en rutas nuevas + 403 en escrituras | BACKEND | 5 |
| Modo sólo lectura transversal en el módulo de trámites | FRONTEND | 8 |
| Selector de alcance + columna de compañía + preferencia por usuario | FRONTEND | 3 |
| **Estadísticas agregadas de la red** (SQL crudo parametrizado + test por consulta) | BACKEND | 8 |
| **Reportes consolidados, filtrables por hijo o agregados** | BACKEND | 5 |
| Auditoría de acceso consolidado | BACKEND | 3 |
| Enmascaramiento de datos personales en listados y reportes | BACKEND | *3, sujeto a D1* |

**Total: 109 SP firmes · 110–125 realistas.**

Tres historias merecen mención aparte porque se subestiman de forma sistemática:

- **Infraestructura de pruebas (8 SP)** — sin ella, la garantía central del diseño no es verificable.
- **Modo sólo lectura transversal en el frontend (8 SP)** — no es un flag en un DTO. Es cada botón,
  menú contextual, acción masiva, mutación optimista, zona de carga de anexos, firma, anulación y
  reasignación del módulo de trámites. Y no basta ocultar: nada debe **disparar** una mutación, porque
  un 403 que el usuario ve es un error, no una defensa.
- **Estadísticas agregadas (8 SP)** — es el único punto donde el compilador no protege nada.

---

## 10. Riesgos

1. **Fuga entre compañías competidoras.** Riesgo dominante, y el producto está estructuralmente
   predispuesto: el fallo no lanza excepción, entrega datos. El caso que más fácil se cuela no es el
   que la épica menciona, sino **hermano contra hermano**: dos hijos de la misma Concesión que no
   deben verse entre sí.
2. **El RLS no protege y el Gateway no valida el JWT.** Esta épica *depende* de esa debilidad y no la
   arregla. **Recomendación formal: abrir un Feature de endurecimiento aparte** —rol de aplicación
   no-owner + `FORCE ROW LEVEL SECURITY`, validación real de firma en el Gateway, filtro global de
   tenant— **antes de que el bloque B llegue a producción**. El bloque B multiplica el costo de
   cualquier fallo futuro de aislamiento.
3. **Las estadísticas y los reportes van por SQL crudo cross-schema**, con la misma semántica
   `null = global` y sin protección del compilador. Un `IN` mal construido suma terceros sin error.
   Mitigación obligatoria: parámetro de array, prohibición de interpolar ids, y test de integración
   con datos de tres tenants por cada consulta ampliada.
4. **La invitación cross-tenant es una perforación obligatoria.** Hoy el forzado del tenant destino es
   el guardarraíl anti-escalada más fuerte que queda en pie. Con una bifurcación fea: si la ruta nueva
   delega en el servicio actual sin cambiarlo, la invitación se creará **en el tenant de la
   Concesión**, y el "administrador del hijo" acabará siendo administrador **del padre** — acceso de
   escritura completo entregado a un tercero, presentado como éxito.
5. **`CompanyOwnTenantFilter` da falsa confianza en rutas con dos `Guid`**: valida el primero e ignora
   el segundo. La validación de parentesco debe ser explícita en el handler, con test negativo por ruta.
6. **La adopción de un tenant existente debe ser SuperAdmin-only, sin excepción.** Si una Concesión
   pudiera fijar el padre de un tenant arbitrario, adoptaría cualquier compañía y obtendría lectura
   sobre ella al instante. Crear un hijo nuevo sí puede ser suyo: el tenant nace vacío.
7. **Regresión más probable:** `CompanyWriteRepository.cs:130` —una de las dos únicas lecturas de
   `tenant_type` en runtime, justo en el camino de creación de compañías que el bloque A modifica.
8. **Colisión de migraciones EF** con otra ola en paralelo sobre el mismo repo. Mitigación: la
   historia de DDL primero, en su propio PR, mergeada antes de abrir el resto.
9. **Deriva de la lista blanca del middleware.** Una ruta nueva que se olvide no da error: da
   `tenantId = null`, que en los listados significa **todos los tenants**. La épica añade varias
   oportunidades de cometerlo; el test que enumera rutas es obligatorio.

---

## 11. Fuera de alcance

Marca blanca (D13) · descarga de documentos y anexos de los hijos (D2) · nietos, profundidad > 2 (D12)
· herencia de representantes legales, mandatarios, baúl de firmas y parámetros documentales · que la
Concesión radique a nombre de un hijo · facturación consolidada · que el SuperAdmin descargue
contenido ajeno (D7) · rediseñar el aislamiento multi-tenant del producto —es el Feature de
endurecimiento del riesgo 2, y **no debe esconderse dentro de un Feature de negocio**— · migración de
grupos "de facto" existentes · autoservicio de registro.

---

## 12. Estado y siguiente paso

**Refinable a Historias de Usuario: F0, A y C, ya.** El bloque B también es estimable; sólo su
historia de enmascaramiento depende de **D1**.

Pendiente antes de escribir los criterios de A: **D3** (quién dentro de la Concesión puede crear
hijos) y **D4** (plazo de desvinculación). **D5** ayuda a dimensionar rendimiento.

**Recomendación de arranque:** las dos primeras historias de F0 —punto único de resolución de tenant e
infraestructura de pruebas de integración— son mejora neta del repositorio **aunque la épica se
cancele**. Son el único trabajo aquí sin riesgo de desperdicio.

La épica #12235 **no se modifica desde este análisis**. Crear los Features hijos y sus Historias es
gate del PO humano.

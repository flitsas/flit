# ADR-0057: Jerarquía de clientes padre-hija con alcance de lectura tipado y cerrado por defecto

**Fecha**: 2026-09-10
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (pendiente), Product Owner (Épica #12235), Architecture Agent
**Tags**: arquitectura, backend, database, seguridad, multi-tenant, modulo-admin, modulo-tramites

> **Citar este ADR por slug**, no por número: el directorio `services/core-api/docs/adr/` tiene
> números duplicados (0030, 0033, 0036, 0050, 0053 y otros). El slug
> `ADR-0057-jerarquia-de-clientes-alcance-tipado-fail-closed` es la referencia estable.

## Revisión 2026-09-10 (tarde) — la clase de la cabeza es un valor de `tenant_type`

Decisión del usuario/PO del 2026-09-10 (segunda charla, tras el requerimiento escrito
`docs/Requerimiento_Concesion_Marca_Blanca.md`), que **revoca D10** y adopta parcialmente la
Opción 3 de este ADR:

- `identity.tenants.tenant_type` amplía su catálogo de 3 a 5 valores:
  `RENTING | CONCESIONARIO | FLIT | CONCESION | MARCA_BLANCA` (DDL 109, HU #12406). Los dos
  nuevos son **tipos de cabeza de red**; los tres anteriores conservan su significado comercial y
  son los únicos admitidos para clientes hijos y clientes sin jerarquía.
- No existe columna `group_kind`. `is_group_parent` se conserva (lo leen el resolver de alcance y el
  trigger) pero queda **acoplado al tipo** por CHECK `ck_tenants_group_parent_by_type`:
  `is_group_parent = (tenant_type IN ('CONCESION','MARCA_BLANCA'))`. Fail-closed: quien escribe un
  tipo de cabeza escribe la marca en la misma fila; no hay trigger que la corrija en silencio.
- «Marcar cabeza» = fijar el tipo en Concesión/Marca Blanca (alta o edición, exclusivo SuperAdmin);
  «desmarcar» = cambiar a un tipo no-cabeza. El trigger `trg_tenant_hierarchy_depth()` rechaza todo
  cambio de tipo hacia/desde/entre clases de cabeza mientras haya hijos vigentes; sin hijos se
  acepta y se audita (`admin.tenant_config_audit_logs`, old/new).
- En código, `TenantScope.Group` sigue exponiendo `GroupKind` (Concesion | MarcaBlanca), ahora
  **derivado de `tenant_type`** en `DbTenantScopeResolver`, leído de BD en cada petición.
- Consecuencias asumidas: un OT o una empresa de renting ya no puede ser cabeza «sin cambiar de
  tipo» (D9 ya estaba revocada: el OT no es cabeza); el tipo deja de ser decorativo en runtime
  (decide política de organismos, marca, dominio y tema de correo); el select de tipo de la consola
  del SuperAdmin muestra los 5 valores con la etiqueta «Concesionario de vehículos» para el dealer
  (HU #12357).
- Motivo: un solo control y una sola pregunta al crear la compañía; el eje comercial de la cabeza
  (que un renting sea a la vez Marca Blanca) se pierde solo a efectos de filtros y analítica.

Las secciones siguientes se conservan como historia de la decisión original; donde digan «atributo
independiente de `tenant_type`» o «D10», prevalece esta revisión.

## Contexto

La Épica #12235 pide que un cliente `CONCESIONARIO` actúe como **cabeza de grupo**: crea clientes
hijos, gestiona su configuración y sus usuarios, y ve **en sólo lectura** los trámites y estadísticas
de su red. Restricción dura del PO: **sin afectar el comportamiento actual**.

El aislamiento multi-tenant del producto descansa hoy en cuatro hechos verificados en código
(`docs/analisis-jerarquia-companias-concesionario.md` §3):

- `identity.tenants` no tenía ninguna noción de jerarquía; `tenant_type` no tiene comportamiento de
  negocio (sólo etiquetas y el CHECK `RENTING | CONCESIONARIO | FLIT`).
- `TenantEnforcementMiddleware` deja `(Guid? tenantId, bool isSuperAdmin)` en `HttpContext.Items`
  donde **`null` significa "todos los tenants"**. Los ~159 filtros `Where` manuales no fallan ante un
  `null` que se cuele: devuelven datos ajenos con apariencia correcta.
- El **RLS es decorativo**: ~65 policies, cero `FORCE ROW LEVEL SECURITY`, y la app conecta como
  owner. No hay red debajo del código de aplicación.
- `TryResolveTenantId` está **duplicado como static local en 10+ archivos de endpoints**; no hay
  punto único de resolución. El JWT emite un solo claim `tenant_id` y el **Gateway no valida la
  firma** en ningún ambiente.

Cualquier ampliación de visibilidad debe, por tanto, construirse asumiendo que el único mecanismo
de aislamiento es el código de aplicación y que su modo de fallo es silencioso. La regla que ordena
el diseño: *la degradación aceptable es "la Concesión no ve a sus hijos"; la inaceptable es "alguien
ve datos de otra compañía"* (§5.1).

## Decisión

Se adopta una jerarquía padre-hija de **profundidad 2 forzada por la base de datos**, con un
**alcance de lectura tipado (`TenantScope`) resuelto desde la base y cerrado por defecto**, expuesto
únicamente a través de **rutas nuevas** y gobernado por interruptores independientes del bit
estructural. Cuatro partes:

### 1. Modelo de datos

`identity.tenants` gana dos columnas (DDL `107-HU12318-tenant-parent-hierarchy.sql`, HU #12318):

- `parent_tenant_id uuid NULL` — un único padre por cliente (columna escalar, sin tabla puente).
  FK `fk_tenants_parent_tenant` con `ON DELETE RESTRICT`; CHECK `ck_tenants_parent_not_self`.
- `is_group_parent boolean NOT NULL DEFAULT false` — la capacidad de ser cabeza de grupo es un
  **atributo independiente de `tenant_type`**. No se crea ningún tipo nuevo ni se toca el CHECK: los
  hijos nacen `CONCESIONARIO`, que ya existe (D10), y un OT (`RENTING` forzado por
  `TransitOfficeTenantWriteRepository`) puede ser cabeza de grupo sin cambiar de tipo (D9).
- Trigger `tr_tenants_hierarchy` → `identity.trg_tenant_hierarchy_depth()`, `BEFORE INSERT OR UPDATE
  OF parent_tenant_id, is_group_parent`: (a) el padre existe, es cabeza de grupo y no tiene padre;
  (b) una cabeza de grupo no cuelga de nadie; (c) quien tiene hijos no puede dejar de ser cabeza ni
  recibir padre. Sin (c) la profundidad 2 sería burlable en dos pasos. Rechazos con
  `ERRCODE = check_violation` (D12).
- **Sin backfill.** Los defaults (`NULL` / `false`) reproducen exactamente el mundo actual.

### 2. `TenantScope`: alcance tipado

Objeto de valor **sellado** con tres fábricas y cuatro miembros:

- `All()` — **`internal`**; sólo el middleware la invoca tras confirmar SuperAdmin.
- `Single(Guid id)` — el tenant propio; alcance por defecto de cualquier usuario.
- `Group(Guid padre, IReadOnlyCollection<Guid> hijos)` — cabeza de grupo con su red.
- `WriteTenantId` (siempre el propio), `ReadTenantIds`, `CanRead(Guid)`, `CanWrite(Guid)`.

El tipo hace imposible expresar "todos" por accidente: no hay `null`, no hay lista vacía que
signifique "sin filtro".

### 3. Resolución cerrada por defecto

- El resolver consulta **la base de datos** (`parent_tenant_id`, `is_group_parent`), **nunca** el
  header, el body ni el token. Sin caché en v1.
- Cualquier fallo del resolver (tenant inexistente, error de datos, excepción) cae a
  `Single(propio)`, **nunca a `All()`**.
- **Conjunto de lectura vacío ⇒ cero filas** (`WHERE 1=0`). Nunca "vacío ⇒ sin filtro". Es la regla
  espejo del `null = todos`.
- **Toda escritura valida `CanWrite`, nunca `CanRead`.** La Concesión escribe sobre la configuración
  de sus hijos; sobre sus trámites sólo lee (D11).
- El middleware añade el canal nuevo y **conserva `tramites.tenantId` e `isSuperAdmin` con los
  mismos valores de hoy**. Para una Concesión: su propio id, nunca `null`, nunca el de un hijo.

### 4. Superficie ampliada acotada

- La lectura ampliada vive en **rutas nuevas**, no en parámetros añadidos a endpoints existentes.
  Los endpoints actuales quedan intactos, el contrato OpenAPI vigente no cambia, y la superficie
  ancha es enumerable con un `grep` y admite policy, auditoría y límite de tasa propios.
- **Punto único de resolución del tenant** que sustituye las copias de `TryResolveTenantId`, con
  test de arquitectura que prohíbe reintroducirlas.
- **Dos interruptores independientes** en columnas distintas del bit estructural `is_group_parent`:
  uno para el **alcance de grupo** (lectura consolidada) y otro para la **configuración heredada**
  (lista de OT del padre acotada a los hijos). La palanca de emergencia no puede ser el mismo bit que
  el invariante protege; apagar un interruptor degrada a "la Concesión no ve/hereda", nunca a fuga.
- Autorización por **capacidad derivada del dato**: policy nueva que exige AdminCompany **y**
  `is_group_parent` **y** que el objetivo sea propio o hijo. `AdminCompanyPolicy` y
  `CompanyOwnTenantFilter` no se tocan. La **adopción** de un tenant existente es SuperAdmin-only.

## Alternativas consideradas

### Opción 1: Tabla puente `tenant_hierarchy` con vigencia (`valid_from` / `valid_to`)

**Pros:** historial de vínculos gratis; permite re-vincular sin perder rastro.
**Cons:** un `valid_to` mal puesto o un reloj desfasado deja visible a un ex-hijo (*fuga con reloj*);
cada lectura debe filtrar por vigencia; el invariante "un padre a la vez" pasa a depender de un
índice parcial temporal difícil de razonar.
**Esfuerzo:** M
**Riesgos:** fuga silenciosa por temporalidad; más superficie en la parte más frágil del sistema.

### Opción 2: Closure table (`ancestor`, `descendant`, `depth`)

**Pros:** consultas de árbol de cualquier profundidad en un `JOIN`.
**Cons:** sobre-diseño para profundidad 2 (D12); dos tablas que mantener consistentes por trigger;
duplica la verdad del vínculo y multiplica los caminos por los que un id ajeno entra a un `IN`.
**Esfuerzo:** L
**Riesgos:** inconsistencia entre la tabla base y el cierre; costo alto sobre cero beneficio real.

### Opción 3: Tipo nuevo de tenant (`CONCESION` / `CONCESION_HIJO`)

**Pros:** la épica lo describe así; fácil de leer en un listado.
**Cons:** obliga a tocar el CHECK y los dos lectores de `tenant_type` en runtime
(`CompanyWriteRepository.cs:130`, `AdminAuditLogRepository.cs:87`); un OT no podría ser cabeza de
grupo sin romper el forzado `RENTING`; mezcla "qué es" con "qué puede". Rechazada por el PO (D10).
**Esfuerzo:** M
**Riesgos:** regresión en el alta de compañías; imposibilita la Concesión-OT (D9).

### Opción 4: Lista de hijos como claim del JWT

**Pros:** sin consulta a base en cada request; alcance disponible en el Gateway.
**Cons:** el Gateway **no valida la firma**: un claim de tenants legibles es entregar el alcance al
atacante; el token es una foto del login, desvincular un hijo no surte efecto hasta que expire
(*fuga con temporizador*); cambia el emisor de tokens, es decir, a todos los consumidores.
**Esfuerzo:** S
**Riesgos:** escalada de lectura trivial; violación directa de "sin afectar el comportamiento actual".

### Opción 5: Ampliar la semántica del `Guid?` actual o introducir un filtro global de consulta

**Pros:** el filtro global es la respuesta correcta a largo plazo: protege también a los ~159 filtros
manuales de hoy.
**Cons:** cambiaría los ~159 filtros de golpe dentro de un Feature de negocio; ampliar el `Guid?` a
"uno, varios o todos" conserva el `null = todos` y añade estados intermedios ambiguos; no es
verificable sin la infraestructura de pruebas contra Postgres real que el repo aún no tiene.
**Esfuerzo:** L
**Riesgos:** regresión masiva silenciosa. **Se difiere al Feature de endurecimiento** (§10.2 del
análisis: rol no-owner + `FORCE ROW LEVEL SECURITY`, firma real en el Gateway, filtro global).

### Opción 6: Herencia de profundidad > 2 (nietos)

**Pros:** cubriría holdings con sub-redes sin rediseño futuro.
**Cons:** la resolución de alcance pasa de una lectura a un recorrido recursivo; la aserción de
parentesco deja de ser `parent == yo` y se vuelve un cierre transitivo en cada handler; multiplica
las combinaciones de la suite anti-fuga. Rechazada por el PO (D12).
**Esfuerzo:** L
**Riesgos:** más caminos de fuga hermano/primo; sin caso de negocio que lo justifique.

## Tradeoff aceptado

Se acepta **duplicar el canal de alcance** (el `Guid?` histórico y el `TenantScope` nuevo conviven)
a cambio de una propiedad que ninguna otra opción ofrece: **cualquier consumidor que ignore el canal
nuevo se comporta idéntico a hoy, y el olvido produce "la Concesión no ve a sus hijos", jamás una
fuga**. Se acepta también no arreglar en esta épica el RLS decorativo ni la firma del JWT — esta
épica *depende* de esa debilidad y no la corrige — con la condición formal de que el Feature de
endurecimiento se abra **antes de que la visibilidad consolidada (Feature #12257) llegue a
producción**.

## Consecuencias

### Lo que se gana

- La migración sale a producción con la jerarquía vacía: **cero cambio observable** para tenants sin
  padre ni hijos.
- La profundidad 2 y la exclusividad del padre las fuerza Postgres, no la aplicación ni la UI.
- Un solo lugar donde se decide "quién es el tenant" y un tipo que no puede expresar "todos" por
  accidente.
- Superficie de lectura ampliada enumerable, auditable y apagable sin tocar el dato estructural.
- Dos historias de F0 (punto único de resolución e infraestructura de pruebas) son mejora neta del
  repo aunque la épica se cancele.

### Lo que se pierde

- Dos canales de alcance conviviendo hasta el Feature de endurecimiento; deuda declarada.
- Sin caché: una consulta adicional por request en rutas de grupo (aceptable en v1; D4/D5 fijan el
  número).
- Los endpoints existentes no ganan la vista consolidada: el frontend consume rutas nuevas.
- Sin nietos ni re-parenting con historial (Opciones 1, 2 y 6).

### Riesgos que este ADR reconoce y exige mitigar

- **Fuga hermano-hermano.** Dos hijos de la misma Concesión no deben verse entre sí; es el caso que
  más fácil se cuela. La suite negativa debe cubrir padre/hijo/**hermano↔hermano**/ajeno por ruta.
- **Invitación cross-tenant.** `POST /security/invitations` fuerza hoy `targetTenantId` = propio. La
  ruta nueva de la Concesión **no puede delegar en ese servicio sin cambiarlo**, o el "administrador
  del hijo" acabará siendo administrador **del padre**. Resolución del tenant destino con tres ramas
  exhaustivas y lista blanca de rol por id. El endpoint existente no se modifica.
- **`CompanyOwnTenantFilter` con dos `Guid`.** Valida el primer `Guid` posicional e ignora el
  segundo. En rutas `/{tenantId}/children/{childId}` la validación de parentesco debe ser explícita
  en el handler, con test negativo por ruta.
- **Deriva de la lista blanca del middleware.** Una ruta nueva olvidada no da error: da
  `tenantId = null` = todos. Test que enumera rutas registradas contra la lista blanca, obligatorio.
- **Analítica por SQL crudo cross-schema.** Parámetro de array, prohibición de interpolar ids y test
  de integración con datos de tres tenants por cada consulta ampliada.

### Cambios operacionales

- La suite de paridad ("un tenant sin jerarquía responde igual que antes") y la suite anti-fuga
  corren contra **Postgres real** (Testcontainers); una suite sobre EF InMemory no prueba nada aquí.
- La historia de DDL (#12318) se mergea primero y en su propio PR para evitar colisión de
  migraciones con olas paralelas.
- Vínculo y desvínculo de hijos, y todo acceso consolidado, dejan rastro en auditoría.

## Pendiente de decisión de negocio

- **D1 · Ley 1581.** ¿Bajo qué figura jurídica accede la Concesión a los datos personales que
  aparecen en los trámites de sus hijos? El titular autorizó el tratamiento al hijo, no al padre.
  Con los artefactos fuera (D2) la exposición baja, pero listados y estadísticas siguen mostrando
  nombres y placas. **No bloquea F0 ni Feature #12254; sí bloquea exponer la visibilidad consolidada
  en producción.** La historia de enmascaramiento queda sujeta a esta decisión. Este ADR **no**
  diseña ningún bypass: ante silencio, el default es no exponer.
- **D3 · Quién administra la red.** Supuesto de arranque adoptado: **cualquier AdminCompany de la
  Concesión** puede crear hijos, gestionar su configuración e invitar administradores (capacidad
  derivada del dato, sin rol nuevo). Si el PO decide distinguir "administrador del holding", será un
  ADR nuevo con `Supersedes` parcial sobre la parte 4 de este.

## ADRs relacionados

- `ADR-0056-generacion-documental-standalone` — precedente de "rutas nuevas, pipeline existente
  intacto" que este ADR reutiliza como patrón.
- Feature de endurecimiento multi-tenant (por abrir) — deberá emitir ADR propio con `Supersedes`
  sobre la coexistencia de canales descrita en §Tradeoff.

## Notas para agentes

- **Database Agent**: el DDL 107 es la referencia; los interruptores de alcance/herencia van en
  columnas propias, nunca reutilizar `is_group_parent`. Grants de OT heredados marcados como
  gobernados por el sistema.
- **Backend Agent**: `TenantScope.All()` es `internal`; ningún endpoint la invoca. Resolver desde
  base, fallo ⇒ `Single(propio)`. Vacío ⇒ `WHERE 1=0`. Escrituras validan `CanWrite`. Rutas nuevas
  entran en la lista blanca del middleware. Cero copias nuevas de `TryResolveTenantId`.
- **Frontend Agent**: todo condicionado a "es cabeza de grupo"; en falso, misma UI y mismas
  llamadas. Modo sólo lectura sobre trámites de hijos: nada debe **disparar** una mutación; un 403
  visible es un error, no una defensa.
- **QA Agent**: suite negativa padre/hijo/hermano/ajeno por ruta ampliada; paridad de la
  Concesión-OT por sus dos caminos de visibilidad; test de lista blanca del middleware; test del
  trigger en sus tres ramas (a/b/c) contra Postgres real.
- **Security Agent**: revisar que ninguna ruta lee alcance desde header/body/token; verificar la
  invitación cross-tenant y las rutas con dos `Guid`; confirmar auditoría de acceso consolidado.
- **Infra Agent**: Testcontainers en CI (Docker disponible en el runner); sin cambios de despliegue
  para la migración 107 (aditiva, sin backfill, reversible).

## Referencias externas

- Épica #12235 · Features #12254 (F0 Fundación), #12255, #12256, #12257 · HUs #12318–#12323.
- `docs/analisis-jerarquia-companias-concesionario.md` (§3, §5, §8, §10, §11).
- `services/core-api/src/Flit.Infrastructure/Persistence/Sql/Ddl/107-HU12318-tenant-parent-hierarchy.sql`.
- Ley 1581 de 2012 (protección de datos personales, Colombia) — para D1.

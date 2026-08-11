# ADR-0043: La elegibilidad para documentos personalizados es un interruptor propio del tenant, desacoplado del canal de notificaciones

**Fecha**: 2026-08-11
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (pendiente), Product Owner (origen del requisito: ola de Notificaciones)
**Tags**: arquitectura, backend, base-de-datos, modulo-companias, documental, notificaciones, multi-tenant, deuda-tecnica
**Feature**: #11348 — `[ADMIN] - Notificaciones: enrutamiento por canal y adaptador Renting`
**HU**: #11362 (este ADR) · #11357 (migración del interruptor propio) · Bug #11311 (el canal no enruta nada)

**Sustituye parcialmente el mecanismo de**: [ADR-0042-documentos-personalizados-por-compania] (**Propuesto**) —
ver §«Relación con ADR-0042». Se conserva **íntegra** su decisión de negocio y su modelo documental; se sustituye
**solo** la fuente del interruptor de habilitación por compañía, que aquel ADR situó en
`admin.tenant_operational_policies.notification_channel`.

**Relacionado**: Bug #11311 (ningún envío de correo consulta el canal) · Feature #11347 (catálogo de
notificaciones) · Feature #11349 (banco de pruebas de envío).

---

## Contexto

`admin.tenant_operational_policies.notification_channel` (`varchar(20) NOT NULL DEFAULT 'flit_smtp'`, valores en
base de datos `flit_smtp` / `tenant_api`, wire `FLIT_SMTP` / `TENANT_API`, enum
`Flit.Admin.Domain.Companies.Settings.NotificationChannel`) tiene hoy **doble semántica**, y las dos no tienen
nada que ver entre sí:

1. **Nominalmente** es el canal de enrutamiento de notificaciones (RF09): por dónde debe salir el correo de una
   compañía, si por el SMTP de FLIT o por la API del propio cliente.
2. **En la práctica** su **único consumidor** en todo el backend es
   `src/Flit.Admin.Application/Companies/PersonalizedDocuments/PersonalizedDocumentChannelGuard.cs`, que lo usa
   como **interruptor de la funcionalidad de documentos personalizados**:
   `IsWriteEnabledAsync` devuelve `settings?.NotificationChannel == NotificationChannel.TenantApi`. Lo invocan los
   cuatro handlers de escritura de versiones (crear, confirmar, activar, desactivar); el listado `GET` a propósito
   no lo aplica. El propio comentario de la clase declara que es «fuente ÚNICA de verdad del interruptor de la
   funcionalidad».

Hechos verificados que enmarcan la decisión:

- **Ningún envío de correo lee ese campo.** Los seis disparadores de correo de FLIT salen todos por el SMTP
  global. Es exactamente el **Bug #11311**, ya radicado: el selector de canal de la interfaz de configuración no
  tiene efecto sobre el correo.
- Fuera del guardián, las únicas referencias al campo son de transporte y persistencia (`SettingsWire`,
  `SettingsMapper`, `SettingsDiff`, `TenantSettingsCodes`, `UpdateTenantSettingsHandler`, el `TenantSettings` por
  defecto y el seed de `TransfersEndpoints`). No hay ninguna otra regla de negocio colgada de él.
- Un tenant **sin** fila de política operativa cae al default `flit_smtp` ⇒ documentos personalizados
  deshabilitados. El default es, por tanto, seguro en ambas semánticas.
- `admin.tenant_operational_policies` es tabla de **fila única por tenant**, con `row_version bigint` como token
  de concurrencia (el repositorio **no usa `xmin`** en ningún sitio) y trigger de auditoría `public.trg_audit_log()`.
- **Ya existen en esa misma tabla dos interruptores de capacidad con exactamente la forma que aquí se propone**:
  `signature_vault_enabled boolean NOT NULL DEFAULT false` y `plate_preassign_enabled boolean NOT NULL DEFAULT false`
  (`Persistence/Sql/Ddl/07-HU10154-admin-tenants.sql:31-32`). Modelar la elegibilidad como booleano no inventa un
  patrón: **reutiliza el que la tabla ya tiene** para habilitar funcionalidades por compañía.
- El aislamiento real entre tenants es el `WHERE tenant_id` **manual**: hay 65 políticas RLS, pero no se evalúan
  (sin `FORCE ROW LEVEL SECURITY` y la aplicación es owner de las tablas). Cualquier lectura nueva debe llevar su
  filtro explícito.

**El riesgo de negocio concreto.** Mientras el campo conserve la doble semántica, la ola de Notificaciones
(#11347/#11348/#11349) invita a hacer precisamente lo que no se puede hacer: cambiar el canal de una compañía para
probar el envío de correo **le enciende de paso los documentos personalizados en producción**. Y en el sentido
inverso, una compañía que necesita documentos personalizados queda **forzada al canal `tenant_api`** aunque su
correo deba seguir saliendo por FLIT. Dicho de otro modo: en cuanto el Bug #11311 se corrija y el campo empiece a
enrutar de verdad, cada cambio de canal se convierte en un cambio no intencionado del expediente documental de esa
compañía. La corrección del bug y el desacople no son dos trabajos independientes: **el desacople es prerrequisito
de la corrección**.

## Decisión

`notification_channel` recupera **una sola** semántica —enrutamiento de notificaciones— y la habilitación de
documentos personalizados pasa a un **interruptor propio y explícito** del tenant.

1. **Campo nuevo** en `admin.tenant_operational_policies`: un booleano de habilitación de documentos
   personalizados, `NOT NULL DEFAULT false`, con la misma forma y las mismas convenciones que
   `signature_vault_enabled` y `plate_preassign_enabled` (HU #11357).

2. **Backfill de paridad**: al aplicar la migración, el campo se pone a `true` en los tenants que hoy tienen
   `notification_channel = 'tenant_api'`, y a `false` en el resto. Los tenants sin fila de política siguen
   deshabilitados por el default. El comportamiento observable el día del despliegue es **idéntico** al de hoy,
   compañía por compañía.

3. **`PersonalizedDocumentChannelGuard` deja de leer el canal** y pasa a leer el campo nuevo. No se toca la
   superficie del guardián (misma firma, mismos cuatro handlers, el `GET` sigue sin aplicarlo), de modo que el
   cambio queda contenido en una línea de política y en su prueba. El nombre del tipo queda desalineado con su
   contenido: se renombra en la misma HU para que no vuelva a sugerir que la habilitación depende del canal.

4. **Dos compuertas, en planos distintos, en serie.** La habilitación de la funcionalidad no es una sola cosa:

   | Compuerta | Alcance | Qué gobierna | Fuente |
   |-----------|---------|--------------|--------|
   | **Elegibilidad** | tenant | Si la compañía puede **configurar** documentos personalizados (rutas de escritura y visibilidad de la sección) | el booleano nuevo |
   | **Efecto** | tenant × tipo de documento | Si un documento del sistema **se sustituye** al generar el expediente | existencia de una versión activa ([ADR-0042]) |

   **Regla de precedencia, y qué pasa cuando discrepan:** el interruptor de elegibilidad **no se lee en el
   pipeline de generación documental**. La sustitución sigue resolviéndose exclusivamente por la existencia de una
   versión activa, tal como decidió [ADR-0042]. Por tanto, apagar la elegibilidad de una compañía que tiene
   versiones activas **congela su configuración en solo lectura pero no retira sus documentos del expediente**:
   para volver al documento del sistema hay que desactivar la versión, que es una operación explícita, auditable y
   reversible que [ADR-0042] ya define. La interfaz debe declarar esa situación (elegibilidad apagada con versión
   activa vigente) en vez de ocultarla.

   El motivo de esa asimetría es deliberado: un cambio de configuración administrativa **nunca** debe alterar en
   silencio el contenido de un expediente. Entre «un documento personalizado sigue emitiéndose después de apagar
   el interruptor» —visible, con dos clics de reversa— y «apagar un interruptor cambia el mandato que recibe el
   organismo de tránsito sin que nadie lo note», se prefiere lo primero.

5. **El canal queda libre para hacer su trabajo.** A partir de aquí, cambiar `notification_channel` no tiene
   ningún efecto sobre documentos, y el enrutamiento de correo (#11348, incluido el adaptador Renting) puede
   apoyarse en el campo sin arrastrar un efecto lateral documental. Este ADR **no** decide cómo se enruta el
   correo ni qué contrato tiene el adaptador Renting; solo retira el obstáculo.

## Alternativas consideradas

### Opción 1 — Campo booleano propio en `tenant_operational_policies`, con backfill (ELEGIDA)

La que implementa la HU #11357.

**Pros:**
- **Reutiliza un patrón vigente de la propia tabla** (`signature_vault_enabled`, `plate_preassign_enabled`): cero
  conceptos nuevos, cero dependencias nuevas, misma auditoría (`trg_audit_log`), mismo token de concurrencia
  (`row_version`), mismo diff de configuración (`SettingsDiff`) y misma superficie de API.
- El backfill da **paridad exacta** el día del despliegue: ninguna compañía gana ni pierde la funcionalidad.
- La intención queda **legible en el esquema**: quien lea la tabla ve un interruptor que dice lo que hace, en vez
  de tener que descubrir que un campo de correo enciende documentos.
- El punto de cambio en código es **uno** (la línea del guardián) y está cubierto por pruebas existentes.
- Deja el canal limpio para el Bug #11311 sin bloquear la ola de Notificaciones.

**Contras:**
- Añade una columna a una tabla que ya tiene once campos de configuración; el catálogo de flags por tenant crece
  sin gobierno formal.
- Introduce, en la letra, el «booleano paralelo» que [ADR-0042] rechazó (§«Relación con ADR-0042»).
- **Dependencia de orden de despliegue**: si el enrutamiento se despliega antes que esta migración, el tenant del
  cliente pierde sus documentos personalizados.
- Dos estados que pueden discrepar (elegibilidad apagada + versión activa), lo que obliga a declarar una regla de
  precedencia y a exponerla en la interfaz.

**Esfuerzo:** S · **Riesgos:** que el backfill se ejecute con la lista de tenants equivocada; mitigable
verificándolo contra la consulta de canales antes y después de aplicar.

### Opción 2 — Derivar la elegibilidad solo de la existencia de una versión activa, sin campo nuevo

Eliminar el guardián: cualquier compañía puede configurar documentos personalizados; el canal queda exclusivamente
para correo, y el único interruptor es el de [ADR-0042].

**Pros:**
- Es la opción más fiel a la letra de [ADR-0042]: un solo interruptor, imposible de contradecir.
- Cero DDL, cero migración, cero dependencia de orden de despliegue.
- Menos estado que mantener y que auditar.

**Contras:**
- **Cambia el alcance del producto sin que nadie lo haya decidido**: hoy la funcionalidad está acotada a compañías
  B2B con canal propio; sin compuerta de elegibilidad, cualquier tenant podría sustituir el mandato y la solicitud
  de trámite virtual que llegan al organismo de tránsito. Eso no es una simplificación técnica, es una decisión
  comercial y de riesgo documental.
- Pierde el escalón de habilitación comercial: no habría forma de decir «esta compañía contrató la funcionalidad».
- La primera carga de un PDF sería, ella misma, el acto de habilitación — sin ningún control previo.
- La interfaz tendría que mostrar la sección a todos los tenants, con el ruido y las preguntas de soporte
  correspondientes.

**Esfuerzo:** S · **Riesgos:** alto e inmediato — un expediente con el mandato equivocado ante un organismo de
tránsito es un incidente regulatorio, no un bug de configuración.

### Opción 3 — Ampliar el enum de canales (p. ej. `tenant_api_documentos`)

Modelar la combinación como valores adicionales del canal.

**Pros:**
- Sin DDL de columna nueva (la columna es `varchar(20)` sin `CHECK`).
- Un único punto de configuración en la interfaz.

**Contras:**
- **Agrava exactamente el problema que se quiere resolver**: conserva la doble semántica y la multiplica por el
  producto cartesiano de canales × capacidades. Con dos capacidades más, el enum explota.
- El enrutamiento de correo tendría que interpretar el canal por prefijo, que es la definición de acoplamiento
  accidental.
- Ilegible en base de datos y en auditoría: el diff de configuración mostraría un cambio de canal cuando lo que
  cambió fue una funcionalidad documental.

**Esfuerzo:** S · **Riesgos:** deuda técnica compuesta; el siguiente que necesite desacoplar tendrá que deshacer
dos cosas en vez de una.

### Opción 4 — Tabla de *feature flags* por tenant (`admin.tenant_feature_flags`)

Modelar la capacidad como fila `(tenant_id, feature_key, enabled)` en una tabla genérica, y migrar a ella también
`signature_vault_enabled` y `plate_preassign_enabled`.

**Pros:**
- Añadir una capacidad futura deja de exigir DDL.
- Gobierno explícito del catálogo de capacidades, con su propia auditoría y su propio historial.
- Existe precedente cercano en el repositorio (`admin.tenant_module_grants`, `admin.tenant_transit_office_grants`)
  para el patrón de concesión por tenant.

**Contras:**
- Es **sobre-diseño para una capacidad**: exige tabla, repositorio, caché de lectura, semilla de llaves y una
  migración de los dos flags existentes para que el modelo no quede a medias (y si no se migran, el resultado son
  dos mecanismos conviviendo, que es peor que uno imperfecto).
- Convierte una lectura de fila única —que ya está en el camino caliente de `TenantSettings`— en una segunda
  consulta, o en un `JOIN` con agregación.
- Un flag sin tipo pierde la validación del esquema y la legibilidad del diff de configuración.
- No aporta nada al problema de hoy, que es de **una** columna.

**Esfuerzo:** L · **Riesgos:** que la migración de los dos flags existentes quede a mitad y el sistema acabe con
dos fuentes de habilitación.

### Opción 5 — No tocar nada y prohibir por proceso el cambio de canal

Documentar que cambiar el canal enciende documentos y confiar en que nadie lo haga.

**Pros:** coste cero.
**Contras:** deja un fallo silencioso armado justo en el módulo cuya razón de ser es tocar el canal; no sobrevive
a la primera prueba de correo del banco de pruebas (#11349). **Descartada sin discusión.**

## Tradeoff aceptado

Se elige la **Opción 1** porque es la única que separa las dos semánticas **sin decidir de paso una cuestión
comercial** (Opción 2) ni pagar una infraestructura de flags que hoy no tiene segundo cliente (Opción 4). El
argumento que la desempata no es teórico: la tabla ya tiene dos interruptores de capacidad exactamente iguales, de
modo que la solución no introduce un patrón nuevo sino que corrige una excepción — `notification_channel` es el
único caso donde una capacidad se coló en un campo de otra cosa.

Se acepta el precio: una columna más, un orden de despliegue que hay que respetar y dos estados que pueden
discrepar. Los tres son visibles y verificables; el problema que resuelven es invisible hasta que rompe un
expediente.

## Relación con ADR-0042 — la tensión, sin maquillar

[ADR-0042] dice en su §Decisión, literalmente:

> «El interruptor de la funcionalidad **es** la existencia de una versión activa por `(tenant, tipo de documento)`:
> no hay un booleano paralelo que pueda contradecirla. El canal se lee siempre de
> `admin.tenant_operational_policies.notification_channel` y **no se copia** a la tabla nueva.»

El booleano de la HU #11357 es, en la letra, un booleano nuevo en el terreno que esa frase acota. **Veredicto:**
hay **contradicción parcial y deliberada con la letra**, y **ninguna con la intención** — pero la frase citada,
además, es internamente inconsistente y por eso hay que enmendarla en vez de reinterpretarla.

**1. La intención de ADR-0042 se conserva intacta.** Lo que aquella frase protege es que el **efecto documental**
—qué PDF entra al expediente— no dependa de un estado duplicado que pueda desmentir a la versión activa. Eso sigue
siendo cierto: el booleano nuevo **no se lee en el pipeline de generación** (§Decisión, punto 4). Nada se copia a
`admin.company_personalized_documents`. La precedencia declarada, la sustitución por `Tipo` y la reversión al
documento del sistema quedan exactamente como las dejó [ADR-0042].

**2. La frase describe mal el estado real del sistema en el momento en que se escribió.** Cuando ADR-0042 afirma
que «no hay un booleano paralelo», ya había una segunda compuerta operando: `PersonalizedDocumentChannelGuard`
impide crear, confirmar, activar y desactivar versiones si el canal no es `tenant_api`. Un booleano derivado de un
campo ajeno **es** un booleano paralelo; simplemente estaba disfrazado de canal. La HU #11357 no añade una segunda
compuerta: **sustituye la fuente de la que ya existe** por una honesta.

**3. ADR-0042 se contradice a sí mismo entre su Decisión y sus notas.** Su §Decisión sostiene que el interruptor
es la versión activa, mientras que su nota para el QA Agent pide verificar que «cambio de canal a `FLIT_SMTP` (se
desactiva el reemplazo y se conserva el historial)». Son dos reglas distintas: una dice que el canal no gobierna
el efecto y la otra dice que sí. **Este ADR resuelve la ambigüedad** a favor de la §Decisión: la elegibilidad
gobierna la **configuración**, no el **efecto**; apagarla no retira documentos del expediente (§Decisión, punto 4).
Esa resolución cambia una expectativa de prueba de ADR-0042 y debe recogerse en el plan de QA.

**4. Alcance de la sustitución.** Este ADR **sustituye parcialmente el mecanismo** de [ADR-0042] en un único
punto: la fuente del interruptor de habilitación por compañía deja de ser `notification_channel` y pasa a ser el
campo propio. Todo lo demás de [ADR-0042] —modelo de versiones, sustitución conservando el `Tipo`, precedencia
declarada, supersede parcial de [ADR-0036], riesgos R-1 a R-5— se conserva **íntegro**. Como [ADR-0042] sigue en
`Propuesto`, lo procedente al aceptarlo es **corregir su §Decisión y su nota de QA** con el texto que aquí se fija,
en vez de dejar dos documentos que se leen distinto. Si el Líder Técnico prefiriera lo contrario —que apagar la
elegibilidad sí retire el efecto— la decisión es reversible, pero entonces hay que decirlo en los dos ADR y asumir
que un cambio de configuración altera expedientes.

## Consecuencias

### Lo que se gana

- Cambiar el canal de notificaciones de una compañía deja de tener efectos documentales. La ola de Notificaciones
  puede tocar el canal —que es su trabajo— sin riesgo colateral.
- El Bug #11311 se puede corregir sin arrastrar una segunda semántica: cuando el canal empiece a enrutar de
  verdad, enrutar es lo único que hará.
- Una compañía puede tener documentos personalizados **y** correo por el SMTP de FLIT, o canal propio **sin**
  documentos personalizados. Las cuatro combinaciones pasan a ser expresables; hoy solo dos lo son.
- La habilitación queda auditada como lo que es: un cambio de capacidad, visible en el diff de configuración y en
  `audit_log` con su propio nombre.

### Lo que se pierde / riesgos

| Id | Riesgo | Tratamiento |
|----|--------|-------------|
| **R-1** | **Orden de despliegue**: si la HU de enrutamiento se despliega antes de que la migración de #11357 esté aplicada, el guardián lee un campo inexistente o sin backfill y **el tenant del cliente pierde sus documentos personalizados**. | La migración de #11357 es **bloqueante**: se aplica **antes** de desplegar el enrutamiento, y el despliegue del enrutamiento verifica su presencia como precondición. No es un `AddColumn` cualquiera: sin el backfill, la columna con default `false` apaga la funcionalidad a quien la tiene. |
| **R-2** | Backfill incorrecto: una compañía con `tenant_api` que quede en `false` pierde la funcionalidad en silencio. | Verificación explícita antes/después contra la lista de tenants con `notification_channel = 'tenant_api'`, y evidencia del recuento en la HU. |
| **R-3** | **Estados discrepantes**: elegibilidad apagada con versión activa vigente ⇒ el expediente sigue llevando el documento personalizado. | Es la regla declarada, no un defecto (§Decisión, punto 4). La interfaz debe mostrar la advertencia y ofrecer la desactivación de la versión. |
| **R-4** | Un tenant que hoy usa `tenant_api` **solo** para tener documentos personalizados quedará enrutando correo por la API del cliente en cuanto #11348 entre en vigor, si nadie revisa su canal. | Revisar los canales de todos los tenants como parte del despliegue de #11348: el desacople no reasigna canales, solo deja de mentir sobre ellos. |
| **R-5** | El catálogo de flags de `tenant_operational_policies` crece sin gobierno (tres capacidades ya). | Aceptado por ahora; la Opción 4 queda documentada como salida cuando aparezca la cuarta o la quinta. |

### Cambios operacionales

- Columna nueva en `admin.tenant_operational_policies` (`boolean NOT NULL DEFAULT false`) + `UPDATE` de backfill
  condicionado a `notification_channel = 'tenant_api'`, en la **misma** migración, para que no exista ninguna
  ventana en la que la columna esté creada y sin poblar.
- DDL idempotente y espejo en `Persistence/Sql/Ddl/07-HU10154-admin-tenants.sql` y en `docs/schema/ddl/`, junto a
  `signature_vault_enabled` y `plate_preassign_enabled`. Migración EF con `Flit.Infrastructure` como startup.
- La tabla ya tiene `trg_audit_log` y `row_version`: la columna nueva hereda auditoría y concurrencia sin trabajo
  adicional. Sí hay que incorporarla al diff de `SettingsDiff` para que el cambio quede narrado.
- Superficie de configuración del tenant: campo nuevo en el contrato de lectura y de actualización, con su
  validación. La sección de documentos personalizados de la interfaz pasa a condicionarse por él y **no** por el
  canal.
- Contrato de despliegue: migración → verificación de backfill → despliegue de enrutamiento. En ese orden.

## Qué NO decide este ADR

- **No** decide cómo se enruta el correo, ni el contrato del adaptador Renting, ni el catálogo de plantillas
  (#11347/#11348).
- **No** corrige el Bug #11311; lo desbloquea.
- **No** cambia el modelo de versiones de documentos personalizados, ni la sustitución por `Tipo`, ni la
  precedencia declarada de [ADR-0042].
- **No** toca el pipeline de generación documental: ningún punto del expediente lee el campo nuevo.
- **No** crea infraestructura de *feature flags* ni migra `signature_vault_enabled` / `plate_preassign_enabled`.
- **No** activa `FORCE ROW LEVEL SECURITY`: la lectura sigue apoyándose en el `WHERE tenant_id` manual, como el
  resto del sistema.
- **No** reasigna el canal de ninguna compañía: solo desacopla el efecto.

## Notas para agentes

- **Database Agent**: columna booleana `NOT NULL DEFAULT false` en `admin.tenant_operational_policies`, colocada
  junto a `signature_vault_enabled` / `plate_preassign_enabled` y con sus mismas convenciones de nombre. Backfill
  a `true` donde `notification_channel = 'tenant_api'`, **en la misma migración**. DDL idempotente, espejo en los
  dos ficheros de esquema, migración EF con `Flit.Infrastructure` como startup. Sin índice: es fila única por
  tenant. Sin `CHECK` adicional.
- **Backend Agent**: cambiar la lectura del guardián (canal → campo nuevo) y renombrarlo para que el nombre deje
  de mencionar el canal; **no** ampliar su superficie: los mismos cuatro handlers de escritura, el `GET` sigue sin
  aplicarlo. Añadir el campo a `TenantSettings`, `TenantSettingsCodes`, `SettingsWire`, `SettingsMapper`,
  `SettingsDiff` y `UpdateTenantSettingsHandler`. El pipeline de generación documental **no se toca**.
- **Frontend Agent**: la visibilidad de la sección de documentos personalizados pasa a depender del campo nuevo,
  no del canal. Estado de discrepancia (elegibilidad apagada + versión activa) **visible**, con el camino para
  desactivar la versión. Aplicar `flit-design-guardian`.
- **QA Agent**: paridad exacta post-migración compañía por compañía (ningún tenant gana ni pierde la
  funcionalidad). Cambiar el canal de `FLIT_SMTP` a `TENANT_API` y viceversa **no** altera documentos personalizados
  —esta expectativa **sustituye** la nota de QA de [ADR-0042]—. Apagar la elegibilidad bloquea las cuatro
  escrituras, conserva el historial y **no** retira el documento del expediente. Tenant sin política operativa ⇒
  deshabilitado. Aislamiento cross-tenant en la lectura nueva.
- **Security Agent**: el campo no es PII y no cambia el modelo de permisos, pero **sí es un control de
  habilitación**: verificar que solo los roles que hoy pueden editar la política operativa puedan alterarlo, y que
  quede en `audit_log`. Confirmar que la lectura lleva su `WHERE tenant_id` explícito (RLS no se evalúa).
- **Infra Agent**: la migración es **bloqueante y previa** al despliegue del enrutamiento (#11348). Registrar la
  precondición en el runbook del despliegue; sin ella, el tenant del cliente pierde sus documentos personalizados.

## ADRs relacionados

- [ADR-0042-documentos-personalizados-por-compania] — **mecanismo sustituido parcialmente** por este ADR
  (§Relación con ADR-0042). Se conserva íntegro su modelo documental.
- [ADR-0036-mandatarios-multiples-y-mandato-por-ot] — no se altera: lo que [ADR-0042] dejó inerte sigue inerte, y
  bajo las mismas condiciones.
- [ADR-0034-validacion-identidad-admin-desacoplada] — precedente de desacoplar una capacidad del mecanismo que la
  arrastraba por conveniencia.
- [ADR-0041-certificaciones-externas-modelo-canonico-persistido] — precedente de sustituir el **mecanismo**
  conservando la **decisión de negocio** del ADR anterior.

## Referencias

- Bug #11311 — el selector de canal de notificaciones no tiene efecto sobre el correo.
- `src/Flit.Admin.Application/Companies/PersonalizedDocuments/PersonalizedDocumentChannelGuard.cs` — único
  consumidor actual del campo.
- `src/Flit.Infrastructure/Persistence/Sql/Ddl/07-HU10154-admin-tenants.sql:24-43` — definición de
  `admin.tenant_operational_policies` y precedente de los dos flags booleanos de capacidad.
- `src/Flit.Admin.Domain/Companies/Settings/NotificationChannel.cs` · `TenantSettingsCodes.cs` ·
  `Application/Companies/Settings/SettingsWire.cs` — codificación BD (`flit_smtp` / `tenant_api`) y wire
  (`FLIT_SMTP` / `TENANT_API`).

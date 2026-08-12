# ADR-0044: Envío a destinatarios reales vs. buzón de control se decide por interruptor del despliegue, no por el nombre del ambiente

**Fecha**: 2026-08-12
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT, Product Owner FLIT (decisión de negocio 2026-08-12), Infra
**Tags**: arquitectura, backend, infra, seguridad, notificaciones, canal-renting

## Contexto

El canal de notificaciones **API Renting** (Feature #11348, HUs #11357-#11364, PR #247, ya en `develop`) decide si un correo sale a su destinatario real o se desvía a un buzón de control mediante un interruptor propio, `RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_OVERRIDE_ENABLED`, cuyo **valor legal** lo dicta el nombre del ambiente:

- `services/core-api/src/Flit.Infrastructure/InfrastructureExtensions.cs:920` — `IsProduction() && override` ⇒ `throw` (en producción el desvío está **prohibido**).
- `services/core-api/src/Flit.Infrastructure/InfrastructureExtensions.cs:933` — `!IsProduction() && !override` ⇒ `throw` (fuera de producción el desvío es **obligatorio**).

Esas dos ramas son los AC3/AC4 de la HU #11364.

El supuesto que las sostiene es falso en este despliegue: `docker-compose.prod.yml:118` fija `ASPNETCORE_ENVIRONMENT: Development` y **ese mismo compose corre en DEV, QA y PDN** (lo dice su propio comentario en `docker-compose.prod.yml:260`). Por tanto `IsProduction()` **nunca** es `true` en ningún ambiente: con `RENTING_API_ENABLED=true` el arranque siempre exige el desvío y prohíbe apagarlo. Hoy **es imposible que un correo salga por Renting a un destinatario real en ningún ambiente**, incluido producción.

Restricción externa dura, que no cambia: **Renting tiene un solo ambiente y es producción** (los tres `.pfx` "de pruebas" del cliente son el mismo archivo byte a byte). Un envío real desde DEV golpea la API productiva del cliente y un buzón real de un cliente final. Por eso el desvío existe y debe seguir siendo el comportamiento por defecto.

**Decisión de negocio del PO (2026-08-12):** debe poderse enviar correo real **desde cualquier ambiente**, no solo desde producción (p. ej. una validación end-to-end acordada con el cliente desde QA), sin que eso abra la puerta a que un despliegue cualquiera empiece a enviar por omisión.

## Decisión

El desvío deja de estar gobernado por `IHostEnvironment.IsProduction()`: cada **despliegue** declara con un interruptor afirmativo y propio, `RENTING_API_SEND_EMAIL_REAL_RECIPIENTS_ENABLED`, si envía a destinatarios reales; **ausente o vacío significa desviar al buzón de control**, y ninguna rama del canal vuelve a consultar el nombre del ambiente.

## Alternativas consideradas

### Opción 1: Interruptor afirmativo de envío real, propio del despliegue, con default seguro (RECOMENDADA)

Una variable nueva, `RENTING_API_SEND_EMAIL_REAL_RECIPIENTS_ENABLED`. `true` (literal, comparación ordinal case-insensitive) ⇒ envío real. Ausente, vacía o `false` ⇒ desvío al buzón de control. Cualquier otro valor no vacío ⇒ el arranque falla. `IsProduction()` desaparece del canal; `RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_OVERRIDE_ENABLED` queda derogada y su presencia con valor no vacío tumba el arranque con un mensaje de migración.

**Pros:**
- Cumple el requisito del PO: cualquier ambiente puede enviar real, incluido DEV/QA, sin tocar `ASPNETCORE_ENVIRONMENT`.
- No se puede encender por omisión: el estado por defecto (variable no declarada) es el seguro, y el encendido es una afirmación literal escrita en el despliegue.
- La propiedad "envío real" pasa a vivir donde de verdad vive el riesgo (el despliegue concreto), no en un nombre de ambiente que hoy es idéntico en los tres.
- Desacopla este guardarraíl del resto de comportamientos que dependen de `IsProduction()`: cambiar el ambiente para depurar otra cosa ya no puede encender correo real.
- La regla se enuncia en una sola frase, verificable con `env | grep`, sin tabla de combinaciones ambiente × interruptor.

**Cons:**
- Una variable más de despliegue, y hay que migrar los tres despliegues a la vez (la derogada tumba el arranque a propósito).
- Pierde la garantía "PDN nunca desvía": un PDN mal configurado desviaría en silencio; se sustituye por observabilidad (log de arranque + `recipient_diverted` en la bitácora), no por falla de arranque.
- El nombre del ambiente sigue mal en los tres despliegues; esta opción no lo arregla, solo deja de depender de él.

**Esfuerzo:** S
**Riesgos:** que un despliegue "arregle" el arranque fallido copiando `..._REAL_RECIPIENTS_ENABLED=true` sin entender qué activa — se mitiga con el mensaje de error, que nombra la variable derogada y dice explícitamente que el valor seguro es no declarar la nueva.

### Opción 2: Corregir `ASPNETCORE_ENVIRONMENT` por ambiente y conservar el gate por `IsProduction()`

`Production` en PDN, `Staging`/`Development` en QA y DEV, y dejar los AC3/AC4 tal como están.

**Pros:**
- Restaura la semántica estándar de .NET y arregla de paso otros guardarraíles que hoy creen estar en desarrollo en PDN.
- Cero código nuevo en el canal: solo configuración.
- La regla ya está implementada y probada.

**Cons:**
- **No cumple el requisito del PO**: el envío real seguiría siendo exclusivo de PDN; DEV/QA seguirían con desvío obligatorio por construcción.
- Reintroduce el acoplamiento peligroso: `ASPNETCORE_ENVIRONMENT` gobierna a la vez Swagger, páginas de error de desarrollo, seeds, migraciones automáticas y ahora destinatarios reales. Un cambio hecho por cualquiera de esos motivos mueve el correo.
- Cambiar el ambiente de PDN a `Production` es un cambio de superficie amplia y de riesgo alto, no evaluado aquí, que no debería viajar dentro de un ajuste de correo.

**Esfuerzo:** M (la variable es trivial; la validación del efecto dominó no lo es)
**Riesgos:** alto — activar `Production` en PDN cambia comportamientos no inventariados en un solo salto.

### Opción 3: Decidir el destinatario real como dato de configuración en BD (por tenant / por canal)

Guardar el interruptor en `admin.notification_channels` (o junto a `admin.notification_test_settings`) y gestionarlo desde la UI de SuperAdmin.

**Pros:**
- Se cambia sin redespliegue y con granularidad por tenant.
- Queda auditado en la tabla de configuración, con autor y fecha.

**Cons:**
- Convierte un guardarraíl de despliegue en un dato replicable: las BD de DEV/QA se restauran desde respaldos de PDN, y una restauración encendería el envío real en DEV sin que nadie lo pidiera. El riesgo viaja con el dump.
- El botón más peligroso del sistema quedaría a un clic en una pantalla de administración, sin gate de despliegue.
- No hay forma de fallar rápido en el arranque: el estado depende de una fila que puede cambiar en caliente.

**Esfuerzo:** M
**Riesgos:** alto — arrastre de configuración por restauración de datos; es exactamente el modo de fallo que el desvío pretende impedir.

## Tradeoff aceptado

Se elige la **Opción 1** porque es la única que cumple el encargo del PO (envío real desde cualquier ambiente) sin aflojar la barrera: hoy el desvío se apagaría poniendo `ASPNETCORE_ENVIRONMENT=Production`, un valor que alguien puede escribir por motivos ajenos al correo; con la Opción 1 solo se apaga escribiendo una variable cuyo único efecto es ese. Se acepta perder la prohibición de desviar en PDN (AC3) a cambio de que el encendido del envío real sea explícito y local; ese riesgo residual es de **silencio**, no de fuga, y se cubre con observabilidad.

Se descartó una variante de "doble llave" (interruptor + una segunda variable con un literal de confirmación tipo `ENVIO-REAL-A-CLIENTES`): no añade seguridad real —quien escribe una variable escribe dos— y sí añade una combinación más que puede quedar a medias en un despliegue.

Se descarta también **renombrar** `RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_EMAIL/_USERNAME` a un nombre de "buzón de control": el renombre no cambia comportamiento y multiplicaría la superficie de fallo de la migración de despliegues. Se conservan los nombres y se corrige su documentación (dejan de ser "de desarrollo": son el buzón de control de cualquier despliegue que no envíe real).

La Opción 2 **no se rechaza como trabajo**: corregir `ASPNETCORE_ENVIRONMENT` por ambiente sigue siendo deseable y debe abordarse en un ADR/HU propios. Lo que este ADR rechaza es que la decisión sobre destinatarios reales **dependa** de ella.

## Derogación explícita de la HU #11364

Este ADR **deroga los AC3 y AC4 de la HU #11364 tal como están implementados** en `InfrastructureExtensions.cs:920` y `:933`:

- **AC3** (en producción el desvío está prohibido) — se elimina. Bajo el nuevo diseño un despliegue de producción puede desviar legítimamente (ventana de pruebas en PDN sin tocar clientes finales).
- **AC4** (fuera de producción el desvío es obligatorio) — se elimina como regla ligada al ambiente y se sustituye por: *el desvío es obligatorio en todo despliegue que no declare `RENTING_API_SEND_EMAIL_REAL_RECIPIENTS_ENABLED=true`*.

**Se conservan íntegros** los AC1, AC2, AC5 y AC6 de esa HU: la sustitución de **todos** los destinatarios (principales y copia oculta), la marca `recipient_diverted` en la bitácora con el destinatario original intacto en `recipient`, el hecho de que la decisión de desviar se tome por un único booleano de opciones dentro del adaptador, y que el SMTP de FLIT quede estructuralmente fuera de alcance.

**Por qué la derogación es segura.** El AC4 no protegía porque el ambiente fuera "no producción": protegía porque, siendo `IsProduction()` siempre falso, el arranque exigía el desvío en los tres despliegues. Esa protección era un accidente de una configuración equivocada, no un diseño — y el día que alguien arreglara `ASPNETCORE_ENVIRONMENT` en PDN (Opción 2, deseable y en el radar) los tres ambientes quedarían gobernados por un nombre que también controla Swagger, seeds y migraciones. El nuevo diseño sustituye ese accidente por una regla intencionada y de la misma dureza: sin una afirmación literal en el despliegue, **no hay ninguna combinación de configuración que produzca un envío a un destinatario real**; y como el desvío exige buzón, tampoco existe el estado "no desvía y no envía real". Respecto al AC3, el riesgo que mitigaba (correos de PDN silenciados sin que nadie se entere) deja de ser un fallo de arranque y pasa a ser observable: log `Warning` en cada arranque diciendo en qué modo quedó el canal, log por envío desviado (ya existente) y la columna `recipient_diverted` de `admin.notification_delivery_logs` visible en el panel.

## Falla rápida — combinaciones exactas

Todas las filas se evalúan **solo dentro de `AddRentingChannel`** y **ninguna** consulta `IHostEnvironment`.

| `RENTING_API_ENABLED` | `RENTING_API_SEND_EMAIL_REAL_RECIPIENTS_ENABLED` | Resultado |
|---|---|---|
| distinto de `true` | cualquiera (incluso la derogada presente) | **Arranca.** No se exige ninguna variable del canal ni material TLS. Se conserva tal cual la regla vigente (AC2 de la HU #11359). |
| `true` | ausente o vacía | **Arranca desviando.** Exige `..._DEVELOPMENT_RECIPIENT_EMAIL` y `..._DEVELOPMENT_RECIPIENT_USERNAME`; si falta cualquiera, **falla** nombrándola. Log `Warning` de arranque: canal en modo desvío. |
| `true` | `false` | Idéntico a la fila anterior (declaración explícita del default). |
| `true` | `true` | **Arranca enviando real.** No se exige el buzón de control (puede quedar vacío). Log `Warning` de arranque: *este despliegue envía a destinatarios REALES de clientes por la API productiva de Renting*. |
| `true` | valor no vacío distinto de `true`/`false` (`1`, `yes`, `ture`…) | **Falla.** Un valor ininteligible no puede degradar en silencio a "desviar": se distingue la **ausencia** (default seguro, no falla) del **error de escritura** (falla). |
| `true` | cualquiera, con `RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_OVERRIDE_ENABLED` presente y no vacía | **Falla** con mensaje de migración que nombra ambas variables. Evita que un despliegue viejo crea que sigue gobernando el desvío. |

Se mantiene además, sin cambios, la validación AC1 del bloque obligatorio del canal (URL base, API key, `.pfx`, passphrase, tiempos, login, remitente) en toda fila con el canal encendido.

## Banco de pruebas de notificaciones — no se toca

La única excepción al desvío (decisión del PO 2026-08-11, HU #11372) es el banco de pruebas: `ControlledMailboxRecipient` marca que el destinatario ya es un buzón controlado configurado por un SuperAdmin, y esa exención es **estructural**, no condicional — solo viaja por la sobrecarga `IExplicitChannelEmailSender.SendAsync(...)` → `IRentingEmailApiSender.SendAsync(request, ControlledMailboxRecipient, ct)`; el camino de notificaciones reales (`TenantChannelEmailRouter.SendAsync(message, ct)`) no tiene por dónde propagarla.

Este ADR cambia **solo el cálculo del booleano** que consume `RentingRecipientOverride.Apply`, no el punto donde se aplica ni la exención. Consecuencias:

- **Prohibido** añadir una segunda condición del tipo "si es banco de pruebas, no desviar": esa regla ya existe una sola vez, en la firma del método, y duplicarla la convertiría en algo desactivable por error.
- Con `..._REAL_RECIPIENTS_ENABLED=true` la exención queda como no-op (no hay desvío que saltar); el comportamiento observable del banco de pruebas es idéntico antes y después.
- Se conserva el efecto lateral ya documentado: si el buzón de pruebas se configura con la dirección de una persona real, esa persona recibe el correo desde la cuenta de Renting en producción.

## Consecuencias

### Lo que se gana
- Se puede enviar correo real desde cualquier ambiente, que es lo que pidió el PO, sin tocar `ASPNETCORE_ENVIRONMENT`.
- El canal deja de estar roto de facto: hoy ningún despliegue puede enviar real, ni siquiera PDN.
- El guardarraíl más peligroso del sistema queda gobernado por una variable de efecto único, legible de un vistazo en el despliegue.
- Desaparece el acoplamiento entre nombre de ambiente y destinatarios; arreglar `ASPNETCORE_ENVIRONMENT` (Opción 2) deja de ser un cambio con efecto colateral sobre el correo.

### Lo que se pierde
- La imposibilidad estructural de que PDN desvíe: ahora un PDN mal configurado desvía en silencio, detectable por log de arranque y por la bitácora, no por falla.
- Se rompe la compatibilidad de configuración: los tres despliegues deben migrar la variable en el mismo movimiento (a propósito, para que nadie quede con una variable inerte que cree activa).

### Cambios operacionales
- Migración de despliegue: retirar `RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_OVERRIDE_ENABLED` de DEV, QA y PDN. Ningún despliegue declara la nueva variable salvo aquel en el que se acuerde envío real.
- El buzón `..._DEVELOPMENT_RECIPIENT_EMAIL/_USERNAME` sigue siendo obligatorio en todo despliegue que no envíe real; conserva su nombre y cambia su documentación.
- Encender el envío real en un ambiente que no sea PDN es una decisión operativa acordada con el cliente (golpea su API productiva); debe quedar registrada y revertirse al cerrar la ventana.
- Al arrancar, el log dice en qué modo quedó el canal: ese es el chequeo de despliegue, junto con un envío desde el banco de pruebas.

## ADRs relacionados

- Feature #11348 / HUs #11357-#11364 (canal API Renting) — este ADR deroga los AC3/AC4 de la HU #11364.
- HU #11372 (decisión del PO 2026-08-11) — exención del banco de pruebas: se conserva sin cambios.
- No supersede ningún ADR: la regla derogada vive en criterios de aceptación de HU, no en un ADR previo.

## Archivos a modificar en la implementación posterior

**Producción (`services/core-api/src/`):**
- `Flit.Infrastructure/InfrastructureExtensions.cs` — `AddRentingChannel`: parseo tri-estado de la nueva variable, eliminación de las dos ramas por `IsProduction()` (líneas 920 y 933), falla por variable derogada presente, falla por valor ininteligible, exigencia del buzón condicionada al modo desvío, log `Warning` de arranque con el modo resultante. Revisar si el parámetro `IHostEnvironment` queda sin uso en este método.
- `Flit.Infrastructure/Notifications/Renting/RentingChannelOptions.cs` — renombrar la propiedad de opciones a la semántica nueva (p. ej. `SendEmailRealRecipientsEnabled`, o `DivertRecipientsEnabled` si se prefiere conservar la polaridad del consumidor) y reescribir la documentación XML de las tres propiedades del bloque.
- `Flit.Infrastructure/Notifications/Renting/RentingRecipientOverride.cs` — consumir el booleano renombrado; actualizar la documentación XML que hoy cita AC3/AC4/AC5 y el ambiente.
- `Flit.Infrastructure/Notifications/Renting/RentingEmailApiSender.cs` — solo comentarios que referencian la regla por ambiente (sin cambio funcional).
- `Flit.Infrastructure/Notifications/Routing/TenantChannelEmailRouter.cs` — solo comentarios (sin cambio funcional); verificar que no queda ninguna referencia a la regla por ambiente.
- `Flit.Infrastructure/Notifications/Admin/NotificationChannelsAdminService.cs` — verificar si expone al panel el estado del desvío; si lo hace, alinear el texto al modo nuevo.
- `Flit.Infrastructure/Persistence/Sql/Ddl/68-notification-delivery-log-recipient-diverted.sql` — el `COMMENT` de la columna cita la variable derogada. **Sin migración nueva ni cambio de esquema**; si el DDL es idempotente y se reejecuta, basta corregir el texto del comentario; si no, dejarlo y corregirlo en el próximo DDL que toque la tabla (decide `database-agent`).

**Configuración y documentación:**
- `docker-compose.prod.yml` (bloque de notificaciones Renting, ~líneas 260-270) — retirar la variable derogada, añadir la nueva **sin default afirmativo** y actualizar el comentario `ATENCIÓN` que explica la regla por ambiente.
- `.env.renting.example` — reescribir el bloque "LEE ESTO ANTES DE ENCENDER EL CANAL" (líneas 8-30) y el bloque "Desvío de destinatario" (líneas 96-117): la tabla `Production/no Production` deja de existir.
- `docs/inventario-correos-plataforma.md` — si documenta el gate por ambiente, alinearlo.

**Pruebas (`services/core-api/tests/`):**
- `Flit.Infrastructure.Tests/Notifications/Renting/RentingRecipientOverrideStartupGateTests.cs` — reescritura completa: los casos dejan de variar `EnvironmentName` y pasan a cubrir la tabla de combinaciones de este ADR (incluidos valor ininteligible y variable derogada presente).
- `Flit.Infrastructure.Tests/Notifications/Renting/RentingChannelDependencyInjectionTests.cs` — clave de configuración renombrada.
- `Flit.Infrastructure.Tests/Notifications/Renting/RentingRecipientOverrideTests.cs` — propiedad renombrada; conservar el test que afirma que el ambiente no participa en la decisión.
- `Flit.Infrastructure.Tests/Notifications/Renting/RentingEmailApiSenderTests.cs` — propiedad renombrada.
- `Flit.Infrastructure.Tests/Notifications/Renting/ControlledMailboxRecipientExemptionTests.cs` — propiedad renombrada; **la exención no cambia**, estos tests deben seguir pasando con la misma intención.
- `Flit.Infrastructure.Tests/Notifications/Routing/TenantChannelEmailRouterTests.cs`, `.../Routing/ExplicitChannelEmailSenderRegistrationTests.cs`, `.../Admin/NotificationChannelsAdminServiceTests.cs` — ajustes por renombre.
- `Flit.Admin.Tests/Notifications/AdminPlataformaNotificacionesEnviosEndpointTests.cs`, `.../AdminPlataformaNotificacionesCanalesEndpointsTests.cs` — verificar que siguen levantando el host con el canal apagado (fila 1 de la tabla) y ajustar si fijan la variable derogada.

## Notas para agentes

- **Backend Agent**: no reintroducir `IHostEnvironment` en la decisión de desvío, ni en el arranque ni en el adaptador. La nueva variable es tri-estado (ausente/vacía ≠ valor inválido); un `bool.TryParse` a secas no basta. No tocar la firma que transporta `ControlledMailboxRecipient`.
- **Database Agent**: no hay cambio de esquema. Solo el texto de un `COMMENT` en el DDL 68; decide si se corrige ahí o en el próximo DDL de esa tabla.
- **QA Agent**: cubrir las seis filas de la tabla de falla rápida, y un caso end-to-end por modo (desvío ⇒ `recipient_diverted = true` con `recipient` original intacto; real ⇒ `recipient_diverted = false`). Verificar explícitamente que el banco de pruebas llega a su buzón en ambos modos.
- **Security Agent**: el punto de revisión es que ninguna configuración por omisión produzca envío real y que el mensaje de error de migración no filtre valores (nombra variables, nunca valores). Revisar que no se registren direcciones de clientes en niveles de log persistidos más allá de lo ya aprobado en la HU #11364.
- **Infra Agent**: la migración de las tres instancias debe ser simultánea (la variable derogada tumba el arranque). Ningún compose puede traer la nueva variable con default `true`. Encender el envío real en un ambiente que no sea PDN requiere acuerdo con el cliente y reversión posterior.
- **Frontend Agent**: sin impacto, salvo que el panel de canales muestre el estado del desvío; en ese caso, ajustar copy.

## Referencias externas

- `services/core-api/src/Flit.Infrastructure/InfrastructureExtensions.cs:823-940` — implementación vigente del gate.
- `docker-compose.prod.yml:118` y `:260` — `ASPNETCORE_ENVIRONMENT: Development` y el comentario que confirma que el mismo compose corre en DEV, QA y PDN.
- `.env.renting.example:8-30` — enunciado vigente de la regla por ambiente (queda obsoleto con este ADR).

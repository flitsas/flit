# ADR-0087: Regeneración automática (best-effort) del consolidado del wizard cuando Flito adjunta un comprobante que llena una casilla del checklist

**Fecha**: 2026-11-23
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT. La aceptación es exclusiva del Líder Técnico humano, en su propio PR (regla FLIT 15); este ADR no pasa a Aceptado por ningún otro camino.
**Tags**: arquitectura, backend, integraciones, documental, consolidado, bug-soporte
**Bug / Épica**: Bug #14660 (proyecto FLIT - SOPORTE). Relacionado: Bug #14625 (resuelto, mismo handler, marca de pago) y Bug #14630 (repo de Flito, no relacionado). Ninguno de los tres tiene Feature/Épica padre en ADO hoy.
**Supersedes**: ninguno.

## Contexto

Flito (sistema externo) compra el SOAT/impuesto y envía el comprobante automáticamente a FLIT 2 vía
`POST /api/v1/external/tramites/{id}/adjuntos` (HU #13263, Épica #12741). El archivo se archiva bien, pero el
**expediente consolidado** del trámite (el PDF fusionado que ve el organismo de tránsito, OT) no refleja ese
documento hasta que un humano entra al trámite y pulsa el botón manual de "Generar consolidado". Ya se coordinó
con el equipo de Flito: el ajuste es 100 % de este lado (core-api); Flito no necesita cambios ni alertas propias.

Restricciones y hallazgos verificados en el código (no solo reportados, releídos para este ADR):

1. **`SubmitExternalAttachmentHandler.HandleAsync`/`DecideAsync`** (`Flit.Tramites.Application/UseCases/ExternalAttachments/SubmitExternalAttachmentHandler.cs`) es deliberadamente liviano (decisión de HU #13263): no corre OCR, no toca el checklist y hoy no toca el consolidado. Corre dentro de `ExternalAttachmentRepository.RunLockedAsync`, que abre su propia transacción con **SQL crudo parametrizado** sobre `ADO.NET` (`DbConnection`/`DbCommand`/`DbTransaction` tomados de `context.Database`) y bloquea la fila del trámite con `FOR NO KEY UPDATE`. **No pasa por el `ChangeTracker` de EF Core.**
2. `enMatriz` (ya calculado en el handler, línea 112) es `true` solo si el tipo documental (`liquidacion_impuesto`, el único que admite este endpoint) llena una casilla real de la matriz/checklist del trámite para esa oficina de tránsito. Es un concepto distinto del merge del PDF, pero en la práctica el merge (`GenericConsolidadoOrdering.SelectByPrecedence`) incluye **todo** adjunto no excluido —los no listados en la precedencia van al final, por fecha—, así que un `liquidacion_impuesto` sí entra al PDF fusionado cuando llega.
3. **Ya existe en el repo un mecanismo genérico, en producción, para exactamente este problema** (Feature #10701/#12760, ADR-0032 Aceptado, Bug #13055):
   - La columna `tramites.procedure_instances.consolidado_wizard_vigente` (ADR-0032) decide si el PDF persistido se sirve tal cual o se regenera.
   - `ConsolidadoVigenciaTracker` (`Flit.Infrastructure/Persistence/ConsolidadoVigenciaTracker.cs`), enganchado en `FlitDbContext.SaveChanges[Async]`, **baja esa bandera automáticamente** cuando CUALQUIER fila del expediente cambia (incluye `ProcedureInstanceAttachment`, salvo el propio `consolidado`) — sin que cada caso de uso tenga que acordarse.
   - El mismo `SaveChanges` **encola la regeneración anticipada** en `IConsolidadoRegeneracionQueue` (HU #12795): una cola en memoria (`Channel` acotado, `ChannelConsolidadoRegeneracionQueue`), con debounce (`ConsolidadoRegeneracionDebouncer`) y un `IHostedService` (`ConsolidadoRegeneracionProcessor`) que llama a `GenerarConsolidadoHandler.HandleAsync(..., force: false)` en segundo plano, respetando las mismas guardas que el camino manual (estado final, migrado V1, `Source="user"`, maestro radicado ante Quipux). Documentado como **best-effort**: si la cola está llena o el proceso se reinicia, el camino perezoso (la próxima vez que alguien pida el consolidado con la bandera abajo) lo reconstruye igual.
   - Este mecanismo YA es el productor de cuatro o más "hitos" distintos (transición de estado, decisión del OT, asignación de placa, adjuntar la Licencia de Tránsito) — ver `TramiteLifecycleService.cs:342`, `OtClientProcedureRepository.cs` (3 sitios), `FurCommand.cs` (2 sitios).
4. **El problema no es la ausencia de un mecanismo automático: es que `ExternalAttachmentRepository` lo evade.** Al escribir con ADO.NET crudo en vez de con el `DbContext` rastreado, el INSERT del comprobante de Flito nunca pasa por `ConsolidadoVigenciaTracker`: la bandera `consolidado_wizard_vigente` no baja y nadie llama a `IConsolidadoRegeneracionQueue.Encolar`. El camino manual (el botón) funciona porque llama a `GenerarConsolidadoHandler` con `force: true`, que ignora la bandera.
5. `GenerarConsolidadoHandler` (`Flit.Tramites.Application/UseCases/ProcedureInstances/ConsolidadoCommand.cs`) es costoso: regenera el FUR en caliente, puede generar la impronta (RUNT/Kyverum, best-effort) y fusiona PDFs. Invocarlo de forma síncrona dentro del `POST` de Flito añadiría latencia variable y una dependencia externa a un endpoint de ingesta deliberadamente liviano, con restricciones de streaming documentadas en el propio endpoint.
6. No hay bus de eventos de dominio interno (ni outbox interno análogo a `FlitoSignalDispatcher`/ADR-0071, que es exclusivamente saliente hacia Flito). Pero, como el punto 3 muestra, **no hace falta uno**: ya existe la pieza exacta que resuelve "ejecutar algo en segundo plano, de forma best-effort, cuando cambia el expediente", construida y probada en producción para otros escritores.
7. Numeración de ADR verificada al momento de escribir: el máximo en el repo (`docs/decisions/`, `services/core-api/docs/adr/`, `services/core-ict/docs/adr/`, excluyendo worktrees) es ADR-0086; este ADR toma ADR-0087.

## Decisión

**Reutilizar `IConsolidadoRegeneracionQueue` (HU #12795) como el disparador, agregando a `SubmitExternalAttachmentHandler` como un productor nuevo** — sin tabla, sin migración, sin servicio nuevo — y cerrar la brecha puntual de `ExternalAttachmentRepository` frente a `ConsolidadoVigenciaTracker` con una invalidación equivalente, expresada en el mismo SQL crudo que ya usa ese repositorio.

Dos efectos, ambos condicionados a `enMatriz == true` y a que el envío haya archivado de verdad (`Status == Created`; un `200 Unchanged` no cambia el expediente):

1. **Invalidación transaccional de la bandera.** Dentro de la MISMA transacción bloqueada (`ExternalAttachmentRepository.Writer`), tras el `INSERT` exitoso del adjunto, un nuevo método `IExternalAttachmentWriter.InvalidateConsolidadoAsync` ejecuta:
   ```sql
   UPDATE tramites.procedure_instances
      SET consolidado_wizard_vigente = false
    WHERE id = @procedure_id
      AND consolidado_wizard_vigente = true
   ```
   Espejo exacto, en SQL crudo, de lo que `ConsolidadoVigenciaTracker.InvalidarAsync` hace para cualquier otro escritor EF. Sin esto, incluso si la cola en memoria se pierde (reinicio del proceso), el camino perezoso seguiría sirviendo el PDF viejo.
2. **Encolado best-effort de la regeneración anticipada.** Tras que `RunLockedAsync` confirme el commit (fuera de la transacción, en `SubmitExternalAttachmentHandler.HandleAsync`), se llama a `regeneracionQueue?.Encolar(tenantId, procedureId, TipoConsolidado.Wizard)`. El worker ya existente (`ConsolidadoRegeneracionProcessor`) regenera el PDF en segundo plano en el orden de siempre (debounce de `ConsolidadoRegeneracionOptions.VentanaDebounce`, hoy 5 s), sin que el organismo de tránsito tenga que esperar a que alguien entre al trámite.

`IConsolidadoRegeneracionQueue` se inyecta en `SubmitExternalAttachmentHandler` como parámetro **opcional** (`IConsolidadoRegeneracionQueue? regeneracionQueue = null`), igual que ya hace `TramiteLifecycleService`: si algún compositor no la cablea (tests, un host reducido), el handler sigue funcionando y solo se pierde la anticipación, nunca la invalidación transaccional del punto 1.

## Alternativas consideradas

### Opción 1: Reutilizar `IConsolidadoRegeneracionQueue` + invalidación transaccional de la bandera (elegida)

**Pros:**
- Cero tablas nuevas, cero migraciones, cero servicios nuevos: reutiliza un worker que ya corre en PDN con debounce, bitácora de fallos (`ConsolidadoFalloBitacora`) y las mismas guardas (estado final, migrado V1, `Source="user"`, maestro radicado) que ya cubren los casos raros de este trámite.
- Es exactamente el mismo patrón que ya resuelve este problema para otros 4+ escritores (transición de estado, decisión del OT, placa, LT): no inventa nada, cierra una omisión puntual.
- La invalidación transaccional (punto 1) hace que, aunque la cola se pierda, el PDF nunca se sirva estancado desde el camino perezoso — coherente con el propio contrato "best-effort, el perezoso reconstruye igual" que ya documenta la cola.
- Esfuerzo mínimo: dos archivos de dominio/infraestructura con ~15 líneas de SQL/C#, un parámetro de constructor opcional en el handler, sin tocar el contrato HTTP con Flito.

**Contras:**
- El disparo automático sigue siendo no durable en el peor caso (reinicio del proceso justo entre el commit y el `Encolar`): el documento solo aparecerá cuando alguien vuelva a pedir el consolidado con la bandera abajo (perezoso) en vez de en segundo plano. Igual que para el resto de hitos de esta cola.
- `Flit.Tramites.Application.UseCases.ExternalAttachments` pasa a depender de un tipo de `UseCases.ProcedureInstances` (mismo ensamblado, acoplamiento interno nuevo pero no de infraestructura).
- Si en algún tipo de trámite/oficina `liquidacion_impuesto` llenara la matriz pero estuviera excluido del merge por mimetype no soportado, la regeneración correría igual sin añadir la página (riesgo preexistente del merge, no introducido por este ADR).

**Esfuerzo:** S
**Riesgos:** olvidar la invalidación en algún otro escritor SQL crudo futuro. Mitigación: el mismo criterio que ya exige HU #10860/Bug #13055 ("quien escribe un adjunto invalida") se documenta explícitamente aquí para el único escritor que hoy evade `ConsolidadoVigenciaTracker`.

### Opción 2: Outbox durable dedicado (al estilo `FlitoSignalDispatcher`/ADR-0071) para este efecto interno

**Pros:**
- Durable ante reinicio del proceso: ninguna solicitud se pierde en memoria.
- Auditable: una fila pendiente es visible y se puede reintentar manualmente si el worker falla.
- Sigue el precedente de "cómo este equipo ya resuelve 'hacer algo de forma confiable después de que un commit se confirme'" (ADR-0071), aunque ese outbox es exclusivamente saliente hacia Flito.

**Contras:**
- Tabla y migración nuevas, worker/cron nuevo, para resolver un problema que la Opción 1 ya resuelve sin DDL: la pieza que de verdad aporta durabilidad —la bandera `consolidado_wizard_vigente`— ya existe y ya se puede invalidar en la misma transacción sin tabla adicional.
- Duplica, en esencia, la función de `IConsolidadoRegeneracionQueue` con peor relación costo/beneficio: contradice la regla "nunca reinventes patrones ya existentes sin justificación documentada", y aquí no hay justificación — la pieza "falta durabilidad" ya no es un hueco real tras la Opción 1.
- Más superficie (DDL, migración, nuevo worker con su propio ciclo de reintentos) para un bug de soporte que se quiere resolver con bajo riesgo y rápido.

**Esfuerzo:** M/L
**Riesgos:** una tabla y un worker nuevos introducen su propia superficie de concurrencia (reclamos, reintentos, purga) que hay que diseñar, migrar y probar — exactamente lo que un bug de soporte de prioridad alta no necesita cuando la Opción 1 ya cubre el caso.

### Opción 3: Mantener el botón manual + alerta/badge visible para el gestor

**Pros:**
- Cero riesgo técnico: ningún cambio al pipeline de generación ni a la capa de datos.
- Coherente con la decisión explícita de "un solo botón" (`docs/ado-drafts/mejoras-modificaciones/HU07-consolidado-unico-disparador.md`) si se interpreta de forma amplia.

**Contras:**
- No resuelve el síntoma reportado: el organismo de tránsito seguiría recibiendo expedientes incompletos hasta que un humano actúe; solo hace el vacío más visible.
- Traslada el problema de ingeniería a UX/soporte (quién ve la alerta, cuándo, si la ignora).
- Contradice la intención explícita del bug, que pide que el documento "aparezca solo"; no sería aceptado como corrección por QA frente al reporte original.
- La decisión "un solo botón" (HU07) es sobre el FRONTEND del gestor (no exponer botones por documento) y no bloquea que el BACKEND dispare la cascada en un flujo que no pasa por el wizard del gestor; invocarla aquí para justificar "no automatizar" sería una lectura forzada del precedente.

**Esfuerzo:** S (backend) + M (frontend, componente de alerta nuevo)
**Riesgos:** el gestor puede no ver o ignorar la alerta; el bug queda abierto desde la perspectiva del reporte original.

## Tradeoff aceptado

Se acepta que el disparo automático siga siendo best-effort (no 100 % durable en el peor caso de reinicio) a cambio de
cero superficie nueva de datos/infraestructura, reutilizando al 100 % una pieza que el propio equipo ya diseñó,
documentó y aceptó (ADR-0032, Bug #13055, HU #12795) para resolver exactamente esta clase de problema para otros
escritores. La invalidación transaccional de `consolidado_wizard_vigente` (punto 1 de la Decisión) es la que
realmente cierra el riesgo de "PDF estancado para siempre": incluso en el peor caso (cola perdida), el consolidado
deja de servirse desde caché y se regenera en el siguiente acceso normal, nunca requiere que alguien sepa que debe
pulsar "Limpiar consolidado". La Opción 2 (outbox durable) se descarta no por ser inferior en abstracto, sino porque
aquí no compra nada que la Opción 1 no compre ya, y sí compra DDL, migración y un worker nuevos — exactamente la
superficie que ADR-0032/HU#12795 ya decidió no necesitar para este tipo de evento.

## Consecuencias

### Lo que se gana
- El comprobante de Flito que llena una casilla del checklist aparece en el consolidado del wizard sin intervención humana, en segundos (ventana de debounce de 5 s por defecto), igual que ya pasa tras asignar placa o decidir en el OT.
- Cero deuda de infraestructura nueva: ni tabla, ni migración, ni servicio.
- El camino perezoso (alguien entra y pide el consolidado) deja de servir un PDF stale para estos trámites, incluso si la cola en memoria se pierde.

### Lo que se pierde
- Sigue existiendo una ventana teórica (reinicio del proceso entre el commit del adjunto y el `Encolar`) en la que la regeneración automática no corre; se resuelve en el siguiente acceso perezoso, no instantáneamente. Es el mismo tradeoff que el resto de hitos de esta cola acepta hoy.
- Un acoplamiento interno nuevo (mismo ensamblado) entre `UseCases.ExternalAttachments` y `UseCases.ProcedureInstances`.

### Cambios operacionales
- Ninguna migración ni variable de entorno nueva. La cola ya tiene sus opciones (`ConsolidadoRegeneracion:VentanaDebounce`, `EsperaMaxima`, `Capacidad`) configuradas para los demás hitos; este productor nuevo comparte la misma configuración sin cambios.
- `docs/integraciones/external-api-tramites-sync.md` (contrato con Flito, §7) actualiza la frase "Solo archiva: no corre OCR ni regenera el consolidado del trámite" para reflejar que, cuando `enMatriz=true`, se encola una regeneración en segundo plano no bloqueante. No cambia ninguna forma de request/response: Flito no necesita ningún cambio ni nueva versión de contrato de su lado.

## ADRs relacionados

- [ADR-0032] (Aceptado) — origen de `consolidado_wizard_vigente` y de "quien cambia el expediente invalida la bandera". Este ADR extiende ese principio al único escritor (`ExternalAttachmentRepository`) que hoy lo evade por escribir con SQL crudo en vez de EF.
- [ADR-0071] (Propuesto) — outbox saliente hacia Flito (`FlitoSignalDispatcher`). Se evalúa como Opción 2 y se descarta para este caso: ese outbox resuelve "avisar a un tercero", no "regenerar un documento interno", y la pieza que de verdad aportaría (durabilidad) ya la cubre la invalidación transaccional de este ADR.
- HU #12795/#12796/#12797/#12798, Bug #13055 (código, sin ADR dedicado) — diseño y guardas de `IConsolidadoRegeneracionQueue`/`ConsolidadoRegeneracionProcessor`, reutilizados sin cambios.

## Notas para agentes

- **Backend Agent**:
  - `services/core-api/src/Flit.Tramites.Domain/ExternalSync/ExternalAttachment.cs`: añadir `Task InvalidateConsolidadoAsync(CancellationToken cancellationToken)` a `IExternalAttachmentWriter`, documentada como espejo de `ConsolidadoVigenciaTracker.InvalidarAsync` para este escritor SQL crudo.
  - `services/core-api/src/Flit.Infrastructure/Persistence/Repositories/ExternalAttachmentRepository.cs`: SQL constante `InvalidateConsolidadoSql` (ver Decisión, punto 1) e implementación en `Writer.InvalidateConsolidadoAsync`, ejecutada en la misma `conn`/`tx` del `RunLockedAsync`.
  - `services/core-api/src/Flit.Tramites.Application/UseCases/ExternalAttachments/SubmitExternalAttachmentHandler.cs`:
    - Constructor: agregar `IConsolidadoRegeneracionQueue? regeneracionQueue = null` (mismo patrón nullable que `TramiteLifecycleService`); `using Flit.Tramites.Application.UseCases.ProcedureInstances;`.
    - `DecideAsync`: tras el `ReplaceAsync` exitoso, si `enMatriz == true`, `await writer.InvalidateConsolidadoAsync(ct)`.
    - `HandleAsync`: tras `RunLockedAsync` y la limpieza de binarios huérfanos, si `result.Result.Status == Created && result.Result.Receipt is { EnMatriz: true } && result.Result.TenantId is { } tenantId`, `regeneracionQueue?.Encolar(tenantId, procedureId, TipoConsolidado.Wizard)`.
    - Actualizar el doc XML del summary (líneas 41-43 hoy): ya no dice "no regenera el consolidado" sin matices; explicar la condición `enMatriz` y que es asíncrono/best-effort, no síncrono dentro del POST.
  - No se requiere ninguna migración ni cambio de esquema: no hay DDL que delegar al Database Agent para este bug.
  - Ningún cambio en `GenerarConsolidadoHandler`, `ConsolidadoRegeneracionProcessor`, `RegenerarConsolidadoAnticipadoHandler` ni `ConsolidadoVigenciaTracker`: se reutilizan tal cual.
- **QA Agent**:
  - Caso principal: trámite con consolidado vigente generado, Flito envía `liquidacion_impuesto` con `enMatriz=true` → tras la ventana de debounce, el consolidado se regenera solo e incluye la página nueva, sin que nadie pulse el botón.
  - `enMatriz=false` → ni se invalida la bandera ni se encola nada (comportamiento idéntico al actual).
  - `200 Unchanged` (mismo SHA-256) → tampoco invalida ni encola (el expediente no cambió).
  - Reinicio simulado del proceso entre el commit y el drenado de la cola (o cola llena) → el consolidado NO se regenera solo, pero la bandera ya está en `false`: la siguiente vez que alguien entra y pide el consolidado (ruta perezosa/manual), se regenera correctamente con la página nueva. Verificar que este camino de respaldo sigue funcionando.
  - Verificar en al menos una configuración real de OT que `liquidacion_impuesto` participa del merge (no es un mimetype no soportado ni queda fuera del orden configurado) para el tipo de trámite típico de SOAT/impuesto.
  - Prueba de integración contra Postgres real (en `Flit.Integration.Tests`, siguiendo el propio reparto que documenta `SubmitExternalAttachmentHandlerTests`: "el SQL real se prueba contra Postgres ahí") que confirme el `UPDATE ... WHERE consolidado_wizard_vigente = true` funciona con la fila bloqueada `FOR NO KEY UPDATE`.
- **Security Agent**: sin cambios de superficie de datos personales ni de permisos; el endpoint externo sigue exigiendo `external.tramites.attachments.write` y sin registrar el archivo en bitácora. La cola en memoria no transporta PII (solo `tenantId`, `procedureInstanceId`, `TipoConsolidado`), igual que los demás productores.
- **Infra Agent**: sin servicios, colas externas ni variables de entorno nuevas. La configuración de `ConsolidadoRegeneracion` (debounce, capacidad) ya existe y es compartida; no hay que dimensionarla de nuevo solo por este productor.
- **Database Agent**: no aplica para este ADR — no hay DDL ni migración nueva. Se reutiliza `tramites.procedure_instances.consolidado_wizard_vigente` (columna existente desde ADR-0032).
- **Frontend Agent**: no aplica — no hay cambio de contrato ni de UI; el botón manual del wizard sigue funcionando exactamente igual (con `force: true`) para el caso en que el gestor quiera regenerar de inmediato.

## Archivos a crear o modificar (repo `services/core-api`)

**Dominio:**
- `services/core-api/src/Flit.Tramites.Domain/ExternalSync/ExternalAttachment.cs` — MODIFICAR: nuevo método en `IExternalAttachmentWriter`.

**Infraestructura:**
- `services/core-api/src/Flit.Infrastructure/Persistence/Repositories/ExternalAttachmentRepository.cs` — MODIFICAR: SQL constante nueva + implementación del método nuevo en `Writer`.

**Aplicación:**
- `services/core-api/src/Flit.Tramites.Application/UseCases/ExternalAttachments/SubmitExternalAttachmentHandler.cs` — MODIFICAR: constructor, `DecideAsync`, `HandleAsync`, doc XML (ver Notas para Backend Agent).

**Documentación:**
- `docs/integraciones/external-api-tramites-sync.md` (línea ~406, bullet "Solo archiva") — MODIFICAR.
- Opcional/no obligatorio, sin requerir coordinación nueva con Flito (no cambia forma ni versión del contrato): `contracts/openapi/external-api.v1.json`, línea ~758, punto (5) de la descripción del endpoint — ajustar el texto para que no afirme sin matices "no regenera el consolidado del trámite".

**Tests:**
- `services/core-api/tests/Flit.Tramites.Application.Tests/UseCases/ExternalAttachments/SubmitExternalAttachmentHandlerTests.cs` — MODIFICAR: `FakeWriter`/`FakeRepository` ganan `InvalidateConsolidadoAsync` (registrar si se llamó); agregar un `RecordingRegeneracionQueue` local (mismo patrón que `ConsolidadoRegeneracionHitosLifecycleTests.cs`); casos nuevos: `enMatriz=true` + `Created` → invalida y encola `TipoConsolidado.Wizard`; `enMatriz=false` → ninguno de los dos; `Unchanged` (mismo SHA-256) → ninguno de los dos; `regeneracionQueue=null` (default) → no lanza.
- `services/core-api/tests/Flit.Integration.Tests/Tramites/ExternalAttachmentEndToEndTests.cs` — MODIFICAR: caso contra Postgres real que siembra un trámite con `consolidado_wizard_vigente = true` y un consolidado previo, envía un adjunto `enMatriz=true` y verifica que la columna queda en `false` tras el `POST`.
- `services/core-api/tests/Flit.Admin.Tests/Integrations/ExternalAttachmentEndpointTests.cs` — REVISAR (no necesariamente modificar): correr para confirmar que el nuevo parámetro opcional del constructor se resuelve sin problemas contra el contenedor de DI real de este host de pruebas.

## Referencias externas

- Ninguna (decisión interna de arquitectura, sin estándar externo involucrado).

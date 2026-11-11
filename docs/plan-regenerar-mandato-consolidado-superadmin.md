# Plan — «Regenerar mandato y consolidado» (solo SuperAdmin)

> Generado: 2026-11-11 · Estado: **refinado** (R1–R5 decididas por el usuario; R6 supuesto) · pendiente: HU en ADO
> Código de referencia: `origin/develop`. Anclas `archivo:línea` del brief de explore-agent del 2026-11-11: reverificar antes de citarlas en la HU.

## Requerimiento (literal del usuario)

> En un trámite en cualquier estado, excepto aprobado, se pueda regenerar el mandato y a su vez el consolidado, SOLO APLICA PARA EL SUPER ADMIN.
> Agregar un nuevo botón / sección que indique "Regenerar mandato y consolidado" con su respectiva descripción.

## Situación actual (código)

- El modal «Gestionar consolidado» (`frontend/components/operacion/AdminTramiteAcciones.tsx:314`) se abre desde el menú de acciones de cada fila de `TramitesTable` y tiene dos secciones:
  - **Regenerar consolidado** → `POST /api/v1/admin/tramites/{id}/consolidado/limpiar`.
  - **Cargar PDF** → `POST …/consolidado/cargar`.
- **Quién lo ve:** un SuperAdmin, o quien tenga el permiso `AdminTramiteLimpiarConsolidado` / `AdminTramiteCargarConsolidado` (`AdminTramiteAuthorization.cs`). En el backend, `RequirePermission` deja pasar siempre al SuperAdmin.
- **Qué hace «Regenerar consolidado» hoy:** llama a `GenerarConsolidadoHandler` con `force:true`. Ese camino regenera en cascada **todos** los documentos en caliente (FUR, compraventa, solicitud virtual, **mandato**, certificados, escrituras, personalizados) y vuelve a armar el consolidado del wizard. El maestro queda solo marcado como desactualizado (`ConsolidadoCommand.cs:220-284`).
- **El mandato no se puede regenerar por separado.** `TryGenerateMandatoAsync` es privado dentro de `GenerarFurHandler` (`FurCommand.cs:377`, `:1329`).
- **El firmante del mandato depende del estado** (`FurCommand.cs:1376-1395`):
  - En borrador o subsanación se recalcula con la prelación de ADR-0066 contra la configuración vigente.
  - **Fuera de borrador manda el `MandateSignerId` guardado**, aunque ya no sea válido. Es documento legal y se conserva la referencia (HU #13142 AC4).
- **El bloqueo por estado no existe en el admin:** hoy no se bloquea ningún estado, salvo un trámite migrado de V1 en estado final (409 `migrado_solo_lectura`). `PermiteGeneracionDocumentalDelGestor` solo se aplica en los endpoints del gestor (`TramiteEstado.cs:218-224`).
- **El maestro radicado en Quipux queda fijo:** `ConsolidadoMaestroCommand.HandleRespetandoRadicacionAsync` lo sirve tal cual y no lo regenera.

**Consecuencia:** el botón actual ya regenera el mandato, pero junto con todo lo demás, en cualquier estado (aprobado incluido) y para quien tenga el permiso. La nueva sección se diferencia en tres cosas: (1) va acotada al mandato, (2) excluye aprobado y (3) es solo para SuperAdmin.

## Decisiones

| # | Pregunta | Respuesta |
|---|---|---|
| R1 | Fuera de borrador, ¿la regeneración recalcula el firmante del mandato con la configuración vigente de Mandatos o conserva el guardado? | **a) Recalcula** con la prelación de ADR-0066 contra la configuración vigente y **sobrescribe `MandateSignerId`**. La bitácora registra el firmante anterior y el nuevo (evento `mandato_regenerado_admin`). Excepción explícita a HU #13142 AC4, solo para esta acción del SuperAdmin (usuario 2026-11-11). |
| R2 | ¿Se regenera solo el mandato (+ consolidado) o toda la cascada (FUR, solicitud virtual, certificados…)? **a) Solo el mandato** + reensamble del consolidado. FUR, compraventa, solicitud virtual, certificados, escrituras y personalizados no se tocan. Requiere extraer `TryGenerateMandatoAsync` de `GenerarFurHandler` a un servicio reutilizable (`IMandatoEmisor` o similar), que `GenerarFurHandler` consuma sin cambiar su comportamiento (usuario 2026-11-11). |
| R3 | ¿Qué consolidado se regenera: el del wizard, el maestro o ambos? ¿Y si el maestro ya está radicado en Quipux? | **b) Ambos en la misma acción.** Wizard vía `GenerarConsolidadoHandler` (sin cascada, con bypass de `Source='user'`) y maestro vía `GenerarConsolidadoMaestroHandler.HandleRespetandoRadicacionAsync` con la matriz de precedencia del OT. **El maestro radicado en Quipux no se reemplaza:** la respuesta lo advierte («el OT conserva el expediente radicado»). Si falla el maestro después de regenerar el mandato y el wizard, la respuesta es éxito parcial con aviso y el maestro queda invalidado, para que se rearme de forma perezosa (usuario 2026-11-11). |
| R4 | ¿Anulado y revocado quedan habilitados? (el literal solo excluye aprobado) **a) No.** Se bloquean los tres estados finales: aprobado, anulado y revocado (`TramiteEstado.EsFinal`). Queda habilitado en borrador, preparado, preasignación, asignado, entregado, rechazado y subsanación (usuario 2026-11-11). |
| R5 | «Solo SuperAdmin»: ¿rol estricto (sin permiso delegable) en front y back? **a) Rol SuperAdmin estricto, sin slug delegable.** El endpoint lleva su propia comprobación de rol: `RequirePermission` solo hace bypass y no excluye a nadie. En el frontend, la sección se muestra solo con `isSuperAdmin`, y además se oculta en modo consulta de red (usuario 2026-11-11). |
| R6 | ¿El botón actual «Regenerar consolidado» se queda como está (incluso en aprobado)? | *supuesto: sin cambios (fuera de alcance)* |

## Alcance (según R1–R6)

**Backend (`backend-agent`)**
1. **Extracción sin cambio de comportamiento:** `TryGenerateMandatoAsync` sale de `GenerarFurHandler` a un servicio reutilizable (p. ej. `IMandatoEmisor`). `GenerarFurHandler` lo consume igual que hoy, y las pruebas actuales del FUR/mandato quedan en verde sin tocarlas.
2. **Modo de firmante «recalcular»** en el servicio: aplica la prelación de ADR-0066 contra la configuración vigente **en cualquier estado** y devuelve el firmante resuelto. Solo lo usa esta acción. El camino del FUR conserva la regla actual (firmante guardado fuera de borrador).
3. **Endpoint** `POST /api/v1/admin/tramites/{id}/mandato/regenerar`:
   - Comprueba el rol SuperAdmin; si no, 403.
   - Exige `X-Tenant-Id`; si falta, 400.
   - Estados finales (aprobado, anulado, revocado) → 409 `estado_no_permite_regenerar_mandato`. Migrado de V1 en estado final → 409 `migrado_solo_lectura`.
   - Compone la matriz de precedencia del OT para el maestro, como hoy `AdminOtEndpoints`.
4. **Handler `RegenerarMandatoYConsolidadoHandler`**, en este orden:
   1. Emite el mandato con el firmante recalculado. Sobrescribe `MandateSignerId` y reemplaza el adjunto `mandato` del sistema (reemplazo seguro). **Respeta el mandato personalizado de la compañía** (`Source='company'`, HU #11313).
   2. Arma el consolidado del wizard **sin la cascada del FUR**, saltándose la protección de `Source='user'`.
   3. Arma el maestro respetando la radicación. Si está radicado, se conserva y se devuelve un aviso.
   4. Registra los eventos `mandato_regenerado_admin` (firmante anterior → nuevo, sin PII de más), `consolidado_generado` y `consolidado_maestro_generado`.
   5. **Fallo del maestro** → éxito parcial con aviso; el maestro queda invalidado.
   6. **Sin configuración ni firmante del OT** → el mandato sale «Sin firmar», igual que hoy, y la respuesta lo advierte.
5. **Respuesta:** firmante resultante, vigencia y sello de ambos consolidados, y la lista de avisos.
6. **Pruebas (TDD):**
   - Los 10 estados: 7 → 200 y 3 → 409.
   - Por rol: SuperAdmin → 200; admin con todos los slugs de `admin-tramites-avanzado` → 403.
   - Firmante: con la configuración de Mandatos cambiada después de radicar, sale el firmante nuevo y `MandateSignerId` se actualiza.
   - Documentos: el FUR, la solicitud virtual y los certificados **no** cambian (mismo attachment id); el mandato personalizado se preserva.
   - Maestro: el radicado se conserva y llega el aviso; si el maestro falla, se devuelve éxito parcial.
   - Contrato: el endpoint en OpenAPI y el `info.version` del spec subido.

**Frontend (`frontend-agent`)**
- **Sección nueva en `ConsolidadoModal`:** «Regenerar mandato y consolidado», visible solo para SuperAdmin.
- **Descripción:** «Vuelve a generar el contrato de mandato con el mandatario que indique hoy la configuración de Mandatos y arma de nuevo los consolidados del gestor y del organismo de tránsito. El FUR y los demás documentos no cambian. No está disponible en trámites aprobados, anulados o revocados.»
- **En estado final:** el botón queda deshabilitado y se muestra el motivo.
- **Antes de ejecutar:** confirmación que advierte que el mandatario puede cambiar.
- **Mientras corre y al terminar:** estado de carga; toast de éxito con los avisos (maestro radicado conservado, éxito parcial, mandato sin firmar); error visible; relectura del detalle.
- **Pruebas** (Vitest + RTL): visibilidad por rol, deshabilitado en los 3 estados finales, avisos, error. Accesibilidad WCAG 2.1 AA.

**Revisión:** `code-review-agent` + `security-agent`, porque cambia permisos y regenera un documento legal con cambio de firmante.

**Fuera de alcance (R6):** el botón actual «Regenerar consolidado» y «Cargar PDF» no cambian, aunque hoy funcionan en aprobado.

## Triaje y estimación

- **Caso C** (capacidad nueva, sin work item) → `/requirement-to-delivery`, **una HU FULLSTACK**.
- **Estimación: 8 SP** (BE 5 + FE 3). Sube por R1 = a (recálculo del firmante) y R3 = b (maestro en la misma acción).
- **Rama:** `feature/AB-<id>-regenerar-mandato-consolidado` desde `develop`, no la rama del diagnóstico.
- **Anclas por verificar al implementar:** `FurCommand.cs:377,1329,1376-1395`; `ConsolidadoCommand.cs:187,205-226,270-284`; `ConsolidadoMaestroCommand.cs:75-94`; `AdminConsolidadoEndpoints.cs:24-90`; `AdminTramiteAcciones.tsx:314-444,676-743`.

## Gates

HU creada en ADO (sprint siguiente al activo, tag DOR, SP Fibonacci, asignada a un humano) → activación con tu «sí» → implementación TDD → evidencias → PR a `develop` con revisión de código y seguridad → reviewer humano → merge con tu «sí».

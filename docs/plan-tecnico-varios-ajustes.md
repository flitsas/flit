# Plan técnico — Varios ajustes (identidad, documentos, wizard y dashboard)

**Fuente:** `varios-ajustes.txt` (14 puntos)
**Base:** `develop` @ `1f3e8942` (ya incluye el PR #199 mergeado)
**Fecha:** 2026-07-28

---

## Resumen

14 puntos que se agrupan en **5 bloques**. El más importante es el bloque A: cuatro síntomas distintos (puntos 1-4) que comparten **dos causas raíz**, así que arreglarlos por separado sería trabajo duplicado.

| Bloque | Puntos | Naturaleza |
|---|---|---|
| **A** — Persona jurídica e identidad apalancada | 1, 2, 3, 4 | Causa raíz compartida |
| **B** — Documentos generados | 5, 7, 12 | Backend PDF |
| **C** — Wizard (reglas y botones) | 6, 10, 13, 14 | Mayoría frontend |
| **D** — Dashboard | 11 | Fullstack |
| **E** — Generación sin restricciones | 15 | Backend, cambia reglas de negocio |

---

## Bloque A — Persona jurídica e identidad apalancada (puntos 1-4)

### Las dos causas raíz

**Causa 1 — `personType` no se deriva del NIT.** El actor se crea siempre con `personType: 'natural'` (`ActorsForm.tsx:125`) y solo cambia si el usuario toca el selector (`:941`). Cuando el paso 1 siembra `owner_document_type = NIT` para el vendedor, queda un actor con `tipoDocumento = 'NIT'` pero `personType = 'natural'`. El código ya convive con esa incoherencia parcheándola en cada sitio de forma distinta: `ActorsForm.tsx:310` acepta `personType === 'juridical' || tipoDocumento === 'NIT'`, pero `ExpedienteVisor.tsx:46` solo mira `documentType === 'NIT'`.

**Causa 2 — la UI y los sellos dependen de una fila LOCAL de `biometric_validations`.** `ExpedienteVisor.tsx:98` resuelve `vendedorBio` con `biometric.find(b => b.partyRole === 'vendedor')`. Cuando la identidad está **apalancada por documento** (HU #10350, sin clonar fila) o **cubierta por el baúl** (ADR-0025), esa fila no existe en el trámite ⇒ `bio = null` y todo lo que cuelga de ella queda vacío. El backend sí sabe resolverla (`FurCommand.cs:605` `ResolveApprovedValidationAsync` cae a `FindVigenteApprovedByDocumentAsync`), pero **ese resolutor no está expuesto a la UI**.

### Punto 1 — El NIT del paso 1 no cambia a Persona Jurídica en el paso del vendedor

**Cambio:** derivar `personType` del tipo de documento al hidratar/sembrar actores, no solo al pulsar el selector.
- `ActorsForm.tsx` — al construir el actor desde `field_values` (el `owner_document_type` sembrado por el paso 1), si el documento es `NIT` ⇒ `personType: 'juridical'`.
- Unificar el criterio en un helper compartido (`esJuridica(actor)`) y usarlo en `ActorsForm.tsx:310` y `ExpedienteVisor.tsx:46`, hoy divergentes.

**Riesgo:** bajo. Ojo con no pisar un cambio manual del usuario (si edita a natural con documento NIT, respetar la edición dentro de la misma sesión de formulario).

### Punto 2 — La variable `firma - ` sale vacía cuando la identidad se apalanca

**Diagnóstico:** `FurCommand.cs:641` arma el sello con `var firma = string.IsNullOrWhiteSpace(v.CertificateHash) ? "-" : v.CertificateHash!;`. El `-` que ve el usuario significa **`certificate_hash` nulo en la fila**. La consulta que trae la identidad apalancada (`ProcedureInstanceRepository.cs:268`) devuelve la entidad completa, así que no se pierde en la lectura: **el dato nunca se guardó**. El único punto que lo escribe es `IdentityValidationResultApplier.cs:58-59`, y solo si el resultado trae `CertificateHash` (el `firmaSerie` de Kyverum, `KyverumVerifyClient.cs:177`).

**Antes de codificar** — comprobación en DEV, decide el arreglo:
```sql
select id, party_role, status, validated_at, certificate_hash is null as sin_hash
from tramites.procedure_instance_biometric_validations
where status = 'aprobado' order by validated_at desc limit 20;
```
- Si **las prevalidaciones standalone** (HU #10867, `procedure_instance_id is null`) son las que salen sin hash ⇒ el camino de aprobación de prevalidación no propaga `firmaSerie`: arreglar ahí.
- Si salen sin hash **también las normales** ⇒ el webhook no trae `firmaSerie` y hay que tomarlo en la reconciliación (`GetStatusAsync`) al aprobar.

**Cambio:** propagar `firmaSerie` en el camino que falte + **backfill** de las filas aprobadas sin hash (script de reconciliación contra Kyverum, no migración ciega). Mientras no haya hash, el sello debe decir algo honesto (`Firma no disponible`) en vez de `-`.

### Punto 3 — Datos del vendedor sin correo ni RL, y mensaje equivocado con baúl

**Diagnóstico** (`ExpedienteVisor.tsx:202-224`):
- `<D label="Email" value={vendedorBio?.email} />` — vacío por la **causa 2**.
- `{esPersonaJuridica(vendedor) && <RepresentanteLegalBlock .../>}` — oculto por la **causa 1**.
- `IdentidadBlock` (`:304`) **no tiene rama de baúl**: siempre habla de certificado de validación de identidad.

**Cambio:**
1. Exponer en el detalle del trámite la identidad **efectiva** por parte (fila propia o apalancada por documento), reutilizando `ResolveApprovedValidationAsync`; que la UI consuma eso en vez de filtrar `biometric` por `partyRole`.
2. Tomar el correo del actor cuando no haya fila biométrica (el actor siempre tiene correo capturado).
3. `esPersonaJuridica` pasa al helper compartido del punto 1 ⇒ el bloque de RL aparece.
4. `IdentidadBlock` gana una rama **baúl**: cuando la firma provenga del baúl, mostrar «Firmado desde el baúl de firmas» con los metadatos del baúl (`FirmaBaulMetadata`), sin ofrecer el visor de certificado.

> **Pregunta abierta:** el punto dice «en el último paso del registro de trámites *Fur*». Lo diagnosticado es la **UI** (`ExpedienteVisor`). Si además se espera el correo y el RL **dentro del PDF del FUR**, es trabajo aparte: el manifiesto (`fur-field-manifest.json`, 80 campos) **no tiene ningún campo de correo ni de representante legal** — habría que calibrar coordenadas nuevas, como se hizo en HU #10921.

### Punto 4 — «Validación de identidad no encontrada» con VID activa

**Diagnóstico:** el texto es el `detail` del 404 de `BiometricaEndpoints.cs:268` (`DescargarCertificadoIdentidad`), que `IdentidadBlock` pinta en su estado de error. Se dispara cuando la fila que la UI cree local pertenece a otro trámite o es una prevalidación standalone: el handler la busca acotada a la instancia y no la encuentra. Misma **causa 2**.

**Cambio:** que la descarga del certificado acepte la validación **apalancada** (resolver por documento del sujeto, no solo por instancia), y que la UI distinga «no hay certificado» de «error». Con el punto 3 resuelto, `validationId` ya apunta a la validación efectiva.

**Riesgo del bloque A:** medio. Toca el contrato del detalle del trámite (nuevo campo de identidad efectiva). Mitigación: campo aditivo y opcional.

---

## Bloque B — Documentos generados (puntos 5, 7, 12)

### Punto 5 — El «25286» pegado a la fecha en el trámite virtual

**Diagnóstico:** confirmado y con causa exacta. `PreflightCommand.cs:934` hidrata `new HydratedField("transit_office_city", match.CityCode, null)` — es el **código DIVIPOLA**, no el nombre. `FurCommand.cs:485` lo pasa como `Ciudad`, y `SolicitudVirtualPdfGenerator.cs:36,52` imprime `$"{ciudad}, {fecha}"` ⇒ «25286, 28 de julio de 2026».

**Cambio:** hidratar el **nombre** de la ciudad (el resolutor del OT ya tiene el registro; añadir `CityName` si no lo expone) y conservar el código en `transit_office_city_code` si algo lo consume. Revisar los demás consumidores de `transit_office_city` antes de cambiar la semántica de la llave.

**Riesgo:** bajo-medio — cambiar el significado de una llave de `field_values` afecta trámites ya creados: los viejos seguirán con el código. Mitigación: al renderizar, si el valor es numérico de 5 dígitos, mapear a nombre.

### Punto 7 — La firma del baúl (PNG) se pasa de tamaño en el FUR

**Diagnóstico:** `FurOverlayRenderer.cs:135-137`:
```csharp
var imageW = Math.Min(SignatureImageMaxWidth, fieldW * 0.38);
DrawImage(gfx, field.X, field.Y, imageW, fieldH, imageBytes);
```
El ancho se acota (115 pt) pero **la altura se fija al alto completo del campo** y no se respeta la relación de aspecto: un PNG alto se estira y se sale del espacio de firma.

**Cambio:** escalar preservando aspecto contra una caja `(imageW, fieldH * k)` con `k ≈ 0.8`, centrando verticalmente, y bajar `SignatureImageMaxWidth`. Calibrar con un render de verificación (pymupdf, como en HU #10921) sobre un FUR real con firma de baúl.

**Riesgo:** bajo, pero **es visual**: exige verificación con render, no solo tests.

### Punto 12 — Formato de fecha AÑO/MES/DÍA sin hora

**Diagnóstico:** superficie amplia y hoy inconsistente. Los PDF usan `dd/MM/yyyy` (`FurCommand.cs:643`), y la UI usa `toLocaleString('es-CO')` **con hora** en al menos 10 sitios (`ExpedienteTimeline.tsx:20`, `PreflightPanel.tsx:233`, `PlateRangesConsole.tsx:218`, `WebhooksSection.tsx:186,329`, `AuditLogTable.tsx:117`, `improntas-nav.ts:41`, `IctLogs.tsx:188`, …).

**Cambio:** un formateador único (`formatFechaCorta` en front, helper equivalente en los generadores) con `yyyy/MM/dd`, y sustituir sitio por sitio.

> **Preguntas abiertas:** (a) ¿el separador es `/` o `-`? (b) ¿aplica también a **bitácoras y auditoría**, donde la hora sí es información útil (webhooks, logs ICT, timeline)? Mi recomendación: aplicarlo a documentos generados y tablas de negocio, y **conservar la hora en trazas técnicas**. Necesito tu confirmación antes de tocar esas 10 pantallas.

---

## Bloque C — Wizard: reglas y botones (puntos 6, 10, 13, 14)

### Punto 6 — Permitir el mismo correo para comprador y vendedor

**Diagnóstico:** regla de dominio en `TraspasoPartes.cs:39-40` (`MismoEmail`) con mensaje en `:51`, más un gemelo en frontend (`mensajePartesTraspasoDuplicadas`).

**Cambio:** eliminar la condición de **correo** conservando la de **documento** (que sí debe seguir bloqueando). Ajustar mensajes y tests de ambos lados.

> **A confirmar:** con el mismo correo, las dos invitaciones biométricas llegan al mismo buzón. Hay que verificar que el enlace por parte siga siendo distinguible (asunto/cuerpo con el rol) para que la persona no valide dos veces la misma parte.

### Punto 10 — Vendedor antes que comprador en el resumen

**Diagnóstico:** `FirmaFurStep.tsx:1043` — `const partes: SignatureParte[] = ['comprador', 'vendedor'];`. (`ExpedienteVisor` ya ordena vendedor→comprador en sus pestañas, así que esto además unifica.)

**Cambio:** invertir el array y revisar cualquier otro listado del paso final que itere partes.

### Punto 13 — Quitar los botones de FASECOLDA tras aceptar

**Diagnóstico:** `AvaluoComercialCard.tsx:117` («Aceptar valor sugerido») y `:162` («Usar» por fuente).

**Cambio:** ocultarlos cuando ya hay valor aceptado, dejando el valor visible con su fuente. Definir el estado «aceptado» (hoy solo se propaga con `onAccept`).

> **A confirmar:** ¿ocultarlos **siempre** o solo **después de aceptar**? El texto dice «al aceptar el valor sugerido, quitar…», que leo como después de aceptar. Si se quieren fuera siempre, el valor tendría que aplicarse automáticamente y eso es otro cambio.

### Punto 14 — Quitar los botones de solicitar firma de la compraventa

**Diagnóstico:** `FirmaFurStep.tsx:1190-1197` (`FirmaParteCard`). **Sin riesgo de bloqueo**: el gate de firma ya está desactivado en `SubmitGate.cs:96-98` (HU #10661/ADR-0028, «la firma de compraventa NO bloquea el traspaso»).

**Cambio:** retirar el bloque de solicitud (y el «Simular firma (DEV)»), conservando el estado cuando ya exista firma. Endpoints y modelo se dejan intactos, como decidió ADR-0028.

---

## Bloque D — Dashboard con los dos actores (punto 11)

**Diagnóstico:** `TramitesTable.tsx:597` solo pinta la columna «Comprador», y el resumen del backend solo resuelve el comprador (`ListProcedureInstancesQuery.cs:49,89` — `BuyerActorType`).

**Cambio:** fullstack.
1. Backend: añadir el vendedor al resumen (`InstanceSummary`), resolviéndolo igual que el comprador.
2. Frontend: columna «Vendedor» antes de «Comprador» (coherente con el punto 10) y recalcular `GRID_COLS`/`GRID_COLS_ADMIN` (`:132-134`), que hoy son plantillas fijas de 10/11 columnas.
3. En matrícula inicial no hay vendedor ⇒ celda vacía, no columna condicional (evita dos layouts).

**Riesgo:** bajo, pero la tabla ya va apretada: conviene revisar el responsive con el `flit-design-guardian`.

---

## Bloque E — Generar consolidado sin restricciones (punto 15)

**Diagnóstico:** `ConsolidadoCommand.cs:74-82` corta con `fur_requerido` si no existe adjunto `fur`, y con `documentos_incompletos` si falta documentación obligatoria. Además `SubmitGate` exige `ImprontaRequerida` en ambas modalidades (`:71,101`).

**Cambio:** invertir la lógica de «bloquear» a «resolver»:
1. Si falta el FUR, generarlo en el momento — `ConsolidadoCommand` **ya recibe** `IExpedienteHotDocumentsRegenerator` (`:41`), así que la pieza está disponible.
2. Si falta la impronta, generarla reutilizando el flujo de `POST /instances/{id}/attachments/generate-impronta` (`AttachmentEndpoints.cs:201`), extraído a un handler invocable.
3. Solo si la generación en cascada falla, devolver el error correspondiente — con el motivo real, no `fur_requerido`.

> **Decisión de negocio pendiente:** ¿se quita también `documentos_incompletos`? Generar el consolidado sin los documentos obligatorios produce un expediente incompleto que luego el OT rechaza. Mi recomendación: **generar igual pero marcar el consolidado como incompleto** y listar lo que falta, en vez de bloquear en silencio.

**Riesgo:** **el más alto del plan**. La impronta llama al RUNT (Kyverum): la generación del consolidado pasa a depender de un externo y a tardar más. Conviene medir y, si duele, encolarlo.

---

## Orden propuesto

| Fase | Puntos | Motivo |
|---|---|---|
| **1** | 1, 3, 4 | Causas raíz compartidas; desbloquean lo que el OT ve del vendedor. Un solo PR. |
| **2** | 2 | Depende de la comprobación en DEV; el backfill es independiente. |
| **3** | 6, 10, 13, 14 | Frontend de bajo riesgo, paralelizable desde el inicio. |
| **4** | 5, 7 | Documentos generados; el 7 exige render de verificación. |
| **5** | 11 | Fullstack aislado. |
| **6** | 15 | El de más riesgo: entra cuando lo demás esté estable. |
| **7** | 12 | Barrido transversal; último para no chocar con los PRs anteriores. |

## Propuesta de HUs

| HU | Tipo | SP | Alcance |
|---|---|---|---|
| Persona jurídica e identidad apalancada en el expediente | FULLSTACK | 5 | Puntos 1, 3, 4 |
| Sello de firma con el certificado de la identidad apalancada | BACKEND | 3 | Punto 2 + backfill |
| Ajustes del wizard: correo compartido, orden y botones | FRONTEND | 3 | Puntos 6, 10, 13, 14 |
| Ciudad del organismo y tamaño de firma en los documentos | BACKEND | 3 | Puntos 5, 7 |
| Vendedor y comprador en el dashboard de trámites | FULLSTACK | 3 | Punto 11 |
| Generación en cascada del consolidado | BACKEND | 5 | Punto 15 |
| Formato de fecha unificado | FULLSTACK | 3 | Punto 12 |

**Total: 25 SP.** Ninguna HU llega a las 800 líneas de PR.

## Preguntas abiertas (bloquean parte del alcance)

1. **Punto 3** — ¿el correo y los datos del RL se esperan solo en la UI o también dentro del PDF del FUR? Si es el PDF, hay que calibrar campos nuevos en el manifiesto.
2. **Punto 12** — separador de fecha y si aplica a bitácoras/auditoría (donde la hora es útil).
3. **Punto 13** — ¿los botones se ocultan tras aceptar o desaparecen siempre?
4. **Punto 15** — ¿se levanta también el bloqueo por documentos incompletos?
5. **Punto 6** — confirmar que dos enlaces biométricos al mismo buzón son distinguibles por rol.
6. **Punto 2** — requiere la consulta en DEV antes de decidir dónde se arregla.

# Plan técnico — Subsanar los huecos de SOAT, RTM, RUES y prendas en el FUR

**Fecha:** 2026-07-28
**Origen:** `docs/informe-fur-datos-incompletos-soat-rtm-rues-prendas.md`
**Severidad:** crítica (documentos del expediente se emiten con celdas en blanco de forma sistemática)
**Estado:** propuesta técnica — sin work items en ADO, sin código

---

## 1. Decisiones tomadas (cierran el diseño)

| # | Decisión | Elegida | Implicación |
|---|---|---|---|
| D1 | Origen de los campos SOAT/RTM que ningún proveedor entrega | **Persistir el OCR + completar mappers** | Sin proveedores ni contratos externos nuevos. El OCR del SOAT **ya extrae** póliza, aseguradora, fechas y estado: hoy se descarta |
| D2 | Dónde imprimir el acreedor de la prenda | **Bloque `observations` del FUR** | Sin recalibrar coordenadas en las 3 plantillas (automotor/maquinaria/remolques) |
| D3 | Entregable | **Plan + implementar Fase 1** | Fase 1 no depende de D1/D2 y es de riesgo bajo |

### Principio rector

> **Ningún documento del expediente debe leer una llave de `field_values` que ningún productor escriba.**

Esa invariante es la que falló y la que la Fase 5 convierte en test automático. Todo lo demás de este
plan es consecuencia de aplicarla.

---

## 2. Estado objetivo — cobertura del certificado SOAT/RTM

| Celda | Hoy | Tras el plan | Fuente |
|---|---|---|---|
| SOAT · N° Póliza | ⬜ vacía | ✅ | OCR (`numero_poliza`) |
| SOAT · Fecha expedición | ⬜ vacía | ✅ | OCR v2 (`fecha_expedicion`) |
| SOAT · Fecha vigencia | ⬜ vacía | ✅ | OCR (`fecha_inicio`) |
| SOAT · Fecha vencimiento | ✅ | ✅ | RUNT (prevalece) + OCR de respaldo |
| SOAT · Estado | ⬜ vacía | ✅ | Verifik `soat.Estado` (F1) |
| SOAT · Entidad | ✅ | ✅ | RUNT |
| RTM · N° RTM | ⬜ vacía | ✅ | OCR `rtm` (nuevo) |
| RTM · Fecha expedición | ⬜ vacía | ✅ | OCR `rtm` |
| RTM · Fecha vigencia | ⬜ vacía | ✅ | OCR `rtm` |
| RTM · Fecha vencimiento | ✅ | ✅ | RUNT (prevalece) |
| RTM · Estado | ⬜ vacía | ✅ | Verifik `rtm.Estado` (F1) |
| RTM · Entidad expide | ⬜ vacía | ✅ | Verifik `rtm.CdaExpide` (F1) |
| Texto intro · fecha consulta | ⬜ vacía | ✅ | `runt_consulta_fecha` (F1) |

**1 de 13 → 13 de 13.** Fase 1 sola ya sube a 6 de 13 sin tocar el wizard ni el OCR.

### Regla de precedencia entre fuentes (decisión de diseño, aplica a todas las fases)

El RUNT es **fuente oficial**; el OCR es **fuente de respaldo**. Por tanto:

1. Una llave escrita con `Source = "consultation"` **no se pisa** con OCR.
2. El OCR solo escribe llaves **ausentes** o cuyo `Source` sea `"ocr"`.
3. Si el RUNT llega después que el OCR (reconsulta), **sí** sobrescribe: la consulta es más fresca y más
   autoritativa.

Esto evita el escenario perverso de que un PDF de SOAT viejo cargado a mano contradiga al RUNT en el
mismo documento, y mantiene `soat_estado` — que además es un **gate de aprobación del OT** (`SoatGate`)
— siempre bajo control de la consulta oficial.

---

## 3. Fases y Historias de Usuario propuestas

Total estimado: **8 HUs · 27 SP**. Las fases 1-2 son secuenciales entre sí; 3 y 4 son independientes y
pueden ir en paralelo; la 5 cierra.

### Fase 1 — Recuperar el dato que ya está en casa `BACKEND · 4 SP`

Cero dependencias. Todo lo que se necesita ya viene deserializado del proveedor y se descarta en el
mapper. Es la fase que se implementa de inmediato (D3).

#### HU-1 · Completar el mapper de Verifik con los campos ya deserializados — `BACKEND · 2 SP`

`VerifikResultMapper.MapHydratedFields` ignora tres campos que `VerifikVehicleResponse` ya expone.
Verifik es **el proveedor cableado en producción** (`13-HU10201-consultation-providers.sql:13`), así que
esto se traduce en tres celdas pobladas en todos los trámites nuevos.

- `soat_estado` ← `soat.Estado`
- `rtm_estado` ← `rtm.Estado`
- `rtm_entidad` ← `rtm.CdaExpide`

```gherkin
Escenario: El certificado refleja el estado del SOAT reportado por el RUNT
  Dado un vehículo cuya consulta Verifik devuelve soat.estado = "VIGENTE"
  Cuando se ejecuta la consulta RUNT_VEHICLE del trámite
  Entonces field_values contiene soat_estado = "VIGENTE" con Source = "consultation"
  Y el certificado SOAT/RTM imprime "VIGENTE" en la celda Estado

Escenario: El certificado refleja la entidad que expidió la RTM
  Dado un vehículo cuya consulta Verifik devuelve tecnomecanica.cdaExpide = "CDA LA 80"
  Cuando se ejecuta la consulta RUNT_VEHICLE del trámite
  Entonces el certificado SOAT/RTM imprime "CDA LA 80" en la celda "Entidad expide RTM"

Escenario: Campo ausente en la respuesta no escribe la llave
  Dado un vehículo cuya consulta Verifik no devuelve tecnomecanica
  Cuando se ejecuta la consulta RUNT_VEHICLE del trámite
  Entonces no se escribe rtm_estado ni rtm_entidad en field_values
  Y las celdas correspondientes quedan en blanco (regla HU #10856)
```

> 🚨 **Riesgo bloqueante — normalización obligatoria.** `soat_estado` alimenta el gate que oculta
> Aprobar/Rechazar del OT (HU #10804). Hoy solo lo escribe `ValidateSoatViaRuntHandler` con el
> vocabulario `vigente|vencido|unknown` en minúscula; **Verifik devuelve `"VIGENTE"` en mayúscula**.
> El dominio tolera la diferencia (`SoatGate.IsSatisfied` usa `OrdinalIgnoreCase`), **pero el frontend
> no**: `frontend/lib/tramites/estados.ts:169,177` compara `soatEstado === 'vigente'` de forma
> estricta. Persistir el valor crudo dejaría `puedeAprobar = false` y `bloqueaAprobacion = true` con el
> SOAT vigente — es decir, **bloquearía la aprobación del OT en trámites correctos**.
> El valor **debe normalizarse al vocabulario de `SoatGate` antes de persistir**, con test que cubra
> ambos consumidores (dominio y frontend). Es el único punto de la Fase 1 con potencial de regresión.

#### HU-2 · Registrar la fecha de la consulta al RUNT — `BACKEND · 2 SP`

`runt_consulta_fecha` se lee en `FurCommand.cs:282` para el texto introductorio del certificado
("En la consulta realizada al RUNT 2.0 **el día ___**") y no la escribe nadie.

- Se persiste en `RunConsultationHandler.UpsertHydratedFields` cuando el template es de `EntityScope = vehicle`.
- Formato `dd/MM/yyyy` en huso Colombia (UTC-5), coherente con `BuildIdentidadSello` en `FurCommand.cs:624-627`.
- **También en HIT de caché**, con la fecha de la consulta **origen** (no la del reúso): el certificado
  debe declarar cuándo se consultó el RUNT de verdad. Hoy `BuildCachedResult` no propaga `queriedAt` para
  esta ruta — hay que exponerlo desde `ExternalQueryCacheService`.

```gherkin
Escenario: La fecha de consulta viaja al certificado
  Dado un trámite en borrador
  Cuando se ejecuta la consulta RUNT_VEHICLE el 2026-07-28
  Entonces field_values contiene runt_consulta_fecha = "28/07/2026"
  Y el certificado SOAT/RTM dice "En la consulta realizada al RUNT 2.0 el día 28/07/2026"

Escenario: Un reúso de caché declara la fecha de la consulta original
  Dado un resultado de vehículo cacheado el 2026-07-20
  Cuando otro trámite lo reutiliza el 2026-07-28
  Entonces runt_consulta_fecha = "20/07/2026"
```

---

### Fase 2 — SOAT y RTM desde el OCR `FULLSTACK · 10 SP`

El hallazgo que abarata esta fase: `DocumentOcrPrompts.Soat` **ya pide** `numero_poliza`, `aseguradora`,
`fecha_inicio`, `fecha_vencimiento` y `estado_poliza`, y el wizard ya corre ese OCR sobre el PDF del SOAT
en matrícula **y** en traspaso (`useProcedureDocuments.ts:15-18`). El dato se extrae, se pinta en el
panel de validación y se tira. Solo falta persistirlo.

#### HU-3 · Persistir en `field_values` lo que el OCR extrae — `FULLSTACK · 5 SP`

El endpoint `POST /ocr/{tipo}` es *stateless por diseño* y no conviene romperlo (no conoce la instancia).
Se añade un caso de uso dedicado en vez de reutilizar `PatchFieldValuesHandler`, que marca todo como
`Source = "user"` y no permite expresar la precedencia de §2.

- **Backend:** `PersistOcrFieldsHandler` + `POST /procedure-instances/{id}/ocr-fields`.
  - Whitelist de llaves **por tipo de documento** (un OCR de `soat` no puede escribir campos del vehículo).
  - `Source = "ocr"`.
  - Aplica la regla de precedencia: no pisa `Source = "consultation"`.
  - Respeta `borrador`/`subsanacion` (mismo criterio que `PatchFieldValuesHandler:44`).
- **Frontend:** al recibir un OCR `verified` de tipo `soat`, se invoca el endpoint con el mapeo:

| Campo OCR | Llave `field_values` |
|---|---|
| `numero_poliza` | `soat_poliza` |
| `aseguradora` | `soat_aseguradora` |
| `fecha_inicio` | `soat_vigencia` |
| `fecha_vencimiento` | `soat_vencimiento` |
| `estado_poliza` | `soat_estado` *(normalizado a `SoatGate`)* |

```gherkin
Escenario: El OCR del SOAT alimenta el certificado
  Dado un trámite en borrador sin consulta RUNT previa
  Cuando el gestor carga el PDF del SOAT y el OCR lo verifica
  Entonces field_values contiene soat_poliza, soat_vigencia y soat_aseguradora con Source = "ocr"
  Y el certificado SOAT/RTM imprime esos tres valores

Escenario: El OCR no contradice al RUNT
  Dado un trámite con soat_vencimiento = "2026-12-31" y Source = "consultation"
  Cuando el OCR de un SOAT antiguo extrae fecha_vencimiento = "2025-12-31"
  Entonces soat_vencimiento conserva "2026-12-31" y Source = "consultation"
  Y soat_poliza sí se escribe, porque ningún proveedor la entrega

Escenario: El OCR no puede escribir fuera de su alcance
  Cuando un OCR de tipo "soat" intenta escribir vehicle_brand
  Entonces la llave se descarta y se responde 400 sin persistir nada

Escenario: Un trámite entregado no admite escritura por OCR
  Dado un trámite en estado "entregado"
  Cuando se intenta persistir campos por OCR
  Entonces se responde "not_draft" y no se altera field_values
```

> **Sobre `soat_estado`:** como es un gate de negocio, se escribe por OCR **solo si la llave está
> ausente**. En cuanto el RUNT se pronuncia (`ValidateSoatViaRuntHandler` o HU-1), manda la consulta.

#### HU-4 · Fecha de expedición del SOAT en el prompt (v2) — `BACKEND · 2 SP`

El prompt v1 extrae `fecha_inicio` (inicio de vigencia) pero **no** la fecha de expedición, que son
distintas y el certificado pide ambas.

`DocumentOcrPrompts` está marcado *"Prompts fijados en v1 (no reescribir)"*, así que esto es un cambio
consciente de contrato: se añade `fecha_expedicion` al bloque EXTRAER y al JSON de salida del prompt de
SOAT, y se versiona el prompt como v2 dejando constancia en el comentario de la clase.

```gherkin
Escenario: El OCR extrae la fecha de expedición del SOAT
  Dado un PDF de SOAT con fecha de expedición 2026-01-15 y vigencia desde 2026-01-20
  Cuando se analiza con el OCR de tipo "soat"
  Entonces el resultado trae fecha_expedicion = "2026-01-15" y fecha_inicio = "2026-01-20"
  Y el certificado imprime ambas en celdas distintas
```

#### HU-5 · OCR de RTM — `FULLSTACK · 3 SP`

No existe prompt de RTM: `DocumentOcrPrompts.SupportedTipos` solo cubre `factura|aduana|impronta|soat`.
El tipo de documento `rtm` **sí existe** ya en el catálogo (`23-HU10520-document-types-seed.sql`), así
que no hace falta tocar el seed.

- Prompt nuevo `Rtm` con el mismo patrón (validaciones, bloque multipágina, JSON sin markdown), extrayendo
  `numero_certificado`, `cda_expide`, `fecha_expedicion`, `fecha_vigencia`, `fecha_vencimiento`, `estado`.
- `'rtm'` añadido a `SupportedTipos` y a `OCR_TIPOS` del frontend (ambas modalidades).
- Persistencia por el endpoint de HU-3 con su propia whitelist → `rtm_numero`, `rtm_entidad`,
  `rtm_expedicion`, `rtm_vigencia`, `rtm_vencimiento`, `rtm_estado`.

```gherkin
Escenario: El OCR de la RTM completa el bloque del certificado
  Dado un trámite de traspaso en borrador
  Cuando el gestor carga el certificado de RTM y el OCR lo verifica
  Entonces el bloque RTM del certificado queda completo en sus seis celdas

Escenario: Matrícula inicial no muestra RTM aunque exista el dato
  Dado un trámite de matrícula inicial con campos rtm_* poblados
  Cuando se genera el FUR
  Entonces el certificado no incluye el bloque RTM (comportamiento HU #10856 intacto)
```

---

### Fase 3 — RUES resuelto por actor en la generación `BACKEND · 5 SP`

Las tres causas del informe (§5.1 precarga que corta la consulta, §5.2 solo persiste en borrador, §5.3
un solo juego de llaves por trámite) tienen una raíz común: **el certificado depende de lo que el wizard
haya alcanzado a escribir**. Se invierte la dependencia.

#### HU-6 · Certificado RUES autosuficiente y por actor — `BACKEND · 5 SP`

- `TryGenerateRuesCertificate` deja de leer llaves planas de instancia y resuelve **por actor jurídico**.
- Si faltan datos para un NIT, consulta el provider `verifik_rues` en el momento de generar, **a través de
  `ExternalQueryCacheService`** (ya existe, con TTL por fuente) para no añadir latencia ni costo por
  trámite en NITs recurrentes.
- Best-effort estricto: un fallo del proveedor **nunca** bloquea el FUR — mismo contrato que el
  certificado RNMC y el de identidad (`FurCommand.cs:771`).
- **Un certificado por actor jurídico**, no uno por trámite: en traspaso PJ → PJ se emiten dos
  (`certificado_rues` y `certificado_rues_comprador`, replicando el patrón ya usado para
  `certificado_identidad` / `certificado_identidad_vendedor` y para las escrituras).

Esto hace irrelevantes §5.1 y §5.2 sin tocar el trigger de inmutabilidad ni el flujo del wizard: la
precarga por directorio puede seguir cortando la consulta para efectos de UX, porque el certificado ya no
depende de ella.

```gherkin
Escenario: NIT precargado desde el directorio de representantes legales
  Dado un actor jurídico cuyo NIT está en el directorio del tenant
  Y que por ello el wizard no ejecutó la consulta RUES
  Cuando se genera el FUR
  Entonces el certificado RUES se emite completo (matrícula mercantil, cámara, actividades)

Escenario: Traspaso entre dos personas jurídicas
  Dado un traspaso con vendedor PJ (NIT A) y comprador PJ (NIT B)
  Cuando se genera el FUR
  Entonces se emiten dos certificados RUES, uno por NIT
  Y ningún certificado mezcla la razón social de una compañía con la matrícula de la otra

Escenario: El proveedor RUES no responde
  Dado un actor jurídico y el proveedor RUES caído
  Cuando se genera el FUR
  Entonces el FUR y el resto de anexos se generan igual
  Y se registra un warning sin emitir un certificado RUES vacío
```

> **Cambio de comportamiento a validar con negocio:** hoy se emite un certificado RUES aunque esté casi
> vacío. Con esta HU, si no hay datos **no se emite**. Es lo correcto (un certificado en blanco no
> certifica nada), pero es un cambio visible en el consolidado y debe anunciarse.

---

### Fase 4 — Prenda: imprimir el acreedor `BACKEND · 3 SP`

#### HU-7 · Acreedor de la prenda en las observaciones del FUR — `BACKEND · 3 SP`

El dato ya llega hasta el generador: `FurCommand.cs:139-141` lo resuelve y `:497` lo pasa como
`FurDocumentData.AcreedorPrenda`. `FurFieldMapper` simplemente nunca lo referencia. Por D2 se compone en
`observations`, que ya es multilínea y ya recibe texto automático vía
`FurTransformationObservations.Compose` (`FurCommand.cs:488-491`).

- El bloque se antepone a las observaciones manuales y a las de transformación, y solo aparece cuando
  `TienePrenda` es verdadero (decisiones `solicitar`/`registrar`; `sin_prenda`/`omitir`/`levantar` no).
- Sin acreedor capturado, se marca la casilla como hoy y no se escribe texto (no se inventa contenido).

```gherkin
Escenario: El FUR nombra al beneficiario del gravamen
  Dado un trámite con prenda "registrar" y acreedor "BANCO XYZ S.A." NIT 890900608
  Cuando se genera el FUR
  Entonces la casilla requested_process_11 está marcada
  Y las observaciones incluyen "GRAVAMEN / PRENDA A FAVOR DE: BANCO XYZ S.A. — NIT 890900608"

Escenario: Prenda sin acreedor capturado
  Dado un trámite con prenda "solicitar" y sin datos del acreedor
  Cuando se genera el FUR
  Entonces la casilla queda marcada y las observaciones no mencionan acreedor

Escenario: Sin prenda no se altera el bloque de observaciones
  Dado un trámite con decisión "sin_prenda" y una transformación de color declarada
  Cuando se genera el FUR
  Entonces las observaciones contienen solo el texto de la transformación (comportamiento ADR-0029 intacto)
```

---

### Fase 5 — Blindaje contra la reincidencia `BACKEND · 5 SP`

#### HU-8 · Guardia de contrato entre documentos y `field_values` — `BACKEND · 5 SP`

Esta es la HU que impide que el problema vuelva. Sin ella, el próximo campo que se añada a un certificado
puede quedar huérfano exactamente igual y nadie se entera hasta que un OT reclama.

Existe el precedente exacto: `FurManifestGuardTests` ya valida que *"todos los tokens del mapper tengan
placement"* en el manifest. Se replica el mismo concepto una capa más abajo.

- Registro declarativo de las llaves de `field_values` que **produce** cada mapper/handler.
- Test que recorre las llaves que **consume** cada generador de documentos y falla si alguna no tiene
  productor declarado, nombrando la llave y el documento.
- Cobertura inicial: certificado SOAT/RTM, certificado RUES, FUR.

```gherkin
Escenario: Una llave consumida sin productor rompe el build
  Dado un generador de documento que lee la llave "soat_poliza"
  Y que ningún mapper ni handler declara producirla
  Cuando corre la suite de guardia
  Entonces el test falla nombrando la llave y el documento que la consume

Escenario: El estado actual del repositorio pasa la guardia
  Dado el conjunto de llaves consumidas por los tres documentos
  Cuando corre la suite de guardia tras las fases 1 a 4
  Entonces no hay llaves huérfanas
```

---

## 4. Orden de ejecución y dependencias

```
Fase 1 ── HU-1 ─┬─────────────────────────────────────► (independiente)
          HU-2 ─┘
                 │
Fase 2 ── HU-3 ──┴─► HU-4 ─► HU-5      (HU-3 es prerrequisito de 4 y 5)

Fase 3 ── HU-6 ─────────────────────────► (independiente, paralelizable)

Fase 4 ── HU-7 ─────────────────────────► (independiente, paralelizable)

Fase 5 ── HU-8 ─────────────────────────► (al final: la guardia debe pasar en verde)
```

- **Ruta crítica:** HU-3 → HU-4/HU-5. Es la fase con más superficie (backend + frontend + prompt).
- **Quick wins:** HU-1 y HU-2 se pueden mergear solas y ya mejoran el documento en producción.
- **Paralelizables:** Fases 3 y 4 no comparten archivos con las 1-2.
- **HU-8 va al final** por definición: su valor es certificar que no quedaron huecos.

### Convenciones FLIT aplicables

- Rama: `feature/AB-XXXXX-fur-datos-incompletos` (una por HU o compartida, según cómo se descomponga).
- Commits: `HUXXXXX: descripción breve`.
- PRs ≤ 800 líneas → HU-3 y HU-6 probablemente exijan PR propio.
- Todas las HUs cierran con `dev-tester` (evidencias PASO 6 en ADO) — obligatorio al terminar cada una.

---

## 5. Riesgos y regresiones a cubrir

| # | Riesgo | Fase | Mitigación |
|---|---|---|---|
| R1 | 🚨 **`soat_estado` es un gate de aprobación del OT.** Verifik devuelve `"VIGENTE"`; el dominio tolera mayúsculas pero `frontend/lib/tramites/estados.ts:169,177` compara estricto contra `'vigente'`. Escribirlo crudo **bloquearía la aprobación del OT con el SOAT vigente** | 1 | Normalizar a minúscula (`SoatGate.Vigente\|Vencido\|Unknown`) antes de persistir + test en dominio **y** en frontend |
| R2 | El OCR pisa un dato oficial del RUNT con un PDF viejo | 2 | Regla de precedencia de §2, verificada con test de contradicción explícito |
| R3 | El trigger `trg_field_value_immutable` rechaza la escritura por OCR fuera de borrador | 2 | El handler valida estado **antes** de escribir y responde `not_draft` limpio (patrón `PatchFieldValuesHandler:44`) |
| R4 | Cambiar el prompt v1 degrada extracciones que hoy funcionan | 2 | Solo se **añade** un campo; se versiona v2 y se prueba contra PDFs reales de SOAT antes de mergear |
| R5 | Consultar RUES en la generación del FUR añade latencia y costo por trámite | 3 | Vía `ExternalQueryCacheService` con TTL por fuente; best-effort con timeout |
| R6 | Dejar de emitir el certificado RUES vacío es un cambio visible en el consolidado | 3 | Anunciar a negocio antes de desplegar (§ nota de HU-6) |
| R7 | Tocar `observations` puede desplazar el texto de transformaciones de color/combustible (ADR-0029) | 4 | Test de composición con prenda + transformación simultáneas |
| R8 | La regeneración del FUR invalida el consolidado maestro; más escrituras ⇒ más regeneraciones | 2,3,4 | Comportamiento ya existente (`ConsolidadoMaestroVigente = false`); verificar que no se dispare en bucle |

---

## 6. Fuera de alcance (deuda consciente)

Se documenta para que sea una decisión y no un olvido:

1. **Mapper de INTEMPO.** Es el único proveedor con el shape completo del SOAT y hoy no hidrata ni un
   campo, pero **no está cableado** (`RUNT_VEHICLE → verifik`). Arreglarlo ahora sería trabajo
   especulativo; queda anotado para cuando se evalúe migrar de proveedor.
2. **Detalle de gravámenes del RUNT.** `IntempoGravamen` trae `nombreAcreedor`, `fechaInscripcion` y
   `estadoPrenda`; los tres proveedores lo reducen a un semáforo ok/warn. Recuperarlo exige un anexo
   nuevo (la opción "certificado de gravámenes" descartada en D2) y se propone como Feature aparte.
3. **Campo dedicado de acreedor en el manifest.** D2 eligió `observations`. Si negocio exige fidelidad
   estricta al formato oficial, implica calibración MLS de las 3 plantillas — mismo esfuerzo que
   HU10921/HU10922.

---

## 7. Referencias de código por HU

| HU | Archivos principales |
|---|---|
| HU-1 | `Flit.Tramites.Application/UseCases/Consultations/VerifikResultMapper.cs` · `VerifikVehicleResponse.cs` · `Flit.Tramites.Domain/Tramites/Services/SoatGate.cs` |
| HU-2 | `UseCases/Consultations/RunConsultationCommand.cs` · `ExternalQueryCacheService` |
| HU-3 | `Flit.Api/Endpoints/Tramites/OcrEndpoints.cs` · `UseCases/ProcedureInstances/PatchFieldValuesCommand.cs` (patrón) · `frontend/hooks/useProcedureDocuments.ts` · `frontend/components/operacion/DocumentChecklist.tsx` |
| HU-4 | `Flit.Tramites.Application/Ocr/DocumentOcrPrompts.cs` |
| HU-5 | `DocumentOcrPrompts.cs` · `frontend/hooks/useProcedureDocuments.ts` |
| HU-6 | `UseCases/ProcedureInstances/FurCommand.cs:675-712` · `Flit.Infrastructure/Consultations/VerifikRuesConsultationProvider.cs` · `Flit.Infrastructure/Documents/RuesCertificatePdfGenerator.cs` |
| HU-7 | `UseCases/ProcedureInstances/FurTransformationObservations.cs` · `FurCommand.cs:488-497` · `Flit.Infrastructure/Documents/Fur/FurFieldMapper.cs` |
| HU-8 | `tests/Flit.Infrastructure.Tests/Documents/FurManifestGuardTests.cs` (precedente) |

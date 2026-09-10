# Plan técnico — Feature #11254 · Ajustes de fidelidad del FUR

> Generado: 2026-08-04 · Fase 2 del workflow `/requirement-to-delivery`
> Fuente del requerimiento: `ajustes-fur-1.txt` · Feature en ADO: #11254
> Sin ADR: no hay entidad de negocio nueva, ni persistencia, ni dependencia nueva, ni contrato alterado.
> Sin cambios de schema, API ni frontend.

## 1. Hallazgos de reconocimiento que condicionan el diseño

| # | Hallazgo | Consecuencia |
|---|---|---|
| H1 | `observations` **no es el único campo `multiline`**: también lo son `vehicle_owner_signature` y `vehicle_buyer_signature`, en las tres plantillas. En automotor el sello del comprador declara `h: 35.3` con `fontSize: 8` — cuatro líneas ocupan 40 pt, o sea **ya desborda hoy**. | Aplicar el auto-encaje a todo `multiline` encogería los sellos de firma de todos los FUR. Se resuelve con la bandera `autoFit` (§4). |
| H2 | `requested_process_11` **solo existe en el manifiesto de automotor**. | Hoy un trámite de maquinaria o remolque con prenda **no marca nada**. El Feature cierra un hueco preexistente; es un cambio de salida visible para esos formatos. |
| H3 | `FurManifestGuardTests.Manifest_MatchesFrozenBaseline` congela la geometría **solo de automotor**. | Los desplazamientos y la casilla nueva rompen esa guardia a propósito: hay que regenerar la línea base con el emisor `EmitBaseline`. Los cambios en maquinaria/remolques no los detecta nadie hoy → CI1 extiende la guardia a los tres. |
| H4 | `Mapper_EmitsOnlyTokensDefinedInManifest` verifica placement **solo contra automotor**. | Un olvido de `requested_process_12` en maquinaria/remolques no se pintaría y no fallaría ningún test. Se añade guardia específica. |

**Lectura fijada (D1):** la casilla de tipo de trámite (**1** matrícula / **2** traspaso) **no cambia**. El único cambio es elegir entre 11 y 12 según la modalidad de prenda. `MarkTramite` conserva intacta la lógica de `requested_process_1/2`.

## 2. UT-1 · Transporte de la modalidad de prenda

### El problema

`FurDocumentData` transporta hoy `bool TienePrenda`, poblado en `FurCommand.cs:172` como
`PrendaDecision.ImplicaGravamen(prendaVigente.Decision)`. `levantar` colapsa a `false`,
**indistinguible de "sin prenda"**: la modalidad se pierde antes de llegar al generador.

### Alternativas evaluadas

| Opción | Descripción | Veredicto |
|---|---|---|
| A | Decisión cruda (`string? PrendaDecisionVigente`) hasta el mapper | Descartada: mete una regla regulatoria en `Flit.Infrastructure`, obliga a comparar strings en la capa de dibujo, y deja dos fuentes de verdad que pueden divergir |
| **B** | Valor semántico ya resuelto: enum `FurPrendaMarking` | **Elegida** |
| C | Derivarla en el mapper de lo que ya viaja | Imposible: `levantar` llega exactamente igual que `sin_prenda`. Derivarlo del texto de observaciones sería parsear una cadena de presentación |

### Decisión — Opción B, variante B1

```
enum FurPrendaMarking { Ninguna, Constitucion, Levantamiento }
```

- La traducción `decisión → marca` vive en el **dominio** (`PrendaDecision.ToFurMarking`), no en Infrastructure.
- `FurCommand` resuelve la marca al ensamblar; `FurFieldMapper` solo hace enum → id de casilla.
- `bool TienePrenda` se sustituye por `FurPrendaMarking PrendaMarking = Ninguna` y `TienePrenda`
  queda como **propiedad calculada** (`PrendaMarking == Constitucion`): una sola fuente de verdad, y
  los call-sites que pasaban `TienePrenda:` con nombre rompen la compilación — fallo ruidoso, no silencioso.
- `ImplicaGravamen` **no se toca**: sigue sirviendo a otros consumidores.

| `PrendaDecision` | `FurPrendaMarking` | Casillas |
|---|---|---|
| `solicitar`, `registrar` | `Constitucion` | tipo (1 ó 2) **+ 11** |
| `levantar` | `Levantamiento` | tipo (1 ó 2) **+ 12** |
| `omitir`, `sin_prenda`, null, sin prenda | `Ninguna` | solo tipo, como hoy |

### CF11 — texto del acreedor en el levantamiento

`FurPrendaObservation.Compose(bool tienePrenda, …)` devuelve `null` cuando no hay gravamen, así que
hoy un levantamiento no declara sobre qué prenda actúa. Pasa a recibir la marca en vez del bool y a
emitir el literal propio **«LEVANTAMIENTO DE GRAVAMEN A FAVOR DE:»**. Sin nombre de acreedor no se
imprime nada: no se inventa contenido. El literal de constitución no cambia.

## 3. UT-2 · Encaje del texto multilínea

### Emplazamiento

Método nuevo **`FurTextFitter.FitMultiline`**, reutilizando los privados `Wrap`, `MaxLines`,
`Truncate` y la inyección de `measure`. **`Fit` no se modifica ni una línea** — sirve la ruta `text`
en producción desde la HU #11048.

El orden de estrategias es deliberadamente el inverso: en un campo `text` (nombre, razón social) se
encoge antes de partir, porque partir un nombre rompe la calibración de la casilla; en un párrafo se
parte antes de encoger, porque la legibilidad manda y el recuadro está pensado para varias líneas.

### Algoritmo

Preprocesado **idéntico al actual** — `Split('\n', RemoveEmptyEntries | TrimEntries)`. Esto es parte
de la garantía de CF4.

1. **Passthrough (garantía CF4).** Si `W <= 0`, o si cada párrafo mide `<= W` al cuerpo declarado y
   `nParrafos * fontSize * 1.25 <= H`, devolver **las mismas líneas y el mismo cuerpo**. El renderer
   dibuja la misma secuencia de `DrawString` con la misma fuente y las mismas líneas base: salida
   idéntica salvo por el desplazamiento del manifiesto.
2. **Envolver al cuerpo base (CF2).** `Wrap` por párrafo, concatenando; los `\n` siguen siendo saltos duros.
3. **Reducir cuerpo re-envolviendo (CF3).** De `fontSize - 0.25` hasta **5 pt** en pasos de `0.25`,
   **re-envolviendo en cada tamaño** (más ancho por línea ⇒ menos líneas). Se acepta el primero que quepa.
4. **Último recurso.** A 5 pt: recortar a las líneas que caben y truncar la última con elipsis, más
   `LogWarning` con el id del trámite. Nunca dibujar fuera de la caja: pisar los campos vecinos de un
   formulario oficial es peor que elidir.

Constantes: `MinMultilineFontSize = 5` propio — el `MinFontRatio = 0.65` de `Fit` daría 4,2–4,7 pt
sobre los cuerpos base 6,5/7,2 y no cumple CF3. `Step` y `LineHeightFactor` se reutilizan.

**Capacidad resultante:**

| Plantilla | Caja de observaciones | Cuerpo base | Líneas a base | Líneas a 5 pt |
|---|---|---|---|---|
| Automotor | 403,1 × 33,0 | 7,2 bold | 3 | 5 |
| Maquinaria | 490 × 38 | 6,5 | 4 | 6 |
| Remolques | 490 × 55 | 6,5 | 6 | 8 |

### D2 — Acotar el auto-encaje: opt-in por manifiesto (CF12)

Por H1, el encaje **no** puede aplicarse a todo `multiline`. Se añade `FurFieldDefinition.AutoFit`
(`bool`, default `false`) y el renderer aplica `FitMultiline` solo si
`field.Type == Multiline && field.AutoFit`. La bandera se declara **únicamente** en `observations`,
en los tres manifiestos. Los sellos de firma salen como hoy por construcción, no por medición.

Se descartaron: condicionar por `field.Id == "observations"` (string mágico en el renderer, invisible
desde el manifiesto) y aplicarlo a todo `multiline` (cambiaría los sellos).

## 4. UT-3 · Desplazamientos y calibración

### 4.1 Desplazamientos (aritmética directa)

Origen top-left ⇒ arriba es `y -= 5`, izquierda es `x -= 2`. Las tres plantillas están a 72 dpi.

| Manifiesto | Campo | Actual (x, y) | Propuesto (x, y) |
|---|---|---|---|
| automotor | `observations` | 381,9 · 477,6 | **379,9 · 472,6** |
| maquinaria | `observations` | 498 · 450 | **496 · 445** |
| remolques | `observations` | 498 · 422 | **496 · 417** |
| automotor | `vehicle_serial_number` | 570,0 · 291,5 | **570,0 · 286,5** |
| remolques | `vehicle_serial_number` | 736 · 204 | **736 · 199** |
| maquinaria | `vehicle_serial_number` | — | no aplica (la plantilla no tiene la casilla) |

Ancho, alto, cuerpo y alineación **no se tocan**: mover la caja sin redimensionarla es lo que hace
CF4 comprobable.

### 4.2 Casillas faltantes (5 declaraciones)

**Automotor · `requested_process_12` — derivación directa.** La casilla 11 calibrada está en
`(286,9 · 170,9)`, o sea el rótulo «11» `(259,0 · 151,4)` **+ (27,9 · 19,5)**. El rótulo «12» está en
`(315,4 · 151,4)`, misma fila:

```json
{ "id": "requested_process_12", "page": 1, "type": "checkbox", "x": 343.3, "y": 170.9, "size": 10.1 }
```

El `size` se hereda del hermano y **no es cosmético**: `DrawCheckbox` deriva la línea base de `Size`
(`Y + Size*0.85`) mientras el cuerpo de la "X" es fijo. Un `size` distinto desalinea la marca
verticalmente respecto de la casilla de al lado.

**Maquinaria y remolques (4 casillas) — medición, no estimación.** No hay hermano calibrado en la
fila `y ≈ 131,7` (las casillas 1 y 2 están en `y ≈ 101–102`). En este orden:

1. **Primario:** `page.get_drawings()` con pymupdf sobre el PDF blank; quedarse con los rectángulos
   cuyo centro caiga junto a cada rótulo. Anclar la declaración al rectángulo real.
2. **Respaldo:** medir los rótulos «1» y «2» de *esa misma plantilla*, calcular el delta contra sus
   casillas ya calibradas (`(101·102)` y `(170·102)` en maquinaria; `(86·101)` y `(155·101)` en
   remolques), promediar y aplicarlo a «11»/«12». **No** transferir el offset de automotor: distinta
   plantilla, distinto tamaño de página (1008×612 vs 792×612) y distinta métrica de casilla.
3. `size = 9`, igual que sus hermanas `requested_process_1/2`.
4. Medir y documentar también la casilla **10** en el mismo barrido: mismo coste ahora, evita repetir
   la calibración cuando aparezca.

### 4.3 Verificación objetiva

1. **Renderizar** con `tools/fur-preview`, ampliado con escenarios: `obs-corta` (control de CF4),
   `obs-media`, `obs-larga`, `obs-extrema` (fuerza truncado), palabra única más ancha que la caja, y
   `prenda-constitucion` / `prenda-levantamiento` **en los tres formatos**.
2. **Medir con pymupdf:**
   - Desplazamientos: `Δy = -5,0 ± 0,1` y `Δx = -2,0 ± 0,1` comparando **el render de antes contra el
     de después**, no contra una expectativa a ojo.
   - Casillas: el bbox de la "X" cae dentro del rectángulo impreso con margen a los cuatro lados, y
     ninguna otra casilla del grupo lleva tinta.
   - **CF4 (prueba dura):** render de `obs-corta` con el manifiesto anterior, aplicar a mano el offset
     `(-2, -5)` al bbox medido, y verificar coincidencia dentro de ±0,1 pt **y cuerpo de fuente
     idéntico**. Si el cuerpo cambió, el passthrough falló.
   - **CF12:** bbox y cuerpo de `vehicle_owner_signature` / `vehicle_buyer_signature` **idénticos**
     antes y después.
3. **Evidencia** en Discussion del work item (nunca en `Custom.Evidences`): PDF antes/después por
   formato, tabla de deltas medidos y recorte del grupo de casillas 10-11-12 de cada plantilla. El
   script de medición se versiona junto a sus hermanos `calibrate-*.py`.

## 5. Archivos por unidad de trabajo

Rutas relativas a `services/core-api/`.

### UT-1 · Modalidad de prenda

**Modificar:** `src/Flit.Tramites.Domain/Tramites/ValueObjects/PrendaDecision.cs` ·
`src/Flit.Tramites.Application/Documents/IFurDocumentGenerator.cs` ·
`src/Flit.Tramites.Application/UseCases/ProcedureInstances/FurCommand.cs` (~172, ~509, ~583) ·
`src/Flit.Tramites.Application/UseCases/ProcedureInstances/FurPrendaObservation.cs` (CF11) ·
`src/Flit.Infrastructure/Documents/Fur/FurFieldMapper.cs` · los tres `fur-field-manifest*.json`.

**Crear:** `src/Flit.Tramites.Application/Documents/FurPrendaMarking.cs`.

**Tests:** `FurPrendaMarkingTests` (decisión × formato, casilla contraria vacía) ·
`PrendaDecisionFurMarkingTests` en Domain.Tests (incluidos `omitir`, null y valor desconocido) ·
`FurMultiFormatManifestTests` (guardia H4: `requested_process_1/2/11/12` en los tres manifiestos) ·
`FurManifestGuardTests` (regenerar línea base).

### UT-2 · Encaje multilínea

**Modificar:** `FurTextFitter.cs` (`FitMultiline` + `MinMultilineFontSize`; `Fit` intacto) ·
`FurFieldModels.cs` (`AutoFit`) · `FurOverlayRenderer.cs:112-115` · los tres manifiestos (`autoFit`
solo en `observations`).

**Tests:** bloque nuevo en `FurTextFitterTests` con `measure` inyectado — passthrough exacto, respeto
de `\n`, envolvido a cuerpo base, reducción escalonada, tope en 5 pt, truncado final, palabra más
ancha que la caja, texto vacío, `W = 0` · `FurObservacionesYFechaTests` (el texto compuesto largo
llega íntegro) · test nuevo de **no-regresión de sellos** (con `AutoFit = false` los
`vehicle_*_signature` producen exactamente las líneas y el cuerpo de hoy).

### UT-3 · Desplazamientos y calibración

**Modificar:** los tres manifiestos (`observations`, `vehicle_serial_number`, subir `version`) ·
`FurManifestGuardTests` (regenerar línea base, añadir `AutoFit` a la huella `Canon`, **extender a los
tres formatos** por CI1) · `tools/fur-preview/Program.cs` (escenarios nuevos).

**Crear:** `tools/fur-preview/calibrate-prenda-boxes.py` · `tools/fur-preview/verify-fur-11254.py`.

**Orden:** UT-3 → UT-2 → UT-1. UT-1 y UT-3 se pisan en la línea base y en los manifiestos: **una sola
rama** con commits separados, o PRs serializados.

## 6. Notas para QA

**Observaciones:** corta (la que hoy cabe — *este es el caso que puede tumbar el Feature*), justo en
el límite de ancho, larga (≈400 car.) en los tres formatos, desmedida (≈2.000 car. → 5 pt, elipsis,
cero tinta fuera), composición completa (gravamen + manual + transformaciones ADR-0029), palabra
única sin espacios más ancha que la caja, acentos y `Ñ`, `\n` explícitos, y sin observaciones.

**Número de serie:** serie larga y serie vacía en automotor y remolques. **Maquinaria no debe pintar
nada** en esa zona.

**Prenda — matriz obligatoria** (5 decisiones × 3 formatos × matrícula/traspaso):

| Decisión | Casillas esperadas | Trampa |
|---|---|---|
| `solicitar` / `registrar` | tipo (1 ó 2) + **11** | la 12 debe quedar vacía |
| `levantar` | tipo (1 ó 2) + **12** | la 11 vacía; hoy no se marcaba ninguna |
| `omitir` / `sin_prenda` / sin prenda | solo tipo | ninguna de las dos |

- **Matrícula con prenda ⇒ 1 + 11**, no 2 + 11 (verificación explícita de D1).
- Prenda versionada: un trámite cuya decisión pasó de `registrar` a `levantar` sale con **12**, y el
  bloque de gravamen no arrastra al acreedor anterior.
- Maquinaria y remolques con prenda: hoy no marcan nada (H2). El antes/después va en la evidencia.

**Lo que NO debe cambiar:** sellos de firma / baúl · cualquier otro campo de las tres plantillas ·
`requested_process_1`/`_2` en trámites sin prenda · el **contenido** del bloque de observaciones (solo
cambia su maquetación, salvo el truncado del caso extremo) · el Expediente Consolidado y el resto de
documentos.

## 7. Notas para el code review

1. `FurTextFitter.Fit` (ruta `text`, HU #11048) **sin una sola línea modificada**.
2. El passthrough de `FitMultiline` usa **el mismo preprocesado** que la rama actual: si diverge, CF4
   se cae en silencio.
3. `AutoFit` es `false` por defecto y está declarado **solo** en `observations`.
4. **Ningún string de `PrendaDecision` aparece en `Flit.Infrastructure`.**
5. Las casillas nuevas heredan el `size` de sus hermanas, y no hay coordenadas a ojo en
   maquinaria/remolques: el commit trae la evidencia de medición.
6. La regeneración de la línea base es deliberada, explicada en el mensaje del commit, y **solo**
   cambian las huellas de los campos que el Feature toca. Cualquier otra línea distinta es arrastre
   accidental.
7. El `version` de los tres manifiestos sube: es la trazabilidad de la calibración.
8. Staging selectivo — rutas explícitas, nunca directorios.

## 8. Riesgos técnicos

| # | Riesgo | Mitigación |
|---|---|---|
| R1 | Maquinaria y remolques no marcan prenda hoy: al añadir la casilla, FUR que salían "limpios" pasan a mostrar gravamen | Declarado en el Feature como corrección de hueco preexistente; antes/después en la evidencia |
| R2 | *(cerrado)* Con `levantar` no se imprimía el acreedor | Resuelto por CF11 |
| R3 | Los sellos de firma también son `multiline` y hoy desbordan | `AutoFit` opt-in + test de no-regresión + comparación de bbox. **Riesgo principal del Feature** |
| R4 | Truncado silencioso de texto legal | `LogWarning` con el id del trámite y los caracteres elididos |
| R5 | El passthrough de CF4 depende de `gfx.MeasureString`: un texto en el límite podría envolverse en un entorno y no en otro | La fuente es embebida (`Documents/Fur/Fonts/*.ttf`), así que la métrica debería ser estable; verificar comparando el PDF de `obs-corta` generado en local y en CI |
| R6 | Regenerar el FUR invalida el consolidado maestro (HU #10926) | Sin backfill: aplica a FUR generados desde el despliegue. No lanzar regeneraciones masivas |
| R7 | Colisión de línea base entre UT-1 y UT-3 | Una sola rama con commits separados; regenerar **siempre** con `EmitBaseline`, nunca a mano |
| R8 | Desalineación vertical de la "X" si una casilla declara `size` distinto al de sus hermanas | Copiar el `size` del hermano; verificar por render |
| R9 | Subir 5 pt acerca las observaciones al borde superior; con varias líneas la última baja más que antes | La verificación comprueba contención **arriba y abajo** con el caso de máximo número de líneas |
| R10 | *(cerrado)* Maquinaria y remolques sin línea base congelada | Resuelto por CI1: la guardia se extiende a los tres formatos |

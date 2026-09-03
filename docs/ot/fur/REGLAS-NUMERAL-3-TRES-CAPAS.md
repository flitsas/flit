# FUR — Reglas del numeral 3 (tres capas)

**Estado:** vigente para desarrollo (dictamen ExpertDocEngine, 2026-08-21).  
**Dueño normativo:** `expert-doc-engine`.  
**Alcance:** cualquier cambio al overlay FUR, mapper, observaciones, simulador SuperAdmin o tipos de trámite que alimenten el formulario.

Este archivo es la **fuente canónica** de cómo se marcan las casillas del numeral 3 (*Trámite solicitado*) y cómo se llena el recuadro **OBSERVACIONES**. No improvisar casillas ni literales: o se cumple esta tabla, o se actualiza **este** artefacto en el mismo cambio (con dictamen de ExpertDocEngine).

Fuentes que este dictamen resume (si chocan, gana la Resolución; este archivo gana sobre el código hasta que el código se alinee):

- `docs/ot/resolutions/Resolución 20233040017145 de 2023 Ministerio de Transporte.pdf` (art. 5.1.8 y trámites RNA)
- Ejemplares en esta misma carpeta (`docs/ot/fur/*.pdf`)
- Catálogo `tramites.procedure_types`

---

## Cómo funciona (unión de capas)

```
casillas numeral 3 = (tabla 1: tipo base)
                   ∪ (tabla 2: prenda complementaria, si aplica)
                   ∪ (tabla 3: cada transformación activa)
```

- **No duplicar.** Si el tipo base ya es `PRENDA_INSCRIPCION`, no vuelvas a aplicar la tabla 2. Si ya es `CAMBIO_COLOR`, no vuelvas a aplicar color en la tabla 3.
- La casilla **1** es solo matrícula/registro. Un trámite de «otros» aislado **no** lleva 1.
- La casilla **2** es solo familia Traspaso.
- Varios trámites del mismo vehículo se acumulan (art. 5.1.8) **solo en las familias que acumulan**: ver el punto siguiente.

### La familia OTROS no acumula (ADR-0050)

Acumular es privilegio de `MATRICULAS` y `TRASPASO`. En la familia `OTROS` **las tablas 2 y 3 no se aplican**: el FUR lleva únicamente la capa del tipo base.

```
familia MATRICULAS | TRASPASO → tabla 1 ∪ tabla 2 ∪ tabla 3
familia OTROS                 → tabla 1   (y nada más)
```

Motivo de producto: en OTROS el cambio o el gravamen **es** el trámite, no un añadido. Un `CAMBIO_COLOR` con una prenda y un blindaje encima no es un cambio de color con extras — son tres trámites distintos, y el organismo devuelve el FUR que los mezcla.

Única excepción, y no es una excepción real: cuando el tipo base **es** de prenda (`PRENDA_INSCRIPCION`, `LEVANTAMIENTO_PRENDA`, `LEVANTAR_INSCRIBIR_PRENDA`, `CAMBIO_ACREEDOR`), su decisión de gravamen sí marca las casillas 11/12 — porque ahí la prenda no es la tabla 2, es la tabla 1. Por eso `CAMBIO_ACREEDOR`, cuya casilla base es la **18**, sigue marcando 11/12 desde su propia decisión.

Dónde vive la regla (no reimplementarla suelta):

| Pieza | Ruta |
|-------|------|
| Declaración por tipo | `tramites.procedure_types.gate_profile` → `allowsComplementaryTransformations`, `allowsComplementaryPrenda` (DDL `87-otros-sin-complementarios.sql`) |
| Resolución perfil → familia | `ProcedureTypeGateProfile.ComplementaryTransformationsAllowed` / `ComplementaryPrendaAllowed` |
| Qué capa es del tipo | `ProcedureTypeLayers.EsTipoPrendaBase` / `TransformacionDelTipo` |
| Casillas | `FurNumeral3Marks.Resolve` (recibe la familia en `modalidad`) |

**La llave ausente no es `false`**: significa «lo que diga la familia». Un perfil grabado antes del DDL 87 —o un snapshot ya congelado— resuelve igual, sin perder los simultáneos de un traspaso en curso.

### Recuadro OBSERVACIONES (párrafo 23) — concatenar, no reemplazar

Varios bloques automáticos **se unen con coma y espacio** (`", "`). Ninguno pisa al anterior.

Orden:

1. Bloque del tipo — `FurTramiteObservation`: trámite de locatario (`MATRICULA_LEASING` y `TRASPASO_UNILATERAL` de tabla 1; `CAMBIO_LOCATARIO` de tabla 4) y **causal de cancelación** (`CANCELACION_MATRICULA`, ver tabla 5)
2. Copropiedad (2+ actores en un lado) — `FurCopropiedadObservation`: `{NOMBRE} es el propietario del {n}%.` por cada actor del lado, en orden de ordinal. No se emite si el lado tiene un solo actor.
3. Gravamen (tabla 2 o tipo prenda) — `FurPrendaObservation`
4. Transformaciones (tabla 3 o tipo cambio) — `FurTransformationObservations`
5. Servicio + empresa vinculadora, **solo si hay razón social** — `FurServicioVinculadoraObservation`
6. Texto libre del gestor (`fur_observations`) — se recorta si no cabe (presupuesto 500 caracteres)

Bloques automáticos separados por `, `. Si faltan datos (nombre del locatario o del acreedor), **sí casilla / sí tipo, no se inventa el texto** de ese bloque.

---

## Numeral 20 — DATOS DE ALERTA

El numeral 20 se llena por dos vías independientes: el **gravamen** del expediente y el **tipo de trámite**. Ninguna sustituye al párrafo 23.

### Por gravamen

Se llena **junto** a las casillas 11/12 del numeral 3 cuando hay inscripción, registro o levantamiento de prenda.

| Acción de prenda | Marca en 20 | A FAVOR DE |
|------------------|-------------|------------|
| Inscribir / registrar / constituir | **LIM. PROPIEDAD** (columna 2) | Nombre del acreedor |
| Levantar | **OTRO** (columna 4) | Nombre del acreedor |
| Levantar e inscribir (mismo FUR) | LIM. PROPIEDAD **y** OTRO | Nombre del acreedor |
| Sin gravamen | Nada | Vacío |

HURTO (1) y EMBARGO (3) no se marcan por prenda. Sin nombre de acreedor: **sí X en la columna, A FAVOR DE vacío**. Overlay: `alert_data_code_2` / `_4` / `_5`.

### Por tipo de trámite

| Código | Marca en 20 | A FAVOR DE |
|--------|-------------|------------|
| `DUPLICADO_PLACA` | **OTRO** (columna 4) | Vacío |
| `DUPLICADO_TARJETA` | **OTRO** (columna 4) | Vacío |
| `TRASLADO_CUENTA` | **OTRO** (columna 4) | Vacío |
| `RADICADO_CUENTA` | **OTRO** (columna 4) | Vacío |

Estos tipos marcan OTRO **por sí mismos**, sin gravamen de por medio, y siguen marcando además su casilla del numeral 3 (15 y 10). No hay acreedor que escribir, así que A FAVOR DE queda vacía: es la misma convención que un gravamen sin nombre. HURTO (1) no se marca aunque el duplicado se origine en un hurto — el motivo del duplicado no se captura en el trámite y el formulario no debe afirmarlo.

Si un mismo FUR llegara por las dos vías, la X de OTRO es una sola casilla: se marca una vez.

---

## Tabla 1 — Trámite base (familia + tipo)

Define `tramites.procedure_types.family` + `code`. Sin prenda ni transformaciones extra.

| Familia | Código | Tipo | Debe marcar (numeral 3) | Observación |
|---------|--------|------|-------------------------|-------------|
| Matrículas | `MATRICULA_NUEVA` | Matrícula inicial | **1** Matrícula / Registro | No hay bloque automático. Solo `fur_observations` si el gestor escribe. |
| Matrículas | `MATRICULA_LEASING` | Matrícula Leasing | **1** | Obligatoria: `Matrícula con locatario por Leasing de {PROPIETARIO} a {NOMBRE_LOCATARIO} TIPO DE DOCUMENTO {TIPO_DOC_LOCATARIO}, NÚMERO DE DOCUMENTO {NUMERO_LOCATARIO}`. Propietario y locatario son partes distintas. |
| Matrículas | `CANCELACION_MATRICULA` | Cancelación de matrícula | **13** Cancelación matrícula / registro | Obligatoria: la **causal** declarada (tabla 5). Sin causal declarada: sí casilla, no texto. |
| Matrículas | `REMATRICULA` | Rematrícula *(inactivo)* | **16** Rematrícula | Sin bloque automático. |
| Traspaso | `TRASPASO_STANDARD` | Traspaso | **2** Traspaso | Sin bloque automático. |
| Traspaso | `TRASPASO_UNILATERAL` | Traspaso Unilateral | **2** | Obligatoria: `Traspaso unilateral por leasing a {NOMBRE_LOCATARIO}., tipo de documento {TIPO_DOC}, número de documento {NUMERO_DOC}.` Firma: el locatario (comprador) **no** firma (art. 5.3.2.2). |
| Traspaso | `TRASPASO_TRANSFERENCIA_DE_DOMINIO` | Traspaso con Transferencia de Dominio | **2** | Sin bloque automático. |
| Otros | `TRASLADO_CUENTA` | Traslado de cuenta | **3** + **18** Otros | Obligatoria: `Traslado de cuenta del Vehículo con placa {PLACA} para la nueva secretaria de {DESTINO}`. **Numeral 20:** OTRO, A FAVOR DE vacío. El trámite lo EXPIDE el organismo de origen —él valida el paz y salvo— así que el encabezado lleva el de origen y el destino solo se declara (`transit_office_destino_*`). Sin destino: sí casillas, no texto. |
| Otros | `RADICADO_CUENTA` | Radicado de cuenta | **4** + **18** Otros | Obligatoria: `Radicado de cuenta en {DESTINO}`. **Numeral 20:** OTRO, A FAVOR DE vacío. Trámite ESPEJO del traslado: la radicación se presenta en el organismo NUEVO, que es quien aprueba, así que el destino ES el organismo del trámite y el encabezado lleva el actual del vehículo (`transit_office_actual_*`). Sin destino: sí casillas, no texto. |
| Otros | `CAMBIO_COLOR` | Cambio de color | **5** Cambio de color | Obligatoria: `Color nuevo(NUEVO COLOR: {COLOR_NUEVO})`. No sumar color otra vez en tabla 3. |
| Otros | `REGRABAR_MOTOR_CHASIS` | Regrabar motor, chasis *(inactivo)* | **7** + **8** | Obligatoria si aplica: `Regrabación de motor: {MOTOR}.` / `Regrabación de chasis: {CHASIS}.` FLIT aún no genera este bloque. |
| Otros | `DUPLICADO_TARJETA` | Duplicado de tarjeta | **10** Duplicado licencia tránsito | Sin bloque automático. Marca además **OTRO** en el numeral 20 (A FAVOR DE vacío). |
| Otros | `PRENDA_INSCRIPCION` | Inscribir prenda | **11** Inscrip. prenda | Obligatoria: ver estructura tabla 2 (constituir). No sumar tabla 2 otra vez. |
| Otros | `LEVANTAMIENTO_PRENDA` | Levantar prenda | **12** Levanta prenda | Obligatoria: ver estructura tabla 2 (levantar). No sumar tabla 2 otra vez. |
| Otros | `LEVANTAR_INSCRIBIR_PRENDA` | Levantar e inscribir *(inactivo)* | **11** + **12** | Unir ambos literales de la tabla 2 con un espacio. |
| Otros | `DUPLICADO_PLACA` | Duplicado de placa | **15** Duplicado de placas | Sin bloque automático. Marca además **OTRO** en el numeral 20 (A FAVOR DE vacío). |
| Otros | `CAMBIO_CARROCERIA` | Cambio de carrocería | **17** Cambio de carrocería | Obligatoria: `Carroceria nueva(NUEVA CARROCERIA: {CARROCERIA_NUEVA})`. No sumar tabla 3. |
| Otros | `CONVERSION_COMBUSTIBLE` | Conversiones de combustible | **18** Otros | Obligatoria: `COMBUSTIBLE_NUEVO: {COMBUSTIBLE_NUEVO}`. El blank no tiene casilla de combustible. |
| Otros | `BLINDAJE` | Blindaje | Ninguna en numeral 3. Marcar **SI** con nivel 1/2/3, **NO** con desmonte | Obligatoria: `BLINDAJE NIVEL 1.` / `BLINDAJE NIVEL 2.` / `BLINDAJE NIVEL 3.` / `DESMONTE DE BLINDAJE.` El trámite tiene cuatro opciones y la casilla es un SI/NO: sin este texto el formulario no distingue un nivel 1 de un nivel 3, y un desmonte sale idéntico a un vehículo que nunca estuvo blindado. Sin opción declarada NO se inventa el texto: sí casilla, no texto. El certificado de blindaje (`certificado_blindaje`) es obligatorio en las cuatro opciones. |
| Otros | `CAMBIO_LOCATARIO` | Cambio de locatario | **18** Otros | Obligatoria: `CAMBIO DE LOCATARIO por Leasing de {PROPIETARIO} a {NOMBRE_LOCATARIO}, TIPO DE DOCUMENTO {TIPO_DOC_LOCATARIO}, NÚMERO DE DOCUMENTO {NUMERO_LOCATARIO}.` «Leasing de» es texto fijo, igual que en `MATRICULA_LEASING`; el propietario va solo con su nombre. Propietario y locatario son partes distintas y hacen falta las dos. Si el expediente es transferencia de dominio, usar `TRASPASO_TRANSFERENCIA_DE_DOMINIO` (casilla **2**). |
| Otros | `CAMBIO_ACREEDOR` | Cambio acreedor *(inactivo)* | **18** Otros | Recomendada: `CAMBIO DE ACREEDOR PRENDARIO: {NOMBRE} - NIT {DOC}.` FLIT aún no genera este bloque. |

Rótulos 6 (cambio de servicio) y 14 (cambio de placas) **no tienen tipo** en el catálogo. No marcarlos hasta que exista código + dictamen.

---

## Tabla 2 — Prenda complementaria

Se **suma** al tipo de la tabla 1 cuando el expediente trae gravamen (wizard / simulador). No aplica si el tipo base ya es una prenda, **ni en la familia OTROS** (ver «La familia OTROS no acumula»).

| Tipo de acción | Debe marcar (numeral 3) | Observación — estructura |
|----------------|-------------------------|--------------------------|
| Ninguna / no aplica | No suma | No imprime bloque de gravamen. |
| Inscribir / registrar / constituir | **+11** Inscrip. prenda | Si hay nombre y documento: `Inscripción de prenda a favor de {NOMBRE_ACREEDOR} identificado con número de documento {DOCUMENTO}`. Si hay nombre sin documento: solo el nombre (sin sufijo vacío). Sin nombre: **sí casilla, no texto**. **Numeral 20:** LIM. PROPIEDAD + A FAVOR DE. |
| Levantar | **+12** Levanta prenda | Si hay nombre y documento: `Levantamiento de prenda a favor de {NOMBRE_ACREEDOR} identificado con número de documento {DOCUMENTO}`. Si hay nombre sin documento: solo el nombre. Sin nombre: **sí casilla, no texto**. **Numeral 20:** OTRO + A FAVOR DE. **Excepción — trámite `LEVANTAMIENTO_PRENDA`:** ahí se captura la entidad ante la que se levantó y el literal pasa a `Levantamiento de prenda ante {ENTIDAD}`, porque el acreedor ya lo nombra el numeral 20 y repetirlo gasta renglones. En traspaso y matrícula la entidad no se captura y el literal es el de esta fila. |
| Levantar e inscribir (mismo FUR) | **+11 +12** | Las dos frases, unidas con `, `. El simulador admite `ambas`; el wizard operativo puede no capturar las dos a la vez. |

Constantes de código: `FurPrendaObservation.Etiqueta`, `EtiquetaLevantamiento` y `SufijoDocumento`. El nombre y el documento del acreedor se imprimen tal cual (trim); no se inventa contenido.

---

## Tabla 3 — Transformaciones complementarias

Se **suma** al tipo de la tabla 1 cuando el gestor activa transformaciones o hay diff RUNT vs efectivo. No duplicar si el tipo base ya es ese cambio, y **no aplica en la familia OTROS** (ver «La familia OTROS no acumula»): allí `CAMBIO_COLOR`, `CAMBIO_CARROCERIA`, `CONVERSION_COMBUSTIBLE` y `BLINDAJE` traen su atributo desde la tabla 1 y ningún otro se les puede añadir.

El recuadro de características del vehículo conserva el dato **RUNT original**; observaciones declaran solo el valor **nuevo** (mayúsculas).

| Tipo de transformación | Debe marcar (numeral 3) | Observación — estructura |
|------------------------|-------------------------|--------------------------|
| Ninguna / no aplica | No suma | No imprime bloque. |
| Cambio de color | **+5** | `Color nuevo(NUEVO COLOR: {COLOR_NUEVO})` |
| Cambio de carrocería | **+17** | `Carroceria nueva(NUEVA CARROCERIA: {CARROCERIA_NUEVA})` |
| Conversión de combustible | **+18** Otros | `COMBUSTIBLE_NUEVO: {COMBUSTIBLE_NUEVO}` |
| Blindaje | No suma numeral 3. Marcar **SI** con nivel 1/2/3, **NO** con desmonte | `BLINDAJE NIVEL {1\|2\|3}.` o `DESMONTE DE BLINDAJE.` (ver tabla 1). |
| Varias a la vez | Unión de 5 y/o 17 y/o 18 (y SI/NO blindado) | Orden fijo: color, carrocería, combustible, blindaje. Ejemplo: `Color nuevo(NUEVO COLOR: MULTICOLOR CON AEROGRAFIAS) Carroceria nueva(NUEVA CARROCERIA: PICKUP) COMBUSTIBLE_NUEVO: DIESEL` |

---

## Tabla 5 — Causal de cancelación de matrícula

La casilla **13** es una sola para cuatro trámites que el organismo tramita distinto. La causal la declara el gestor en el paso de requerimientos (`field_values.cancelacion_causal`) y de ella cuelgan tanto el texto del párrafo 23 como los documentos obligatorios del checklist.

| Causal (`cancelacion_causal`) | Observación — literal | Documentos obligatorios |
|-------------------------------|-----------------------|-------------------------|
| `DECISION_JUDICIAL` | `CANCELACIÓN POR DECISIÓN JUDICIAL.` | Acto de decisión judicial (`oficio_judicial`) |
| `PERDIDA_TOTAL_FUERZA_MAYOR` | `CANCELACIÓN POR PÉRDIDA TOTAL - FUERZA MAYOR.` | `certificado_dijin` + `certificado_aseguradora_perito` + `certificado_autoridad_administrativa` |
| `PERDIDA_TOTAL_ACCIDENTE` | `CANCELACIÓN POR PÉRDIDA TOTAL - ACCIDENTE.` | `certificado_dijin` + `certificado_aseguradora_perito` + `certificado_autoridad_administrativa` |
| `DECISION_VOLUNTARIA` | `CANCELACIÓN POR DECISIÓN VOLUNTARIA.` | `certificado_dijin` |

Los documentos de la causal son obligatorios **todos**, no uno cualquiera de la lista. `cert_tradicion` sigue siendo obligatorio de base en las cuatro. Sin causal declarada: **sí casilla 13, no texto** y ningún documento de causal exigido — el paso no deja continuar hasta que el gestor elija.

Dónde vive la regla (no reimplementarla suelta):

| Pieza | Ruta |
|-------|------|
| Causales y sus documentos | `Flit.Tramites.Domain/Tramites/ValueObjects/CancelacionCausal.cs` |
| Obligatoriedad en el checklist | `ConditionalDocumentRules.Cancelacion` (vía `TramiteDocumentContext.CancelacionCausal`) |
| Literal del párrafo 23 | `FurTramiteObservation.ComposeCancelacion` |
| Catálogo de documentos | DDL `92-cancelacion-causales.sql` |
| Espejo del asistente | `frontend/lib/catalogs/cancelacion.ts` |

---

## Ejemplo cerrado

**Traspaso** + inscribir prenda + color y carrocería → casillas **2 + 11 + 5 + 17**.

```
Inscripción de prenda a favor de FONDEICON identificado con número de documento 890900608, Color nuevo(NUEVO COLOR: NEGRO), Carroceria nueva(NUEVA CARROCERIA: PICKUP)
```

---

## Código que debe respetar este artefacto

| Pieza | Ruta |
|-------|------|
| Casillas | `services/core-api/src/Flit.Infrastructure/Documents/Fur/FurFieldMapper.cs` (`MarkTramite`) |
| Manifest | `services/core-api/src/Flit.Infrastructure/Documents/Fur/fur-field-manifest.json` |
| Observaciones | `FurTramiteObservation`, `FurPrendaObservation`, `FurTransformationObservations`, `FurServicioVinculadoraObservation`, `FurObservacionesComposer` |
| Ensamblado real | `FurCommand.AssembleData` |
| Simulador | `FurPreviewSample` + `frontend/` admin plataforma FUR |
| Copropietarios (titulares y firmas, no casillas) | `FurFieldMapper` / `FurOverlayRenderer` / `FurCopropiedadObservation` — HU #12048; ver `docs/design/MULTIPLE-PROPIETARIO-documentos-autogenerados.md` |

Al implementar, contrastar **objetivo (estas tablas)** vs **lo que el mapper emite hoy**. Las brechas conocidas (p. ej. marcar 1 en trámites que no son matrícula; no emitir 3, 4, 10, 13, 15, 16) no se perpetúan: el desarrollo nuevo debe cerrarlas o dejar el gap explícito en el PR citando este archivo.

---

## Cómo se gobierna

1. **Leer este archivo** antes de tocar FUR (regla Cursor `fur-numeral-3-reglas` + pre-flight de `expert-doc-engine`).
2. Si el producto cambia una casilla o un literal, **actualizar este markdown en el mismo PR**.
3. Tests unitarios de mapper / observaciones deben afirmar los literales de las tablas 2 y 3, no copias sueltas distintas.

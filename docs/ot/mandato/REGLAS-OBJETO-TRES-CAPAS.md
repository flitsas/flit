# Mandato — Reglas del objeto del contrato (tres capas)

**Estado:** vigente para desarrollo (dictamen ExpertDocEngine, 2026-08-21).  
**Dueño normativo:** `expert-doc-engine`.  
**Alcance:** cualquier cambio al generador de mandato, compositor de `{{tramite}}`, simulador SuperAdmin o tipos de trámite que alimenten el Contrato Privado de Mandato.

Este archivo es la **fuente canónica** del *copy* que se inserta en el objeto del contrato. No improvisar literales: o se cumple esta tabla, o se actualiza **este** artefacto en el mismo cambio (con dictamen de ExpertDocEngine).

Es **genérica a las 4 redacciones** (`generico`, `municipio`, `sabaneta`, `bello`): ninguna plantilla nombra prenda ni transformaciones; todas reciben el mismo valor compuesto en `{{tramite}}` (o el hueco equivalente del PDF propio del OT). Lo que cambia entre formatos es el envoltorio (comparecencia, indemnidad SETSA/MAB, facultades largas), no este copy.

Fuentes que este dictamen resume (si chocan, gana la Resolución en requisitos del tercero; **este archivo gana sobre el código** hasta que el código se alinee):

- `docs/ot/resolutions/Resolución 20233040017145 de 2023 Ministerio de Transporte.pdf` (art. **5.1.6** — trámite por tercero: RUNT + contrato de mandato o poder)
- Ejemplares en esta misma carpeta (`docs/ot/mandato/*.pdf`)
- Catálogo `tramites.procedure_types`
- Paralelo FUR: `docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md` (mismas tres capas; el FUR marca casillas + observaciones, el mandato **solo concatena copy**)

---

## Qué NO va en este copy

El objeto **no** lleva locatario, acreedor, color/carrocería/combustible *nuevos*, NIT ni ciudad. Eso es del FUR (párrafo 23) o de otras cláusulas del contrato.

Evidencia: `Fur_MatriculaLeasing+Transformacion.pdf` → objeto `MATRÍCULA INICIAL CON CAMBIO DE COLOR` (no dice leasing).  
`Fur_TraspasoUnilateral_PJxPJ.pdf` → objeto `TRASPASO` (no dice unilateral).

Placa, organismo, fecha y firmas **no** se concatenan aquí: cada plantilla los pone en su propio hueco.

---

## Cómo funciona (concatenación de capas)

```
fragmentos = [copy tabla 1]
            + [copy tabla 2, si aplica]
            + [cada copy tabla 3 activa]

{{tramite}} = Componer(fragmentos)
```

### No duplicar

- Si el tipo base **ya es** prenda (`PRENDA_INSCRIPCION`, `LEVANTAMIENTO_PRENDA`, `LEVANTAR_INSCRIBIR_PRENDA`), **no** vuelvas a aplicar la tabla 2.
- Si el tipo base **ya es** esa transformación (`CAMBIO_COLOR`, `CAMBIO_CARROCERIA`, `CONVERSION_COMBUSTIBLE`, `BLINDAJE`), **no** vuelvas a aplicar esa fila en la tabla 3.

### Componer — conector canónico

Mayúsculas invariantes. No se usa el `name` del catálogo en minúsculas.

| N.º de fragmentos | Fórmula | Ejemplo |
|-------------------|---------|---------|
| 1 | el fragmento | `MATRÍCULA INICIAL` |
| 2 | `{a} CON {b}` | `TRASPASO CON LEVANTAMIENTO DE PRENDA` |
| 3 o más | `{a} CON {b} Y {c} Y …` | `TRASPASO CON LEVANTAMIENTO DE PRENDA Y CAMBIO DE COLOR` |

Orden fijo de los complementos (después del trámite base):

1. Prenda tabla 2 (si **ambas**: primero levantamiento, luego inscripción — mismo orden que el FUR)
2. Transformaciones tabla 3: color → carrocería → combustible → blindaje

**Plantilla SETSA (ejemplar):** a veces aparece un guion (`TRASPASO - CON CAMBIO DE COLOR` en `Fur_Traspaso+Transformacion_PJxPN.pdf`). Eso es **envoltorio de Sabaneta**, no parte del copy canónico. El valor de `{{tramite}}` sigue siendo `TRASPASO CON CAMBIO DE COLOR`.

### Dos sintaxis de prenda (no mezclar)

Los ejemplares distinguen **trámite = la prenda** vs **prenda complementaria**:

| Rol | Literal |
|-----|---------|
| Tipo base inscripción | `INSCRIBIR PRENDA` — `Fur_OtroTramite_InscrPrenda PN.pdf` |
| Tipo base levantamiento | `LEVANTAR PRENDA` — `Fur_OtroTramite_LevantPrenda.pdf` |
| Complemento inscripción | `INSCRIPCIÓN DE PRENDA` — `Fur_TraspasoPrenda_PJxPN.pdf` (`TRASPASO CON …`) |
| Complemento levantamiento | `LEVANTAMIENTO DE PRENDA` — `Fur_Traspaso+Transformacion+Prenda_PNxPJ.pdf` |

---

## Tabla 1 — Trámite base (familia + tipo)

Define `tramites.procedure_types.family` + `code`. Sin prenda ni transformaciones extra. Este fragmento **siempre** es el primero.

| Familia | Código | Tipo | Copy (fragmento 1) | Evidencia / nota |
|---------|--------|------|--------------------|------------------|
| Matrículas | `MATRICULA_NUEVA` | Matrícula inicial | `MATRÍCULA INICIAL` | `Fur_Matricula_PN Normal.pdf`, `Fur_Matricula_PJ_Firma Baul.pdf`, `Fur_Matricula_PJ_Firma VID.pdf`, `Fur_Matricula_MultiplesP (Ambos PN).pdf` |
| Matrículas | `MATRICULA_LEASING` | Matrícula Leasing | `MATRÍCULA INICIAL` | `Fur_MatriculaLeasing+Transformacion.pdf` — el mandato **no** dice leasing |
| Matrículas | `CANCELACION_MATRICULA` | Cancelación de matrícula | `CANCELACION DE MATRICULA` | `Fur_OtroTramite_CancelacionMatricula.pdf` (SETSA; sin tilde en CANCELACION) |
| Matrículas | `REMATRICULA` | Rematrícula *(inactivo)* | `REMATRÍCULA` | Sin ejemplar de mandato. Literal por catálogo; no inventar más texto |
| Traspaso | `TRASPASO_STANDARD` | Traspaso | `TRASPASO` | `Fur_TraspasoUnilateral_PJxPJ.pdf`, `Fur_Traspaso_PJbaulxPJvid.pdf`, `Fur_Traspaso_PJvid xPNvid.pdf` — **no** usar `TRASPASO DE PROPIEDAD` |
| Traspaso | `TRASPASO_UNILATERAL` | Traspaso Unilateral | `TRASPASO` | `Fur_TraspasoUnilateral_PJxPJ.pdf` — el mandato **no** dice unilateral |
| Traspaso | `TRASPASO_TRANSFERENCIA_DE_DOMINIO` | Traspaso con Transferencia de Dominio | `TRASPASO` | Sin ejemplar propio; mismo copy que traspaso (el objeto no detalla la causal) |
| Otros | `TRASLADO_CUENTA` | Traslado de cuenta | `TRASLADO DE CUENTA` | Sin ejemplar de mandato |
| Otros | `RADICADO_CUENTA` | Radicado de cuenta | `RADICADO DE CUENTA` | Sin ejemplar de mandato |
| Otros | `CAMBIO_COLOR` | Cambio de color | `CAMBIO DE COLOR` | `Fur_OtroTramite_CambioColor_PJ.pdf` — no sumar color en tabla 3 |
| Otros | `REGRABAR_MOTOR_CHASIS` | Regrabar motor, chasis *(inactivo)* | `REGRABACIÓN DE MOTOR Y CHASIS` | Sin ejemplar de mandato |
| Otros | `DUPLICADO_TARJETA` | Duplicado de tarjeta | `DUPLICADO DE TARJETA` | `Fur_OtroTramite_DuplicadoTarjeta PJ.pdf` |
| Otros | `PRENDA_INSCRIPCION` | Inscribir prenda | `INSCRIBIR PRENDA` | `Fur_OtroTramite_InscrPrenda PN.pdf` — no sumar tabla 2 |
| Otros | `LEVANTAMIENTO_PRENDA` | Levantar prenda | `LEVANTAR PRENDA` | `Fur_OtroTramite_LevantPrenda.pdf` — no sumar tabla 2 |
| Otros | `LEVANTAR_INSCRIBIR_PRENDA` | Levantar e inscribir *(inactivo)* | `LEVANTAMIENTO DE PRENDA Y INSCRIPCIÓN DE PRENDA` | Sin ejemplar de mandato. Un solo fragmento (no tabla 2) |
| Otros | `DUPLICADO_PLACA` | Duplicado de placa | `DUPLICADO DE PLACA` | `Fur_OtroTramite_DuplicadoPlaca.pdf` |
| Otros | `CAMBIO_CARROCERIA` | Cambio de carrocería | `CAMBIO DE CARROCERÍA` | Sin ejemplar de mandato. Alineado a catálogo/FUR; no sumar tabla 3 |
| Otros | `CONVERSION_COMBUSTIBLE` | Conversiones de combustible | `CONVERSIONES DE COMBUSTIBLE` | `Fur_OtroTramite_CambioCombus.pdf` — no usar `CAMBIO DE COMBUSTIBLE`; no sumar tabla 3 |
| Otros | `BLINDAJE` | Blindaje | `BLINDAJE` | `Fur_OtroTramite_Blindaje.pdf` — no sumar blindaje en tabla 3 |
| Otros | `CAMBIO_LOCATARIO` | Cambio de locatario | `CAMBIO DE LOCATARIO` | Sin ejemplar de mandato |
| Otros | `CAMBIO_ACREEDOR` | Cambio acreedor *(inactivo)* | `CAMBIO DE ACREEDOR PRENDARIO` | Sin ejemplar de mandato |

Si llega un `code` publicado que no está en la tabla: usar `procedure_types.name` en **mayúsculas invariantes** (no inventar un sinónimo). Documentar el code aquí en el mismo PR.

---

## Tabla 2 — Prenda complementaria

Se **suma** al tipo de la tabla 1 cuando el expediente trae gravamen (wizard / simulador) y el tipo base **no** es ya una prenda.

| Tipo de acción | Copy (fragmento) | Evidencia |
|----------------|------------------|-----------|
| Ninguna / no aplica | *(vacío — no suma)* | — |
| Inscribir / registrar / constituir | `INSCRIPCIÓN DE PRENDA` | `Fur_TraspasoPrenda_PJxPN.pdf` → `TRASPASO CON INSCRIPCIÓN DE PRENDA` |
| Levantar | `LEVANTAMIENTO DE PRENDA` | `Fur_Traspaso+Transformacion+Prenda_PNxPJ.pdf` → `TRASPASO CON LEVANTAMIENTO DE PRENDA`; también `Fur_Traspaso_PNvidxPNvid.pdf` (SETSA) |
| Levantar e inscribir (mismo mandato) | `LEVANTAMIENTO DE PRENDA` + `INSCRIPCIÓN DE PRENDA` (dos fragmentos, en ese orden) | Sin ejemplar con ambas. Concatenar con la fórmula Componer. El simulador admite `ambas`. |

No se nombra al acreedor ni el NIT en el objeto.

---

## Tabla 3 — Transformaciones complementarias

Se **suma** al tipo de la tabla 1 cuando el gestor activa transformaciones. No duplicar si el tipo base ya es ese cambio.

| Tipo de transformación | Copy (fragmento) | Evidencia |
|------------------------|------------------|-----------|
| Ninguna / no aplica | *(vacío)* | — |
| Cambio de color | `CAMBIO DE COLOR` | `Fur_MatriculaLeasing+Transformacion.pdf` → `MATRÍCULA INICIAL CON CAMBIO DE COLOR`; `Fur_Traspaso+Transformacion_PJxPN.pdf` → `TRASPASO CON CAMBIO DE COLOR` |
| Cambio de carrocería | `CAMBIO DE CARROCERÍA` | Sin ejemplar de mandato. Mismo vocablo que el catálogo/FUR |
| Conversión de combustible | `CONVERSIONES DE COMBUSTIBLE` | Como trámite base: `Fur_OtroTramite_CambioCombus.pdf`. Complemento: mismo literal (no `CAMBIO DE COMBUSTIBLE`) |
| Blindaje | `BLINDAJE` | Como trámite base: `Fur_OtroTramite_Blindaje.pdf`. Complemento: mismo literal |
| Varias a la vez | Un fragmento por cada activa, orden color → carrocería → combustible → blindaje | `Fur_Matricula_PJ_Con Transformaciones.pdf` usa el atajo `MATRÍCULA INICIAL CON TRANSFORMACIÓN(ES)` **sin listar**. Para generación: **listar** las activas (el simulador y el wizard sí las conocen). No usar el atajo `TRANSFORMACIÓN(ES)` si se puede nombrar cada una |

---

## Ejemplos cerrados

| Escenario | Capas | `{{tramite}}` |
|-----------|-------|----------------|
| Matrícula PN | T1 | `MATRÍCULA INICIAL` |
| Traspaso PJ | T1 | `TRASPASO` |
| Matrícula leasing + color | T1 + T3 color | `MATRÍCULA INICIAL CON CAMBIO DE COLOR` |
| Traspaso + inscribir prenda | T1 + T2 inscripción | `TRASPASO CON INSCRIPCIÓN DE PRENDA` |
| Traspaso + levantar prenda | T1 + T2 levantamiento | `TRASPASO CON LEVANTAMIENTO DE PRENDA` |
| Traspaso + levantar prenda + color | T1 + T2 + T3 | `TRASPASO CON LEVANTAMIENTO DE PRENDA Y CAMBIO DE COLOR` |
| Traspaso + ambas prendas + color y carrocería | T1 + T2×2 + T3×2 | `TRASPASO CON LEVANTAMIENTO DE PRENDA Y INSCRIPCIÓN DE PRENDA Y CAMBIO DE COLOR Y CAMBIO DE CARROCERÍA` |
| Solo inscribir prenda | T1 (no T2) | `INSCRIBIR PRENDA` |
| Solo cambio de color | T1 (no T3 color) | `CAMBIO DE COLOR` |
| Solo blindaje | T1 | `BLINDAJE` |
| Cancelación (SETSA) | T1 | `CANCELACION DE MATRICULA` |
| Duplicado de placa (MAB) | T1 | `DUPLICADO DE PLACA` |

---

## Código que debe respetar este artefacto

| Pieza | Ruta |
|-------|------|
| Composición de `{{tramite}}` | `MandatoObjetoComposer` |
| Identidad del tipo (code/familia) | `MandatoTramiteIdentity` |
| Generador (las 4 plantillas) | `MandatoPdfGenerator` |
| Muestra / simulador | `MandatoPreviewSample`, `MandateSimulatorService` |
| Simulador UI | `MandatoSimuladorPanel` |

Al implementar, contrastar **objetivo (estas tablas)** vs **lo que el compositor emite hoy**. Brechas conocidas a **cerrar** (no perpetuar):

| Hoy (código) | Debe (este dictamen) |
|--------------|----------------------|
| `TRASPASO DE PROPIEDAD` o `name` del catálogo (`TRASPASO`) según rama | `TRASPASO` (tabla 1) |
| Complemento inscripción: `PRENDA` | `INSCRIPCIÓN DE PRENDA` |
| Tipo base inscripción: no distinguía infinitivo | `INSCRIBIR PRENDA` |
| Combustible complemento: `CAMBIO DE COMBUSTIBLE` | `CONVERSIONES DE COMBUSTIBLE` |
| Unión con comas y «Y» (`A, B Y C`) | `A CON B Y C` (sin coma) |

---

## Cómo se gobierna

1. **Leer este archivo** antes de tocar el objeto del mandato (pre-flight de `expert-doc-engine`).
2. Si el producto cambia un literal, **actualizar este markdown en el mismo PR**.
3. Tests del compositor deben afirmar los literales de las tablas 1–3, no copias sueltas distintas.
4. Las 4 plantillas no duplican estas reglas: solo interpolan `{{tramite}}`.

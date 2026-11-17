# Ficha del catálogo de reglas del dictamen — versiones 1 y 2

> **Vigente: versión 2 (2026-11-11).** Los dictámenes nuevos se emiten con `catalogo_version = 2`. Los ya emitidos conservan la versión 1 y no se reevalúan; un override de un dictamen v1 vuelve a las plantillas de v1. La v1 sigue sembrada intacta en `diagnostico.reglas_dictamen` (textos, efectos y `vigente`); la v2 se siembra con la migración `P2_CatalogoDictamenV2`. Código: `CatalogoReglasDictamen` (`Filas` = v2, `FilasV1`, `FilasDe(version)`, `Historico`).

## Versión 2 — multas bloqueantes (punto 2 del plan `plan-ajustes-diagnostico-2026-11-11.md`)

Rama `feature/ajustes-diagnostico-2026-11-11`. Decisiones 2.1–2.6 del plan y hallazgos V1–V3 y V8. 17 reglas por tipo de trámite (51 filas): las 14 de v1 más `SIMIT_SIN_VERIFICAR`, `SIMIT_LICENCIA` y `SIMIT_ACUERDO_PAGO`.

### Qué cambia respecto a v1

| Código | Dispara cuando | `TRASPASO_STANDARD` y `TRASPASO_TRANSFERENCIA_DE_DOMINIO` | `TRASPASO_UNILATERAL` |
| --- | --- | --- | --- |
| `SIMIT_VEHICULO` | SIMIT del vehículo: comparendos > 0 o sin paz y salvo. Ya no mira licencia ni acuerdos | `BLOQUEA` (política FLIT: la norma pide el paz y salvo de las personas, no del vehículo) | `ADVIERTE` |
| `SIMIT_PROPIETARIO` | SIMIT del propietario, solo sin leasing: comparendos > 0 o sin paz y salvo | `BLOQUEA` | `ADVIERTE` |
| `SIMIT_LOCATARIO` | SIMIT del locatario (leasing, o cuando hay documento de locatario): comparendos > 0 o sin paz y salvo | `BLOQUEA` | `ADVIERTE` |
| `SIMIT_SIN_VERIFICAR` | Falta una consulta SIMIT exigida: la del vehículo; la del propietario sin leasing, o la del locatario en leasing (error, no aplica, sin resultado, leasing sin documento del locatario o sin lectura del locatario). Un solo motivo aunque falten varias; la fuente es la primera que falta (vehículo, propietario, locatario) | `BLOQUEA` | `ADVIERTE` |
| `SIMIT_LICENCIA` | Algún SIMIT leído (vehículo, propietario o locatario) reporta licencia suspendida o cancelada. Una regla por fuente | `ADVIERTE` | `ADVIERTE` |
| `SIMIT_ACUERDO_PAGO` | Algún SIMIT leído tiene un acuerdo con saldo pendiente distinto de cero. Una regla por fuente. Si además dice «sin paz y salvo», bloquea por la regla de multas | `ADVIERTE` | `ADVIERTE` |
| `FUENTE_PENDIENTE` | Solo RUNT del vehículo, RUNT del propietario e impuesto en error. El SIMIT faltante ya no la dispara: lo cubre `SIMIT_SIN_VERIFICAR` | `ADVIERTE` | `ADVIERTE` |

**Bug RTM/SOAT por clase (ola 2026-11-11, hallazgos V4/V5, dictamen de expert-doc-engine; entra en la misma v2, sin v3):**

| Código | Condición v2 | `TRASPASO_STANDARD` / `TRASPASO_TRANSFERENCIA_DE_DOMINIO` | `TRASPASO_UNILATERAL` |
| --- | --- | --- | --- |
| `SOAT_NO_VIGENTE` | SOAT no vigente en carro, moto, motocarro o clase desconocida. El remolque/semirremolque está exento y no dispara; la maquinaria dispara `SOAT_MAQUINARIA` | `BLOQUEA` | `BLOQUEA` |
| `SOAT_MAQUINARIA` (nueva) | SOAT no vigente en maquinaria: NA con advertencia hasta confirmar si se le exige | `ADVIERTE` | `ADVIERTE` |
| `RTM_NO_VIGENTE` | RTM no vigente desde la primera revisión: 2 años de matrícula para moto, motocarro (mototriciclo, tricimoto, cuatrimoto, cuadriciclo) y carro de servicio público; 5 para el resto de carros y para la clase desconocida. Maquinaria y remolque: no aplica. Sin fecha de matrícula se exige | `BLOQUEA` | `ADVIERTE` (art. 5.3.2.2: el unilateral no valida RTM) |

Clase y servicio salen del detalle RUNT (`clase_vehiculo`, `clasificacion`, `tipo_servicio`), con el RUNT normalizado y el tipo de placa (maquinaria `AA999999`, remolque `R/S` + dígitos) como respaldo (`ClasificadorVehiculoDictamen`). Plantillas: `SOAT_MAQUINARIA` = «La maquinaria no registra un SOAT vigente en el RUNT. Mientras se confirma si se le exige, es una advertencia y el traspaso sigue viable.»; `RTM_NO_VIGENTE` = «La revisión técnico-mecánica no está vigente y el vehículo ya cumplió el plazo de su primera revisión (2 años de matrícula para motos, motocarros y servicio público; 5 años para particulares), así que el traspaso queda bloqueado.» (en el unilateral termina en «. En el traspaso unilateral es una advertencia y el trámite sigue viable.»). Migración `P7a_DictamenV2RtmSoatPorClase`.

Sin cambio: `ESTADO_VEHICULO`, `LIMITACION`, `GRAVAMEN`, `GARANTIA_MOBILIARIA`, `PRENDA`, `INFRACCION_RUNT`, `LICENCIA_CONDUCTOR` (sigue advirtiendo, nunca bloquea), `IMPUESTO`. En leasing no se evalúa el SIMIT del propietario, ni para multas ni para «sin verificar».

El traspaso unilateral advierte en las reglas de multas por la Res. 20233040017145, art. 5.3.2.2 (no exige paz y salvo). Es una decisión derivada del dictamen normativo y queda pendiente de confirmación del usuario (hallazgo V2).

### Plantillas v2

Las reglas que bloquean no dicen «Es una advertencia». Las que no cambian conservan el texto de v1 (sección 6).

| Código | `TRASPASO_STANDARD` / `TRASPASO_TRANSFERENCIA_DE_DOMINIO` | `TRASPASO_UNILATERAL` |
| --- | --- | --- |
| `SIMIT_VEHICULO` | El vehículo tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. Por política de FLIT el traspaso queda bloqueado hasta que se pongan al día. | El vehículo tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. En el traspaso unilateral es una advertencia y el trámite sigue viable. |
| `SIMIT_PROPIETARIO` | El propietario tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. El traspaso queda bloqueado hasta que se ponga al día. | El propietario tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. En el traspaso unilateral es una advertencia y el trámite sigue viable. |
| `SIMIT_LOCATARIO` | El locatario tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. El traspaso queda bloqueado hasta que se ponga al día. | El locatario tiene multas pendientes en el SIMIT: comparendos por pagar o no está a paz y salvo. En el traspaso unilateral es una advertencia y el trámite sigue viable. |
| `SIMIT_SIN_VERIFICAR` | No se pudo verificar en el SIMIT si hay multas pendientes: la consulta falló, no respondió o falta el documento del locatario. El traspaso queda bloqueado hasta completar la consulta. | No se pudo verificar en el SIMIT si hay multas pendientes: la consulta falló, no respondió o falta el documento del locatario. En el traspaso unilateral es una advertencia y el trámite sigue viable. |
| `SIMIT_LICENCIA` | El SIMIT reporta una licencia de conducción suspendida o cancelada. Es una advertencia y no cambia el veredicto. | (igual) |
| `SIMIT_ACUERDO_PAGO` | Hay un acuerdo de pago con saldo pendiente en el SIMIT. Es una advertencia y no cambia el veredicto. | (igual) |

Orden de las reglas en el dictamen: `SOAT_NO_VIGENTE`, `SOAT_MAQUINARIA`, `RTM_NO_VIGENTE`, `ESTADO_VEHICULO`, `SIMIT_VEHICULO`, `SIMIT_PROPIETARIO`, `SIMIT_LOCATARIO`, `SIMIT_SIN_VERIFICAR`, `SIMIT_LICENCIA`, `SIMIT_ACUERDO_PAGO`, `LIMITACION`, `GRAVAMEN`, `GARANTIA_MOBILIARIA`, `PRENDA`, `INFRACCION_RUNT`, `LICENCIA_CONDUCTOR`, `IMPUESTO`, `FUENTE_PENDIENTE`.

---

## Versión 1 (2026-11-03) — histórica

Lo que sigue es la ficha original de v1, sin cambios. Rige para los dictámenes emitidos con `catalogo_version = 1`.

Documento para completar a mano. Con las respuestas se arma la siembra de `diagnostico.reglas_dictamen`. La tabla `diagnostico.dictamenes` no se siembra: cada fila nace cuando un vehículo de un lote Con Análisis IA termina, y el motor escribe el veredicto a partir de estas reglas.

Fecha: 2026-11-03. Rama: `feature/AB-12881-diagnostico-d0-d2`. Épica relacionada: #12678. Sin work items nuevos.

Decisiones confirmadas e implementadas en `evaluador-reglas-dictamen-v1.md`. Acuerdo al día significa saldo pendiente en cero. `PRENDA`, `IMPUESTO` y `FUENTE_PENDIENTE` quedan cerradas en la versión 1, con efecto `ADVIERTE`.

Completé las celdas que quedaron vacías (condición, plantilla, estados, veredicto de la fuente pendiente). Donde la ficha decía «por definir», la celda lleva la marca `(propuesta)`.

## Cómo llenarla

- En cada casilla de **Decisión**, escribe `sí`, `no` o el efecto `BLOQUEA` / `ADVIERTE`.
- La columna **Propuesta** es una referencia. Cámbiala si no aplica.
- La condición va en una frase. No hace falta JSON.
- Una regla con efecto `BLOQUEA` deja el dictamen en `BLOQUEADO`. Si solo hay `ADVIERTE`, o ninguna regla dispara, el veredicto es `VIABLE`.
- El redactor explica ese veredicto. No lo cambia.
- Al modelo solo viajan el código, la fuente, el efecto, el monto en pesos si existe, y del vehículo la placa, clase, marca, línea, modelo y servicio.

## 1. Alcance del catálogo

| Pregunta | Propuesta | Decisión |
| --- | --- | --- |
| Versión del catálogo | 1 | si |
| Misma regla para los tres tipos de la familia TRASPASO | Sí. Se repite una fila por código de trámite | si |
| La prenda queda fuera de esta versión | Sí | no |

Códigos de trámite que ya usa el producto:

- `TRASPASO_STANDARD`
- `TRASPASO_UNILATERAL`
- `TRASPASO_TRANSFERENCIA_DE_DOMINIO`

Si un tipo debe tener reglas distintas, indícalo aquí:

```text
Los vehículos con menos de 5 años de matrícula o de registro en el RUNT no validan RTM.
```

Esa excepción aplica a los tres tipos. El resto de las reglas es el mismo.

## 2. Reglas

Marca si entra en la versión 1 y con qué efecto. Si no entra, deja la condición y la plantilla en blanco.

| Código | Qué mira | Propuesta | Entra en v1 (sí/no) | Efecto (`BLOQUEA` / `ADVIERTE`) | Condición en una frase |
| --- | --- | --- | --- | --- | --- |
| `SOAT_NO_VIGENTE` | Estado del SOAT en el RUNT | `BLOQUEA` si no está vigente | si | BLOQUEA | Dispara si el SOAT está vencido, no vigente, sin registro o en estado desconocido. No dispara si está vigente o si no requiere. |
| `RTM_NO_VIGENTE` | Estado de la revisión técnico-mecánica | `BLOQUEA` si no está vigente | si | ADVIERTE o BLOQUEA según la fecha de matrícula o de registro del vehículo | Con menos de 5 años de matrícula no se evalúa. Con 5 años o más, o sin fecha de matrícula, bloquea si la RTM está vencida, no vigente, sin registro o en estado desconocido. No dispara si está vigente o si no requiere. |
| `ESTADO_VEHICULO` | Estado del vehículo en el RUNT | `BLOQUEA`. Los estados conocidos son REGISTRADO, ACTIVO e INACTIVO | si | ADVIERTE | Dispara si el estado es INACTIVO o distinto de REGISTRADO y ACTIVO. REGISTRADO y ACTIVO no disparan. Ningún estado bloquea. |
| `SIMIT_VEHICULO` | Paz y salvo, comparendos o suspensión del vehículo | `ADVIERTE`, sin umbral de pesos | si | ADVIERTE | Dispara si hay comparendos, no está en paz y salvo, figura suspendido o cancelado, o tiene un acuerdo de pago que no está al día. Un acuerdo al día no dispara. |
| `SIMIT_PROPIETARIO` | Lo mismo sobre el propietario | `ADVIERTE` | si | ADVIERTE | Dispara si el propietario tiene comparendos, no está en paz y salvo, tiene la licencia suspendida o cancelada, o un acuerdo de pago que no está al día. En leasing no se evalúa: la deuda que se mira es la del locatario. |
| `SIMIT_LOCATARIO` | Lo mismo sobre el locatario, si hay documento. En leasing el propietario suele ser la financiera | ADVIERTE | si | ADVIERTE | Dispara, cuando hay documento de locatario, si tiene comparendos, no está en paz y salvo, tiene la licencia suspendida o cancelada, o un acuerdo de pago que no está al día. En leasing esta es la deuda que se mira. Si es leasing y no hay documento, advierte que no se pudo consultar. |
| `LIMITACION` | Embargo u otra limitación a la propiedad | ADVIERTE | si | ADVIERTE | Dispara si el RUNT trae al menos una limitación a la propiedad. |
| `GRAVAMEN` | Gravamen registrado | ADVIERTE | si | ADVIERTE | Dispara si el RUNT marca que el vehículo tiene gravámenes. |
| `GARANTIA_MOBILIARIA` | Garantía mobiliaria | ADVIERTE | si | ADVIERTE | Dispara si el RUNT trae al menos una garantía mobiliaria. |
| `PRENDA` | Prenda registrada en el RUNT | ADVIERTE (propuesta). Entra en la versión 1 porque la prenda no queda fuera | si (propuesta) | ADVIERTE (propuesta) | Dispara si el RUNT marca que el vehículo tiene prenda. |
| `INFRACCION_RUNT` | Infracciones que trae el RUNT del vehículo | ADVIERTE | si | ADVIERTE | Dispara si el RUNT trae al menos una infracción del vehículo, aunque SIMIT también advierta por la misma deuda. |
| `LICENCIA_CONDUCTOR` | Licencias, suspensiones y certificados del propietario o del locatario | ADVIERTE | si | ADVIERTE | Dispara si la licencia está suspendida, vencida o cancelada, o si no hay certificado de aptitud vigente. Se mira la del propietario y, en leasing, la del locatario. |
| `IMPUESTO` | Impuesto departamental. El monto sí está en pesos enteros | ADVIERTE si el monto es mayor que cero (propuesta) | si (propuesta) | ADVIERTE (propuesta) | Dispara si la celda trae un monto mayor que cero. No dispara si está en no aplica o el monto es cero. |
| `FUENTE_PENDIENTE` | RUNT y SIMIT del vehículo y del propietario cuando la celda no llegó | ADVIERTE y el dictamen se emite con lo que haya (propuesta) | si (propuesta) | ADVIERTE (propuesta) | Dispara si alguna de las cuatro fuentes obligatorias está en error, no aplica o sin resultado. Advierte la novedad y no bloquea por sí sola. |

Estados del vehículo que bloquean (`ESTADO_VEHICULO`):

```text
Ninguno. REGISTRADO y ACTIVO no disparan. INACTIVO, y cualquier estado distinto de esos dos, advierte.
```

## 3. SOAT y RTM

Para cada caso, escribe `bloquea`, `no dispara` o el efecto que corresponda.

| Caso | Propuesta SOAT | Decisión SOAT | Propuesta RTM | Decisión RTM |
| --- | --- | --- | --- | --- |
| Vigente | No dispara | No dispara | No dispara | No dispara |
| Vencido | Bloquea | Bloquea | Bloquea | Bloquea |
| No vigente | Bloquea | Bloquea | Bloquea | Bloquea |
| No requiere | No dispara | No dispara | No dispara | No dispara |
| Sin registro | Bloquea | Bloquea | Bloquea | Bloquea |
| Estado desconocido | Bloquea | Bloquea | Bloquea | Bloquea |

La RTM solo aplica esas decisiones cuando el vehículo tiene 5 años o más de matrícula, o cuando el RUNT no trae la fecha. Con menos de 5 años la RTM no se evalúa.

## 4. SIMIT sin monto en pesos

El total de SIMIT llega hoy como texto. Un umbral en pesos queda para cuando exista el total tipado.

| Hecho | ¿Regla propia o se une a otra? | Efecto | Decisión |
| --- | --- | --- | --- |
| Hay comparendos | Propuesta: entra en `SIMIT_*` | `ADVIERTE` | `ADVIERTE` |
| No está en paz y salvo | Propuesta: entra en `SIMIT_*` | `ADVIERTE` | `ADVIERTE` |
| Licencia suspendida o cancelada en SIMIT | Propuesta: entra en `SIMIT_*` | `ADVIERTE` | `ADVIERTE` |
| Hay acuerdo de pago | Entra en `SIMIT_*`. No es una regla aparte | ADVIERTE y no bloquea | ADVIERTE Y NO BLOQUEA |
| Acuerdo de pago al día | Entra en `SIMIT_*` y no dispara | No advierte y no bloquea | NO ADVIERTE Y NO BLOQUEA |

A quién se le mira la deuda en un leasing:

```text
Al locatario. SIMIT_PROPIETARIO no se evalúa en leasing. Si no hay documento de locatario, SIMIT_LOCATARIO advierte que la deuda no se pudo consultar.
```

## 5. Fuente obligatoria incompleta

Fuentes obligatorias de TRASPASO: RUNT del vehículo, SIMIT del vehículo, RUNT del propietario y SIMIT del propietario.

| Pregunta | Propuesta | Decisión |
| --- | --- | --- |
| Esas cuatro fuentes son las obligatorias | Sí | si |
| Si una celda obligatoria está en error o no aplica | El dictamen se emite con lo que haya y advierte la celda pendiente | se emite con la información que hay y se notifican las novedades o celdas pendientes |

Si la decisión es emitir igual, indica el veredicto y el código de la regla:

```text
Código FUENTE_PENDIENTE, efecto ADVIERTE.
El veredicto lo deciden las demás reglas con la información disponible.
Esta regla no deja el dictamen en BLOQUEADO por sí sola.
```

## 6. Texto de plantilla

Una oración por regla que entra en la versión 1. Es el texto que ve el cliente cuando la redacción automática está pendiente.

| Código | Plantilla |
| --- | --- |
| `SOAT_NO_VIGENTE` | El SOAT no está vigente, así que el traspaso queda bloqueado. |
| `RTM_NO_VIGENTE` | La revisión técnico-mecánica no está vigente y el vehículo tiene 5 años o más de matrícula, así que el traspaso queda bloqueado. |
| `ESTADO_VEHICULO` | El vehículo no figura activo en el RUNT. Es una advertencia y el traspaso sigue viable. |
| `SIMIT_VEHICULO` | El vehículo tiene comparendos, no está en paz y salvo, figura suspendido o cancelado, o tiene un acuerdo de pago que no está al día. Es una advertencia. |
| `SIMIT_PROPIETARIO` | El propietario tiene comparendos, no está en paz y salvo, tiene la licencia suspendida o cancelada, o un acuerdo de pago que no está al día. Es una advertencia. |
| `SIMIT_LOCATARIO` | El locatario tiene comparendos, no está en paz y salvo, tiene la licencia suspendida o cancelada, o un acuerdo de pago que no está al día. Es una advertencia. En leasing, si no hay documento, no se pudo consultar su deuda. |
| `LIMITACION` | El vehículo tiene una limitación a la propiedad. Es una advertencia. |
| `GRAVAMEN` | El vehículo tiene un gravamen registrado. Es una advertencia. |
| `GARANTIA_MOBILIARIA` | El vehículo tiene una garantía mobiliaria registrada. Es una advertencia. |
| `PRENDA` | El vehículo tiene una prenda registrada. Es una advertencia. |
| `INFRACCION_RUNT` | El RUNT del vehículo registra infracciones. Es una advertencia, además de lo que informe el SIMIT. |
| `LICENCIA_CONDUCTOR` | La licencia de conducción está suspendida, vencida o cancelada, o no hay certificado de aptitud vigente. Es una advertencia. |
| `IMPUESTO` | El vehículo tiene impuesto departamental pendiente. Es una advertencia. |
| `FUENTE_PENDIENTE` | Falta el resultado de una consulta obligatoria. El dictamen se emite con la información disponible y esta novedad queda advertida. |

## 7. Notas

```text
Propuestas agregadas al completar la ficha:
- PRENDA entra en la versión 1 y advierte, igual que el gravamen y la garantía mobiliaria.
- IMPUESTO entra y advierte solo cuando el monto es mayor que cero.
- FUENTE_PENDIENTE advierte la celda obligatoria que no llegó y no bloquea por sí sola.
- La RTM no se evalúa con menos de 5 años de matrícula. Sin fecha, sí se evalúa.
- En leasing la deuda de SIMIT se mira en el locatario.
```

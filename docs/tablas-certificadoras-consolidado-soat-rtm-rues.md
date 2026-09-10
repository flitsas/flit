# Tablas certificadoras del expediente consolidado — SOAT, RTM y RUES

> Generado: 2026-08-07 · Rama `develop` @ `0d1277b4` · Revisión de solo lectura (no se modificó código)

Qué valores espera cada celda de las tablas certificadoras que entran al Expediente Consolidado, de
dónde sale cada uno y qué pasa cuando no llega.

Alcance: los dos PDF generados por FLIT que contienen tablas certificadoras.

| Documento | Tipo de adjunto | Generador | Ensamblador de datos |
|---|---|---|---|
| Certificado de vigencia SOAT y RTM (+ Avalúo) | `certificado_soat_rtm` | `SoatRtmCertificatePdfGenerator.cs` | `FurCommand.cs:345-382` |
| Certificado RUES | `certificado_rues` (+ sufijo de rol) | `RuesCertificatePdfGenerator.cs` | `FurCommand.cs:903-969` |

Ambos se generan dentro del handler del FUR y se fusionan después en el consolidado
(`TraspasoConsolidadoOrdering.cs` / `MatriculaConsolidadoOrdering.cs`).

**Regla transversal de negocio (HU #10856 / #10589):** valor ausente ⇒ **celda en blanco**. Nunca un
guion, nunca "N/A", nunca un marcador. Está implementada como `Disp()` en los dos generadores
(`SoatRtmCertificatePdfGenerator.cs:175`, `RuesCertificatePdfGenerator.cs:181`) y aguas arriba, en los
mappers, como "si el valor viene vacío no se escribe la llave".

---

## 1. Certificado de vigencia SOAT y RTM

### 1.1 Encabezado

El texto introductorio se arma en `SoatRtmCertificatePdfGenerator.cs:73-82`:

> "En la consulta realizada al RUNT 2.0 **el día {fecha}** el vehículo de placas **{placa}** se encontró
> la siguiente información del estado de **{SOAT | SOAT y REVISIÓN TECNOMECÁNICA}**."

| Dato | `field_key` esperado | Quién lo escribe | Si falta |
|---|---|---|---|
| Placa | `plate` | Los tres mappers de vehículo | Se imprime la frase sin placa |
| Fecha de consulta | `runt_consulta_fecha` | **Solo** `RunConsultationCommand.cs:239` | Desaparece el fragmento "el día ..." |

`runt_consulta_fecha` se persiste como `dd/MM/yyyy` en hora de Colombia (UTC-5) y el generador la
reformatea a `AAAA/MM/DD` con `FlitDocumentDate.Normalize`. En un HIT de caché se declara la fecha de
la consulta **origen**, no la del reúso — decisión correcta: el certificado debe decir cuándo se
consultó el RUNT de verdad.

### 1.2 Tabla SOAT — 6 celdas

Se pinta **siempre** que se emita el documento. Orden de pintado (2 filas × 3 chips):

| # | Etiqueta en el PDF | `field_key` | Formato esperado |
|---|---|---|---|
| 1 | N° Póliza | `soat_poliza` | Texto libre |
| 2 | Fecha expedición | `soat_expedicion` | Fecha (se normaliza a `AAAA/MM/DD`) |
| 3 | Fecha vigencia | `soat_vigencia` | Fecha |
| 4 | Fecha de vencimiento | `soat_vencimiento` | Fecha |
| 5 | Estado | `soat_estado` | **Vocabulario cerrado**: `vigente` / `vencido` / `unknown` |
| 6 | Entidad expide SOAT | `soat_aseguradora` | Texto libre |

**La celda "Estado" es especial.** `soat_estado` es la única llave de estas tablas que además alimenta
un gate: `SoatGate` decide si el Organismo de Tránsito puede aprobar el trámite, y el frontend compara
de forma **estricta** contra `"vigente"` en minúscula (`lib/tramites/estados.ts`). Por eso todo
productor debe pasar por `SoatGate.Normalize` antes de persistir. Al pintar, `EstadoSoatDisplay`
(`FurCommand.cs:1329`) la pasa a mayúscula y **convierte `unknown` en celda vacía** — el certificado no
afirma un estado que el RUNT no dijo.

### 1.3 Tabla RTM — 6 celdas

| # | Etiqueta en el PDF | `field_key` | Formato esperado |
|---|---|---|---|
| 1 | N° RTM | `rtm_numero` | Texto libre |
| 2 | Fecha expedición | `rtm_expedicion` | Fecha |
| 3 | Fecha vigencia | `rtm_vigencia` | Fecha |
| 4 | Fecha de vencimiento | `rtm_vencimiento` | Fecha |
| 5 | Estado | `rtm_estado` | Texto; los mappers producen `VIGENTE` / `NO VIGENTE` / `NO APLICA` |
| 6 | Entidad expide RTM | `rtm_entidad` | Texto libre (nombre del CDA) |

A diferencia del SOAT, `rtm_estado` **no alimenta ningún gate**, así que se persiste tal cual lo
reporta el proveedor (Kyverum lo traduce con `MapVigencia`, Verifik lo escribe crudo).

**Cuándo se pinta esta tabla** (`RtmCertificado.Aplica`, HU #11136): solo si el trámite **no es
matrícula inicial** *y* el vehículo tiene **más de 5 años** desde `vehicle_registration_date`. Sin esa
fecha, la tabla **se muestra** — fallo seguro deliberado: omitir una RTM exigible deja el expediente
incompleto ante el OT, incluir una de más solo añade información.

### 1.4 Bloque Avalúo — solo traspaso

Tabla de 2 columnas (`Tipo de avalúo` / `Valor avalúo`) con una fila por fuente. No sale de
`field_values` sino del handler de avalúo (Feature #10707). Etiquetas en `FurCommand.cs:1054`:
`fasecolda` → "AVALÚO FASECOLDA", `mercado_libre` → "AVALÚO COMERCIAL", `base_gravable` → "AVALÚO BASE
GRAVABLE". El valor se formatea como `$ 1.234.567` con cultura invariante (el contenedor corre en
globalization-invariant mode).

### 1.5 Condición para que el documento exista

`FurCommand.cs:351` — se emite si hay `soat_vencimiento`, **o** `rtm_vencimiento`, **o** `avaluo is not
null`.

---

## 2. Quién llena cada celda: matriz proveedor → celda

Esta es la parte que decide si una celda sale llena o en blanco. La cadena por defecto para
`vehicle_plate` y `vehicle_vin` es `["kyverum_runt", "verifik"]` (`appsettings.json:66-67`), es decir
**Kyverum es el proveedor primario** y Verifik solo entra si aquel falla. Intempo está implementado
pero **fuera de la cadena por defecto**.

Leyenda: ● lo entrega · ○ no lo entrega

| Celda | `field_key` | kyverum_runt (primario) | verifik (fallback) | intempo (fuera de cadena) | OCR del PDF |
|---|---|:--:|:--:|:--:|:--:|
| N° Póliza | `soat_poliza` | ○ | ● `noPoliza` | ● `noPoliza` | ● `numero_poliza` |
| Fecha expedición SOAT | `soat_expedicion` | ○ | ● `fechaExpedicion` | ● | ● `fecha_expedicion` |
| Fecha vigencia SOAT | `soat_vigencia` | ○ | ● `fechaVigencia` | ● | ● `fecha_inicio` |
| Fecha vencimiento SOAT | `soat_vencimiento` | ● `fechaVencimSoat` | ● | ● | ● `fecha_vencimiento` |
| Estado SOAT | `soat_estado` | ● `estado` | ● | ● | ● `estado_poliza` |
| Entidad SOAT | `soat_aseguradora` | ● `razonSocialAsegur` | ● `entidadExpideSoat` | ● | ● `aseguradora` |
| N° RTM | `rtm_numero` | ○ | ◐ tolerante | ○ | ● `numero_certificado` |
| Fecha expedición RTM | `rtm_expedicion` | ○ | ◐ tolerante | ○ | ● `fecha_expedicion` |
| Fecha vigencia RTM | `rtm_vigencia` | ○ | ◐ tolerante | ○ | ● `fecha_vigencia` |
| Fecha vencimiento RTM | `rtm_vencimiento` | ● `fechaVencimientoRvt` | ● `fechaVencimiento` | ○ | ● `fecha_vencimiento` |
| Estado RTM | `rtm_estado` | ● `vigente` (traducido) | ● `estado` | ○ | ● `estado` |
| Entidad RTM | `rtm_entidad` | ○ | ● `cdaExpide` | ○ | ● `cda_expide` |
| *(regla de aplicabilidad RTM)* | `vehicle_registration_date` | **○** | ● `fechaMatricula` | ● `fechaMatricula` | ○ |

**◐ tolerante** = `VerifikResultMapper.cs:248-250`. Ninguna muestra real documenta esos tres campos, así
que se buscan por nombres candidatos (`noCertificado`, `numeroCertificado`, `nroCertificado`,
`noRevision`, `numeroRevision`, y equivalentes de fecha) dentro de lo que el proveedor mandó y el
modelo no declara (`JsonExtensionData`). Sin coincidencia, no se escribe la llave. Es una lista de
apuestas pendiente de cerrar con una sonda real.

**Precedencia entre fuentes** (`OcrFieldsCommand.cs:125-133`): el OCR **solo puede pisar lo que él
mismo escribió**. Un valor de consulta o digitado por el usuario manda sobre lo que diga un PDF; si el
RUNT llega después, sí sobrescribe al OCR. La comprobación es por lista blanca de origen
(`Source == "ocr"`), no por lista negra — cualquier fuente futura queda protegida por defecto.

---

## 3. Certificado RUES

Tres secciones (`RuesCertificatePdfGenerator.cs:54-66`): **REGISTRO COMERCIAL** (grilla de chips),
**REPRESENTACIÓN LEGAL** (bloque de texto justificado) y **ACTIVIDADES ECONÓMICAS** (tabla).

### 3.1 REGISTRO COMERCIAL — 20 celdas, en orden de pintado

| # | Etiqueta en el PDF | `field_key` | Campo en la respuesta Verifik RUES v3 |
|---|---|---|---|
| 1 | NIT | `rues_nit` | `data.commercialRegistry.NIT` (o el NIT del contexto, que tiene prioridad) |
| 2 | Acrónimo | `rues_sigla` | `acronym` |
| 3 | Nombre Negocio | `rues_razon_social` | `businessName` |
| 4 | Cámara Comercio | `rues_camara_comercio` | `chamberCommerce` |
| 5 | Dirección Comercial | `rues_direccion` | `commercialAddress` |
| 6 | Ubicación | `rues_municipio` | `companyLocation` |
| 7 | Tipo Compañía | `rues_tipo_compania` | `companyType` |
| 8 | Email | `rues_email` | `email` |
| 9 | Categoría Inscripción | `rues_categoria` | **derivado** de `data.category` (`RM` → "Registro Mercantil") |
| 10 | Fecha Inscripción | `rues_fecha_matricula` | `enrollmentDate` |
| 11 | Id | `rues_id_rm` | `idRm` |
| 12 | Año Renovado | `rues_ultimo_ano_renovado` | `lastRenewedYear` |
| 13 | Fecha Renovación | `rues_fecha_renovacion` | `renewalDate` |
| 14 | Fecha Actualización | `rues_fecha_actualizacion` | `lastUpdatedDate` |
| 15 | Tipo Organización | `rues_tipo_organizacion` | `organizationType` |
| 16 | Razón Cancelación | `rues_razon_cancelacion` | `reasonForCancellation` |
| 17 | Número Registro | `rues_matricula_mercantil` | `registrationNumber` |
| 18 | Estado Registro | `rues_estado` | `registrationStatus` |
| 19 | Ciudad Cámara | `rues_camara_ciudad` | `chamberCity` |
| 20 | Departamento Cámara | `rues_camara_departamento` | `chamberDepartment` |

Las celdas 10, 13 y 14 pasan por `FlitDocumentDate.Normalize`; las demás se imprimen tal cual.

Dos valores por defecto que **nunca dejan la celda vacía** (`VerifikRuesConsultationProvider.cs:163-164`):
si falta `businessName` se persiste literalmente `"Sin razón social"`, y si falta `registrationStatus`
se persiste `"DESCONOCIDO"`. Son los dos únicos campos del certificado que violan la regla de "ausente
⇒ en blanco", a propósito: `rues_razon_social` es la condición de emisión y `rues_estado` decide el
color del check.

### 3.2 REPRESENTACIÓN LEGAL

Un solo bloque de texto: `rues_representacion_legal` ← `commercialRegistry.legalRepresentatives.faculty`,
o sea **las facultades** del representante. La lista estructurada de representantes (nombre, tipo y
número de documento, rol) llega en la misma respuesta y **no se modela ni se pinta**
(`VerifikRuesConsultationProvider.cs:435-445`): pintarla es una decisión de layout pendiente del PO.

### 3.3 ACTIVIDADES ECONÓMICAS

Tabla `Código | Nombre | Descripción`, alimentada por `rues_actividades_json` — un JSON compacto con
`{codigo, nombre, descripcion}` por ítem, deserializado en `FurCommand.cs:1020`. JSON ausente o
ilegible ⇒ la tabla se pinta con cabecera y **sin filas** (no se rompe la generación).

### 3.4 De dónde salen los datos: orden de resolución

Por cada actor con tipo de documento jurídico (`FurCommand.cs:914-927`), en este orden:

1. **Snapshot congelado** — `rues_snapshots_json`, indexado por NIT (ADR-0037 / HU #11133). Es la
   fuente de verdad y no cuesta una llamada al proveedor. Queda inmutable en cuanto el trámite sale de
   edición, porque un trigger de BD bloquea la escritura de `field_values`.
2. **Llaves `rues_*` de instancia** — pero **solo si `rues_nit` coincide con el NIT de ese actor**. Son
   un juego único por trámite: en un traspaso PJ → PJ la segunda consulta pisa a la primera, y usarlas
   a ciegas mezclaba la razón social de una compañía con la matrícula de la otra.
3. **Consulta en vivo** — `RuesActorDataResolver`, proveedor `verifik_rues`. **Deliberadamente no lee
   la caché de reúso** (un certificado no da fe de un payload cacheado), aunque sí la escribe. Se
   registra en log para poder medir cuántos trámites siguen dependiendo de ella.

### 3.5 Condición para que el certificado exista

`FurCommand.cs:930` — **sin `rues_razon_social` no se emite**. Antes se emitía siempre que hubiera un
actor con NIT, aunque saliera con 19 casillas en blanco; un certificado en blanco no certifica nada.

Se emite **uno por actor jurídico**: el comprador conserva el tipo `certificado_rues` y los demás roles
llevan sufijo (`certificado_rues_vendedor`). En cada regeneración se retiran los certificados cuyo tipo
ya no aplica, para que el consolidado no arrastre el de un actor que dejó de ser jurídico.

---

## 4. Consideraciones

Ordenadas por impacto sobre lo que ve el OT en el expediente.

### 4.1 Con el proveedor primario, 6 de las 12 celdas de SOAT/RTM dependen del OCR

Kyverum es el primario de la cadena y su contrato es corto **a propósito** — las fixtures reales
(`tests/Flit.Tramites.Application.Tests/Consultations/Fixtures/KyverumRunt/*.json`) traen del SOAT
exactamente tres campos y de la RTM otros tres. El modelo lo declara así en vez de inventar nombres,
decisión correcta y documentada (`KyverumRuntVehicleResponse.cs:167-211`).

La consecuencia práctica es que **en la ruta normal** (Kyverum responde bien, sin fallback) estas seis
celdas solo se llenan si el usuario cargó el PDF y el OCR funcionó:

- SOAT: N° Póliza, Fecha expedición, Fecha vigencia
- RTM: N° RTM, Fecha expedición, Fecha vigencia, Entidad expide RTM (siete, contando esta)

Verifik las cubriría, pero **solo se invoca cuando Kyverum falla**. Es decir: paradójicamente, el
certificado sale *más completo* cuando el proveedor primario se cae.

### 4.2 La regla de antigüedad de la RTM está inerte con el proveedor primario

`RtmCertificado.Aplica` necesita `vehicle_registration_date`, y **Kyverum no entrega fecha de
matrícula** (lo dice el propio modelo, `KyverumRuntVehicleResponse.cs:192-194`). Solo la escriben
`VerifikResultMapper.cs:188` e `IntempoVehicleResultMapper.cs:156`.

Resultado: por la ruta normal la fecha nunca llega, `Aplica` cae en su rama de fallo seguro y **la
tabla de RTM se pinta en todo trámite que no sea matrícula inicial**, sin importar la antigüedad. El
comportamiento es el mismo que antes de la HU #11136. La regla no está mal escrita — le falta el
insumo.

### 4.3 El encabezado atribuye al RUNT datos que pueden venir del OCR

El texto dice literalmente *"En la consulta realizada al RUNT 2.0 el día X se encontró la siguiente
información"*. Pero por 4.1, varias de esas celdas pueden provenir del OCR de un PDF cargado a mano.
El documento no distingue el origen. Sumado a que `runt_consulta_fecha` solo la escribe la consulta
explícita, un certificado cuyos datos vengan íntegramente del OCR afirmaría una consulta al RUNT que
puede no haber ocurrido.

Es un asunto de veracidad documental, no de código roto: conviene decidir con el PO si el encabezado
debe matizarse o si la procedencia debe declararse por celda.

### 4.4 En traspaso el certificado SOAT/RTM se emite siempre, aunque esté vacío

La condición de emisión incluye `avaluo is not null`, y `BuildAvaluoAsync` **nunca devuelve null en
traspaso**: sin handler o sin fuentes devuelve `new AvaluoInfo([])`. Así que en traspaso la condición
es siempre verdadera y el documento se genera aunque no haya ni un solo dato de SOAT ni de RTM — con
las doce celdas en blanco y la tabla de avalúo con cabecera y cero filas.

Choca con el criterio que sí se aplicó al RUES ("un certificado en blanco no certifica nada",
`FurCommand.cs:930`). Los dos documentos deberían usar el mismo criterio.

### 4.5 `certificado_soat_rtm` no está en la prelación por defecto del consolidado

Ni `TraspasoConsolidadoOrdering.cs:13-43` ni `MatriculaConsolidadoOrdering.cs:11-38` incluyen
`certificado_soat_rtm` en su lista `Precedence`. Los tipos no listados caen al final
(`rank = Precedence.Length + 1`, desempatado por `UploadedAt`).

`certificado_rues` sí está listado, pero **`certificado_rues_vendedor` no** — con lo que en un traspaso
PJ → PJ el certificado del comprador queda en su sitio y el del vendedor se va al final, separado del
primero.

Solo se corrige si el OT configuró explícitamente un orden: en ese caso manda su lista, traducida por
`ConsolidadoDocumentCodeMap` (que es justamente donde se resolvió que el código de catálogo
`certificado_vigencia_soat_rtm` y el tipo de adjunto `certificado_soat_rtm` no coinciden). Sin
configuración de OT, el orden por defecto los deja fuera de lugar.

### 4.6 "Razón Cancelación" se imprime en empresas activas

La celda 16 muestra `reasonForCancellation` tal cual. En las tres capturas reales disponibles
(Bancolombia, Renting Colombia y Autopremier, todas **ACTIVAS**) ese campo llega con el valor
`"SOCIEDAD COMERCIAL"`, que no es una razón de cancelación sino el tipo de sociedad. Con 3 de 3 no es
anécdota: el proveedor está usando ese campo para otra cosa, y el certificado lo publica bajo una
etiqueta que induce a error.

### 4.7 "Ubicación" y "Cámara Comercio" traen el mismo valor

`companyLocation` y `chamberCommerce` llegan idénticos en las capturas reales (`MEDELLIN PARA
ANTIOQUIA`). La celda "Ubicación" no está mostrando el municipio de la empresa sino la jurisdicción de
la cámara. Las celdas 19 y 20 (Ciudad/Departamento Cámara, añadidas por la HU #11132) sí traen el dato
limpio y desagregado, así que la 6 queda como ruido redundante.

### 4.8 Dirección y correo salen siempre en blanco

`commercialAddress` y `email` llegan vacíos en las tres capturas reales, mientras el mock los muestra
llenos (`VerifikRuesConsultationProvider.cs:116`). Es el mismo patrón que provocó el defecto original
de la HU #11132: el mock enseña un certificado que el servicio real no produce. Conviene alinear el
mock con lo que de verdad devuelve el proveedor, para que el hueco se vea en DEV.

### 4.9 La fecha del snapshot RUES existe pero no se imprime

`RuesSnapshots.QueriedAt` está implementada y probada, y **no tiene ningún llamador en producción** —
solo tests. El certificado RUES dice "En la consulta realizada al RUES sobre el NIT X..." sin declarar
cuándo, aunque el dato esté guardado. El certificado SOAT/RTM sí declara su fecha. La asimetría importa
porque el RUES viene de un snapshot congelado al registrar, que puede tener meses.

### 4.10 `rues_actividad_economica` se produce y no se pinta

El mapper deriva la actividad principal y la persiste, y `RuesCertificateData` la transporta como
`ActividadEconomica`, pero **no hay ninguna celda en la grilla que la use**. La sección ACTIVIDADES
ECONÓMICAS ya cubre esa información en forma de tabla. Es un campo muerto en el contrato del
certificado: o se pinta o se retira.

### 4.11 La lista de representantes legales se descarta en cada consulta

Solo se consume `faculty`. En cada consulta al RUES llega también la lista estructurada de
representantes (nombre, tipo y número de documento, rol) y se descarta. Está declarado como decisión
consciente pendiente del PO, no como olvido — pero implica que **el dato se paga y se tira** en cada
llamada.

---

## 5. Cómo verificar cualquiera de estas afirmaciones

**Contrato de llaves.** Existe un test guardián que fija qué llave escribe quién y quién la lee:
`services/core-api/tests/Flit.Tramites.Application.Tests/UseCases/ProcedureInstances/FieldValueContractGuardTests.cs`.
Es el sitio correcto para comprobar que una llave nueva no se queda sin productor o sin consumidor.

**Valores reales de un trámite** (base local):

```sql
select field_key, value_text, source, updated_at
from tramites.procedure_instance_field_values
where procedure_instance_id = '<id>'
  and (field_key like 'soat_%' or field_key like 'rtm_%' or field_key like 'rues_%'
       or field_key in ('plate','runt_consulta_fecha','vehicle_registration_date'))
order by field_key;
```

La columna `source` distingue el origen: `consultation` (proveedor) vs `ocr` (PDF cargado). Es la forma
directa de confirmar el punto 4.1 en un trámite concreto.

**Snapshot RUES congelado:**

```sql
select value_text
from tramites.procedure_instance_field_values
where procedure_instance_id = '<id>' and field_key = 'rues_snapshots_json';
```

**Suites relevantes:**

```bash
cd services/core-api
dotnet test tests/Flit.Tramites.Application.Tests/Flit.Tramites.Application.Tests.csproj \
  --filter "FullyQualifiedName~RuesSnapshots|FullyQualifiedName~FurHandler|FullyQualifiedName~FieldValueContractGuard"
```

---

## 6. Documentos relacionados

- `docs/plan-tecnico-tablas-certificadoras.md` — plan de la Feature #11131 (7 HUs, #11132-11138)
- `docs/informe-fur-datos-incompletos-soat-rtm-rues-prendas.md` — Feature #10972
- `docs/consulta-rues-nits-procesamiento.md` — capturas reales del RUES y su procesamiento
- `docs/consulta-runt-nzs920-procesamiento.md` — capturas reales del RUNT (3 placas)
- ADR-0037 — snapshot congelado del RUES

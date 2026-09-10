# Reporte de status — novedades reportadas 2026-08-19

> Generado: 2026-08-19 · Base analizada: `feature/AB-11611-siguientes-novedades` (4 commits sobre `develop` @ `6bd536be`)
> Método: 4 barridos `explore-agent` de solo lectura + verificación directa del hilo orquestador sobre los hallazgos accionables.
> Alcance: **diagnóstico**. No se modificó una sola línea de código.

## Resumen ejecutivo

| # | Novedad | Veredicto | Clasificación | Bloqueante para cerrar |
|---|---------|-----------|---------------|------------------------|
| 1 | 403 en Placas preasignadas + endpoint visible en pantalla | **Dos defectos distintos.** La fuga de la URL está confirmada en código; el 403 en sí no se puede atribuir sin traza | 1a: Bug · 1b: por determinar | Traza de red / rol del usuario del caso |
| 2 | El Mandato solo lista MATRÍCULA INICIAL Y CAMBIO DE COMBUSTIBLE | **No es defecto.** Límite de alcance heredado de la HU #11206 | Cambio (comportamiento nuevo) | Decisión de alcance del PO |
| 3 | El dígito de preferencia de placa es opcional | **Comportamiento intencional**, documentado en el dominio y con test que lo fija | Decisión de producto | Decisión del PO ("EVALUAR") |
| 4 | Tabla carrocería↔categorías intermitente | **Defecto plausible con causa raíz aguas arriba**; el catálogo es estático y determinista, el dato que lo alimenta no | Bug | Un caso real con placa/VIN y clase devuelta |

**Lectura de conjunto:** de las cuatro novedades, solo una (#4) es un defecto clásico. La #1 son en realidad dos problemas superpuestos que conviene separar. La #2 y la #3 son peticiones de cambio de comportamiento sobre código que hoy hace deliberadamente lo que hace — tratarlas como bugs llevaría a "arreglar" algo que se decidió así.

---

## Novedad 1 — 403 en Placas preasignadas y endpoint visible en pantalla

### Lo reportado
El módulo de Placas preasignadas muestra un error 403 en el frontend y en pantalla se ve el endpoint.

### Lo que dice el código

Son **dos defectos independientes** que el reporte junta porque se ven a la vez.

**1a — La URL cruda en pantalla: CONFIRMADO.** Verificado directamente:

`frontend/lib/api/client.ts:95-118` — `apiFetch` solo extrae el campo `detail` del `ProblemDetails` **cuando el status es 422**. Para cualquier otro status no-ok construye:

```ts
throw new ApiError(response.status, `Error ${response.status} al llamar ${path}`, data);
```

`PlateRangesConsole.tsx:53-61` captura la excepción y pinta `e.message` tal cual en un `role="alert"` (`:200`).

Consecuencias:
- **Es transversal, no del módulo de placas.** Cualquier pantalla que reciba 403, 404, 409 o 500 muestra la ruta interna de la API al usuario final.
- **Se pierde el motivo real.** El backend sí manda un `detail` legible (ver 1b), pero el cliente lo descarta para todo lo que no sea 422. El usuario ve una URL en vez de "La preasignación no está habilitada entre la compañía y el OT".
- El comentario de `client.ts:99-100` muestra que este mismo bug ya se corrigió una vez, pero **solo para el 422**.

**1b — El 403 en sí: NO ATRIBUIBLE desde el código.** Hay dos orígenes posibles y son indistinguibles sin la traza real:

| Origen | Dónde | Qué significa |
|---|---|---|
| Autorización por rol | `AdminPlateRangesEndpoints.cs:23-26` → policy `OtModulePolicy` (`ApiSecurityExtensions.cs:120-124`, exige `SuperAdmin` u `ot_admin`) | El usuario no tiene el rol. 403 del framework, **sin cuerpo legible** |
| Regla de negocio | `AdminPlateRangesEndpoints.cs:281-285` (`AssignRangeAsync`) | `IsAssignmentAllowedAsync` es falso: flag, grant o `allow_plate_preassign` apagados. 403 **con `detail` explicativo** |

Agravante encontrado: `frontend/components/admin/transit-offices/ot-nav.ts:9-31` registra el tab "Preasignación" **sin metadatos de rol ni permiso**. El control de acceso vive 100% en el backend, así que un usuario sin el rol adecuado **ve el tab** y cada petición le revienta en 403 con la URL expuesta. Eso encaja exactamente con el síntoma descrito.

### Para cerrar el diagnóstico
Se necesita, del caso reportado: **el rol del usuario** y **la ruta + status del fallo en la pestaña de red**. Si es un GET de listado, es rol (origen 1); si es el POST de asignar rango, es regla de negocio (origen 2).

### Recomendación
Separar en dos work items. El 1a es un Bug de frontend acotado y de alto valor (arregla el síntoma en toda la aplicación, no solo aquí): leer `detail` del `ProblemDetails` en cualquier status no-ok y no incluir nunca el `path` en un mensaje destinado a la UI. El 1b queda a la espera de la traza; si resulta ser rol, el trabajo real es ocultar el tab a quien no lo puede usar.

---

## Novedad 2 — El Mandato no lista la prenda

### Lo reportado
Se registró una MI con prenda + cambio de combustible y el Mandato solo muestra "MATRÍCULA INICIAL Y CAMBIO DE COMBUSTIBLE". Se pide mostrar toda transformación o modificación.

### Lo que dice el código
**El sistema hace exactamente lo que se le pidió que hiciera.** No hay filtrado roto: hay una whitelist cerrada, en dos sitios que deben moverse a la vez.

1. `MandatoObjetoComposer.Componer` (`Flit.Tramites.Domain/Documents/MandatoObjetoComposer.cs:41`) compone el objeto como `nombre del trámite` + `transformaciones activas`. No admite ningún otro insumo (`:41-70`).
2. Las transformaciones salen de `FurCommand.TransformacionesActivas` (`Flit.Tramites.Application/UseCases/ProcedureInstances/FurCommand.cs:960-971`), que lee `field_values` filtrando por **exactamente tres claves**: `cambio_color`, `cambio_carroceria`, `cambio_combustible`.
3. `MandatoObjetoComposer` solo conoce esas mismas tres etiquetas (`:17-32`) y **descarta en silencio cualquier clave desconocida** (`:40`).

**La prenda no podría aparecer aunque estuviera en `field_values`**, porque no se modela así: es su propio agregado (`ProcedureInstancePrenda` / `PrendaGate`, Feature #10585), explícitamente fuera de este mecanismo — así lo documenta `ConditionalDocumentRules.cs:22-24`.

El alcance original está escrito en la cabecera del composer (`:4-13`, HU #11206): *"el trámite y, si el vehículo se transforma durante él, también las transformaciones"*. La prenda nunca entró en ese enunciado.

### Recomendación
Esto es un **cambio de comportamiento**, no una corrección. Ampliar el objeto del Mandato exige antes una decisión de negocio que el código no puede tomar:

- ¿Qué es "toda transformación o modificación"? ¿Solo prenda, o también leasing, cambio de servicio, regrabación de VIN, etc.?
- ¿Con qué texto legal aparece cada una en el objeto del mandato? El Mandato es un documento con efectos jurídicos; la redacción no es cosmética.
- ¿Se resuelve por catálogo dinámico o ampliando la whitelist? Hoy no hay catálogo: ampliar significa tocar los dos puntos (`FurCommand:960` y `MandatoObjetoComposer:17-32`) cada vez.

Sugerido: HU nueva con criterios de aceptación que enumeren las modificaciones a incluir y su literal exacto. Si se aprueba, vale la pena evaluar el paso a catálogo para no repetir el ciclo en la siguiente modificación.

---

## Novedad 3 — Obligatoriedad del dígito de preferencia de placa

### Lo reportado
Al crear una MI donde el VIN no tiene placa, el sistema deja guardar sin seleccionar dígito de preferencia. Sin él, el OT asigna cualquier placa. Se pide **evaluar** volverlo obligatorio.

### Lo que dice el código
Es opcional **por diseño explícito y documentado**, en las tres capas:

| Capa | Evidencia |
|---|---|
| Wizard (paso consulta) | `TramiteWizard.tsx:3175-3187` — el `<select>` incluye `<option value="">Sin preferencia</option>` como valor por defecto, sin `required` |
| Wizard (paso FUR) | `FirmaFurStep.tsx:603, 748, 860` — mismo campo, mismo `field_value`, también opcional |
| Dominio | `OtClientProcedure.cs:42-48` — *"Es SOLO una guía para el OT al asignar: puede elegir una placa que termine en este dígito o cualquier otra. null si el gestor no indicó preferencia."* |
| Persistencia | `OtClientProcedureRepository.cs:741-743` — proyecta con `FirstOrDefault()`; sin `field_value` resuelve a `null` sin error |
| Test | `OtClientProcedureHandlerTests.cs:929-946` — fija el contrato: se proyecta `null` cuando no existe el campo |

Dos matices relevantes para la decisión:

- **El dígito no influye en el ruteo.** `IPlatePreassignPolicy.DecideAsync` (`IPlatePreassignPolicy.cs:84-98`) decide la ruta con el flag de compañía + grant + `allow_plate_preassign` del OT. Con o sin dígito, sin placa el trámite cae igual en `Preasignado`.
- **El selector ya se deshabilita** cuando no hay `transitOfficeId` o la preasignación no está activa (`TramiteWizard.tsx:3179`). O sea: hay casos donde el usuario **no puede** elegir dígito aunque quisiera. Volverlo obligatorio sin contemplar esa rama dejaría trámites imposibles de continuar.

No existe ningún flag por tenant u OT que gobierne la obligatoriedad; el único flag relacionado (`plate_route_active` / `preasignacionActiva`) solo habilita el selector.

### Recomendación
La evaluación pedida es una decisión de producto, y el punto de choque está identificado: **si se vuelve obligatorio, hay que definir qué pasa cuando el selector está deshabilitado** (OT sin preasignación activa). Opciones a poner sobre la mesa:

1. **Obligatorio solo cuando el selector está habilitado** — coherente, bajo riesgo, no bloquea a nadie.
2. **Obligatorio siempre, con opción explícita "Sin preferencia"** — obliga a un acto consciente sin bloquear; resuelve el problema real (que el gestor lo pase por alto) sin efectos colaterales.
3. **Obligatorio duro** — requiere además decidir el comportamiento en OT sin preasignación, y probablemente validación server-side (hoy no existe ninguna).

La 2 es la que mejor ataca lo reportado: el problema no es que el dígito falte, es que se omite sin darse cuenta.

---

## Novedad 4 — Tabla de carrocería intermitente

### Lo reportado
Verificar la tabla que compara la carrocería del vehículo para saber qué categorías sugerir al cambiarla; está intermitente y fallando.

### Precisión de nomenclatura
No existe una tabla "carrocería → categorías". Lo que existe es el catálogo **inverso**: `clase de vehículo → carrocerías permitidas` (`frontend/lib/catalogs/bodywork-by-class.json`, generado de `carroceria.xlsx` el 2026-08-03, **18 clases**). Se usa para filtrar el selector del subtrámite "Cambio de Carrocería" en `VehicleTransformationsCard.tsx:80-84`. Se asume que es a esto a lo que apunta el reporte; si se refiere a otra cosa (p. ej. la clasificación AUTOMOTOR/MAQUINARIA/REMOLQUES del FUR multi-plantilla, Feature #10918), es un mecanismo distinto y habría que rebarrer.

### Lo que dice el código
**El catálogo es estático y determinista: no puede fallar de forma intermitente.** Lo intermitente es el dato que lo alimenta.

La cadena completa:

1. El componente lee `vehicle_class` de `field_values` y filtra localmente — **no hace ninguna llamada de red propia** (`VehicleTransformationsCard.tsx:80-84`).
2. `vehicle_class` lo puebla la **consulta RUNT de placa/VIN**, que es el único flujo no mockeado por defecto en FLIT (`VERIFIK_VEHICLE_MODE` default `real`, `InfrastructureExtensions.cs:401-403`).
3. Hay **tres mapeadores distintos** poblando ese mismo campo, cada uno desde un campo de origen con nombre propio: `KyverumRuntVehicleResultMapper.cs:199`, `IntempoVehicleResultMapper.cs:163`, `VerifikResultMapper.cs:194`. Cuál responde depende de `ConsultationProviderChainResolver` (`InfrastructureExtensions.cs:392-546`).
4. El lookup es **coincidencia exacta**: `normalizeVehicleClass` solo hace `trim` + `toUpperCase` (`bodywork-by-class.ts:19-21`). **No hay normalización de tildes en ninguna capa** — verificado: cero coincidencias de `RemoveDiacritics` o equivalente en todo `services/core-api/src`.
5. Sin match, `getBodyworksForVehicleClass` devuelve `[]` y el componente **no reporta error**: pinta *"No hay carrocerías disponibles para la clase X"* y deja el selector vacío (`:82-84`, `:365-366`). **Fallo silencioso, sin log del valor recibido.**

### Hipótesis principal
Las 18 claves del catálogo están **sin tildes y con una ortografía concreta**:

```
MOTOCICLETA | CAMION | TRACTOCAMION | BUS | AUTOMOVIL | CAMIONETA | REMOLQUE | MICROBUS
BUSETA | CAMPERO | MOTOTRICICLO | CUATRIMOTO | SEMIREMOLQUE | MOTOCARRO | VOLQUETA
CICLOMOTOR | TRICIMOTO | CUADRICICLO
```

Dos observaciones que explican la intermitencia:

- **Los mocks devuelven valores que sí casan** (`IntempoConsultationProvider.cs:103` → `"AUTOMOVIL"`, `VerifikConsultationProvider.cs:183` → `"CAMIONETA"`). El camino mockeado siempre funciona; el real no está garantizado.
- El RUNT devuelve habitualmente clases **con tilde** (`CAMIÓN`, `AUTOMÓVIL`) y con ortografía distinta a la del catálogo — nótese `SEMIREMOLQUE` con una sola R, cuando la forma usual es `SEMIRREMOLQUE`. Cualquiera de esas variantes falla el lookup en silencio.

Esto explica el patrón exacto reportado: funciona para unos vehículos y no para otros, sin mensaje de error, según qué proveedor respondió y con qué clase.

### Para confirmar
Un caso real que falle: **placa/VIN + el valor exacto de `vehicle_class` guardado en `field_values`** de ese trámite. Con eso se pasa de hipótesis fuerte a causa raíz cerrada en una consulta.

### Recomendación
Bug de frontend con dos frentes: normalizar el lookup (tildes, variantes ortográficas conocidas) y **dejar de fallar en silencio** — hoy no hay ni un log del valor crudo que no casó, que es justamente por lo que esto lleva tiempo sin diagnosticarse.

---

## Trabajo pendiente que cruza con estas novedades

- La rama actual `feature/AB-11611-siguientes-novedades` lleva 4 commits (Bugs #11612–#11615) de la tanda anterior, con **PR #277 abierto a develop**. Ninguna de las cuatro novedades de hoy está cubierta por esos Bugs.
- Las ramas de placa (`AB-10587`, `AB-10806`, `AB-11482`, `fix/AB-TBD-subsanacion-estado-y-error-placa`) están **todas mergeadas en develop**: las novedades 1 y 3 son sobre código ya integrado, no sobre trabajo en vuelo.
- La novedad 3 confirma lo que ya registraban las HU #10804/#10805: el dígito se implementó **como guía**, no como requisito. No hay contradicción entre lo entregado y lo especificado.

## Qué se necesita del usuario para avanzar

| Novedad | Qué falta | De quién |
|---|---|---|
| 1b | Rol del usuario + ruta y status en la pestaña de red del caso reportado | Quien reportó |
| 2 | Decisión de alcance: qué modificaciones entran al objeto del Mandato y con qué literal | PO |
| 3 | Decisión de producto sobre la obligatoriedad (ver las 3 opciones) | PO |
| 4 | Un trámite que falle: placa/VIN + valor de `vehicle_class` guardado | Quien reportó |

La 1a es la única accionable hoy sin insumos adicionales.

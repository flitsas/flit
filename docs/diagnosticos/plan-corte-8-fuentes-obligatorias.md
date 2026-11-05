# Plan del corte 8 — fuentes obligatorias en el alta

Fecha: 2026-11-04. Rama `feature/AB-12881-diagnostico-d0-d2`. Épica #12678, sin historia nueva. Este corte no se implementa hasta confirmar el plan y elegir una opción. No activa la épica.

El alta de un Con Análisis IA ya exige las fuentes que traiga el tipo y la pantalla ya las marca y las bloquea. El catálogo sigue entregando la lista vacía. Este corte llena esa lista. No cambia el evaluador, el lector, la cola ni el PDF.

## Dictamen normativo

Fuente: Resolución 20233040017145 de 2023, PDF local `docs/ot/resolutions/Resolución 20233040017145 de 2023 Ministerio de Transporte.pdf`. Artículos citados por el texto de ese PDF, no por el resumen de la skill.

La resolución no nombra los códigos de Diagnóstico (`RUNT`, `SIMIT_VEHICULO`, `SIMIT_PROPIETARIO`, `RUNT_PROPIETARIO`, `IMPUESTO_DEPARTAMENTAL`). La tabla de abajo es el puente. Donde la norma no elige un código, la celda dice que no alcanza.

| Qué exige la norma | Dónde | Código de Diagnóstico |
| --- | --- | --- |
| Confrontar el vehículo en el RUNT con la licencia, el QR o las improntas | art. 5.3.2.1 num. 2, pp. 30-31 | `RUNT`. Ya es obligatorio en todo diagnóstico (`ALCANCE_SIN_RUNT`), con o sin este corte |
| SOAT y revisión técnico-mecánica vigentes del vehículo, validados en el RUNT | art. 5.3.2.1 num. 4, p. 31 | No hay fuente aparte. Salen del detalle del `RUNT` |
| Propietario a paz y salvo por multas, vía la interoperabilidad SIMIT–RUNT | art. 5.3.2.1 num. 4, p. 31 | `SIMIT_PROPIETARIO`. No es `SIMIT_VEHICULO`: el numeral habla del propietario, no de la placa |
| Si el propietario es banco o compañía de financiamiento, el organismo solo valida el paz y salvo del locatario | art. 5.3.2.1 num. 4, párrafo 2, p. 31 | `SIMIT_LOCATARIO`, y solo en ese caso. En el alta todavía no se sabe quién es el propietario: el RUNT no se ha consultado. Una casilla fija no puede expresar esta condición |
| Pago de impuestos del vehículo, con copias de los recibos. La interoperabilidad con la entidad territorial es facultativa | art. 5.3.2.1 num. 5, pp. 31-32, y art. 5.1.9, p. 12. Remolques y semirremolques exentos (Ley 488 de 1998, mismo num. 5) | No obliga a consultar `IMPUESTO_DEPARTAMENTAL` |
| Limitaciones y gravámenes | art. 5.3.2.1 num. 3, p. 31 | Van en el `RUNT` del vehículo. No son otra fuente |
| Traspaso unilateral de leasing: no se exige RTM ni paz y salvo de infracciones | art. 5.3.2.2 nums. 1 y 3, p. 32 | En `TRASPASO_UNILATERAL` no se marca `SIMIT_PROPIETARIO`, `SIMIT_VEHICULO` ni `SIMIT_LOCATARIO`. La RTM no tiene casilla propia |
| Licencia del propietario | No está en el procedimiento de 5.3.2.1 | `RUNT_PROPIETARIO` no sale de este artículo. Es regla del catálogo v1, no de la resolución |
| Tres códigos con la misma lista | R-17 lo proponía. El art. 5.3.2.2 lo impide | `TRASPASO_STANDARD` y `TRASPASO_UNILATERAL` no pueden compartir la lista |

`TRASPASO_TRANSFERENCIA_DE_DOMINIO` no tiene artículo propio. El plan lo trata como el traspaso ordinario del 5.3.2.1 hasta que alguien diga que es otro procedimiento. Eso es una lectura de producto, no una cita.

## Información que no alcanza

Con lo que hay en la resolución y en el repo no se puede cerrar una sola lista para la familia.

- Los códigos de fuente son de FLIT. La norma no los elige.
- La propuesta de R-17 (RUNT vehículo, SIMIT vehículo, RUNT propietario, SIMIT propietario, igual para los tres tipos) contradice el art. 5.3.2.2 y llama «SIMIT del vehículo» a lo que el num. 4 llama paz y salvo del propietario.
- El «únicamente» del párrafo del banco puede leerse de dos maneras: solo cambia el paz y salvo (del propietario al locatario) o reemplaza todo el numeral 4. El plan no elige esa lectura.
- El paz y salvo del locatario depende de un hecho que el alta no tiene.
- El impuesto automatizado es facultativo para la entidad territorial. Exigir `IMPUESTO_DEPARTAMENTAL` en el alta sería decisión de producto.
- `RUNT_PROPIETARIO` (licencias) no está en el 5.3.2.1.

Lo que sí se puede sembrar, como piso y no como la lista de R-17:

| Tipo | Piso de este plan | Qué no se marca por la norma |
| --- | --- | --- |
| `TRASPASO_STANDARD` | `SIMIT_PROPIETARIO` | `SIMIT_VEHICULO`, `RUNT_PROPIETARIO`, `SIMIT_LOCATARIO`, `RUNT_LOCATARIO`, `AVALUO`, `IMPUESTO_DEPARTAMENTAL` |
| `TRASPASO_TRANSFERENCIA_DE_DOMINIO` | el mismo piso, a falta de artículo propio | lo mismo |
| `TRASPASO_UNILATERAL` | ninguna fuente adicional | cualquier `SIMIT_*` |

`RUNT` no entra en esa tabla: ya está bloqueado para todo diagnóstico, antes de mirar el tipo.

## Qué ya está

| Pieza | Dónde | Qué hace hoy |
| --- | --- | --- |
| Validación | `ValidadorCabecera` | Solo en Con Análisis IA, después de las demás reglas. Si al tipo le faltan fuentes en el alcance, 422 `FUENTES_OBLIGATORIAS_FALTANTES`. En Simple no consulta la lista. `RUNT` falta → `ALCANCE_SIN_RUNT` en cualquier modo |
| Catálogo | `CatalogoTiposTramiteGrpc` | Lee el gRPC de Trámites y construye cada `TipoTramite` con `FuentesObligatorias` en `[]` |
| Contrato | `GET /tipos-tramite` | Publica `fuentesObligatorias`. El test de API afirma que el arreglo llega vacío |
| Pantalla | `SeccionAlcance` | `RUNT` y cada código de `obligatorias` salen marcados, bloqueados y con tooltip. Si el contrato de la compañía no incluye una obligatoria, avisa y no la ofrece |
| Contrato de la compañía | `fuentes_habilitadas` | Qué puede consultar esa empresa. No es la lista del trámite |
| Reglas del dictamen | `CatalogoReglasDictamen.TiposTramite` | Los tres códigos comparten las mismas reglas, incluido `FUENTE_PENDIENTE` para RUNT, SIMIT vehículo, RUNT propietario y SIMIT propietario |

Marcar la casilla no apaga ni enciende una regla. Si la fuente no se consulta, el evaluador puede seguir advirtiendo `FUENTE_PENDIENTE`. Este corte no toca el evaluador.

## Opciones

Las dos dejan el validador como está. Cambian quién llena `FuentesObligatorias`. El gRPC de Trámites no trae esta lista: hoy se descarta a propósito. Ninguna opción espera un cambio de core-api.

### A — lista en código

Un mapa en Diagnóstico, de código de trámite a fuentes, con el piso de la tabla de arriba. `CatalogoTiposTramiteGrpc` deja de pasar `[]` para esos tres códigos. El resto de los tipos sigue en vacío.

No hay pantalla nueva ni migración. Cambiar la lista es un despliegue. Sirve para cumplir el corte 8 y nada más.

### B — configurador

La lista es un catálogo del producto, editable, con dos niveles: la familia y, si existe, el tipo. El tipo gana sobre la familia. La familia `TRASPASO` cubre los tres códigos mientras un tipo no tenga fila propia.

Quién edita: backoffice FLIT, igual que el interruptor de redacción. No la compañía. `fuentes_habilitadas` sigue siendo el contrato. Si el configurador marca una fuente que la compañía no tiene, la pantalla ya muestra el aviso de «no contratada» y el alta sigue respondiendo `FUENTES_OBLIGATORIAS_FALTANTES`.

Qué guarda: `tipo_tramite_codigo` o, en su lugar, `familia`; `fuente`; vigente. Catálogo global, sin `tenant_id`. Una migración nueva. ADR en Propuesto antes de la migración: la lista del trámite es de FLIT, no de la empresa. La opción A de ese ADR es la tabla. La B es la lista en código (esta opción A del plan). La C es por compañía: se descarta aquí, porque una empresa podría quitar el paz y salvo que el art. 5.3.2.1 num. 4 pide.

Pantalla nueva en el backoffice de Diagnóstico: por familia y por tipo, las casillas del enum `Fuente` menos `RUNT` (ese no se puede apagar). Guardar `TRASPASO_UNILATERAL` con un `SIMIT_*` queda bloqueado en API y en la pantalla, por el art. 5.3.2.2 num. 3. El piso de `SIMIT_PROPIETARIO` en los otros dos tipos se puede ampliar. No se puede borrar: el configurador agrega fuentes de producto, no dispensa las del artículo.

El `GET /tipos-tramite` arma `fuentesObligatorias` con la fila del tipo o, si no hay, con la de la familia. La parametrización no cambia de componente: ya consume ese arreglo.

La semilla de la migración es el piso de la tabla. Sin semilla, el configurador nacería vacío y el corte 8 no marcaría nada.

## Qué queda fuera

- Elegir la lectura del párrafo del banco en el 5.3.2.1 num. 4.
- Hacer obligatorio `SIMIT_LOCATARIO` en el alta. Falta el propietario.
- Hacer obligatorio `IMPUESTO_DEPARTAMENTAL`, `SIMIT_VEHICULO` o `RUNT_PROPIETARIO` por cita de la resolución. Si se agregan, es por el configurador (opción B) o por un cambio de producto aparte, no por este dictamen.
- Cambiar `EvaluadorDictamen`, `FUENTE_PENDIENTE`, el catálogo de reglas o el redactor.
- Publicar la lista en el gRPC de Trámites.
- Una lista distinta por compañía.
- Activar la épica #12678.
- Tocar lotes Simple. Siguen sin tipo y sin esta lista.
- Regenerar dictámenes ya emitidos. La lista aplica al alta siguiente.

## Archivos

Comunes a las dos opciones:

| Archivo | Papel |
| --- | --- |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Catalogos/CatalogoTiposTramiteGrpc.cs` | Deja de fijar `[]` cuando el tipo tiene lista |
| `services/core-diagnostico/tests/Flit.Diagnostico.Tests/Api/TiposTramiteEndpointsTests.cs` | El arreglo vacío deja de ser la aserción de los tres traspasos |
| `services/core-diagnostico/src/Flit.Diagnostico.Domain/Catalogos/TipoTramite.cs` | El comentario deja de decir que la lista llega vacía hasta D6 |

Solo la opción A:

| Archivo | Papel |
| --- | --- |
| El mismo `CatalogoTiposTramiteGrpc.cs` | El mapa del piso, en código |

Solo la opción B:

| Archivo | Papel |
| --- | --- |
| Migración nueva de `core-diagnostico` | Tabla global y semilla del piso. No se edita una migración ya aplicada |
| ADR nuevo, estado Propuesto | Catálogo global de fuentes por familia y por tipo |
| Endpoints de backoffice | Leer y guardar. Rechazan `RUNT` y un `SIMIT_*` en `TRASPASO_UNILATERAL` |
| Pantalla del backoffice de Diagnóstico | El configurador. Estados vacío, cargando, error y lleno |

`ValidadorCabecera` y `SeccionAlcance` no se reescriben. Los tests que ya cubren «obligatorias marcadas» y `FUENTES_OBLIGATORIAS_FALTANTES` se corren con la lista del piso. Si alguno afirma el vacío de los tres traspasos, se actualiza en el rojo.

## Pruebas

Sin Postgres en la opción A. Con Postgres en la opción B, solo para la semilla y el guardado.

Afirman:

- `TRASPASO_STANDARD` y `TRASPASO_TRANSFERENCIA_DE_DOMINIO` publican `SIMIT_PROPIETARIO` y no publican `SIMIT_VEHICULO` ni `RUNT_PROPIETARIO`.
- `TRASPASO_UNILATERAL` publica la lista vacía.
- Un Con Análisis IA de traspaso ordinario sin `SIMIT_PROPIETARIO` en el alcance responde `FUENTES_OBLIGATORIAS_FALTANTES`.
- El mismo alta en Simple no mira esta lista.
- Un tipo que no es de la familia sigue con la lista vacía.
- Opción B: guardar `SIMIT_VEHICULO` en `TRASPASO_UNILATERAL` se rechaza. Guardarlo en `TRASPASO_STANDARD` hace que el `GET /tipos-tramite` lo incluya, además del piso. Quitar `SIMIT_PROPIETARIO` del ordinario se rechaza.

El formato, con severidad error, solo sobre los `.cs` del diff. `db-schema-validator` solo si se elige la opción B.

## Listo cuando

- Quien arma un Con Análisis IA de traspaso ordinario ve `SIMIT_PROPIETARIO` marcado y bloqueado, y el alta lo exige.
- Quien arma un traspaso unilateral no gana un SIMIT obligatorio.
- `RUNT` sigue bloqueado por la regla de siempre, no por esta lista.
- La opción quedó elegida. Si es B, el configurador existe, la semilla es el piso y el ADR está en Propuesto.
- No se activó la épica #12678.
- El registro de la corrida queda en este documento, en local, sin Azure DevOps.

## Registro de la corrida

2026-11-04. Opción elegida: **B**. La épica #12678 no se activó. No hay work item nuevo ni comentario en Azure DevOps. ADR-0078 sigue en Propuesto.

| Pieza | Dónde |
| --- | --- |
| ADR | `docs/decisions/ADR-0078-diagnostico-fuentes-obligatorias-por-tramite.md` |
| Migración | `20261104061749_D8_FuentesObligatorias`. Cuatro cabeceras. Tres líneas `SIMIT_PROPIETARIO`. `TRASPASO_UNILATERAL` es cabecera sin líneas |
| Resolución y PUT | `ResolucionFuentesObligatorias`, `CatalogoTiposTramiteGrpc`, `FuentesObligatoriasBackofficeEndpoints` |
| Pantalla | `/backoffice/fuentes-obligatorias`, solo en el dock de backoffice |

Corridas locales, con `-p:NuGetAudit=false` donde el restore se detiene por `NU1903`:

- `EntidadesConTenantTests`: 2 superadas. Veredicto de schema: `OK_TO_MERGE_DB`, con las excepciones de catálogo global citadas en el ADR.
- Rojo de la API, antes de los endpoints: 12 con error y 11 superadas. El dominio ya resolvía. El GET de tipos seguía en `[]` y el PUT respondía 404.
- Verde: `ResolucionFuentesObligatoriasTests`, `FuentesObligatoriasBackofficeTests`, `TiposTramiteEndpointsTests` y `CrearDiagnosticoTests`, 38 superadas.
- Pantalla: `FuentesObligatoriasBackoffice.test.tsx`, 5 superadas.

No se abrió el navegador: no había servidor de Diagnóstico con sesión de backoffice FLIT. El clic, el foco del candado y el guardado contra el API quedan sin ese pase.

Sin certificación QA. El ADR no pasó a Aceptado. No hay commit ni PR de este corte.

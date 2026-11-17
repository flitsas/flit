# Plan — párrafo del dictamen en el PDF

Fecha: 2026-11-04. Rama `feature/AB-12881-diagnostico-d0-d2`. Épica #12678, sin historia nueva. Este plan no se implementa hasta confirmarlo. No es el corte 8: ese sigue siendo marcar las fuentes obligatorias en el alta.

La huella ya cambia cuando cambia el dictamen. El archivo no lo muestra. Este plan dibuja el párrafo en el PDF del vehículo, con el mismo texto que ya entra en la huella.

## Qué ya está

| Pieza | Dónde | Qué hace hoy |
| --- | --- | --- |
| Contenido | `ContenidoReporteVehiculo.Dictamen` | Si hay vigente, `LectorContenidoReporte` arma `DictamenEnHuella`: veredicto, estado y los textos de `alertamientos` unidos por un salto de línea (`TextoDeAlertamientos`). Sin fila, el campo queda null. |
| Huella | `ParaHuella` | Con dictamen suma `dictamen/veredicto`, `dictamen/estado` y `dictamen/texto`. Sin dictamen no suma claves. La descarga compara esa huella y, si no hay vigente o no coincide, genera otro PDF. |
| Dibujo | `PdfVehiculo.Documento` | Título, logo, pares del diagnóstico y las secciones de las fuentes. No lee `Dictamen`. `VersionPlantilla` es 4. |
| Completada | `RedactorEnCola.ConTexto` | El texto del modelo se copia en cada alertamiento. En pendiente, cada uno conserva su plantilla. |
| Invalidación | Corte 6 | Al pasar a `COMPLETADA`, el PDF vigente de ese vehículo deja de serlo. El Excel y el ZIP no se tocan. Un ZIP ya entregado no se regenera. |
| Panel | Corte 7 | Muestra veredicto, alertamientos y, si está pendiente, «redacción IA pendiente». El PDF no tiene que parecerse al panel casilla por casilla: el párrafo es el texto, no el código ni la fuente. |

## Dónde va

Una sección, después de los pares del diagnóstico y antes de «Datos del vehículo». Usa `SeccionConTitulo` con el título «Dictamen», la misma tipografía de las otras secciones. El veredicto no se comunica solo con color: se lee «Viable» o «Bloqueado».

Si `Dictamen` es null, la sección no existe. Un lote simple, o un vehículo que todavía no tiene fila, sale como hoy. No hay un aviso de «sin dictamen».

El cuerpo, en este orden:

1. El veredicto, con la palabra. `VIABLE` se lee «Viable». `BLOQUEADO` se lee «Bloqueado».
2. Si el estado es `PENDIENTE`, la línea «redacción IA pendiente». En `COMPLETADA` esa línea no sale.
3. Los párrafos. Son los textos de los alertamientos, en el orden del arreglo. Si el texto siguiente es igual al anterior, no se repite: en `COMPLETADA` el modelo está copiado en cada alertamiento y el informe muestra ese párrafo una vez. En pendiente las plantillas son distintas y sale una por regla.

No salen código, efecto, fuente, versión, catálogo, override, lease, intentos, placa ni documentos. La placa y el VIN siguen donde ya están, en los pares del diagnóstico. El texto que se dibuja es el de `dictamen/texto`, sin armar otra redacción.

## Plantilla

`PdfVehiculo.VersionPlantilla` pasa de 4 a 5. La versión entra en la huella. Un PDF ya guardado, tenga o no dictamen, no coincide y la descarga siguiente lo genera de nuevo. Sin dictamen el archivo se ve igual, salvo que lo produjo la plantilla 5. Con dictamen, el archivo nuevo trae la sección.

No se genera el PDF en el cierre, en el override ni en la cola. Sigue valiendo lo del corte 6 y del corte 7: la siguiente descarga es la que arma el archivo. Un ZIP que ya se entregó no se vuelve a armar. El ZIP que se pida después incluye los PDF de la plantilla 5, porque el empaquetador usa el mismo `PdfVehiculo`.

El Excel no dibuja el párrafo y no cambia de versión.

## Qué queda fuera

- Marcar las fuentes obligatorias en el alta. Corte 8.
- Regenerar un ZIP ya entregado.
- Cambiar `ConTexto`, el prompt, el catálogo, el evaluador o el lector de hechos.
- Mostrar en el PDF el código, el efecto o la fuente de cada alertamiento.
- Una sección de dictamen en el Excel.
- Activar la épica #12678.
- Una migración. `db-schema-validator` no aplica.

## Archivos

| Archivo | Papel |
| --- | --- |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Reportes/PdfVehiculo.cs` | Dibuja la sección y sube `VersionPlantilla` a 5 |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Reportes/ContenidoReporteVehiculo.cs` | El comentario deja de decir que el PDF no dibuja el párrafo. La huella no gana claves |
| `services/core-diagnostico/tests/Flit.Diagnostico.Tests/Reportes/PdfVehiculoTests.cs` | La versión de plantilla es 5. El texto del PDF, sin Postgres |

No se toca `LectorContenidoReporte`, `RedactorEnCola`, el panel ni los contratos.

## Pruebas

Sin Postgres. Se genera el PDF y se lee el texto, como `PdfVehiculoDisenoV2Tests`. Afirma:

- Sin `Dictamen`, el texto no trae el título «DICTAMEN» ni «redacción IA pendiente».
- Con veredicto `BLOQUEADO`, estado `PENDIENTE` y dos textos distintos, salen «Bloqueado», «redacción IA pendiente» y los dos textos, en ese orden, antes de «DATOS DEL VEHÍCULO».
- Con estado `COMPLETADA` y el mismo texto repetido en cada alertamiento, el párrafo sale una vez y no sale «redacción IA pendiente».
- `VersionPlantilla` es 5.
- `ParaHuella` sigue teniendo solo `dictamen/veredicto`, `dictamen/estado` y `dictamen/texto` cuando hay dictamen, y ninguna clave `dictamen/` cuando no hay.

El formato, con severidad error, solo sobre los `.cs` del diff.

## Listo cuando

- Quien descarga el PDF de un vehículo con dictamen lee el veredicto y el párrafo. Si la redacción sigue pendiente, también lee «redacción IA pendiente».
- Un vehículo sin dictamen no gana esa sección.
- El párrafo no repite el texto del modelo una vez por regla.
- La descarga siguiente reemplaza el PDF viejo, porque la plantilla es la 5. No se regenera un ZIP ya entregado y el Excel no cambia.
- No hay migración y no se activa la épica #12678.
- El registro de la corrida queda en este documento, en local, sin Azure DevOps.

## Corrida

Fecha: 2026-11-04. Sin Azure DevOps y sin migración. No se activó la épica #12678. No hubo commit ni PR.

El `dotnet test` y el `dotnet build` literales se detienen en el restore por NU1903 (`Microsoft.DotNet.ILCompiler` 10.0.8, GHSA-p3jp-9v83-76w9), ajeno a este cambio. Las corridas de abajo añaden `-p:NuGetAudit=false` solo en el comando, sin tocar el csproj. El `dotnet format` con ese `-p` responde `Restore operation failed`; se repitió con `--no-restore` (el restore ya había quedado de la corrida anterior) y no reescribió los `.cs`, así que no se volvió a correr el verde.

### Rojo

Antes de tocar producción. Log: `.cursor/state/pdf-dictamen-rojo.txt`.

```
dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --filter "FullyQualifiedName~PdfVehiculoDisenoV2Tests" --nologo -p:NuGetAudit=false
```

`Con error! - Con error:     3, Superado:    24, Omitido:     1, Total:    28, Duración: 3 s - Flit.Diagnostico.Tests.dll (net10.0)`

Fallaron porque `VersionPlantilla` seguía en 4 y `Documento` no dibujaba el dictamen:

- `Version_de_plantilla_es_5`: Expected PdfVehiculo.VersionPlantilla to be 5, but found 4.
- `Dictamen_pendiente_bloqueado_pinta_veredicto_aviso_y_los_dos_parrafos_antes_de_datos_del_vehiculo`: Expected i to be greater than or equal to 0 because «Bloqueado» aparece después del anterior, but found -1 (difference of -1).
- `Dictamen_completada_no_repite_el_parrafo_ni_el_aviso_y_lee_viable`: Expected cuenta to be 1, but found 0 (difference of -1).

Pasaron `Sin_dictamen_el_pdf_no_trae_el_titulo_ni_la_redaccion_pendiente` y `Para_huella_sin_dictamen_no_suma_claves_y_con_dictamen_solo_las_tres`: sin sección no hay título ni aviso, y `ParaHuella` ya tenía solo esas tres claves. Omitido `Escribe_pdf_de_muestra_si_se_pide` (sin `FLIT_PDF_MUESTRA`). El mensaje completo de cada aserción, con el texto aplanado del PDF, está en el log.

### Verde

Mismo filtro, después de producción. Log: `.cursor/state/pdf-dictamen-verde.txt`.

```
dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --filter "FullyQualifiedName~PdfVehiculoDisenoV2Tests" --nologo -p:NuGetAudit=false
```

`Correctas! - Con error:     0, Superado:    27, Omitido:     1, Total:    28, Duración: 3 s - Flit.Diagnostico.Tests.dll (net10.0)`

### Formato

Cwd `services/core-diagnostico`. Exit 0. Advertencia de carga del área de trabajo, sin cambios en los archivos.

```
dotnet format src/Flit.Diagnostico.Api/Flit.Diagnostico.Api.csproj --severity error --include src/Flit.Diagnostico.Api/Modulos/Reportes/PdfVehiculo.cs --include src/Flit.Diagnostico.Api/Modulos/Reportes/ContenidoReporteVehiculo.cs --no-restore
dotnet format tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --severity error --include tests/Flit.Diagnostico.Tests/Reportes/PdfVehiculoTests.cs --no-restore
```

### Build

Log: `.cursor/state/pdf-dictamen-build.txt`.

```
dotnet build services/core-diagnostico/tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --nologo -p:NuGetAudit=false
```

```
Compilación correcta.
    0 Advertencia(s)
    0 Errores

Tiempo transcurrido 00:00:20.82
```

### Regresión

Un solo `dotnet test`, sin `--no-build`. Log: `.cursor/state/pdf-dictamen-regresion.txt`.

```
dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --filter "FullyQualifiedName~PdfVehiculoDisenoV2Tests|FullyQualifiedName~DetalleRuntSeguridadTests|FullyQualifiedName~Rearmar_solo_pone_monto_en_impuesto_y_la_huella_suma_el_dictamen_si_lo_hay" --nologo -p:NuGetAudit=false
```

`Correctas! - Con error:     0, Superado:    28, Omitido:     1, Total:    29, Duración: 2 s - Flit.Diagnostico.Tests.dll (net10.0)`

No hay una clase `DetalleRuntSeguridadTests`: ese archivo es el partial `PdfVehiculoDisenoV2Tests`, así que sus facts ya entran en el primer patrón. El superado de más respecto al verde (28 contra 27) es `Rearmar_solo_pone_monto_en_impuesto_y_la_huella_suma_el_dictamen_si_lo_hay`. El omitido es otra vez la muestra visual.

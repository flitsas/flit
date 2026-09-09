# Feature — Generación documental autónoma

> Transferencia de Dominio y Certificado RUES · Versión refinada local · Estado: borrador

## Objetivo

Habilitar en FLIT un módulo administrativo independiente de los trámites para generar documentos de
Transferencia de Dominio y Certificados RUES, individualmente o mediante lotes, descargarlos y consultar
su historial con trazabilidad por empresa y usuario.

El módulo no crea ni requiere una `ProcedureInstance`, no modifica expedientes y no asocia sus documentos
a un radicado, FUR o checklist de trámite.

## Valor

- Permitir emisión documental sin iniciar un trámite.
- Reducir trabajo manual y habilitar generación masiva controlada.
- Conservar evidencia reproducible de la información usada para cada documento.
- Proteger el aislamiento entre compañías y permitir auditoría administrativa.

## Referencias

- Plantilla normativa: [`plantilla-transferencia-dominio.md`](plantilla-transferencia-dominio.md).
- Plan de implementación: [`plan-tecnico-generacion-masiva-docs.md`](plan-tecnico-generacion-masiva-docs.md).
- Resolución 20233040017145 de 2023: artículos 5.1.5, 5.1.6, 5.1.8, 5.3.2.1 y 5.3.2.2.

La plantilla es un instrumento parametrizable y no garantiza por sí sola la admisión del documento por
un organismo de tránsito ni sustituye la validación jurídica del negocio.

## Incrementos

| Incremento | Entrega |
|---|---|
| I1 | Módulo base, Certificado RUES individual, historial, descarga y RBAC |
| I2 | Transferencia de Dominio individual en escenarios A, B y C |
| I3 | XLSX masivo, procesamiento asíncrono, seguimiento y ZIP |

La secuencia obligatoria es I1 → I2 → I3.

## Decisiones funcionales

1. RUES consulta los datos en vivo por NIT, genera el PDF localmente con QuestPDF y conserva un snapshot
   inmutable para reproducibilidad.
2. No se utiliza el proveedor externo de PDF RUES en este módulo.
3. Los PDF se almacenan en S3/file-manager; PostgreSQL conserva metadata, integridad y auditoría.
4. La carga masiva usa una plantilla XLSX versionada, admite hasta 100 filas y puede mezclar tipos de
   documento mediante `document_type`.
5. Transferencia exige `scenario`:
   - `A`: traspaso ordinario del artículo 5.3.2.1.
   - `B`: transferencia unilateral de entidad financiera al locatario del artículo 5.3.2.2.
   - `C`: transferencia de entidad financiera a tercero, sin exenciones del artículo 5.3.2.2.
6. A/B/C clasifican la operación jurídica; no clasifican personas naturales o jurídicas.
7. Los lotes se siguen dentro de la aplicación mediante polling; no incluyen email ni push.
8. La descarga en lote produce un ZIP por streaming y contiene únicamente documentos generados.
9. Los estados de generación son `generated` y `error`; las descargas se registran mediante fecha y
   contador, no mediante un estado `descargado`.
10. SuperAdmin consulta metadata global, pero solo genera y descarga contenido de su tenant. AdminCompany
    lista, genera y descarga exclusivamente dentro de su tenant.

## Actores y permisos

| Actor | Consultar metadata | Generar | Descargar contenido |
|---|---|---|---|
| AdminCompany | Solo tenant propio | Solo tenant propio | Solo tenant propio |
| SuperAdmin | Todos los tenants | Solo tenant propio | Solo tenant propio |

Los intentos de acceso a contenido de otro tenant deben responder 403 o 404 y no deben revelar URLs
presignadas, snapshots ni datos personales.

## Criterios funcionales

### Generación individual

- **CF-01:** el módulo ofrece formularios diferenciados para Certificado RUES y Transferencia de Dominio.
- **CF-02:** la generación se realiza sin trámite, radicado ni `procedure_instance_id`.
- **CF-03:** un documento generado correctamente queda disponible para descarga inmediata y se registra
  en el historial.
- **CF-04:** el flujo RUES consulta el NIT en vivo, permite revisar la información obtenida y genera el
  certificado mediante la plantilla QuestPDF local.
- **CF-05:** el registro RUES conserva el snapshot de los datos empleados; una reproducción desde ese
  snapshot no vuelve a consultar al proveedor.
- **CF-06:** Transferencia obliga a seleccionar exactamente uno de los escenarios A, B o C.
- **CF-07:** campos, cláusulas, soportes informados y firmas cambian según la matriz del anexo normativo.
- **CF-08:** en el escenario B, la ausencia de firma del locatario en el instrumento se deriva del diseño
  unilateral; la exención normativa expresa sobre firma corresponde al Formato Único.
- **CF-09:** el sistema bloquea inconsistencias verificables con los datos ingresados y presenta como
  prevalidaciones las condiciones que dependen de RUNT, RUES, SIMIT, SOAT o RTM.
- **CF-10:** antes de generar Transferencia, la interfaz informa que FLIT no garantiza suficiencia
  jurídica ni aprobación por el organismo de tránsito.

### Generación masiva

- **CF-11:** el usuario puede descargar y cargar una plantilla XLSX v1 con máximo 100 filas.
- **CF-12:** cada fila declara `document_type`; las transferencias declaran además `scenario`.
- **CF-13:** una fila inválida queda en error con código y campo identificable sin cancelar las demás.
- **CF-14:** el lote informa progreso y resumen de resultados mediante polling dentro de la aplicación.
- **CF-15:** los documentos exitosos permiten descarga individual y ZIP por streaming; los errores no
  se incluyen en el ZIP.
- **CF-16:** repetir una solicitud con la misma clave de idempotencia no duplica el lote ni sus archivos.

### Historial

- **CF-17:** el historial muestra tipo, escenario cuando aplique, usuario, empresa, fecha y resultado.
- **CF-18:** permite filtrar por tipo, rango de fecha y usuario; en I3 también por lote.
- **CF-19:** permite redescargar el PDF existente sin regenerarlo y actualiza `downloaded_at` y
  `download_count`.
- **CF-20:** SuperAdmin puede consultar metadata de otros tenants, pero no descargar su contenido.

### UX y accesibilidad

- El módulo utiliza el patrón administrativo de improntas con pestañas Generar e Historial.
- Cada pantalla cubre estados vacío, cargando, error y lleno.
- Formularios, tablas, modales y progreso cumplen WCAG 2.1 AA, navegación por teclado, foco visible,
  labels y errores asociados.
- Los estados nunca se comunican únicamente mediante color.

## Requisitos no funcionales

- API versionada bajo `/api/v1/admin/documentos`.
- Contrato OpenAPI actualizado junto con cada endpoint.
- PDF fuera de PostgreSQL; no se permite almacenamiento `BYTEA`.
- Snapshot RUES inmutable y hash SHA-256 del PDF persistido.
- Procesamiento masivo en background, con límite de concurrencia y recuperación de elementos atascados.
- URLs presignadas de vida corta y sin exposición en logs.
- Aislamiento tenant mediante filtro de repositorio y RLS como defensa en profundidad.
- Datos personales minimizados en logs, errores, XLSX persistido y metadata global.

## Fuera de alcance

- Asociar documentos con trámites o incorporarlos automáticamente a un expediente.
- Reemplazar FUR, contrato de leasing, declaración de terminación/opción o mandato.
- Certificar validez jurídica o garantizar aceptación por el organismo de tránsito.
- Usar el PDF RUES suministrado por el proveedor externo.
- Notificaciones por email o push.
- Personalización de plantillas por compañía.
- Descarga de contenido cross-tenant por SuperAdmin.

## Historias propuestas

| HU | Alcance | SP | Incremento | Dependencias |
|---|---|---:|---|---|
| HU-01 | Shell, navegación, permisos y cuatro estados UI | 3 | I1 | — |
| HU-02 | Persistencia standalone, RUES individual, snapshot y S3 | 5 | I1 | HU-01 |
| HU-03 | Historial, filtros, auditoría y redescarga | 3 | I1 | HU-02 |
| HU-04 | RBAC AdminCompany y metadata global SuperAdmin | 2 | I1 | HU-02 |
| HU-05 | Transferencia escenario A | 5 | I2 | HU-02, anexo normativo |
| HU-06 | Transferencia escenarios B y C | 8 | I2 | HU-05 |
| HU-07 | XLSX, lotes, ítems y worker asíncrono | 8 | I3 | HU-02, HU-05, HU-06 |
| HU-08 | Seguimiento in-app y ZIP streaming | 5 | I3 | HU-07 |

**Total estimado:** 39 SP.

## Riesgos principales

- Divergencia entre el certificado RUES standalone y el generado dentro del expediente.
- Aplicación incorrecta de las excepciones del artículo 5.3.2.2.
- Exposición cross-tenant de documentos o snapshots.
- Costos y límites del proveedor RUES durante lotes.
- Ítems atascados, duplicados o archivos huérfanos durante procesamiento asíncrono.

## Preguntas no bloqueantes

- Retención definitiva de PDF y snapshots.
- Nombre final del permiso granular de lectura y generación.
- Intervalo de polling dentro del rango operativo de 3 a 5 segundos.
- Política de reintentos y concurrencia del worker.
- Inclusión futura de manifiesto de resultados dentro del ZIP.

## DoR local

El alcance, criterios, dependencias, estimación y anexo normativo están definidos. Antes de pasar a
`Active` deben completarse en ADO:

- Sprint siguiente al activo.
- Area Path.
- Tag `DOR`.
- AssignedTo humano.

**Veredicto:** `MISSING_4`. Listo para redactar el ADR en estado `Propuesto` y registrar/refinar las HUs;
no está autorizado para implementación ni transición de estado.

---

*Borrador local refinado — Proyecto FLIT - EVOLUTION.*

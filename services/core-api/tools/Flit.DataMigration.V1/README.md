# Flit.DataMigration.V1 — migrador de trámites V1 → V2

Consola que migra trámites desde **Flit V1** (NestJS/Postgres) hacia **Flit V2** (este core-api).
Dos instancias: **1 = data plana** (`--tipo transfer`) y **2 = adjuntos/binarios**
(`--tipo transfer-attachments`).

> **Este programa NO decide qué se migra.** Recibe una lista de ids de V1 y la ejecuta tal cual,
> sin filtrar por estado ni por compañía. La selección es una decisión de negocio que vive fuera.

## Uso

```bash
cd tools/Flit.DataMigration.V1

# Simular (no escribe nada: hace rollback al final)
dotnet run -- --tipo transfer --ids 8617 --dry-run

# Migrar de verdad
dotnet run -- --tipo transfer --ids 8617

# Lote desde archivo, con tope
dotnet run -- --tipo transfer --ids-file lista.txt --limit 50

# Re-migrar algo ya migrado (borra y vuelve a crear)
dotnet run -- --tipo transfer --ids 8617 --force

# Instancia 2: adjuntos (el trámite ya debe estar migrado con --tipo transfer)
dotnet run -- --tipo transfer-attachments --ids 8617 --dry-run
dotnet run -- --tipo transfer-attachments --ids 8617

# Instancia 3: documentos que V1 genera en caliente y nunca guarda
dotnet run -- --tipo transfer-documents --ids 8617 --dry-run
dotnet run -- --tipo transfer-documents --ids 8617
```

Las tres instancias van en ese orden: primero la data plana, luego los adjuntos, y al final los
documentos generados.

| Opción | Qué hace |
|---|---|
| `--tipo` | `transfer` (data plana), `transfer-attachments` (adjuntos cargados) o `transfer-documents` (documentos generados por V1). |
| `--ids` | Ids separados por coma o espacio. |
| `--ids-file` | Archivo con los ids (uno por línea, `#` comenta). |
| `--limit` | Procesa como máximo *n* de la lista. |
| `--dry-run` | Simula todo y revierte. **Siempre correrlo antes.** |
| `--force` | Re-migra aunque ya esté en `migration_map`. |
| `--conservar-jpg-identidad` | Solo `transfer-attachments`. Migra también las imágenes sueltas de la validación de identidad (ver *Qué no se migra*). |

## Laboratorio local

Para probar contra una copia de producción de V1 sin tocar ningún ambiente real. Todo corre en
local y **las tres piezas tienen que apuntar a la misma copia**: los ids de V1 se repiten entre
ambientes, y un snapshot de otro ambiente devolvería documentos ajenos sin dar error.

1. **Bases.** La copia de V1 (p. ej. `pdn_copy_updated`) y una V2 de laboratorio clonada de dev.
   Una base con solo las migraciones no sirve, porque le falta el catálogo `catalogs.transit_offices`:
   ```bash
   createdb flit_migrador_lab
   pg_dump -Fc flit2dev_backup | pg_restore --no-owner -d flit_migrador_lab
   dotnet ef database update --project src/Flit.Infrastructure --startup-project src/Flit.Api \
     --connection "Host=localhost;Database=flit_migrador_lab;Username=…"
   ```
2. **Ids a migrar.** `lab/entregados.sql` lista los entregados:
   ```bash
   psql -d pdn_copy_updated -At -v tipo=transfer -f lab/entregados.sql > ids-transfer.txt
   ```
3. **Servicio de PDF de V1** (`BackSrvPdfService`), solo para la instancia 3. Sin él, V1 no puede
   armar el FUR, la portada, las cartas selfie ni el mandato; en el clúster lo resuelve como
   `back-svc-pdfservice-grpc-pdn`, un nombre que desde local no existe.
   ```bash
   DOTNET_ROLL_FORWARD=Major \
   PdfService__ChromiumPath="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" \
   dotnet run                                   # escucha en localhost:4601
   ```
4. **V1** (`BackCrudTransfer`, rama `release`), solo para la instancia 3. En una rama local, su
   `.env` debe apuntar `DB_*` y `TRANSFER_DB_*` a la copia, con `*_SYNCHRONIZE=false`, y
   `PDF_SERVICE_URL="dns:///localhost:4601"`. Ese `.env` **no se commitea**. Se levanta con
   `npm run dev:swagger` en el puerto 3201.
5. **Migrar**, siempre con `--dry-run` primero:
   ```bash
   export FLITMIG_ConnectionStrings__V1Source="Host=localhost;Database=pdn_copy_updated;Username=…"
   export FLITMIG_ConnectionStrings__V2Target="Host=localhost;Database=flit_migrador_lab;Username=…"
   export FLITMIG_Migration__CreateTenantIfMissing=true   # solo en el laboratorio
   export FLITMIG_V1Snapshot__BaseUrl="http://localhost:3201/"
   dotnet run -- --tipo transfer --ids-file ids-transfer.txt --dry-run
   ```
   Para usar la UI de `/admin/migracion`, levantar `Flit.DataMigration.Api` con las mismas
   variables más `FLITMIG_MigracionApi__Enabled=true` y `FLITMIG_MigracionApi__ApiKey`, y el
   frontend con `MIGRACION_API_URL` y `MIGRACION_API_KEY`.

Para que el organismo vea un migrado en su bandeja, la empresa necesita un grant vigente con ese
organismo (`admin.tenant_transit_office_grants`).

## Configuración

`appsettings.json` trae las conexiones del laboratorio local. En cualquier entorno real deben
venir por variable de entorno y **nunca** versionarse con credenciales:

```bash
export FLITMIG_ConnectionStrings__V1Source="Host=…;Database=…"
export FLITMIG_ConnectionStrings__V2Target="Host=…;Database=…"
export FLITMIG_Migration__CreateTenantIfMissing=false   # obligatorio en producción

# Solo para --tipo transfer-documents: API de traspasos de V1 y file-manager de V2
export FLITMIG_V1Snapshot__BaseUrl="https://…/"
export FLITMIG_V1Snapshot__AuthToken="…"   # opcional: el endpoint de snapshot no pide autenticación desde el 2026-07-29
export FLITMIG_TargetFileManager__BaseUrl="https://…/"
```

### Por qué existe `transfer-documents`

V1 arma su expediente **en caliente**: cada vez que alguien lo pide lo genera y lo descarta. Lo
único que persiste es el consolidado, y ni siquiera siempre — al devolver un trámite a borrador o
al rechazarlo, V1 borra los ids de PDF. En producción, 1.682 de los 20.148 traspasos aprobados no
tienen consolidado guardado, y 413 de los 2.103 borradores tampoco.

Eso significa que la portada, el FUR, la compraventa del sistema, las cartas selfie, el mandato, el
trámite virtual, la limitación de propiedad, la carta declaratoria y la autorización al apoderado
**no existen como archivo en ninguna parte**. V2 no tiene generadores para casi ninguno, así que un
trámite migrado los perdería para siempre el día que V1 se apague.

Esta instancia se los pide a V1 con `GET /vehicle-transfer-migration/:id/snapshot`, que los
construye y los devuelve en la respuesta **sin escribir nada en V1**, y los persiste en V2. Para un
borrador el resultado es exactamente lo que el usuario vería si abriera el trámite en V1 ese mismo
día, que es lo que hace su visor: siempre regenera.

Ajustes de alcance (opcionales):

| Config | Por defecto | Qué hace |
|---|---|---|
| `V1Snapshot:Include` | `generated` | Solo lo que V1 no persiste. `all` suma los adjuntos ya cargados, para auditoría. |
| `V1Snapshot:Consolidated` | `auto` | Arma el consolidado solo si V1 no tiene uno guardado (pesa 9-12 MB). `always` / `never`. |

Lo que V1 no logre construir se reporta en el resultado con su motivo: **ninguna pieza se descarta
en silencio**. Las improntas no se firman, porque firmarlas exigiría escribir en V1: se entrega el
PDF ya firmado si existe y, si no, el original marcado como degradado.

### Qué no se migra, y por qué

Dos cosas se omiten a propósito. Las dos se reportan por trámite; ninguna desaparece en silencio.

**1. Piezas que V1 no genera, sino que descarga.** Las improntas y el certificado de vigencia son
"generados" solo cuando el trámite no trae ya el archivo. Si lo trae, V1 lo descarga y a lo sumo le
estampa un rótulo — el mismo binario que ya copió `transfer-attachments`. El snapshot reporta el
`sourceFileId` que resolvió en tiempo de ejecución y esta instancia lo coteja contra
`migration_attachment_map`: si ya está, no lo vuelve a guardar.

De ahí que **`sourceFileId = null` signifique exactamente "esto se pierde el día que V1 se apague"**.

**2. Las imágenes sueltas de la validación de identidad.** `frontalCard.jpg`, `backCard.jpg` y
`userSelfie.jpg` van las tres al tipo `cedulas` y las tres quedan embebidas en la carta selfie, junto
con el nombre, el documento, el hash de la transacción y la firma. En producción son ~70.000
archivos que repiten lo que ya dice un PDF del mismo expediente.

La decisión es **por parte** y usa la misma condición que V1 para construir la carta
(`validation_identity = true` y selfie presente). Si una parte no la cumple, V1 tampoco produce la
carta y las imágenes son la única evidencia que existe: se migran. Son 375 trámites en producción.

Nunca se omiten `id_attached_buyer_id` / `id_attached_seller_id`: esos son PDF que el usuario cargó
a mano y la carta no los contiene.

> **Ojo con el orden.** `transfer-attachments` descarta esas imágenes *prediciendo* que
> `transfer-documents` traerá la carta. Si la segunda instancia no se corre, esas imágenes no llegan
> a V2 por ninguna vía. `transfer-documents` verifica la predicción y avisa por trámite y por parte
> cuando la carta no llegó; se recuperan corriendo `transfer-attachments
> --conservar-jpg-identidad` (no hace falta `--force`: solo añade lo que falta).

## Cómo funciona

```
V1 (copia)  ──leer──►  traducir  ──►  escribir  ──►  V2
                          ▲                │
              diccionarios│                └──► migration_map (la libreta)
```

1. **Leer** — `PostgresV1SourceReader` lee de una **copia** de V1. El origen nunca se modifica.
   Normaliza los `''` de V1 a `null` (V1 no usa NULL para "sin dato").
2. **Traducir** — `TransferMapper` produce el grafo de entidades de V2. Es una función pura.
3. **Escribir** — `ProcedureInstanceLoader`, respetando la secuencia obligatoria (abajo).
4. **Anotar** — `migration_map` registra qué id de V1 quedó como qué uuid de V2.

### La secuencia obligatoria

`tr_procedure_instance_field_values_immutable` solo permite escribir `field_values` cuando el
trámite padre está en `borrador`. Como los históricos llegan en estados finales, el orden es:

```
1. INSERT procedure_instance con status = 'borrador'
2. INSERT actors
3. INSERT field_values          ← el trigger lo permite porque el padre sigue en borrador
4. UPDATE status = <estado real>
5. INSERT status_history
6. INSERT migration_map          ← todo en la MISMA transacción
```

Invertir este orden falla con `check_violation`.

## Garantías

- **Idempotente.** Los uuid de V2 son determinísticos (UUID v5 derivado de `tabla:id` de V1), y
  `migration_map` evita reprocesar. Correr dos veces no duplica.
- **Transaccional por trámite.** Un trámite malo va a cuarentena; el resto del lote sigue.
- **Cero pérdida.** Toda columna de V1 con dato que no tenga `field_key` destino se conserva en
  `legacy_v1_extras` (jsonb). El estado original queda en `legacy_process_status`.
- **No adivina.** Si un NIT resuelve a dos tenants distintos, el trámite va a cuarentena en vez
  de asignarse al azar.

## Decisiones que hay que conocer

| Tema | Decisión |
|---|---|
| Estado final | Manda el **master** de V1, no el último evento del historial (divergen en ~23%). |
| Estados 4 (Sent) y 8 (Archived) | No existen en V2 → se colapsan al más cercano **y** se avisa. Pendiente de negocio. |
| Adjuntos (instancia 2) | Copia origen→destino con **dos file-managers configurables** (`Source`/`TargetFileManager`). `Mode=Copy` descarga del origen y sube al destino (stores distintos, p. ej. AWS→MinIO); `Mode=Reference` no mueve el binario y usa el id de V1 como `storage_path` (mismo store). Escribe `procedure_instance_attachments` con `source='migration'` y `sha256` real. Ver `Mapping/AttachmentColumnMap.cs` (columna→`tipo`) y `migration_attachment_map` (libreta). La referencia jsonb `legacy_attachments` se conserva como respaldo. |
| `reference_number` | El migrador **no** lo fija: lo compone el trigger `tr_procedure_instances_radicado` de V2 con el prefijo de familia y el consecutivo global (`FT1-0000123`), igual que a un trámite nativo. La trazabilidad a V1 vive en `migration.migration_map` y en `is_migrated`. Un migrado no se distingue por el radicado. |
| Organismo de tránsito | `traffic_secretary_code` se cruza con `catalogs.transit_offices` y fija `TransitOfficeId` y el field_value `transit_office_id`, como el flujo nativo. Sin cruce queda el texto de V1 y un aviso: el trámite no llega a ninguna bandeja de organismo. |
| Copropietarios | Hasta 4 actores por rol con ordinal y `ownership_percentage` (ADR-0053). El titular se escribe de último para que `comprador_nombre` del listado sea el suyo. |
| Revocado | El estado 9 de matrícula va a `revocado` (HU #12165). |
| Tipo de documento | V1 usa la convención RUNT de una letra (`C`, `N`, `P`, `T`); se traduce a `CC`, `NIT`, `PAS`, `TI`. |
| Usuario | Los registros se atribuyen a un usuario de sistema (`migracion.v1@flitsas.io`), no a una persona. |

### Configuración de adjuntos (instancia 2)

`Attachments:Mode` (`Copy`|`Reference`) + `SourceFileManager` / `TargetFileManager`
(`BaseUrl`, `FilesPath`, `AuthToken`). Las URL cambian por ambiente (dev/qa/pdn); van en
`appsettings.Local.json` o por env `FLITMIG_SourceFileManager__BaseUrl`, etc. El file-manager de
V2 = el de producción de V1 (mismo bucket S3), y a futuro V2 usa MinIO — por eso el modo es
configurable. El round-trip (leer del origen → sha256 → subir al destino → verificar sha256) se
validó de punta a punta contra AWS pdn → MinIO dev.

## Estado actual (2026-09-28)

**Alcance vigente:** solo los trámites en estado **Entregado** de V1 (traspaso 5, matrícula 6). Es
la Epic #13046 de FLIT - EVOLUTION; en la copia `pdn_copy_updated` son 215. El migrador no filtra
por estado: la lista de ids la arma una consulta aparte.

En julio las tres instancias quedaron probadas para los dos trámites contra `pdn_copy_updated` y
verificadas en la UI de V2. El 14 de septiembre se adaptó el mapeo a la estructura de V2 de ese
momento (organismo de tránsito, copropietarios, revocado, cabeza de red). Desde entonces V2 cambió
el radicado, los estados de preasignación y asignación, y la ruta por RUNT; la corrida sobre los
entregados es la que confirma que el migrador sigue al día.

Otros servicios siguen bloqueados: V2 no publica esos tipos de trámite.

### Pruebas

Las pruebas del cargador (`Loading/ProcedureInstanceLoaderDbTests`) abren una conexión de verdad,
porque lo que rompe este migrador vive en la base: triggers, FK y CHECK. En CI corren contra el
Postgres del workflow `core-api.yml`, con todas las migraciones aplicadas. Sin cadena de conexión
se saltan.

Para correrlas en local contra una base limpia:

```bash
createdb flit_migrador_ci
export ConnectionStrings__Core="Host=localhost;Database=flit_migrador_ci;Username=…"
dotnet ef database update --project src/Flit.Infrastructure --startup-project src/Flit.Api
dotnet test tests/Flit.DataMigration.Tests
```

### Cuidados en V2 con un trámite migrado

El expediente de V1 llega como adjunto tipo `consolidado` (`source = migration`) y es el que vale
para el organismo. Lo que V2 hace después con él:

- **El OT no lo toca.** Abrir, regenerar y aprobar trabajan con `consolidado_maestro`, que excluye
  al de V1. El maestro lleva la portada de V2 y además las piezas migradas.
- **Se reemplaza en cuanto el trámite vuelve a gestionarse en V2.** La protección
  `migrado_solo_lectura` de `GenerarConsolidadoHandler` solo cubre los estados finales. Estos
  caminos regeneran el consolidado del gestor, y con él pueden regenerarse el FUR y el mandato:
  - el visor o el botón regenerar del gestor, cuando subsana un rechazo;
  - «Limpiar» o «Cargar consolidado» del admin.
  El rechazo del OT por sí solo no lo reemplaza: la regeneración anticipada que dispara omite a
  los migrados (`RegenerarConsolidadoAnticipadoHandler`, `OmitidoMigrado`). Al aprobar, V2 intenta
  regenerar, el guard lo frena y queda un evento `regeneracion_documental_fallida` con
  `migrado_solo_lectura`. Es ruido en la bitácora, no un error.
- **Es intencional.** Un migrado rechazado se subsana en V2 y desde ahí es un trámite de V2, así
  que su expediente pasa a ser el nuevo. Si hace falta el de V1 después de eso, su trazabilidad
  sigue en `migration.migration_attachment_map`.
- **Prenda y transformación se traducen al modelo de V2** (HU #13072), así que el listado, los
  filtros y Consultas las marcan igual que en un nativo:

  | V1 | V2 |
  |---|---|
  | `registered_pledge` («Inscripción de prenda a favor de…») | prenda `registrar`, acreedor `pledge_in_favour` |
  | `has_garment_lifting` | prenda `levantar`, acreedor `warranty_creditor_*` |
  | Acreedor del RUNT sin inscripción ni levantamiento | prenda `omitir`. **Sin** marca, como un nativo |
  | `switch_vehicle_color`, `switch_vehicle_bodywork`, `switch_vehicle_fuel_type` | `cambio_*` = `true`. El dato de V1 pasa a `vehicle_*_runt` y `new_vehicle_*` queda como efectivo |
  | `is_armored_vehicle` | `blindaje` = `true` |

  `is_dismantling_armor` (desmonte de blindaje) no tiene equivalente en V2 y se queda en
  `legacy_v1_extras`, igual que todas las columnas originales.

### Lo que falta antes de producción

1. **`CreateTenantIfMissing` debe ser `false`.** En laboratorio crea tenants; en producción un NIT
   sin tenant es cuarentena, no algo que se invente solo.
2. **Cruzar los NIT reales de V1 contra los tenants de la V2 de producción.** Los del backup son
   MOCK. `tenant_id` es NOT NULL **sin FK**: un NIT mal resuelto mete los trámites de una empresa
   dentro de otra y RLS los esconde de su dueño.
3. **El V1 que ejecute la instancia 3 tiene que coincidir con el esquema de su base.** La rama
   `develop` de V1 declara columnas que producción no tiene (MFA, liveness) y TypeORM las mete en el
   SELECT. En laboratorio, V1 corre en local contra la copia con un `.env` propio.
4. **Un entregado migrado tiene que poder decidirse en V2**: aparecer en la bandeja de su organismo
   y aprobarse o rechazarse. Lo verifica la Feature #13048.
5. **Reporte de reconciliación exportable** — hoy solo va a consola.

Diseño completo y contexto de negocio: `repos/migration-flit-v1-to-v2/`.

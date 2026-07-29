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

## Configuración

`appsettings.json` trae las conexiones del laboratorio local. En cualquier entorno real deben
venir por variable de entorno y **nunca** versionarse con credenciales:

```bash
export FLITMIG_ConnectionStrings__V1Source="Host=…;Database=…"
export FLITMIG_ConnectionStrings__V2Target="Host=…;Database=…"
export FLITMIG_Migration__CreateTenantIfMissing=false   # obligatorio en producción

# Solo para --tipo transfer-documents: API de traspasos de V1 y file-manager de V2
export FLITMIG_V1Snapshot__BaseUrl="https://…/"
export FLITMIG_V1Snapshot__AuthToken="…"
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
| `reference_number` | `MIG-TR-{id de V1}` — nunca colisiona con el consecutivo `TRM-{año}-{n}` de la app. |
| Tipo de documento | V1 usa la convención RUNT de una letra (`C`, `N`, `P`, `T`); se traduce a `CC`, `NIT`, `PAS`, `TI`. |
| Usuario | Los registros se atribuyen a un usuario de sistema (`migracion.v1@flitsas.io`), no a una persona. |

### Configuración de adjuntos (instancia 2)

`Attachments:Mode` (`Copy`|`Reference`) + `SourceFileManager` / `TargetFileManager`
(`BaseUrl`, `FilesPath`, `AuthToken`). Las URL cambian por ambiente (dev/qa/pdn); van en
`appsettings.Local.json` o por env `FLITMIG_SourceFileManager__BaseUrl`, etc. El file-manager de
V2 = el de producción de V1 (mismo bucket S3), y a futuro V2 usa MinIO — por eso el modo es
configurable. El round-trip (leer del origen → sha256 → subir al destino → verificar sha256) se
validó de punta a punta contra AWS pdn → MinIO dev.

## Estado actual (2026-07-28)

Las **tres instancias** funcionan para los **dos trámites**, probadas contra datos reales de
producción (`pdn_copy_updated`) y verificadas en la UI de V2:

| | data plana | adjuntos | documentos generados |
|---|---|---|---|
| **Traspaso** | ✅ | ✅ | ✅ |
| **Matrícula inicial** | ✅ | ✅ | ✅ |

- Integridad comprobada bajando los binarios de MinIO y recalculando el sha256.
- Re-ejecución (`--force`) reparada en las tres instancias y verificada.
- ⛔ Otros servicios — bloqueado: V2 no publica esos tipos de trámite.

### Lo que falta antes de producción

1. **`CreateTenantIfMissing` debe ser `false`.** En laboratorio crea tenants; en producción un NIT
   sin tenant es cuarentena, no algo que se invente solo.
2. **Cruzar los NIT reales de V1 contra los tenants de la V2 de producción.** Los del backup son
   MOCK. `tenant_id` es NOT NULL **sin FK**: un NIT mal resuelto mete los trámites de una empresa
   dentro de otra y RLS los esconde de su dueño.
3. **El V1 que ejecute la instancia 3 tiene que coincidir con el esquema de producción.** La rama
   `develop` declara columnas que producción no tiene (MFA, liveness) y TypeORM las mete en el
   SELECT: contra pdn real, ese V1 no puede leer las tablas afectadas.
4. **Multipropietario** (321 matrículas en pdn): hoy solo se migra el titular y se avisa. Falta la
   decisión de producto sobre cómo representar a los copropietarios en V2.
5. **Los trámites migrados no salen en el listado por defecto** de V2: carga los 200 más recientes y
   filtra en cliente. Todo lo migrado es histórico, así que solo se alcanza por URL directa.
6. **Reporte de reconciliación exportable** — hoy solo va a consola.

Diseño completo y contexto de negocio: `repos/migration-flit-v1-to-v2/`.

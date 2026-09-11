# ADR-0057: Almacenamiento de archivos por ambiente (MinIO / Contabo / S3) y traducción del flujo de subida

**Fecha**: 2026-09-10
**Status**: Aceptado (2026-09-10)
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana — regla FLIT 15), equipo core-api / core-ict, infraestructura
**Tags**: arquitectura, infraestructura, almacenamiento, s3, minio, contabo, file-manager, vps, gitops
**Relacionado**: repositorio `flit-file-manager-minio`; `flit-gitops` (clúster k3s de la 169); ADR-0022 (lifecycle service único)
**Alcance**: `flit-file-manager-minio`, los manifiestos de `flit-gitops` y `FileManagerAttachmentStorage` en **core-api y core-ict** — ver D1.

---

## Contexto

`flit-file-manager-minio` es el servicio Go que gestiona los adjuntos de FLIT 2.0. Hoy corre en la
VPS **177** en tres ambientes (dev/qa/pdn) por docker compose, y en los tres guarda los bytes en un
**contenedor MinIO** montado junto al servicio, sobre un volumen de la propia VPS.

Se contrató un **Object Storage de Contabo** (250 GB, región EU, endpoint `eu2.contabostorage.com`,
bucket `flit-pdn-bucket-eu2`) con el objetivo de sacar los archivos del disco del servidor. En
paralelo, PDN se desplegará en la **VPS 169**, el clúster k3s gobernado por `flit-gitops` y Argo CD.

Conviene deshacer una ambigüedad de nombres que ha causado confusión: en este proyecto "MinIO"
son **dos cosas distintas**.

| | Qué es | Qué pasa con Contabo |
|---|---|---|
| **Servidor MinIO** | El contenedor que guarda los bytes en un volumen de la VPS | **Lo reemplaza Contabo** |
| **`minio-go`** | La librería cliente del servicio Go. Es un **SDK de S3 genérico** | **Se queda**: ya sabe hablar con MinIO, AWS S3 y Contabo |

Por eso apuntar el servicio a Contabo no exigió reescribir la capa de almacenamiento.

### El hallazgo que condiciona todo (verificado contra el servicio real, 2026-09-10)

Contabo es **Ceph RGW detrás de un gateway Kong**, y ese gateway rechaza toda petición cuya firma
no viaje ni en la cabecera `Authorization` ni en la query string. Se probaron las cuatro formas:

| Forma de firmar | Resultado |
|---|---|
| PUT con cabecera `Authorization` (SDK normal) | OK |
| GET presignado (firma en query) | HTTP 200 |
| PUT presignado (firma en query) | HTTP 200 |
| **POST policy** (credenciales dentro del cuerpo multipart) | **HTTP 401** |
| Anónimo sin firma (control) | HTTP 401 |

El 401 del POST policy devuelve **`{"message":"Unauthorized"}`** — JSON, no el XML de S3: lo genera
Kong, no el object storage. Regla práctica: **si el cuerpo de un error de storage es JSON y no XML,
no lo produjo S3**.

Esto importa porque el flujo de subida del servicio (`PresignedPostPolicy` en
`internal/storage/minio.go`, heredado del S3 legado) es exactamente el que Contabo rechaza.
**MinIO y AWS S3 sí aceptan POST policy**; Contabo es la excepción de los tres, así que el método
de subida no puede ser una decisión global del servicio.

Dos incógnitas quedaron descartadas en la misma prueba: `EnsureBucket` arranca sin problema (el
bucket se crea a mano y `MakeBucket` no llega a ejecutarse) y **no hace falta configurar CORS**,
porque quien sube es core-api desde el servidor, no el navegador.

---

## D1 — El servicio declara el método; el cliente lo obedece. Las subidas siguen siendo directas

**Decisión**: el file-manager devuelve siempre la URL **del storage real** y declara en la respuesta
con qué método hay que subir. El cliente (`FileManagerAttachmentStorage` en core-api y core-ict)
lee ese campo y hace `POST multipart` o `PUT` de bytes crudos según corresponda. **Los bytes nunca
pasan por el servicio**, en ningún ambiente.

```
DEV/QA (MinIO)   core-api ──POST multipart──> MinIO
PDN  (Contabo)   core-api ──PUT bytes crudos──> Contabo
                      └── el método lo dice la respuesta, no lo adivina el cliente
```

El contrato se extiende de forma **retrocompatible**:

```jsonc
"presignedUrl": {
  "url": "https://eu2.contabostorage.com/flit-pdn-bucket-eu2/pruebas/2026/09/10/<id>_doc.pdf",
  "method": "PUT",        // nuevo. "POST" si se omite.
  "fields": {}            // solo en POST; vacío o ausente en PUT
}
```

Regla del cliente: **si `method` es `PUT`, subir el binario crudo; en cualquier otro caso, POST
multipart con los `fields` primero y `file` al final** (comportamiento actual). Un cliente antiguo
contra un backend POST sigue funcionando sin cambios, porque `method` ausente significa POST.

El método lo decide el **proveedor**, no el ambiente: `minio` y `s3` → POST policy; `contabo` → PUT
presignado, porque su gateway rechaza el POST policy (ver Contexto).

Las **descargas** no cambian en ningún caso: GET presignado directo contra el storage.

---

## D2 — Selección de backend por ambiente

**Decisión**: el backend se elige por configuración, con un `STORAGE_PROVIDER` que fija los
defaults sensatos de cada proveedor. Las variables `MINIO_*` pasan a `STORAGE_*` (el prefijo actual
miente en cuanto apunta a algo que no es MinIO), manteniendo los nombres viejos como alias durante
una release para no romper los despliegues existentes.

| `STORAGE_PROVIDER` | Endpoint | Método de subida | SSL |
|---|---|---|---|
| `minio` | `minio:9000` | `POST` (policy) | no |
| `contabo` | `eu2.contabostorage.com` | **`PUT`** (presignado) | sí |
| `s3` | `s3.<región>.amazonaws.com` | `POST` (policy) | sí |

Asignación acordada:

| Ambiente | Rama | Backend | Método |
|---|---|---|---|
| dev | `develop` | MinIO local (177) | POST |
| qa | `staging` | MinIO local (177) | POST |
| pdn | `release` | **Contabo** (169) | **PUT** |

---

## D3 — El bucket se lee de la fila, no de la configuración

**Decisión**: `Stat`, `Remove` y la firma de descarga pasan a usar el `bucket` almacenado en la fila
de `files`, no el de la configuración.

Hoy la tabla guarda la columna `bucket` pero **el código nunca la lee**: `internal/storage/minio.go`
usa siempre `s.bucket`. Con un único backend por ambiente da igual; en cuanto convivan dos, el
servicio no sabría de dónde vino cada archivo y los registros antiguos apuntarían al sitio
equivocado. Se hace ahora porque más adelante implicaría migrar datos.

La escritura sigue usando el bucket de la configuración (es donde van los archivos nuevos); la
lectura respeta el histórico. Esto además convierte un futuro cambio de backend en una operación
sin migración: los archivos viejos se siguen sirviendo desde donde estén.

---

## D4 — Dominios de PDN: uno solo

**Decisión**: PDN expone **un único host**, `filemanager.flitsas.com`, apuntando a la 169.

Los otros dos subdominios que existen en dev/qa desaparecen con Contabo, porque desaparecen sus
motivos:

| Subdominio (dev) | Para qué existe | ¿En PDN? |
|---|---|---|
| `devfilemanager.flitsas.online` | La API del servicio | **Sí, el único** |
| `devminio.flitsas.online` | Host público contra el que se firman las presignadas | No: ahora se firman contra `eu2.contabostorage.com` |
| `devminioconsole.flitsas.online` | Consola de administración del contenedor MinIO | No: Contabo trae su propio panel |

**Riesgo asumido**: en el clúster 169 la convención es `*.vps.flitsas.com` y
`filemanager.vps.flitsas.com` **ya está ocupado por el FileManager legado** (Node + AWS S3 +
DynamoDB + Cognito). Se acepta salir de la convención para que el nombre nuevo se lea como el
sucesor y no como una variante del legado; a cambio conviven dos nombres parecidos hasta que el
legado se apague, lo que exige que la documentación de operación los distinga con claridad.

---

## Alternativas consideradas

**Que el file-manager traduzca POST→PUT internamente**, devolviendo una URL hacia sí mismo, para
que el contrato no cambiara en absoluto. Se descarta: obliga a que **todos los bytes de PDN pasen
por el pod**, lo que reintroduce por la puerta de atrás el transporte que el diseño presignado
evita — ancho de banda doble, límites de cuerpo en el ingress, streaming cuidadoso y un punto de
fallo nuevo. El campo `method` cuesta unas pocas líneas en dos clientes y conserva la propiedad
más valiosa del diseño: **el servicio mueve JSON, no archivos**.

**Traducir en un proxy externo** (nginx con Lua, o un sidecar). Mismo coste de transporte que la
anterior, una pieza más que operar y un sitio más donde se pierden las trazas.

**Doble escritura en la aplicación** (productivo + réplica simultáneos). Incompatible con las URLs
presignadas: el servicio no tiene los bytes que duplicar, así que exigiría que **todos** los
adjuntos pasaran por el pod en todos los ambientes. La réplica se resolverá por otra vía, fuera del
servicio (`rclone`/`mc mirror` como job, o replicación nativa: Contabo soporta versioning, que es
el requisito). Queda **fuera del alcance de este ADR**.

**Dejar PDN con MinIO propio en la 169.** No cumple el objetivo de sacar los archivos del disco del
servidor, y en k3s obligaría a resolver almacenamiento persistente para el volumen.

---

## Tradeoff aceptado

**Los consumidores del file-manager quedan acoplados a un detalle del transporte**: tienen que
saber hacer POST multipart y PUT crudo, y elegir según lo que diga el servicio. Se acepta a cambio
de conservar las subidas directas — el servicio sigue moviendo JSON y no archivos, que es la
propiedad que sostiene el dimensionamiento del pod y del ingress.

El acoplamiento se acota con dos reglas: **el cliente nunca deduce el método** (lo lee de `method`,
jamás del proveedor ni del ambiente, que no conoce), y **`method` ausente significa POST**, de modo
que un cliente sin actualizar sigue funcionando contra los backends que ya usaban POST.

**Validación directa en PDN.** Por estar en fase de implementación y sin datos productivos en
juego, se acepta probar el camino PUT/Contabo directamente en PDN, sin ensayo previo en QA. La
contrapartida es que **dev y qa no ejercitan nunca el camino de PUT**: mientras PDN sea el único
ambiente con Contabo, un fallo en esa rama del código solo aparece allí. Cuando PDN deje de ser un
ambiente en implementación, esto deberá revisarse.

---

## Consecuencias

- El compose de PDN pierde el servicio `minio`, su volumen y los dos subdominios con sus vhosts y
  certificados; PDN queda como un contenedor Go contra Postgres y Contabo.
- Dev y QA no cambian de comportamiento: siguen en MinIO y POST policy.
- **core-api y core-ict se despliegan antes o a la vez** que el file-manager de PDN: un cliente sin
  el soporte de `method` que reciba `PUT` fallará la subida. El orden seguro es cliente primero
  (es retrocompatible y no rompe nada mientras todos los backends sigan en POST), servicio después.
- Los archivos ya existentes en el MinIO de PDN de la 177 **no se migran**: el PDN de la 169 es un
  ambiente nuevo en paralelo. D3 deja la puerta abierta a servir ambos orígenes si en el futuro se
  decide unificarlos.
- Las credenciales S3 de Contabo son **de la cuenta entera**, no por bucket: quien las tenga alcanza
  todos los buckets del almacenamiento. Van en un SealedSecret y conviene rotarlas si se sospecha
  exposición (el panel las regenera en Account → Security & Access).
- Los 250 GB son una cuota compartida por todo lo que se guarde en ese Object Storage.

---

## Checklist obligatorio (cambiar el backend de un ambiente)

1. Bucket creado en el proveedor y credenciales selladas (nunca en el ConfigMap).
2. `STORAGE_PROVIDER` coherente con lo que el proveedor acepta (¿admite POST policy?).
3. Los consumidores desplegados **ya entienden `method`** antes de que el servicio empiece a
   devolver `PUT`.
4. Prueba de ciclo completo contra el proveedor real: crear, subir, descargar con
   `content-disposition`, borrar.
5. Verificar que los archivos previos del ambiente se siguen sirviendo (D3).

---

## Notas para agentes

- **No "arregles" el POST policy contra Contabo.** No es un bug de firma ni de reloj: el gateway no
  ve las credenciales porque viajan en el cuerpo. Está verificado contra el servicio real.
- **`minio-go` no implica MinIO.** Es un SDK de S3; no lo cambies por otro por el nombre.
- El validador de nombres de bucket de `minio-go` rechaza `tenant:bucket`. La URL de Contabo no
  lleva prefijo de tenant, así que el bucket va con nombre pelado.
- La cadena de conexión contra `pgbouncer.flit-apps:6432` **no admite parámetros de arranque**
  (`search_path` incluido): el esquema se califica en las consultas.

---

## Referencias externas

- Contabo — [Generar access/secret keys](https://help.contabo.com/en/support/solutions/articles/103000282843-how-do-i-generate-access-and-secret-keys-for-object-storage-)
- Contabo — [Compatibilidad S3 (Ceph)](https://help.contabo.com/en/support/solutions/articles/103000275459-is-contabo-object-storage-compatible-with-s3-storage-)
- Contabo — [Versioning](https://help.contabo.com/en/support/solutions/articles/103000282907-does-object-storage-support-versioning-)

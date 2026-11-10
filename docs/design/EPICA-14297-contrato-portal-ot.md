# Épica #14297: contrato de Marca y Precios del portal del OT

Contrato compartido entre backend y frontend de los Features #14374 (Marca) y #14387 (Precios). Los AC de cada HU en ADO mandan. Si un detalle de este contrato tiene que cambiar, el agente lo informa en su reporte final.

## Reglas comunes

- Base: `core-api`, cambios aditivos. FLIT 2 y marca blanca no cambian de comportamiento.
- Alcance de OT: patrón de `frontend/lib/api/admin-ot*.ts` y `AdminOtMetricsEndpoints`. El `ot_admin` trabaja con el organismo de su token e ignora `transitOfficeId`. El SuperAdmin debe enviar `?transitOfficeId=`.
- Autorización: rol, sin permiso nuevo. Otro organismo responde 403.
- Actualización inmediata: marca, precios y listados del portal no usan caché por tiempo en `core-api`, BFF ni front. Lo publicado se ve en la siguiente carga o consulta.
- Errores: `{ code, message }` con el motivo exacto en español.

## Marca (#14374)

Modelo: tabla por `transit_office_id`, con borrador y publicado como `admin.tenant_brandings`. DDL `180-*`, migración `20261105120000_HU14375_*` con `[DbContext]` y `[Migration]`.

### Administración

Grupo `/api/v1/admin/ot/portal-marca` (`?transitOfficeId=` para SuperAdmin):

| Método | Ruta | Cuerpo | Respuesta |
|---|---|---|---|
| GET | `` | | `PortalMarca` |
| PUT | `` | `{ platformName, colors: { primary, secondary, onPrimary }, rowVersion }` | `PortalMarca` |
| POST | `/logo` | multipart `file` | `PortalMarca` |
| DELETE | `/logo` | | `PortalMarca` |
| POST | `/banner` | multipart `file` | `PortalMarca` |
| DELETE | `/banner` | | `PortalMarca` |
| POST | `/publish` | `{ rowVersion }` | `PortalMarca` |
| POST | `/retire` | `{ rowVersion }` | `PortalMarca` |
| GET | `/imagenes/{id}` | | Imagen, también de borrador, solo con sesión |

```ts
interface PortalMarcaSnapshot {
  platformName: string | null;
  colors: { primary: string; secondary: string; onPrimary: string } | null;
  logoId: string | null;
  bannerId: string | null;
}
interface PortalMarca {
  transitOfficeId: string;
  draft: PortalMarcaSnapshot;
  published: PortalMarcaSnapshot | null;
  publishedVersion: number;
  publishedAt: string | null;
  publishedBy: { userId: string } | null;
  hasUnpublishedChanges: boolean;
  logoUrl: string | null;   // imagen del borrador
  bannerUrl: string | null; // imagen del borrador
  completeness: { isComplete: boolean; missing: string[] };
  rowVersion: number;
}
```

- Para publicar son obligatorios nombre y colores. Logo y banner son opcionales: sin ellos, marca FLIT para esa imagen.
- Contraste mínimo 4,5:1, igual que `BrandAssetValidator` y `lib/brand/contrast`, validado en el servidor.
- Logo: PNG, JPEG o WebP, hasta 512 KB. Banner: los mismos formatos, hasta 2 MB. Se valida el contenido real.
- `409` si `rowVersion` no coincide.
- Códigos: `PORTAL_MARCA_CONTRASTE`, `PORTAL_MARCA_INCOMPLETA`, `PORTAL_MARCA_ARCHIVO_INVALIDO`, `PORTAL_MARCA_CONFLICTO`.

### Entrega al portal

- RPC nuevo en `contracts/proto/flit/tramites/v1/persona_natural.proto`: `ObtenerMarcaPunto(codigo)`, junto con `ResolverCanalPunto`, y `ObtenerImagenMarcaPunto(id)`.
- BFF, con el proxy del portal delante:
  - `GET /api/v1/persona-natural/punto/marca/{codigo}`
  - `GET /api/v1/persona-natural/marca/imagenes/{id}`, solo para imágenes publicadas, con `Cache-Control: public, max-age=31536000, immutable`. Cada imagen tiene id propio, así que nunca se ve una anterior.

```ts
interface MarcaPunto {
  origen: "organismo" | "flit";
  nombre: string | null;
  colores: { primario: string; secundario: string; sobrePrimario: string } | null;
  logoUrl: string | null;
  bannerUrl: string | null;
  version: number;
}
```

Sin marca publicada, con código inválido o ante una falla, responde 200 con `origen: "flit"` y lo demás nulo. Sin datos internos ni borradores.

#### Carrusel de la columna lateral (mejora sin HU)

Hasta 6 imágenes configurables por el OT en lugar de «Servicios Adicionales»; parte de la marca (borrador → publicar, mismo alcance y mismas imágenes: `admin.transit_office_portal_brand_images`, kind `carousel`, DDL 187). El documento jsonb agrega `carousel: [{ imageId, alt (1..120), linkUrl (http/https, ≤500) | null }]`; ausente = vacío. Máx. 512 KB por imagen (PNG, JPEG o WebP, contenido real). El ámbito web no admite carrusel.

Administración (grupo `/api/v1/admin/ot/portal-marca`, devuelven `PortalMarca` completo con `carousel: [{ imageId, imageUrl, alt, linkUrl }]` del borrador):
- `POST /carrusel` multipart `file`, `alt`, `linkUrl?` (agrega al final; con 6, `409 PORTAL_MARCA_CARRUSEL_LLENO`).
- `PUT /carrusel/{imageId}` `{ alt, linkUrl }`; `PUT /carrusel/orden` `{ imageIds }` (permutación exacta, si no `PORTAL_MARCA_DATOS_INVALIDOS`); `DELETE /carrusel/{imageId}`.

Portal: proto `ObtenerMarcaPuntoResponse.carrusel = 9` (`CarruselItem { imagen_id, alt, enlace }`, solo origen `organismo`, desde el PUBLICADO) y en el JSON del BFF:

```ts
interface MarcaPunto { /* ...campos anteriores... */ carrusel: { imagenUrl: string; alt: string; enlace: string | null }[] } // [] si no hay
```

El BFF descarta ítems con id inválido, alt vacío o enlace que no sea http/https, y entrega como máximo 6.

#### Banners principales (lista de hasta 6; mejora sin HU)

El «banner principal» deja de ser UNA imagen y pasa a ser una LISTA de hasta 6 banners reordenables, cada uno con imagen, texto alternativo obligatorio y enlace opcional. Solo el portal del organismo (origen `organismo`); la web de persona/FLIT no cambia. Misma mecánica que el carrusel (borrador → publicar, snapshot jsonb, sin `rowVersion` en estas rutas), DDL 188 / migración `20261109120000_PortalMarcaBanners`.

- Imágenes: se reutiliza el kind `banner` y sus límites (hasta 2 MB; PNG, JPEG o WebP, contenido real). El CHECK de kind no cambia. No hay validación de dimensiones ni de relación de aspecto (el banner actual tampoco la tenía).
- Documento jsonb: `banners: [{ imageId, alt (1..120), linkUrl (http/https, ≤500) | null }]`; ausente = vacío. El cambio de `banners` cambia el snapshot y por lo tanto `hasUnpublishedChanges`; publicar sube `publishedVersion`.

Administración (grupo `/api/v1/admin/ot/portal-marca`, mismos permisos/tenant que el carrusel; devuelven `PortalMarca` completo con `banners: [{ imageId, imageUrl, alt, linkUrl }]` del borrador, y `draft.banners` / `published.banners` en los snapshots):
- `POST /banners` multipart `file`, `alt` (obligatorio), `linkUrl?` (agrega al final; con 6, `409 BANNERS_LLENO`).
- `PUT /banners/{imageId}` `{ alt, linkUrl }`; `PUT /banners/orden` `{ imageIds }` (permutación exacta, si no `400 PORTAL_MARCA_DATOS_INVALIDOS`); `DELETE /banners/{imageId}`.
- Errores: `PORTAL_MARCA_ARCHIVO_INVALIDO` / `PORTAL_MARCA_DATOS_INVALIDOS` (400), `BANNERS_LLENO` (409), `PORTAL_MARCA_NO_ENCONTRADA` (404), `PORTAL_MARCA_PROHIBIDO` (403). El ámbito web responde `PORTAL_MARCA_DATOS_INVALIDOS`.

**Compatibilidad con el banner único.** Se conservan `bannerId` / `bannerUrl` y los endpoints `POST` / `DELETE /banner` sin cambiar su contrato, pero ahora operan sobre `banners[0]`:
- `bannerId` / `bannerUrl` = el PRIMER banner de la lista (null / vacío si no hay).
- `POST /banner` reemplaza la imagen de `banners[0]` conservando su texto alternativo y enlace; si no hay banners, crea el primero con texto «Banner del organismo» y sin enlace.
- `DELETE /banner` quita `banners[0]` (el siguiente pasa a ser el primero); sin banners no hace nada.
- La marca web (ámbito sin organismo) sigue con su `bannerId` propio; su lectura nunca entrega banners al portal.

**Migración del banner existente** (DDL 188, idempotente): en cada organismo, el `bannerId` del borrador y del publicado pasa a `banners[0] = { imageId, alt: "Banner del organismo", linkUrl: null }` y se retira la clave `bannerId` del jsonb. Si el documento ya tiene `banners`, solo se retira un `bannerId` residual. La versión publicada no cambia (el portal sigue sirviendo la misma imagen). Además, el lector trata un documento con `bannerId` y sin `banners` como `banners = [bannerId]`, por si algún documento llegara sin migrar. `Down` restituye `bannerId = banners[0]` y descarta el resto de la lista.

Portal: proto `ObtenerMarcaPuntoResponse.banners = 10` (`BannerItem { imagen_id, alt, enlace }`, solo origen `organismo`, desde el PUBLICADO; `banner_id` = el primero). JSON del BFF `GET /api/v1/persona-natural/punto/marca/{codigo}`:

```ts
interface MarcaPunto { /* ...campos anteriores... */
  bannerUrl: string | null; // compatibilidad: URL del primer banner
  banners: { imagenUrl: string; alt: string; enlace: string | null }[]; // [] si no hay o si el origen no es organismo
}
```

El BFF descarta ítems con id inválido, alt vacío o enlace que no sea http/https, y entrega como máximo 6. Las imágenes de banners publicados se sirven por `GET /api/v1/persona-natural/marca/imagenes/{id}` (la misma ruta pública); una imagen que no está en el publicado vigente responde 404.

### Pantallas

- Hub del OT: pestaña `Portal Persona Natural`, ruta `/admin/transit-offices/[id]/portal-persona-natural`, con subpestañas. Las crea el frontend de Marca en `ot-nav.ts` y en `components/admin/transit-offices/portal-pn/PortalPnTabs.tsx`.
- Subpestañas de esta entrega: `Cobro` (la llena Precios) y `Diseño`. `Reportes` llega con #14399.
- Portal: `app/o/[codigo]/page.tsx` lee la marca en el servidor, sin caché, e inyecta los tokens antes de la primera pintura.

## Precios (#14387)

Grupo `/api/v1/admin/ot/portal-tarifas` (`?transitOfficeId=` para SuperAdmin), sobre `admin.persona_natural_tariffs`. Los endpoints de Plataforma no cambian.

| Método | Ruta | Cuerpo | Respuesta |
|---|---|---|---|
| GET | `` | filtros de hoy: `soloVigentes`, `tipoTramiteId`, `concepto`, `fecha` | `TarifaOt[]` |
| GET | `/{id}/historial` | | `TarifaOt[]` |
| GET | `/tipos` | | tipos del portal para el formulario |
| POST | `` | `{ tipoTramiteId, valores: [{ concepto, valorCop }], vigenteDesde }` | versiones creadas |

```ts
type TarifaOt = TarifaPnView & {
  editable: boolean;           // false en valores generales y conceptos ajenos
  creadaPorNombre: string | null;
};
```

- `ot_admin` lista las de su organismo y los valores generales que le aplican. Solo escribe en su organismo.
- Conceptos del `ot_admin`: `DERECHOS_TRANSITO`, `CAMBIO_COLOR`, `CONVERSION_COMBUSTIBLE`, `CAMBIO_CARROCERIA`. `SERVICIO_FLIT` y `ENVIO_DOCUMENTO` responden 403. El SuperAdmin escribe todos.
- `tipoTramiteId` es obligatorio para el `ot_admin` al registrar un precio. Al listar es un filtro opcional.
- En los valores generales, el `ot_admin` ve `FLIT` como autor y no recibe los ids de quien los fijó.
- `creadaPorNombre` también se agrega, sin romper nada, a las respuestas de historial de Plataforma.

### Mismo día (#14389)

Un valor con inicio igual al de una versión que empezó el mismo día reemplaza a esa versión, sin error de tarifa solapada. La fila reemplazada no se borra, porque una cotización pudo congelar su id. Sugerencia: columna `superseded_at` no mapeada en EF, índice único parcial `WHERE superseded_at IS NULL`, y todas las lecturas vigentes la excluyen. Los dos valores quedan en la auditoría. DDL `181-*`, migración `20261105130000_HU14389_*`.

### Sin precio (#14391)

`ListarOrganismos` y `ListarTiposDisponibles` no ofrecen lo que no tiene precio. Se quita la caché del BFF en estas consultas y el `staleTime` de `useCatalogos`.

### Pantalla

Sección `components/admin/transit-offices/portal-pn/OtPreciosSection.tsx`, que reutiliza `TarifasTab`, `TarifaFormModal` y `TarifaHistorialModal` con un cliente de API inyectado. Cliente: `frontend/lib/api/admin-ot-portal-tarifas.ts`.

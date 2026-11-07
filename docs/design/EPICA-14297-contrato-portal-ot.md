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

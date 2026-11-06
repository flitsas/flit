# Épica #14297: contrato de Dominio, Medios y switches, Reportes y Diseño web

Complementa `EPICA-14297-contrato-portal-ot.md`. Los AC de cada HU mandan. Si un detalle cambia, se informa en el reporte final.

## Reglas comunes

- Cambios aditivos en `core-api`: FLIT 2 y marca blanca no cambian de comportamiento.
- Alcance: `ot_admin` con el organismo del token; `transitOfficeId` ajeno da 403. SuperAdmin envía `?transitOfficeId=`, obligatorio. Rol, sin permiso nuevo.
- Autorización: `OtAdminOrSuperAdminPolicy` en `/api/v1/admin/ot/*`. `SuperAdminPolicy` en Plataforma. Las firmas nuevas bajo `/api/v1/admin/ot/portal-` no entran a `OtPermissionMap`.
- Errores `{ code, message }`, con el motivo exacto en español.
- Sin caché por tiempo: lo guardado se ve en la siguiente consulta.
- DDL reservados: 182 Medios, 183 Dominio, 184 Diseño web. Migraciones `20261106100000_HU14393_*`, `20261106110000_HU14381_*`, `20261106120000_HU14401_*`, con `[DbContext]` y `[Migration]`. Reportes no lleva DDL salvo índice justificado (185, `20261106130000_HU14397_*`).

## Medios y switches (#14392)

Ámbito: `transit_office_id`, nulo para la web de persona.

- `admin.persona_natural_payment_methods`: ámbito, `method` (`PASARELA`, `PIN`), `enabled`, auditoría.
- `admin.persona_natural_charge_switches`: ámbito, `procedure_type_id`, `method`, `charge` (`TRAMITE`, `GESTION`), `enabled`, auditoría.
- `TRAMITE` agrupa derechos de tránsito y transformaciones. `GESTION` agrupa servicio FLIT y envío. Sin fila, el switch vale encendido si hay monto: una instalación sin configurar cobra como hoy.
- PIN no existe en la web: se rechaza con el motivo exacto.
- Encender exige monto mayor que cero en ese tipo y organismo. Apagar conserva el monto, que vive en las tarifas.
- `ot_admin` mueve solo `TRAMITE`; `GESTION` da 403. SuperAdmin mueve todo.

Rutas: `/api/v1/admin/ot/portal-cobro` y `/api/v1/admin/plataforma/persona-natural/cobro`.

| Método | Ruta | Cuerpo |
|---|---|---|
| GET | `` | |
| PUT | `/medios` | `{ method, enabled }` |
| PUT | `/switches` | `{ tipoTramiteId, method, charge, enabled }` |

```ts
interface CobroPortal {
  medios: { method: "PASARELA" | "PIN"; enabled: boolean }[];
  tipos: {
    tipoTramiteId: string; nombre: string;
    medios: { method: string; ofrecido: boolean; conceptos: {
      charge: "TRAMITE" | "GESTION"; enabled: boolean; montoCop: number; editable: boolean;
    }[] }[];
  }[];
}
```

Entrega al portal (#14395): RPC `ListarMediosPago(tipo, organismo)` y campo opcional `medio` en `Cotizar`. Sin `medio`, la respuesta no cambia. BFF: `GET /api/v1/persona-natural/medios-pago?...`, y su par en el punto. La cotización congelada (valores y versión) se guarda en la solicitud del BFF al radicar, y lo ya radicado no se recalcula (reglas 9 y casos borde). Construir la pasarela, el PIN en caja y el paso de pago es de #14189 y #14190.

## Dominio (#14380)

Decisión propuesta en ADR-0081, en `Propuesto`: alternativa B.

- Tabla `admin.transit_office_domains`, sin tocar `admin.tenant_domains` ni sus disparadores. Reutiliza `DomainStateMachine`, verificación TXT, `Domains:Reserved` y el sello `X-Flit-Domain`.
- Unicidad de `host` entre las dos tablas, en base de datos, con disparador en ambas. Un host de marca blanca sigue funcionando igual.
- Un dominio vigente por organismo.
- Sin `purpose` ni `TenantProductFlags`. No aplica la activación manual de red: con TXT correcto y certificado emitido pasa a `active`.
- `DomainContext`: resultado nuevo `Portal(transitOfficeId)`. El login de FLIT no se abre por ese host, y no acota a ninguna red.
- Provisioning: `/internal/domains/provisioning` incluye estos dominios, sin cambiar lo que entrega de marca blanca, y el borde enruta el host al portal.

Rutas `/api/v1/admin/ot/portal-dominio`: `GET`, `POST { host }`, `POST /verificar`, `DELETE`.

```ts
interface DominioPortal {
  host: string; estado: "pendiente" | "verificado" | "activo" | "fallido";
  motivo: string | null; dns: { tipo: string; nombre: string; valor: string }[];
  rowVersion: number;
}
```

Portal: BFF `GET /api/v1/persona-natural/punto/dominio/{host}` devuelve el código del organismo solo si el dominio está activo. La raíz del dominio pinta el punto de ese organismo, sin escribir código. `/o/{codigo}` sigue funcionando. Enlace, QR y enlaces de correo usan el dominio activo.

Certificados (#14384): solo se entrega código y runbook. Nada se instala ni se despliega en un VPS: requiere confirmación humana.

Front: el `domain` de `PortalPnDesignSection`, con `DomainRegisterForm` y `DomainStatusPanel` y su cliente inyectado.

## Reportes (#14396)

Lee trámites de FLIT 2.0 por las columnas de canal de #14296 (ya en develop). Canales: `pn_web`, `pn_punto`, `aplicacion`. El gestor nunca aparece.

Rutas: `GET /api/v1/admin/ot/portal-reportes` y `GET .../export`; `GET /api/v1/admin/plataforma/persona-natural/reportes` y `.../export`.

Filtros: `canal`, `tipoTramiteId`, `desde`, `hasta`, `transitOfficeId` (solo SuperAdmin), `page`, `pageSize`.

```ts
interface FilaReportePortal {
  canal: string; organismo: string | null; tipo: string; radicado: string | null; estado: string;
  fechaCaida: string; modalidad: string | null; valorTramite: number | null; valorGestion: number | null;
}
```

Sin pago aún, modalidad y valores van vacíos, sin inventar datos. El Excel usa el exportador existente y respeta alcance y filtros. Verificar de qué campo sale el organismo de un trámite web, porque `transit_office_id` cambia al enviar al OT.

Front: subpestaña `Reportes`, la primera, en `PortalPnTabs`, y pestaña `Reportes` en `PersonaNaturalConsole`. Modelo de tabla de trámites, `TablaFiltrosBar`, «Armando el reporte…», vacío «No hay trámites del portal en FLIT 2.0 con esos filtros.».

## Diseño web (#14400)

Mismo modelo de marca, con ámbito web. Sin banner: los de la web siguen en Contenido.

Rutas `/api/v1/admin/plataforma/persona-natural/marca-web`, con `SuperAdminPolicy`, misma forma de `PortalMarca` y sin `/banner`. RPC `ObtenerMarcaWeb`. BFF `GET /api/v1/persona-natural/marca/web`, misma forma de `MarcaPunto`, y falla o sin marca da `origen: "flit"`.

Portal: la raíz de la web lee la marca en el servidor y la inyecta antes de la primera pintura, con `cssDeMarca`. Una marca web no afecta a ningún `/o/{codigo}`, y viceversa. Con dominio de organismo activo manda la marca del organismo.

Front: pestaña `Diseño web` en `PersonaNaturalConsole`, con las reglas de `lib/brand/portal-contrast.ts`, y acceso a los banners de Contenido.

## UX de la épica

Botón principal píldora `#557EFF`, secundaria `#162744`. Switch ON `#8CC63F`, OFF `#91A5B5`, con estado también en texto. Texto de éxito `#4F7A12` con icono `#8CC63F`. Errores `#C10000`. Miga de pan, texto de 12 px como mínimo, foco visible. Las tablas siguen el modelo de trámites.

### Medios y switches: lo implementado (HU #14393 y #14395)

- Ámbito: `transit_office_id` nulo = web de persona. Sin fila, la pasarela está encendida y el PIN apagado; el switch vale encendido si el concepto tiene monto mayor que cero. El PIN no existe en la web (CHECK en base y rechazo `PORTAL_COBRO_PIN_EN_WEB`).
- `montoCop` de cada concepto de cobro es el del renglón base: `TRAMITE` = derechos de tránsito, `GESTION` = servicio FLIT. Las transformaciones siguen el switch de `TRAMITE` y el envío el de `GESTION`.
- gRPC `PersonaNaturalService`: `Cotizar` gana `medio` y `ambito_organismo_id` (vacío = web; con valor, el organismo del punto); nuevo `ListarMediosPago(tipo, organismo, ambito)` con los medios ofrecidos y su total base. Medio no ofrecido: razón `medio_no_disponible`.
- BFF: `GET /api/v1/persona-natural/medios-pago?organismoId&tipoCodigo`, `GET /api/v1/persona-natural/punto/medios-pago?tipoCodigo` y `medio` opcional en `POST /cotizaciones`. `POST /solicitudes/{id}/radicar` acepta un cuerpo opcional `{ medio, adicionales, totalCop }`: con `medio` cotiza en ese instante, guarda la cotización en `personanatural.frozen_quote` (una por solicitud, solo inserción) en la misma transacción que encola, y responde 409 `cotizacion_cambio` (con el nuevo `totalCop`) si el total que la persona vio ya cambió.

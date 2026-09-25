import { searchOtClientProcedures } from "@/lib/api/admin-ot";
import { tramitesClient } from "@/lib/api/tramites-client";
import type { ListInstancesParams } from "@/lib/api/types/procedure-runtime";
import type { OtClientProceduresParams } from "@/lib/api/types-ot";
import type { DrFlitSearchContext } from "./dr-flit-context";
import type { DrFlitIntentId } from "./dr-flit-intents";
import {
  fechaRadicacion,
  mapBiometricToResult,
  mapInstanceSummaryToResult,
  mapNetworkInstanceToResult,
  mapOtProcedureToResult,
} from "./dr-flit-mappers";
import type {
  DrFlitTramiteResult,
  DrFlitTramiteSearchResult,
  DrFlitValidacionResult,
} from "./dr-flit-types";

const GUID_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

/** Tamaño de página del chat: cabe en el panel sin scroll infinito; `total` dice cuántos hay más. */
export const DR_FLIT_PAGE_SIZE = 20;

export function isGuid(value: string): boolean {
  return GUID_RE.test(value.trim());
}

function compactDocument(value: string): string {
  return value.replace(/[\s.-]/g, "");
}

function looksLikeDocument(value: string): boolean {
  return /^\d{5,}$/.test(compactDocument(value));
}

/**
 * HU-D — un documento escrito «1.234.567» o «1 234 567» no casa por subcadena con el `1234567`
 * almacenado: se compacta antes de enviarlo. Un nombre viaja tal cual.
 */
export function normalizeClienteQuery(value: string): string {
  const v = value.trim();
  return looksLikeDocument(v) ? compactDocument(v) : v;
}

/**
 * Criterio en los términos que comparten las tres APIs (tenant, red y bandeja OT): `placa`/`vin`
 * exactos, y `busqueda` para el cliente (HU #12187/#12218: nombre y documento de las partes por
 * subcadena, radicado exacto). Una sola llamada en lugar de comprador + vendedor por separado.
 */
type CriterioComun = Pick<ListInstancesParams, "placa" | "vin" | "busqueda"> &
  Pick<OtClientProceduresParams, "placa" | "vin" | "busqueda">;

function criterioComun(intent: Exclude<DrFlitIntentId, "tramite">, value: string): CriterioComun {
  if (intent === "placa") return { placa: value.toUpperCase() };
  if (intent === "vin") return { vin: value.toUpperCase() };
  return { busqueda: normalizeClienteQuery(value) };
}

/**
 * HU-B — ¿este resultado ES el radicado que el usuario escribió? Misma lectura tolerante que
 * `Radicado.TryLeer` del backend: con prefijo compara el texto canónico (mayúsculas, sin guiones
 * ni espacios); solo dígitos compara el consecutivo (`12` casa con `FT1-0000012`).
 */
export function esMismoRadicado(radicado: string, termino: string): boolean {
  const canon = (v: string) => v.toUpperCase().replace(/[\s-]/g, "");
  const t = termino.trim();
  if (!t || !radicado || radicado === "—") return false;
  if (/^\d+$/.test(t)) {
    const consecutivo = radicado.match(/(\d+)$/)?.[1];
    return consecutivo != null && Number(consecutivo) === Number(t);
  }
  return canon(radicado) === canon(t);
}

/**
 * El detalle por ID se resuelve en UN tenant (`X-Tenant-Id`: el elegido en Trámites o, si no, el
 * del JWT). Un Super Admin cuyo JWT apunta a su propia compañía no encuentra por esta ruta el
 * trámite de un cliente (404/403), y una cabeza de red no puede abrir así el de una hija.
 * Verificado contra el backend local (E2E SA-09). Se traduce a un mensaje accionable en vez del
 * error técnico: el radicado sí funciona en todos los alcances.
 */
async function buscarPorGuid(
  value: string,
  ctx: DrFlitSearchContext,
): Promise<DrFlitTramiteSearchResult> {
  let detail: Awaited<ReturnType<typeof tramitesClient.getInstance>>;
  try {
    detail = await tramitesClient.getInstance(value);
  } catch (err) {
    if (ctx.role === "superadmin") {
      throw new Error(
        "Para abrir un trámite por ID como Super Admin, elige primero la compañía en Trámites; o búscalo por radicado, que funciona en todas las compañías.",
      );
    }
    if (ctx.network.active) {
      throw new Error(
        "No pude abrir ese ID en el alcance de red. Búscalo por radicado, placa o VIN.",
      );
    }
    throw new Error(
      err instanceof Error && err.message
        ? `No encontré un trámite con ese ID en tu alcance (${err.message}). Prueba con el radicado.`
        : "No encontré un trámite con ese ID en tu alcance. Prueba con el radicado.",
    );
  }
  const field = (key: string) =>
    detail.fieldValues?.find(
      (f) =>
        f.fieldKey?.toLowerCase() === key || f.fieldKey?.toLowerCase().endsWith(`.${key}`),
    )?.valueText ?? null;
  const item: DrFlitTramiteResult = {
    id: detail.id,
    radicado: detail.referenceNumber || "—",
    fecha: fechaRadicacion(detail.createdAt),
    estado: detail.status,
    placa: (field("placa") ?? "—").toUpperCase(),
    vin: field("vin") ?? "—",
    tipoTramite: detail.referenceNumber ? `Trámite ${detail.referenceNumber}` : "Trámite",
    compania: null,
    href: `/tramites/${detail.id}`,
  };
  return { items: [item], total: 1 };
}

/**
 * Búsqueda de trámites de DR-FLIT. Una sola entrada; el CONTEXTO decide a qué API preguntar:
 *
 *  - `ot_admin` → bandeja del organismo (`client-procedures`), que es su universo.
 *  - `admin_company` con red activa → `network/instances/search` (yo + mis hijas, HU #12362/#12363);
 *    con un hijo elegido, `childTenantId` viaja en el cuerpo y el servidor lo intersecta con la red.
 *  - resto (`gestor`, `admin_company` propio, `superadmin`) → `instances/search` del tenant. El
 *    SuperAdmin no manda `X-Tenant-Id` y ve todas las compañías: por eso conserva `compania`.
 *
 * Siempre por el camino que devuelve `total`: «encontré 37, te muestro 20» es una respuesta; «20»
 * a secas sería una respuesta falsa (HU #12104).
 */
export async function searchTramites(
  intent: DrFlitIntentId,
  rawValue: string,
  ctx: DrFlitSearchContext,
): Promise<DrFlitTramiteSearchResult> {
  const value = rawValue.trim();
  if (!value) return { items: [], total: 0 };

  if (intent === "tramite") {
    // HU-B — el usuario conoce el radicado; el GUID se sigue aceptando (enlaces internos, soporte).
    if (isGuid(value)) return buscarPorGuid(value, ctx);
    const res = await buscarConCriterio({ busqueda: value }, ctx);
    // `busqueda` casa el radicado exacto Y el resto de campos por subcadena: si hubo radicado
    // exacto, lo demás («12» dentro de una cédula) es ruido y se descarta.
    const exactos = res.items.filter((it) => esMismoRadicado(it.radicado, value));
    return exactos.length > 0 ? { items: exactos, total: exactos.length } : res;
  }

  return buscarConCriterio(criterioComun(intent, value), ctx);
}

/** Enruta un criterio ya normalizado a la API que corresponde al contexto (ver `searchTramites`). */
async function buscarConCriterio(
  criterio: CriterioComun,
  ctx: DrFlitSearchContext,
): Promise<DrFlitTramiteSearchResult> {
  if (ctx.role === "ot_admin") {
    // POST `/client-procedures/search` y no el GET: verificado contra el backend local, el GET
    // ignora `busqueda` (devuelve la bandeja entera); solo el camino POST (HU #12217/#12218) la aplica.
    const page = await searchOtClientProcedures({
      ...criterio,
      page: 1,
      pageSize: DR_FLIT_PAGE_SIZE,
      sortBy: "createdAt",
      sortDir: "desc",
    });
    const items = (page.data ?? []).map(mapOtProcedureToResult);
    return { items, total: page.totalCount ?? items.length };
  }

  const paginacion = {
    take: DR_FLIT_PAGE_SIZE,
    skip: 0,
    sortBy: "createdAt",
    sortDir: "desc" as const,
  };

  if (ctx.network.active) {
    const res = await tramitesClient.searchNetworkInstances({
      ...criterio,
      ...paginacion,
      ...(ctx.network.childTenantId ? { childTenantId: ctx.network.childTenantId } : {}),
    });
    return { items: res.items.map(mapNetworkInstanceToResult), total: res.total };
  }

  const res = await tramitesClient.searchInstances({ ...criterio, ...paginacion });
  const withCompania = ctx.role === "superadmin";
  return {
    items: res.items.map((it) => mapInstanceSummaryToResult(it, withCompania)),
    total: res.total,
  };
}

/** Busca validaciones de identidad por documento o nombre. */
export async function searchValidaciones(
  rawValue: string,
): Promise<DrFlitValidacionResult[]> {
  const value = rawValue.trim();
  if (!value) return [];

  const filters = looksLikeDocument(value)
    ? { documentNumber: compactDocument(value), page: 1, pageSize: DR_FLIT_PAGE_SIZE }
    : { name: value, page: 1, pageSize: DR_FLIT_PAGE_SIZE };

  const res = await tramitesClient.listTenantBiometricValidations(filters);
  return (res.validations ?? []).map(mapBiometricToResult);
}

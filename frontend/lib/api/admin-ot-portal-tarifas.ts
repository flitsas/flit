// HU #14390 (Feature #14387, Épica #14297) — cliente de los PRECIOS del portal desde el hub del OT. Contrato:
// `docs/design/EPICA-14297-contrato-portal-ot.md` (sección Precios).
//   GET  /api/v1/admin/ot/portal-tarifas?soloVigentes&tipoTramiteId&concepto&fecha[&transitOfficeId]
//   GET  …/portal-tarifas/{id}/historial
//   GET  …/portal-tarifas/tipos
//   POST …/portal-tarifas   { tipoTramiteId, valores:[{concepto,valorCop}], vigenteDesde }
// El `ot_admin` trabaja con el organismo de su token y no envía `transitOfficeId`; el SuperAdmin lo envía siempre.
import { apiFetch } from "./client";
import type { NuevasTarifas, TarifaPn, TarifasFiltro } from "./admin-pn-tarifas";

export const OT_PORTAL_TARIFAS_BASE = "/api/v1/admin/ot/portal-tarifas";

/** `TarifaOt` = tarifa del portal + permiso y autor que decide el servidor. */
export type TarifaOt = TarifaPn & { editable: boolean; creadaPorNombre: string | null };

/** Tipo de trámite ofrecido en el formulario del OT. */
export interface TipoPortalOt {
  id: string;
  codigo?: string;
  nombre: string;
}

export interface OtTarifasScope {
  /** Solo el SuperAdmin lo envía; el `ot_admin` lo omite y el servidor usa su token. */
  transitOfficeId?: string;
}

const q = (scope?: OtTarifasScope) =>
  scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : {};

export function fetchTarifasOt(
  filtro: Omit<TarifasFiltro, "organismoId"> = {},
  scope?: OtTarifasScope,
  signal?: AbortSignal,
): Promise<TarifaOt[]> {
  return apiFetch<TarifaOt[]>(OT_PORTAL_TARIFAS_BASE, {
    query: { ...filtro, ...q(scope) },
    signal,
  });
}

export function fetchHistorialTarifaOt(
  id: string,
  scope?: OtTarifasScope,
  signal?: AbortSignal,
): Promise<TarifaOt[]> {
  return apiFetch<TarifaOt[]>(`${OT_PORTAL_TARIFAS_BASE}/${id}/historial`, {
    query: q(scope),
    signal,
  });
}

export function fetchTiposTarifaOt(
  scope?: OtTarifasScope,
  signal?: AbortSignal,
): Promise<TipoPortalOt[]> {
  return apiFetch<TipoPortalOt[]>(`${OT_PORTAL_TARIFAS_BASE}/tipos`, { query: q(scope), signal });
}

/** El organismo no viaja en el cuerpo: sale del token (ot_admin) o de `transitOfficeId` (SuperAdmin). */
export function registrarTarifasOt(
  body: NuevasTarifas,
  scope?: OtTarifasScope,
): Promise<TarifaOt[]> {
  const { tipoTramiteId, valores, vigenteDesde } = body;
  return apiFetch<TarifaOt[]>(OT_PORTAL_TARIFAS_BASE, {
    method: "POST",
    query: q(scope),
    body: { tipoTramiteId, valores, vigenteDesde },
  });
}

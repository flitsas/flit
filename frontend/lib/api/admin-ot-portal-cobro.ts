// HU #14394 (Feature #14392, Épica #14297) — cliente de los MEDIOS DE PAGO Y SWITCHES de cobro del portal. Contrato:
// `docs/design/EPICA-14297-contrato-resto.md` (sección Medios y switches).
//   GET  /api/v1/admin/ot/portal-cobro[?transitOfficeId]
//   PUT  …/medios    { method, enabled }
//   PUT  …/switches  { tipoTramiteId, method, charge, enabled }
// La web de persona usa la misma forma en `/api/v1/admin/plataforma/persona-natural/cobro` y no tiene PIN.
// El `ot_admin` trabaja con el organismo de su token; el SuperAdmin envía `transitOfficeId` siempre.
import { apiFetch } from "./client";
import { PN_BASE } from "./admin-pn-tarifas";

export const OT_PORTAL_COBRO_BASE = "/api/v1/admin/ot/portal-cobro";
export const PN_COBRO_BASE = `${PN_BASE}/cobro`;

export type MedioPago = "PASARELA" | "PIN";
export type ConceptoCobro = "TRAMITE" | "GESTION";

export interface MedioBandera {
  method: MedioPago;
  enabled: boolean;
}

export interface SwitchCobro {
  charge: ConceptoCobro;
  enabled: boolean;
  montoCop: number;
  /** Lo decide el servidor: el `ot_admin` no mueve la gestión. */
  editable: boolean;
}

export interface MedioDeTipo {
  method: MedioPago;
  ofrecido: boolean;
  conceptos: SwitchCobro[];
}

export interface TipoCobro {
  tipoTramiteId: string;
  nombre: string;
  medios: MedioDeTipo[];
}

export interface CobroPortal {
  medios: MedioBandera[];
  tipos: TipoCobro[];
}

export interface CambioMedio {
  method: MedioPago;
  enabled: boolean;
}

export interface CambioSwitch {
  tipoTramiteId: string;
  method: MedioPago;
  charge: ConceptoCobro;
  enabled: boolean;
}

/** Cliente con el ámbito ya resuelto (OT, SuperAdmin en un OT o web de persona). Debe ser estable entre renders. */
export interface CobroApi {
  obtener: (signal?: AbortSignal) => Promise<CobroPortal>;
  guardarMedio: (cambio: CambioMedio) => Promise<unknown>;
  guardarSwitch: (cambio: CambioSwitch) => Promise<unknown>;
}

export interface OtCobroScope {
  /** Solo el SuperAdmin lo envía; el `ot_admin` lo omite y el servidor usa su token. */
  transitOfficeId?: string;
}

function crearApi(base: string, query: Record<string, string>): CobroApi {
  return {
    obtener: (signal) => apiFetch<CobroPortal>(base, { query, signal }),
    guardarMedio: (cambio) =>
      apiFetch<unknown>(`${base}/medios`, { method: "PUT", query, body: cambio }),
    guardarSwitch: (cambio) =>
      apiFetch<unknown>(`${base}/switches`, { method: "PUT", query, body: cambio }),
  };
}

export function crearCobroApiOt(scope?: OtCobroScope): CobroApi {
  return crearApi(
    OT_PORTAL_COBRO_BASE,
    scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : {},
  );
}

export function crearCobroApiPlataforma(): CobroApi {
  return crearApi(PN_COBRO_BASE, {});
}

// Presentación de la vigencia de identidad de un sujeto administrativo (HU #11059 / HU #11060).
//
// El representante legal y el mandatario del OT son entidades independientes (ADR-0036), pero su
// identidad se valida con el MISMO servicio y vence con las mismas reglas, así que el rótulo, el color
// y la acción disponible se deciden aquí una sola vez. El backend calcula el estado en
// `AdminIdentityVigencia` y ahora devuelve también hasta cuándo es válida.

import { formatFecha } from "@/lib/format/date";

/** Estados que expone el backend (`AdminIdentityVigencia`). */
export type AdminIdentityStatus = "valid" | "expired" | "pending" | "none";

export interface AdminIdentityUi {
  /** Rótulo del chip. */
  label: string;
  /** Estilo del chip (paleta FLIT). */
  style: { background: string; color: string };
  /** true solo cuando la identidad está aprobada y vigente (icono de escudo "ok"). */
  isValid: boolean;
  /** Rótulo del botón de acción: enviar / reenviar / renovar. */
  action: string;
}

const IDENTITY_UI: Record<AdminIdentityStatus, AdminIdentityUi> = {
  valid: {
    label: "Identidad validada",
    style: { background: "rgba(112,207,58,0.14)", color: "#3f7a15" },
    isValid: true,
    action: "Reenviar validación",
  },
  expired: {
    label: "Identidad vencida",
    style: { background: "rgba(245,158,11,0.16)", color: "#b45309" },
    isValid: false,
    action: "Renovar validación",
  },
  pending: {
    label: "Validación en proceso",
    style: { background: "rgba(85,126,255,0.14)", color: "#3559c7" },
    isValid: false,
    action: "Reenviar validación",
  },
  none: {
    label: "Identidad sin validar",
    style: { background: "rgba(240,90,53,0.12)", color: "#c2410c" },
    isValid: false,
    action: "Enviar validación",
  },
};

/** Presentación del estado; cae a "sin validar" ante un estado desconocido o ausente. */
export function identityUi(status: string | null | undefined): AdminIdentityUi {
  return IDENTITY_UI[(status ?? "none") as AdminIdentityStatus] ?? IDENTITY_UI.none;
}

/**
 * ¿Hay una validación previa (vigente, vencida o en curso)? Con previa se REENVÍA/RENUEVA; sin ninguna
 * se ENVÍA por primera vez.
 */
export function hasPriorIdentity(status: string | null | undefined): boolean {
  return (status ?? "none") !== "none";
}

/**
 * HU #11059 / HU #11060 — con la identidad VIGENTE no se ofrece renovar: el backend reutilizaría la
 * vigente (no reenvía), así que el botón prometía algo que no ocurre. En su lugar se informa hasta
 * cuándo es válida.
 */
export function puedeRenovarIdentidad(status: string | null | undefined): boolean {
  return (status ?? "none") !== "valid";
}

/**
 * Leyenda de vigencia para un estado `valid`: "Válida hasta AAAA/MM/DD". Devuelve null cuando no
 * aplica (otro estado) o cuando la aprobación no tiene caducidad registrada: en ese caso no hay fecha
 * que prometer.
 */
export function vigenciaLabel(
  status: string | null | undefined,
  validUntil: string | null | undefined,
): string | null {
  if ((status ?? "none") !== "valid" || !validUntil) return null;
  const fecha = formatFecha(validUntil, "");
  return fecha ? `Válida hasta ${fecha}` : null;
}

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

// ── HU #11756 (ADR-0050) — matriz de rótulos + copy por estado ───────────────
//
// El ADR-0050 fija CUATRO estados canónicos de identidad (distintos del texto del chip de arriba,
// que es de HU anteriores): "sin validación", "en curso", "aprobada y vigente", "vencida". Y exige DOS
// rótulos SIEMPRE visibles y NUNCA fusionados en una sola cadena que confunda las dos fuentes:
//   - Identidad: {uno de los 4 estados}
//   - Firma del baúl: vigente hasta {fecha}  |  Firma del baúl: sin firma vigente
//
// El copy de invitación al módulo Identidad respeta la precedencia D8 del ADR-0025 (baúl > identidad):
// con firma de baúl vigente NO se invita a prevalidar, sin importar el estado de identidad.

/** Mapeo del estado interno (`AdminIdentityStatus`) al rótulo canónico del ADR-0050. */
const ESTADO_CANONICO: Record<AdminIdentityStatus, string> = {
  none: "sin validación",
  pending: "en curso",
  valid: "aprobada y vigente",
  expired: "vencida",
};

/** Ruta del módulo Identidad dentro de la SPA admin (dock ≡ URL, HU #11507/#11508). */
export const IDENTITY_MODULE_HREF = "/?m=validaciones";

/** Rótulo `Identidad: {estado}` — SIEMPRE presente, aunque el estado sea "sin validación". */
export function identidadRotulo(status: string | null | undefined): string {
  const estado = ESTADO_CANONICO[(status ?? "none") as AdminIdentityStatus] ?? ESTADO_CANONICO.none;
  return `Identidad: ${estado}`;
}

/**
 * Rótulo `Firma del baúl: ...` — SIEMPRE presente, con dos formas: vigente hasta {fecha} o sin firma
 * vigente. Con `vigente=true` pero sin fecha registrada (dato incompleto) se informa "vigente" a secas
 * en vez de prometer una fecha que no existe.
 */
export function firmaBaulRotulo(
  vigente: boolean | null | undefined,
  vigenteHasta: string | null | undefined,
): string {
  if (!vigente) return "Firma del baúl: sin firma vigente";
  const fecha = vigenteHasta ? formatFecha(vigenteHasta, "") : "";
  return fecha ? `Firma del baúl: vigente hasta ${fecha}` : "Firma del baúl: vigente";
}

/** ¿El tipo de documento corresponde a persona jurídica (NIT)? Normaliza espacios y mayúsculas. */
function esNit(documentType: string | null | undefined): boolean {
  return (documentType ?? "").trim().toUpperCase() === "NIT";
}

export interface IdentityCopyContext {
  identityStatus?: string | null;
  /** ¿Tiene firma del baúl vigente HOY? Manda sobre todo lo demás (D8, ADR-0025). */
  firmaBaulVigente?: boolean | null;
  /** Tipo de documento de la persona; "NIT" ⇒ persona jurídica, sin prevalidación (ADR-0036). */
  documentType?: string | null;
}

export interface IdentityCopyResult {
  /** Texto a mostrar bajo los rótulos; `null` cuando no hay nada que decir. */
  message: string | null;
  /** `true` cuando el mensaje debe ir acompañado del enlace al módulo Identidad. */
  showLink: boolean;
}

/**
 * Copy por estado (CF-03, HU #11756). Reglas, en orden de precedencia:
 *
 * 1. Firma de baúl vigente ⇒ nunca se invita a prevalidar (D8 del ADR-0025 manda sobre todo).
 * 2. Sin firma vigente y estado `sin validación` o `vencida` ⇒ se invita a ir al módulo Identidad,
 *    con copy DISTINTO entre ambos casos. Enlace simple, sin precargar el documento.
 * 3. Dentro del punto 2, si el documento es NIT (persona jurídica, ADR-0036) no se enlaza a un flujo
 *    imposible: se informa que no aplica prevalidación.
 * 4. Estados `en curso` o `aprobada y vigente` (sin firma vigente) ⇒ sin invitación: ya está en curso
 *    o ya quedó resuelta por el módulo Identidad.
 */
export function identityCopy(ctx: IdentityCopyContext): IdentityCopyResult {
  if (ctx.firmaBaulVigente) {
    return { message: null, showLink: false };
  }

  const status = (ctx.identityStatus ?? "none") as AdminIdentityStatus;
  if (status !== "none" && status !== "expired") {
    return { message: null, showLink: false };
  }

  if (esNit(ctx.documentType)) {
    return {
      message: "Persona jurídica (NIT): no aplica prevalidación de identidad.",
      showLink: false,
    };
  }

  return {
    message:
      status === "none"
        ? "Esta persona todavía no tiene una validación de identidad. Puedes iniciarla desde el módulo Identidad."
        : "La validación de identidad de esta persona venció. Puedes renovarla desde el módulo Identidad.",
    showLink: true,
  };
}

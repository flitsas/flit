// Presentación del estado de validación de identidad de un mandatario (HU #10994/#11000).
// Compartido por el panel de edición y la tabla del hub Admin OT para que el rótulo, el color y la
// acción disponible sean los mismos en ambos sitios.

/** Estados que expone el backend en `MandateSigner.identityStatus`. */
export type MandateSignerIdentityStatus = "valid" | "expired" | "pending" | "none";

export interface MandateSignerIdentityUi {
  /** Rótulo del chip. */
  label: string;
  /** Estilo del chip (paleta FLIT). */
  style: { background: string; color: string };
  /** true solo cuando la identidad está aprobada y vigente (icono de escudo "ok"). */
  isValid: boolean;
  /** Rótulo del botón de acción: enviar / reenviar / renovar. */
  action: string;
}

const IDENTITY_UI: Record<MandateSignerIdentityStatus, MandateSignerIdentityUi> = {
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
export function identityUi(status: string | null | undefined): MandateSignerIdentityUi {
  return IDENTITY_UI[(status ?? "none") as MandateSignerIdentityStatus] ?? IDENTITY_UI.none;
}

/**
 * ¿Hay una validación previa (vigente, vencida o en curso)? Con previa se REENVÍA/RENUEVA; sin ninguna
 * se ENVÍA por primera vez.
 */
export function hasPriorIdentity(status: string | null | undefined): boolean {
  return (status ?? "none") !== "none";
}

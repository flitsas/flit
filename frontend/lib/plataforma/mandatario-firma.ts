/**
 * HU #11715/#11716/#11717 — si un mandatario está en condiciones de firmar el mandato ante un
 * organismo.
 *
 * La regla la impone el backend (`MandateSignerSigningCapability`); esto es solo para explicarla en
 * pantalla antes de que el guardado falle. Replica la precedencia de `MandatarioFirmaResolver`:
 * imagen del baúl → sello de la validación de identidad vigente → línea en blanco.
 */

/** Estado de la validación de identidad del mandatario, tal como lo publica el backend. */
export type MandatarioIdentityStatus = "valid" | "expired" | "pending" | "none";

export interface MedioDeFirma {
  /** Firma del baúl elegida para el mandatario. */
  signatureVaultId?: string | null;
  /** Correo de contacto (ya no es medio de firma, HU #13132). */
  email?: string | null;
  identityStatus?: MandatarioIdentityStatus | null;
  /**
   * HU #13248 (F9) - forma de firma de la ficha. Con `biometria` la firma depende SOLO de la
   * validación propia del mandatario: una firma de baúl suelta no cuenta.
   */
  signatureMethod?: "baul" | "biometria" | null;
  /** Veredicto del servidor (vigencia + validación propia). Si viene, manda sobre el cálculo local. */
  signatureValid?: boolean;
  signatureInvalidReason?: string | null;
}

/**
 * `valid` = el mandatario tiene SU validación biométrica APROBADA, sin renovación mientras su vigencia
 * propia esté activa (HU #13130b: la ventana de 30 días rige solo el trámite). HU #13248 (F9): `pending`
 * (enviada o en proceso) y `expired` NO cuentan; solo la aprobada habilita la firma.
 */
const IDENTIDAD_RESUELTA: readonly string[] = ["valid"];

/**
 * Si el mandatario puede firmar electrónicamente: con firma del baúl, o con una validación de
 * identidad vigente o en camino.
 *
 * <p>HU #13132 (HU #13122 en el backend): el correo YA NO cuenta como medio de firma. Es solo un dato
 * de contacto; la biometría la origina y vigila el módulo Identidad.</p>
 */
export function puedeFirmarElectronicamente(medio: MedioDeFirma): boolean {
  if (medio.signatureValid !== undefined) return medio.signatureValid;
  if (medio.signatureVaultId && medio.signatureMethod !== "biometria") return true;
  return IDENTIDAD_RESUELTA.includes(medio.identityStatus ?? "none");
}

/**
 * Organismos de `seleccionados` en los que el mandatario quedaría sin poder firmar. Vacío ⇒ se puede
 * habilitar en todos.
 *
 * <p>HU #13133: la firma física ya no es medio de firma, así que no hay organismos exentos.</p>
 */
export function organismosSinMedioDeFirma(
  seleccionados: readonly string[],
  medio: MedioDeFirma,
): string[] {
  return puedeFirmarElectronicamente(medio) ? [] : [...seleccionados];
}

/** Qué le falta al mandatario, para decírselo al gestor en vez de un «no se pudo guardar». */
export function motivoSinFirma(medio: MedioDeFirma): string {
  if (medio.signatureInvalidReason === "mandatario_fuera_de_vigencia") return "Su vigencia terminó.";
  if (medio.signatureMethod === "biometria") return "Pendiente de validación de identidad.";
  return "Aún no tiene una firma guardada en el baúl ni su validación de identidad aprobada.";
}

/** Cómo se presenta el medio de firma del mandatario en listados del OT. */
export type TipoFirmaMandatario =
  | "baul"
  | "identidad"
  | "identidad_pendiente"
  | "identidad_sin_validar"
  | "sin_medio";

export function tipoDeFirmaMandatario(medio: MedioDeFirma): TipoFirmaMandatario {
  const conIdentidad = medio.signatureMethod === "biometria";
  if (medio.signatureVaultId && !conIdentidad) return "baul";
  if (medio.identityStatus === "valid") return "identidad";
  if (medio.identityStatus === "pending") return "identidad_pendiente";
  // Con validación de identidad elegida y sin validación propia: no es «sin medio», está pendiente.
  if (conIdentidad) return "identidad_sin_validar";
  return "sin_medio";
}

export function etiquetaTipoFirma(tipo: TipoFirmaMandatario): string {
  switch (tipo) {
    case "baul":
      return "Baúl de firmas";
    case "identidad":
      return "Validación de identidad";
    case "identidad_pendiente":
      return "Identidad en curso";
    case "identidad_sin_validar":
      return "Validación pendiente";
    default:
      return "Sin medio de firma";
  }
}

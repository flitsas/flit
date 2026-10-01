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
}

/**
 * `valid` = el mandatario tiene una validación biométrica APROBADA, sin renovación mientras su vigencia
 * propia esté activa (HU #13130b: la ventana de 30 días rige solo el trámite). `expired` ya no significa
 * «aprobada hace más de 30 días» y NO cuenta. `pending` sí — la validación va en camino y el mandatario
 * podrá firmar cuando llegue.
 */
const IDENTIDAD_RESUELTA_O_EN_CURSO: readonly string[] = ["valid", "pending"];

/**
 * Si el mandatario puede firmar electrónicamente: con firma del baúl, o con una validación de
 * identidad vigente o en camino.
 *
 * <p>HU #13132 (HU #13122 en el backend): el correo YA NO cuenta como medio de firma. Es solo un dato
 * de contacto; la biometría la origina y vigila el módulo Identidad.</p>
 */
export function puedeFirmarElectronicamente(medio: MedioDeFirma): boolean {
  if (medio.signatureVaultId) return true;
  return IDENTIDAD_RESUELTA_O_EN_CURSO.includes(medio.identityStatus ?? "none");
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
export function motivoSinFirma(_medio: MedioDeFirma): string {
  return "No tiene firma en el baúl ni validación de identidad aprobada.";
}

/** Cómo se presenta el medio de firma del mandatario en listados del OT. */
export type TipoFirmaMandatario =
  | "baul"
  | "identidad"
  | "identidad_pendiente"
  | "sin_medio";

export function tipoDeFirmaMandatario(medio: MedioDeFirma): TipoFirmaMandatario {
  if (medio.signatureVaultId) return "baul";
  if (medio.identityStatus === "valid") return "identidad";
  if (medio.identityStatus === "pending") return "identidad_pendiente";
  return "sin_medio";
}

export function etiquetaTipoFirma(tipo: TipoFirmaMandatario): string {
  switch (tipo) {
    case "baul":
      return "Firma del baúl";
    case "identidad":
      return "Validación de identidad";
    case "identidad_pendiente":
      return "Identidad en curso";
    default:
      return "Sin medio de firma";
  }
}

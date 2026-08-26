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
  /** Correo al que se envía la validación de identidad al registrarlo. */
  email?: string | null;
  identityStatus?: MandatarioIdentityStatus | null;
}

/**
 * `expired` NO cuenta: una validación vencida no estampa sello y renovarla es una acción explícita
 * del gestor. `pending` sí — la validación va en camino y el mandatario podrá firmar cuando llegue.
 */
const IDENTIDAD_RESUELTA_O_EN_CURSO: readonly string[] = ["valid", "pending"];

/**
 * Si el mandatario puede firmar electrónicamente: con firma del baúl, o con una validación de
 * identidad vigente o en camino.
 *
 * <p>El correo cuenta porque un mandatario nuevo todavía no tiene identidad vigente —se le envía al
 * registrarlo—, y exigir `valid` haría imposible dar de alta a nadie que no tuviera ya firma en el
 * baúl.</p>
 */
export function puedeFirmarElectronicamente(medio: MedioDeFirma): boolean {
  if (medio.signatureVaultId) return true;
  if (medio.email && medio.email.trim() !== "") return true;
  return IDENTIDAD_RESUELTA_O_EN_CURSO.includes(medio.identityStatus ?? "none");
}

/**
 * Organismos de `seleccionados` en los que el mandatario quedaría sin poder firmar. Vacío ⇒ se puede
 * habilitar en todos.
 *
 * <p>Los marcados como de firma física quedan exentos: ahí la línea en blanco es el resultado
 * correcto, porque el gestor eligió que ese organismo se firme a mano.</p>
 */
export function organismosSinMedioDeFirma(
  seleccionados: readonly string[],
  fisicos: readonly string[],
  medio: MedioDeFirma,
): string[] {
  if (puedeFirmarElectronicamente(medio)) return [];
  const aMano = new Set(fisicos);
  return seleccionados.filter((id) => !aMano.has(id));
}

/** Qué le falta al mandatario, para decírselo al gestor en vez de un «no se pudo guardar». */
export function motivoSinFirma(medio: MedioDeFirma): string {
  return medio.identityStatus === "expired"
    ? "Su validación de identidad está vencida y no tiene firma en el baúl."
    : "No tiene firma en el baúl ni validación de identidad.";
}

/**
 * Tipos documentales «múltiples» (bolsas): subir uno NUNCA reemplaza al anterior, se acumulan
 * (HU #12046). Espejo de `AttachmentRules.TiposMultiples` del backend (core-api); si cambia allá,
 * cambiar aquí. Comparar siempre en minúsculas.
 */
export const TIPOS_DOCUMENTO_MULTIPLES: ReadonlySet<string> = new Set(["otro", "anexos_generales"]);

export function esTipoDocumentoMultiple(tipo: string): boolean {
  return TIPOS_DOCUMENTO_MULTIPLES.has(tipo.toLowerCase());
}

/**
 * Presentación del número de documento en listados.
 *
 * <p>Los listados mostraban el documento enmascarado (`••••1234`). El negocio pidió verlo completo
 * porque los últimos cuatro dígitos no bastan para identificar a la persona durante la operación.
 * La función se conserva como punto único para que ese criterio se pueda cambiar en un solo sitio:
 * antes la lógica de enmascarado estaba copiada en cinco componentes.</p>
 */
export function formatDocumentNumber(documentNumber: string | null | undefined): string {
  return documentNumber?.trim() ?? "";
}

/** Documento precedido de su tipo (p. ej. "CC 1020304050"). Omite el que falte. */
export function formatDocumentWithType(
  documentType: string | null | undefined,
  documentNumber: string | null | undefined,
): string {
  return [documentType?.trim(), formatDocumentNumber(documentNumber)]
    .filter(Boolean)
    .join(" ");
}

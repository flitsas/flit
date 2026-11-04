import { TriangleAlert } from "lucide-react";

export const TEXTO_MODO_PRUEBAS = "Modo de pruebas: la validación de identidad no es obligatoria en este entorno.";

/** Aviso discreto (tono advertencia) que solo se pinta con `pruebas.omitirIdentidad` activo. */
export function AvisoModoPruebas({ id }: { id?: string }) {
  return (
    <p id={id} role="status" className="flex items-center gap-2 rounded-[9px] border border-warning bg-warning/10 p-3 text-sm text-warning">
      <TriangleAlert aria-hidden className="h-4 w-4 shrink-0" />
      {TEXTO_MODO_PRUEBAS}
    </p>
  );
}

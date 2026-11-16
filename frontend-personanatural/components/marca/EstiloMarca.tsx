// HU #14379 — Tokens de la marca dentro del HTML del servidor: la primera pintura ya sale con los colores del organismo.
// Componente de servidor: el CSS lo arma `cssDeMarca` solo con colores #RRGGBB validados.
import { cssDeMarca, type MarcaAplicable } from "@/lib/marca/marca";

export function EstiloMarca({ marca }: { marca: MarcaAplicable | null }) {
  if (!marca) return null;
  const css = cssDeMarca(marca);
  if (!css) return null;
  return <style data-marca-punto={marca.version}>{css}</style>;
}

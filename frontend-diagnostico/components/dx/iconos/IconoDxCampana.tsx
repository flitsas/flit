import { BaseIconoDx, type IconoDxProps } from "./BaseIconoDx";

/** Campana de avisos de Diagnóstico. Familia IconoDx (HU #14562 · UI-3): se importa por ruta, sin barril. */
export function IconoDxCampana(props: Omit<IconoDxProps, "children" | "viewBox">) {
  return (
    <BaseIconoDx {...props}>
      <path d="M256 704V448a256 256 0 0 1 512 0v256l80 96H176z" />
      <path d="M416 864a96 96 0 0 0 192 0" />
    </BaseIconoDx>
  );
}

import { BaseIconoDx, type IconoDxProps } from "./BaseIconoDx";

/** Avalúo comercial (etiqueta de precio). Familia IconoDx (HU #14562 · UI-3): se importa por ruta, sin barril. */
export function IconoDxAvaluo(props: Omit<IconoDxProps, "children">) {
  return (
    <BaseIconoDx {...props}>
      <path d="M128 480V192a64 64 0 0 1 64-64h288l384 384a64 64 0 0 1 0 90L592 874a64 64 0 0 1-90 0z" />
      <circle cx="320" cy="320" r="56" />
    </BaseIconoDx>
  );
}

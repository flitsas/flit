import { BaseIconoDx, type IconoDxProps } from "./BaseIconoDx";

/** Impuesto vehicular (edificio público). Familia IconoDx (HU #14562 · UI-3): se importa por ruta, sin barril. */
export function IconoDxImpuesto(props: Omit<IconoDxProps, "children">) {
  return (
    <BaseIconoDx {...props}>
      <path d="M128 384L512 160l384 224zM224 384v352M416 384v352M608 384v352M800 384v352M128 864h768" />
    </BaseIconoDx>
  );
}

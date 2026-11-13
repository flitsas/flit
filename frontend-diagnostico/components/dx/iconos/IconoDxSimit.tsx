import { BaseIconoDx, type IconoDxProps } from "./BaseIconoDx";

/** SIMIT del vehículo (tiquete de multa). Familia IconoDx (HU #14562 · UI-3): se importa por ruta, sin barril. */
export function IconoDxSimit(props: Omit<IconoDxProps, "children" | "viewBox">) {
  return (
    <BaseIconoDx {...props}>
      <path d="M160 256h704v160a96 96 0 0 0 0 192v160H160V608a96 96 0 0 0 0-192z" />
      <path d="M512 384v144M512 640v8" />
    </BaseIconoDx>
  );
}

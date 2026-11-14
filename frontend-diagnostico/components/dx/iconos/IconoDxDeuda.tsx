import { BaseIconoDx, type IconoDxProps } from "./BaseIconoDx";

/** Estado con deuda (billete con moneda al centro). Familia IconoDx (HU #14562 · UI-3): se importa por ruta, sin barril. */
export function IconoDxDeuda(props: Omit<IconoDxProps, "children">) {
  return (
    <BaseIconoDx {...props}>
      <rect x="96" y="256" width="832" height="512" rx="96" />
      <circle cx="512" cy="512" r="112" />
      <path d="M224 512v0M800 512v0" />
    </BaseIconoDx>
  );
}

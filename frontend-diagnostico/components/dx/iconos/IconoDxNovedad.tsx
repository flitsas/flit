import { BaseIconoDx, type IconoDxProps } from "./BaseIconoDx";

/** Estado novedad (círculo con «i» informativa). Familia IconoDx (HU #14562 · UI-3): se importa por ruta, sin barril. */
export function IconoDxNovedad(props: Omit<IconoDxProps, "children">) {
  return (
    <BaseIconoDx {...props}>
      <circle cx="512" cy="512" r="384" />
      <path d="M512 336v0M512 464v224" />
    </BaseIconoDx>
  );
}

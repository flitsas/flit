import { BaseIconoDx, type IconoDxProps } from "./BaseIconoDx";

/** Estado pendiente (reloj). Familia IconoDx (HU #14562 · UI-3): se importa por ruta, sin barril. */
export function IconoDxPendiente(props: Omit<IconoDxProps, "children">) {
  return (
    <BaseIconoDx {...props}>
      <circle cx="512" cy="512" r="384" />
      <path d="M512 304v208l144 96" />
    </BaseIconoDx>
  );
}

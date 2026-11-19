import { BaseIconoDx, type IconoDxProps } from "./BaseIconoDx";

/** Estado error (círculo con equis). Familia IconoDx (HU #14562 · UI-3): se importa por ruta, sin barril. */
export function IconoDxError(props: Omit<IconoDxProps, "children">) {
  return (
    <BaseIconoDx {...props}>
      <circle cx="512" cy="512" r="384" />
      <path d="M384 384l256 256M640 384L384 640" />
    </BaseIconoDx>
  );
}

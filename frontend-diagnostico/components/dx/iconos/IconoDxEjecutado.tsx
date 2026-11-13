import { BaseIconoDx, type IconoDxProps } from "./BaseIconoDx";

/** Estado ejecutado (círculo con visto). Familia IconoDx (HU #14562 · UI-3): se importa por ruta, sin barril. */
export function IconoDxEjecutado(props: Omit<IconoDxProps, "children">) {
  return (
    <BaseIconoDx {...props}>
      <circle cx="512" cy="512" r="384" />
      <path d="M352 528l112 112 224-256" />
    </BaseIconoDx>
  );
}

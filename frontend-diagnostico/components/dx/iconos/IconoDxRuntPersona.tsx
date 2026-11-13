import { BaseIconoDx, type IconoDxProps } from "./BaseIconoDx";

/** RUNT de una persona (propietario o locatario). Familia IconoDx (HU #14562 · UI-3): se importa por ruta, sin barril. */
export function IconoDxRuntPersona(props: Omit<IconoDxProps, "children" | "viewBox">) {
  return (
    <BaseIconoDx {...props}>
      <circle cx="384" cy="320" r="160" />
      <path d="M128 864c0-176 112-288 256-288 64 0 120 20 160 56" />
      <circle cx="736" cy="640" r="96" />
      <path d="M736 736v160M736 832h80" />
    </BaseIconoDx>
  );
}

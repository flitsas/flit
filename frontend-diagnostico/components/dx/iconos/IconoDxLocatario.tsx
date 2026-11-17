import { BaseIconoDx, type IconoDxProps } from "./BaseIconoDx";

/** Locatario (persona con un documento, glifo `f-locatario` de la maqueta). Familia IconoDx (HU #14562 · UI-3): se importa por ruta, sin barril. */
export function IconoDxLocatario(props: Omit<IconoDxProps, "children">) {
  return (
    <BaseIconoDx {...props}>
      <circle cx="384" cy="320" r="160" />
      <path d="M128 864c0-176 112-288 256-288 48 0 96 12 136 36" />
      <rect x="608" y="560" width="288" height="336" rx="48" />
      <path d="M688 688h128M688 800h80" />
    </BaseIconoDx>
  );
}

import { BaseIconoDx, type IconoDxProps } from "./BaseIconoDx";

/** Licencia de conducción. Familia IconoDx (HU #14562 · UI-3): se importa por ruta, sin barril. */
export function IconoDxLicencia(props: Omit<IconoDxProps, "children" | "viewBox">) {
  return (
    <BaseIconoDx {...props}>
      <rect x="112" y="208" width="800" height="608" rx="96" />
      <circle cx="336" cy="448" r="88" />
      <path d="M208 672c16-64 64-96 128-96s112 32 128 96" />
      <path d="M592 432h208M592 560h208M592 688h96" />
    </BaseIconoDx>
  );
}

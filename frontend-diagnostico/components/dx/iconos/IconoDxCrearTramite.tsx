import { BaseIconoDx, type IconoDxProps } from "./BaseIconoDx";

/** Crear trámite (derivar a Trámites). Familia IconoDx (HU #14562 · UI-3): se importa por ruta, sin barril. */
export function IconoDxCrearTramite(props: Omit<IconoDxProps, "children" | "viewBox">) {
  return (
    <BaseIconoDx {...props}>
      <path d="M128 512h416M416 384l128 128-128 128M640 160h192a64 64 0 0 1 64 64v576a64 64 0 0 1-64 64H640" />
    </BaseIconoDx>
  );
}

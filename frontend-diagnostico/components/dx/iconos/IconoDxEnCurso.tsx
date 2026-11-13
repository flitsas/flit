import { BaseIconoDx, type IconoDxProps } from "./BaseIconoDx";

/** Estado en curso (arco abierto con flecha, quieto en la grilla). Familia IconoDx (HU #14562 · UI-3): se importa por ruta, sin barril. */
export function IconoDxEnCurso(props: Omit<IconoDxProps, "children">) {
  return (
    <BaseIconoDx {...props}>
      <path d="M832 512a320 320 0 1 1-94-226" />
      <path d="M624 286h114V172" />
    </BaseIconoDx>
  );
}

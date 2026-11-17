import { BaseIconoDx, type IconoDxProps } from "./BaseIconoDx";

/** Origen manual del backoffice (lápiz sobre la línea de escritura). Familia IconoDx (HU #14562 · UI-3): se importa por ruta, sin barril. */
export function IconoDxManual(props: Omit<IconoDxProps, "children">) {
  return (
    <BaseIconoDx {...props}>
      <path d="M640 160l160 160-416 416H224V576z" />
      <path d="M560 240l160 160M512 864h320" />
    </BaseIconoDx>
  );
}

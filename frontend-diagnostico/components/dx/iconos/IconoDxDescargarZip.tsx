import { BaseIconoDx, type IconoDxProps } from "./BaseIconoDx";

/** Descargar ZIP de evidencias (archivo con flecha hacia abajo). Familia IconoDx (HU #14562 · UI-3): se importa por ruta, sin barril. */
export function IconoDxDescargarZip(props: Omit<IconoDxProps, "children">) {
  return (
    <BaseIconoDx {...props}>
      <path d="M256 96h368l208 208v560a64 64 0 0 1-64 64H256a64 64 0 0 1-64-64V160a64 64 0 0 1 64-64z" />
      <path d="M624 96v208h208" />
      <path d="M512 448v320M384 640l128 128 128-128" />
    </BaseIconoDx>
  );
}

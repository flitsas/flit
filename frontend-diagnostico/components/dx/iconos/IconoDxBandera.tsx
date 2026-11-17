import { BaseIconoDx, type IconoDxProps } from "./BaseIconoDx";

/**
 * Bandera de saneamiento (spec-ui §1.2): mástil y paño con cola de golondrina. Hueca = sin solicitar; rellena = con
 * saneamiento, así el estado se distingue por la forma y no solo por el color. Familia IconoDx: se importa por ruta.
 */
export function IconoDxBandera({ encendida = false, ...props }: Omit<IconoDxProps, "children"> & { encendida?: boolean }) {
  return (
    <BaseIconoDx {...props}>
      <path d="M256 896V128" />
      <path d="M256 176h512l-112 176 112 176H256z" fill={encendida ? "currentColor" : "none"} />
    </BaseIconoDx>
  );
}

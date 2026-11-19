import { BaseIconoDx, type IconoDxProps } from "./BaseIconoDx";

/**
 * Dictamen del vehículo (documento con visto, bicolor). Familia IconoDx (HU #14562 · UI-3): se importa por ruta, sin barril.
 * HU #14562 · m-10: el visto va con la superficie (blanco en claro, como la maqueta `m-dictamen`): sobre el acento da 3,6:1;
 * con el tinte daba 2,1:1.
 */
export function IconoDxDictamen(props: Omit<IconoDxProps, "children">) {
  return (
    <BaseIconoDx {...props}>
      <path d="M256 96h368l208 208v560a64 64 0 0 1-64 64H256a64 64 0 0 1-64-64V160a64 64 0 0 1 64-64z" />
      <path d="M624 96v208h208M320 448h256M320 576h160" />
      <circle cx="704" cy="752" r="176" style={{ fill: "var(--dx-icono-acento)", stroke: "var(--dx-icono-acento)" }} />
      <path d="M616 752l64 64 112-112" style={{ stroke: "var(--di-surface-elevated)" }} />
    </BaseIconoDx>
  );
}

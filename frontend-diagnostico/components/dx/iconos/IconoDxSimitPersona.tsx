import { BaseIconoDx, type IconoDxProps } from "./BaseIconoDx";

/** SIMIT de una persona. Familia IconoDx (HU #14562 · UI-3): se importa por ruta, sin barril. */
export function IconoDxSimitPersona(props: Omit<IconoDxProps, "children">) {
  return (
    <BaseIconoDx {...props}>
      <circle cx="352" cy="320" r="144" />
      <path d="M112 864c0-160 104-272 240-272 56 0 104 16 144 44" />
      <path d="M576 576h336v96a48 48 0 0 0 0 96v96H576v-96a48 48 0 0 0 0-96z" />
    </BaseIconoDx>
  );
}

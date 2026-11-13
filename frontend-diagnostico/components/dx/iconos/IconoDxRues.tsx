import { BaseIconoDx, type IconoDxProps } from "./BaseIconoDx";

/** RUES (edificio de la empresa). Familia IconoDx (HU #14562 · UI-3): se importa por ruta, sin barril. */
export function IconoDxRues(props: Omit<IconoDxProps, "children" | "viewBox">) {
  return (
    <BaseIconoDx {...props}>
      <rect x="192" y="96" width="640" height="832" rx="64" />
      <path d="M416 928V768h192v160M352 256v8M512 256v8M672 256v8M352 416v8M512 416v8M672 416v8M352 576v8M512 576v8M672 576v8" />
    </BaseIconoDx>
  );
}

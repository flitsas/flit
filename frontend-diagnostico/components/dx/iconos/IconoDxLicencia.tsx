import { BaseIconoDx, type IconoDxProps } from "./BaseIconoDx";

/**
 * Licencia de conducción. Familia IconoDx (HU #14562 · UI-3): se importa por ruta, sin barril. Se distingue de IconoDxRunt
 * (auditoría m-14): franja superior de tarjeta oficial, foto rectangular a la izquierda y un volante pequeño abajo a la derecha.
 */
export function IconoDxLicencia(props: Omit<IconoDxProps, "children">) {
  return (
    <BaseIconoDx {...props}>
      <rect x="96" y="192" width="832" height="640" rx="96" />
      <path d="M96 336h832" />
      <rect x="192" y="432" width="208" height="272" rx="32" />
      <path d="M496 464h304M496 576h160" />
      <circle cx="752" cy="672" r="80" />
      <path d="M672 672h160M752 672v80" />
    </BaseIconoDx>
  );
}

import { createElement } from "react";
import { appIcon } from "@flit/shell/apps";
import { IconoModulo } from "@flit/ui/Icono";
import { MAPA_ICONO } from "@flit/ui/iconos-mapas";

/** Degradado de marca de la propuesta (`--di-gradient-brand`, azul → cian). */
export const BRAND_GRADIENT = "var(--di-gradient-brand)";

/** Ícono de un producto sobre el degradado de marca; apagado (gris) si todavía no está disponible. */
export function ProductIcon({ icon, size = "md", muted = false }: { icon: string; size?: "md" | "lg"; muted?: boolean }) {
  const box = size === "lg" ? "h-14 w-14 rounded-2xl" : "h-11 w-11 rounded-xl";
  const glyph = size === "lg" ? "h-7 w-7" : "h-5 w-5";
  return (
    <span
      className={`grid shrink-0 place-items-center ${box} ${
        muted ? "bg-secondary text-flit-brand" : "text-white shadow-[var(--shadow-button)]"
      }`}
      style={muted ? undefined : { background: BRAND_GRADIENT }}
    >
      {createElement(appIcon(icon), { className: glyph, "aria-hidden": true })}
    </span>
  );
}

/** Lado del disco del ícono del producto, en px (mismo hueco que la ilustración anterior: 36, 64 y 80). */
const ARTE = { sm: 36, md: 64, lg: 80 } as const;

/**
 * Ícono del producto del Manual UI Kit 2026 en su disco (`IconoModulo`): el mismo que usan la cabecera y el dock de cada
 * producto (`tramites`, `comparendos`, `diagnostico`). Es decorativa: el nombre va al lado, en texto. Si
 * el producto no tiene ilustración se queda con su ícono de siempre. «Próximamente» (`muted`) la apaga, como el menú
 * de productos de la barra.
 */
export function ProductArt({
  code,
  icon,
  size = "md",
  muted = false,
}: {
  code: string;
  icon: string;
  size?: keyof typeof ARTE;
  muted?: boolean;
}) {
  if (!(code in MAPA_ICONO)) return <ProductIcon icon={icon} size={size === "lg" ? "lg" : "md"} muted={muted} />;
  return <IconoModulo clave={code} disco size={ARTE[size]} className={`shrink-0 ${muted ? "opacity-45 grayscale-[0.4]" : ""}`} />;
}

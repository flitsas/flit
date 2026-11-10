import { createElement } from "react";
import { appIcon } from "@flit/shell/apps";
import { ilustracionDe } from "@flit/ui/ilustraciones";

/** Degradado de marca de Trámites (cian → azul). */
export const BRAND_GRADIENT = "linear-gradient(120deg,var(--color-flit-tech) 0%,var(--color-flit-brand) 100%)";

/** Ícono de un producto sobre el degradado de marca; apagado (gris) si todavía no está disponible. */
export function ProductIcon({ icon, size = "md", muted = false }: { icon: string; size?: "md" | "lg"; muted?: boolean }) {
  const box = size === "lg" ? "h-14 w-14 rounded-2xl" : "h-11 w-11 rounded-xl";
  const glyph = size === "lg" ? "h-7 w-7" : "h-5 w-5";
  return (
    <span
      className={`grid shrink-0 place-items-center ${box} ${
        muted ? "bg-secondary text-flit-brand" : "text-white shadow-[var(--nav-sombra-activo)]"
      }`}
      style={muted ? undefined : { background: BRAND_GRADIENT }}
    >
      {createElement(appIcon(icon), { className: glyph, "aria-hidden": true })}
    </span>
  );
}

const ARTE = { sm: "h-9 w-9", md: "h-16 w-16", lg: "h-20 w-20" } as const;

/**
 * Ilustración del producto de la propuesta «Diseño Inteligente» (HU #14487): la misma WebP que usan la cabecera y el
 * dock de cada producto (`tramites`, `comparendos`, `diagnostico`). Es decorativa: el nombre va al lado, en texto. Si
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
  const src = ilustracionDe(code);
  if (!src) return <ProductIcon icon={icon} size={size === "lg" ? "lg" : "md"} muted={muted} />;
  return (
    // eslint-disable-next-line @next/next/no-img-element -- ilustración decorativa estática
    <img src={src} alt="" aria-hidden="true" className={`${ARTE[size]} shrink-0 object-contain ${muted ? "opacity-45 grayscale-[0.4]" : ""}`} />
  );
}

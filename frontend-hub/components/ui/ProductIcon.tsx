import { createElement } from "react";
import { appIcon } from "@flit/shell/apps";

/** Degradado de marca de Trámites (cian → azul). */
export const BRAND_GRADIENT = "linear-gradient(120deg,#00dbd5 0%,#557eff 100%)";

/** Ícono de un producto sobre el degradado de marca; apagado (gris) si todavía no está disponible. */
export function ProductIcon({ icon, size = "md", muted = false }: { icon: string; size?: "md" | "lg"; muted?: boolean }) {
  const box = size === "lg" ? "h-14 w-14 rounded-2xl" : "h-11 w-11 rounded-xl";
  const glyph = size === "lg" ? "h-7 w-7" : "h-5 w-5";
  return (
    <span
      className={`grid shrink-0 place-items-center ${box} ${
        muted ? "bg-[var(--color-flit-bg)] text-flit-brand dark:bg-white/10" : "text-white shadow-[var(--nav-sombra-activo)]"
      }`}
      style={muted ? undefined : { background: BRAND_GRADIENT }}
    >
      {createElement(appIcon(icon), { className: glyph, "aria-hidden": true })}
    </span>
  );
}

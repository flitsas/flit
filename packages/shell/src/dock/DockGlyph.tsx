import { IconoModulo } from "@flit/ui/Icono";
import { MAPA_ICONO } from "@flit/ui/iconos-mapas";
import type { NavIcon } from "../nav";

/** Clave del kit a partir de la ruta que declara el catálogo (`/assets/kit/modulo/reportes.svg`, `…/reportes.webp`). */
function claveDe(ruta: string): string | null {
  const nombre = ruta.split("/").pop()?.replace(/\.(svg|webp|png)$/, "") ?? "";
  return nombre in MAPA_ICONO ? nombre : null;
}

/**
 * Lo que va a la izquierda de cada píldora y entrada del dock: el ícono del Manual UI Kit 2026 de la clave que el
 * catálogo declara (en currentColor, así sigue el azul del activo y el tema); si la clave no tiene ícono en el kit,
 * el ícono de línea de siempre. Siempre decorativa: el nombre ya lo lleva el texto o el aria-label del control.
 */
export function DockGlyph({ ilustracion, icon: Icon, className }: { ilustracion?: string; icon: NavIcon; className?: string }) {
  const clave = ilustracion ? claveDe(ilustracion) : null;
  if (clave) {
    return (
      <span aria-hidden="true" className={`dock-glifo dock-glifo--icono ${className ?? ""}`}>
        <IconoModulo clave={clave} size="66%" />
      </span>
    );
  }
  return (
    <span aria-hidden="true" className={`dock-glifo dock-glifo--icono ${className ?? ""}`}>
      <Icon className="h-[55%] w-[55%]" strokeWidth={1.75} aria-hidden="true" />
    </span>
  );
}

import type { NavIcon } from "../nav";

/**
 * Lo que va a la izquierda de cada píldora y entrada del dock (HU #14479): la ilustración del prototipo «Diseño
 * Inteligente» cuando el catálogo la declara; si no, el ícono de línea de siempre. Siempre decorativa: el nombre ya
 * lo lleva el texto o el aria-label del control.
 */
export function DockGlyph({ ilustracion, icon: Icon, className }: { ilustracion?: string; icon: NavIcon; className?: string }) {
  if (ilustracion) {
    // eslint-disable-next-line @next/next/no-img-element
    return <img src={ilustracion} alt="" aria-hidden="true" className={`dock-glifo ${className ?? ""}`} />;
  }
  return (
    <span aria-hidden="true" className={`dock-glifo dock-glifo--icono ${className ?? ""}`}>
      <Icon className="h-[55%] w-[55%]" strokeWidth={1.8} aria-hidden="true" />
    </span>
  );
}

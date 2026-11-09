"use client";

import { LogoInicio, useMarca } from "@/components/marca/MarcaPunto";
import { NAVEGACION } from "@/lib/landing/contenido";

/** `anclas`: el punto de un organismo solo muestra las secciones que dibuja (sin «Nosotros» ni «Servicios»). */
export function Cabecera({ anclas }: { anclas?: readonly string[] } = {}) {
  // HU #14403: con la marca de la web publicada se dibuja su logo (y nombre); sin marca, el logo FLIT de siempre.
  const marca = useMarca();
  return (
    <header className="dashboard-header flex min-h-16 flex-wrap py-2 shrink-0 items-center justify-between gap-4 border-b border-border">
      <LogoInicio alt="FLIT 2.0" etiqueta={marca?.nombre ? `${marca.nombre} — Ir al inicio` : "FLIT 2.0 — Ir al inicio"} />
      <nav aria-label="Secciones" className="b2c-nav flex gap-5 text-sm font-medium">
        {NAVEGACION.filter(({ ancla }) => !anclas || anclas.includes(ancla)).map(({ etiqueta, ancla }) => (
          <a key={ancla} href={`#${ancla}`} className="hover:text-primary">{etiqueta}</a>
        ))}
      </nav>
    </header>
  );
}

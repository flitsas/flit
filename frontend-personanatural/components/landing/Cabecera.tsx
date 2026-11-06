"use client";

import Link from "next/link";
import { LogoPortal, useMarca } from "@/components/marca/MarcaPunto";
import { ASSETS } from "@/lib/assets";
import { NAVEGACION } from "@/lib/landing/contenido";

export function Cabecera() {
  // HU #14403: con la marca de la web publicada se dibuja su logo (y nombre); sin marca, el logo FLIT de siempre.
  const marca = useMarca();
  return (
    <header className="dashboard-header flex min-h-16 flex-wrap py-2 shrink-0 items-center justify-between gap-4 border-b border-border">
      <Link href="/" aria-label={marca?.nombre ? `${marca.nombre} — Ir al inicio` : "FLIT 2.0 — Ir al inicio"}>
        {marca ? (
          <LogoPortal alt="FLIT 2.0" />
        ) : (
          // SVG propio: no gana nada con el optimizador de imágenes.
          // eslint-disable-next-line @next/next/no-img-element
          <img src={ASSETS.logo} alt="FLIT 2.0" className="h-9 w-auto" />
        )}
      </Link>
      <nav aria-label="Secciones" className="b2c-nav flex gap-5 text-sm font-medium">
        {NAVEGACION.map(({ etiqueta, ancla }) => (
          <a key={ancla} href={`#${ancla}`} className="hover:text-primary">{etiqueta}</a>
        ))}
      </nav>
    </header>
  );
}

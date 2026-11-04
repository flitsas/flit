"use client";

import Image from "next/image";
import { ASSETS } from "@/lib/assets";
import type { BannerVista } from "@/lib/configuracion";
import { INTERVALO_CARRUSEL_MS } from "@/lib/landing/contenido";
import { boton } from "./estilos";
import { useCarrusel } from "./useCarrusel";

interface Diapositiva {
  clave: string;
  src: string;
  alt: string;
  enlace: string | null;
  /** Imagen publicada: la sirve el BFF (con ETag), no el optimizador de Next. */
  publicada: boolean;
}

const LOCALES: readonly Diapositiva[] = ASSETS.banners.map((b) => ({ clave: b.src, src: b.src, alt: b.alt, enlace: null, publicada: false }));

/** Con banners publicados los usa; sin ninguno, los 2 banners locales del diseño. */
export function CarruselBanners({ banners }: { banners?: readonly BannerVista[] | null }) {
  const diapositivas: readonly Diapositiva[] = banners && banners.length > 0 ? banners.map((b) => ({ clave: b.id || b.src, src: b.src, alt: b.alt, enlace: b.enlace, publicada: true })) : LOCALES;
  const { activa: indice, ir } = useCarrusel(diapositivas.length, INTERVALO_CARRUSEL_MS);
  // Si la lista cambia a una más corta, el índice guardado puede quedar fuera de rango.
  const activa = indice < diapositivas.length ? indice : 0;
  return (
    <section aria-label="Comunicados" aria-roledescription="carrusel" className="dashboard-banner relative">
      <div className="h-full overflow-hidden rounded-[9px] border border-border">
        <div className="flex h-full transition-transform duration-500 motion-reduce:transition-none" style={{ transform: `translateX(-${activa * 100}%)` }}>
          {diapositivas.map((b, i) => (
            <div key={b.clave} aria-hidden={i !== activa} className="relative h-full w-full shrink-0">
              <Image src={b.src} alt={b.alt} fill priority={i === 0} unoptimized={b.publicada} sizes="(max-width: 900px) 100vw, 75vw" className="object-contain" />
              {b.enlace && (
                <a
                  href={b.enlace}
                  target="_blank"
                  rel="noopener noreferrer"
                  aria-label={`${b.alt} (se abre en una pestaña nueva)`}
                  tabIndex={i === activa ? 0 : -1}
                  className="absolute inset-0 focus-visible:outline-2 focus-visible:outline-ring"
                />
              )}
            </div>
          ))}
        </div>
      </div>
      {diapositivas.length > 1 && (
        <div className="carousel-dots flex items-center justify-center gap-1">
          {diapositivas.map((b, i) => (
            <button key={b.clave} type="button" aria-label={`Ir al banner ${i + 1}`} aria-current={i === activa} onClick={() => ir(i)} className={boton("ghost", "h-5 w-8 p-0", "none")}>
              <span className={`banner-dot h-2 rounded-full ${i === activa ? "w-5 bg-primary" : "w-2 bg-primary/25"}`} />
            </button>
          ))}
        </div>
      )}
    </section>
  );
}

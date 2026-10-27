"use client";

import Image from "next/image";
import { ASSETS } from "@/lib/assets";
import { INTERVALO_CARRUSEL_MS } from "@/lib/landing/contenido";
import { boton } from "./estilos";
import { useCarrusel } from "./useCarrusel";

export function CarruselBanners() {
  const banners = ASSETS.banners;
  const { activa, ir } = useCarrusel(banners.length, INTERVALO_CARRUSEL_MS);
  return (
    <section aria-label="Comunicados" aria-roledescription="carrusel" className="dashboard-banner relative">
      <div className="h-full overflow-hidden rounded-[9px] border border-border">
        <div className="flex h-full transition-transform duration-500 motion-reduce:transition-none" style={{ transform: `translateX(-${activa * 100}%)` }}>
          {banners.map((b, i) => (
            <div key={b.src} aria-hidden={i !== activa} className="relative h-full w-full shrink-0">
              <Image src={b.src} alt={b.alt} fill priority={i === 0} sizes="(max-width: 900px) 100vw, 75vw" className="object-contain" />
            </div>
          ))}
        </div>
      </div>
      <div className="carousel-dots flex items-center justify-center gap-1">
        {banners.map((b, i) => (
          <button key={b.src} type="button" aria-label={`Ir al banner ${i + 1}`} aria-current={i === activa} onClick={() => ir(i)} className={boton("ghost", "h-5 w-8 p-0", "none")}>
            <span className={`banner-dot h-2 rounded-full ${i === activa ? "w-5 bg-primary" : "w-2 bg-primary/25"}`} />
          </button>
        ))}
      </div>
    </section>
  );
}

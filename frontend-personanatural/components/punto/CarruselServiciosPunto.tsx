"use client";

// Carrusel de imágenes del OT en la columna lateral del punto, en lugar de «Servicios Adicionales». Las imágenes y sus
// enlaces los configura el organismo en su hub (Diseño) y llegan validados con la marca (`marca.carrusel`): rutas del propio
// BFF y enlaces http/https. Proporción 3:2 (recomendado 900 × 600 px): la imagen llena el recuadro y recorta un poco los
// lados en pantallas angostas. Avanza solo, se detiene al pasar el cursor o enfocar, y con «reducir movimiento» solo a mano.
import { useState } from "react";
import { boton } from "@/components/landing/estilos";
import { useCarrusel } from "@/components/landing/useCarrusel";
import { INTERVALO_CARRUSEL_MS } from "@/lib/landing/contenido";
import type { ItemCarrusel } from "@/lib/marca/marca";

export function CarruselServiciosPunto({ items }: { items: readonly ItemCarrusel[] }) {
  const [pausado, setPausado] = useState(false);
  // El avance automático se apaga mientras la persona interactúa (sin diapositivas que contar, el hook no programa nada).
  const { activa: indice, ir } = useCarrusel(pausado ? 0 : items.length, INTERVALO_CARRUSEL_MS);
  const activa = indice < items.length ? indice : 0;

  return (
    <section
      aria-label="Imágenes destacadas del organismo"
      aria-roledescription="carrusel"
      className="dashboard-card carrusel-punto relative overflow-hidden !p-0"
      onMouseEnter={() => setPausado(true)}
      onMouseLeave={() => setPausado(false)}
      onFocus={() => setPausado(true)}
      onBlur={() => setPausado(false)}
    >
      <div className="aspect-[3/2] w-full overflow-hidden">
        <div className="flex h-full transition-transform duration-500 motion-reduce:transition-none" style={{ transform: `translateX(-${activa * 100}%)` }}>
          {items.map((item, i) => (
            <div key={item.imagenUrl} aria-hidden={i !== activa} className="relative h-full w-full shrink-0">
              {/* eslint-disable-next-line @next/next/no-img-element -- ruta del propio BFF, sin optimizador de Next */}
              <img src={item.imagenUrl} alt={item.alt} className="h-full w-full object-cover" loading={i === 0 ? "eager" : "lazy"} />
              {item.enlace && (
                <a
                  href={item.enlace}
                  target="_blank"
                  rel="noopener noreferrer"
                  aria-label={`${item.alt} (se abre en una pestaña nueva)`}
                  tabIndex={i === activa ? 0 : -1}
                  className="absolute inset-0 focus-visible:outline-2 focus-visible:outline-ring"
                />
              )}
            </div>
          ))}
        </div>
      </div>
      {items.length > 1 && (
        <div className="absolute inset-x-0 bottom-1 flex items-center justify-center gap-1">
          {items.map((item, i) => (
            <button key={item.imagenUrl} type="button" aria-label={`Ir a la imagen ${i + 1}`} aria-current={i === activa} onClick={() => ir(i)} className={boton("ghost", "h-5 w-6 p-0", "none")}>
              <span className={`h-2 rounded-full ${i === activa ? "w-4 bg-primary" : "w-2 bg-primary/40"}`} />
            </button>
          ))}
        </div>
      )}
    </section>
  );
}

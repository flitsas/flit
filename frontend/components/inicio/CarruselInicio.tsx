"use client";

import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { Icono, IconoModulo } from "@flit/ui/Icono";
import { bannerImageUrl, type ActiveBanner } from "@/lib/api/public-banners";
import { bannerAmbientGradient, useDominantColor } from "@/hooks/useDominantColor";

/**
 * Carrusel del Inicio, compartido por las vistas de Gestor, OT y SuperAdmin (rediseño de los 3 dashboards).
 * Extraído tal cual de `Dashboard.tsx`: el slide fijo va primero (HU #12242), no es configurable y no puede faltar;
 * detrás, los banners Activos del Administrador. Un banner cuya imagen falla se retira sin romper el resto (AC3).
 */

/** UI Kit 2026: la lámina `.banner-kit__lamina--degradado` del manual, con el contraste AA calculado con texto blanco. */
export const BRAND_GRADIENT = "linear-gradient(160deg, #007d79 0%, #1670b8 55%, #1d53ff 100%)";

const ROTACION_MS = 6000;

export interface SlideBienvenida {
  /** Antetítulo opcional en versalitas («TU OPERACIÓN, SIN PAUSAS»). */
  antetitulo?: string;
  titulo: string;
  cuerpo: ReactNode;
}

type Slide =
  | ({ type: "welcome" } & SlideBienvenida)
  | { type: "banner"; id: string; name: string; imageUrl: string; linkUrl: string | null };

export interface CarruselInicioProps {
  /** Slides fijos (al menos uno). Van antes de los banners. */
  bienvenida: SlideBienvenida[];
  banners: ActiveBanner[];
  className?: string;
  "data-testid"?: string;
  /** Nombre accesible de la región («Novedades de tu operación»). */
  "aria-label"?: string;
}

export function CarruselInicio({
  bienvenida,
  banners,
  className = "",
  "data-testid": testId = "dashboard-hero-banner",
  "aria-label": ariaLabel,
}: CarruselInicioProps) {
  const [failedBannerIds, setFailedBannerIds] = useState<Set<string>>(new Set());
  const markBannerFailed = useCallback((id: string) => {
    setFailedBannerIds((prev) => (prev.has(id) ? prev : new Set(prev).add(id)));
  }, []);

  const slides = useMemo<Slide[]>(
    () => [
      ...bienvenida.map((b) => ({ type: "welcome" as const, ...b })),
      ...banners
        .filter((b) => !failedBannerIds.has(b.id))
        .map((b) => ({
          type: "banner" as const,
          id: b.id,
          name: b.name,
          imageUrl: bannerImageUrl(b.id),
          linkUrl: b.linkUrl,
        })),
    ],
    [bienvenida, banners, failedBannerIds],
  );

  const [slide, setSlide] = useState(0);
  useEffect(() => {
    const id = setInterval(() => setSlide((v) => (v + 1) % slides.length), ROTACION_MS);
    return () => clearInterval(id);
  }, [slides.length]);

  // Defensivo: si un banner falla después de posicionar el índice en él, `slides` puede encoger antes de reacomodarse.
  const s = slides[slide] ?? slides[0];
  const bannerColor = useDominantColor(s.type === "banner" ? s.imageUrl : undefined);
  const fijo = s.type === "welcome";

  return (
    <section
      aria-label={ariaLabel}
      className={`relative overflow-hidden flex flex-col justify-between h-full min-h-[200px] max-sm:min-h-[180px] text-white ${
        fijo ? "rounded-[9px]" : "rounded-2xl"
      } ${className}`}
      data-testid={testId}
    >
      {/* Fondo: lámina degradada del kit en el slide fijo; en un banner, el color promedio de la imagen (respaldo
          visible cuando `object-contain` deja margen abajo de `md`). */}
      <div
        className="absolute inset-0"
        style={{ background: fijo ? BRAND_GRADIENT : bannerAmbientGradient(bannerColor) }}
      />
      {s.type === "banner" && (
        // eslint-disable-next-line @next/next/no-img-element -- imagen del banner servida por la API, tamaño libre
        <img
          src={s.imageUrl}
          alt={s.name}
          onError={() => markBannerFailed(s.id)}
          className="absolute inset-0 h-full w-full object-contain object-center md:object-cover md:object-[80%_center]"
        />
      )}
      {/* Velo para el contraste de los controles sobre cualquier imagen; sobre la lámina no hace falta. */}
      <div
        className="absolute inset-0"
        style={{
          background: fijo ? "transparent" : "linear-gradient(180deg, rgba(0,0,0,0) 45%, rgba(0,0,0,0.35) 100%)",
        }}
      />
      {fijo && <div className="absolute -right-[50px] -top-[70px] h-[190px] w-[190px] rounded-full bg-white/[0.14]" />}
      {s.type === "welcome" ? (
        <div className="relative flex flex-1 flex-col justify-center gap-2.5 max-w-[85%] px-6 pt-6 pb-16">
          {s.antetitulo && (
            <p className="text-xs font-semibold uppercase tracking-wide text-white/85">{s.antetitulo}</p>
          )}
          <div className="flex min-w-0 items-center gap-3">
            <span
              aria-hidden="true"
              className="grid h-[42px] w-[42px] shrink-0 place-items-center rounded-[9px] bg-white/[0.22]"
            >
              <IconoModulo clave="banners" size={22} />
            </span>
            <h2 className="text-2xl md:text-[1.75rem] font-bold leading-tight line-clamp-2 min-w-0 text-white">
              {s.titulo}
            </h2>
          </div>
          <p className="max-w-[58ch] text-sm md:text-base leading-snug line-clamp-3 text-white">{s.cuerpo}</p>
        </div>
      ) : s.linkUrl ? (
        <a href={s.linkUrl} target="_blank" rel="noopener noreferrer" aria-label={s.name} className="group absolute inset-0">
          <span className="absolute inset-0 bg-black/0 transition-colors duration-200 group-hover:bg-black/25" />
          <span className="absolute inset-0 flex items-center justify-center opacity-0 transition-opacity duration-200 group-hover:opacity-100">
            <span className="flex h-10 w-10 items-center justify-center rounded-full bg-white/90 text-flit-primary">
              <Icono nombre="externo" className="h-5 w-5" />
            </span>
          </span>
        </a>
      ) : (
        <span className="sr-only">{s.name}</span>
      )}
      {/* Controles fijos al fondo (Bug #12584, defecto 2): no dependen del alto del contenido vecino. */}
      <div className="absolute inset-x-0 bottom-0 flex items-center justify-between px-6 pb-5">
        <div className="flex gap-1">
          {slides.map((_, i) => (
            <button
              key={i}
              onClick={() => setSlide(i)}
              aria-label={`Slide ${i + 1}`}
              className={`h-2 rounded-full transition-all ${i === slide ? "bg-current" : "bg-white/50"}`}
              style={{ width: i === slide ? 22 : 8 }}
            />
          ))}
        </div>
        <div className="flex items-center gap-1">
          <button
            onClick={() => setSlide((v) => (v - 1 + slides.length) % slides.length)}
            disabled={slides.length <= 1}
            aria-label="Anterior"
            className="h-[30px] w-[30px] rounded-full grid place-items-center bg-white/[0.22] text-white hover:bg-white/[0.36] disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-white/[0.22] focus-visible:outline-2 focus-visible:outline-white"
          >
            <Icono nombre="izquierda" size={18} />
          </button>
          <button
            onClick={() => setSlide((v) => (v + 1) % slides.length)}
            disabled={slides.length <= 1}
            aria-label="Siguiente"
            className="h-[30px] w-[30px] rounded-full grid place-items-center bg-white/[0.22] text-white hover:bg-white/[0.36] disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-white/[0.22] focus-visible:outline-2 focus-visible:outline-white"
          >
            <Icono nombre="derecha" size={18} />
          </button>
        </div>
      </div>
    </section>
  );
}

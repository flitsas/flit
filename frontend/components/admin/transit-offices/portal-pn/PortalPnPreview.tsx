"use client";

import { useMemo, type CSSProperties } from "react";
import { isValidHexColor } from "@/lib/brand/contrast";
import type { BrandColors } from "@/lib/api/branding";
import { TEXTOS_VISTA_PREVIA_OT, textoSegunImagenes, type TextosVistaPrevia } from "./textos-diseno";

export interface PortalPnPreviewProps {
  colors: BrandColors;
  platformName: string;
  logoSrc: string | null;
  /** Primer banner del portal. */
  bannerSrc: string | null;
  /** Cantidad de banners; `undefined` si la marca no lleva banners (la web de persona): no se dibuja ese recuadro. */
  bannerCount?: number;
  /** Si el borrador tiene logo propio (para el texto del pie). Sin indicarlo, se deduce de `logoSrc`. */
  tieneLogo?: boolean;
  /** Textos propios (los del organismo por defecto). */
  textos?: TextosVistaPrevia;
  /** Primera imagen del carrusel. */
  carouselSrc?: string | null;
  /** Cantidad de imágenes del carrusel; `undefined` si la marca no lleva carrusel (no se dibuja nada). */
  carouselCount?: number;
}

/**
 * Así verá la persona el punto del organismo con esta marca, antes de publicar: encabezado con logo y nombre, banner,
 * título, botón y tarjeta de acción. Usa las mismas variables que el portal (`--primary`, `--primary-foreground`,
 * `--dashboard-action-background`), redefinidas solo dentro de este recuadro; ningún valor viene del texto escrito.
 */
export function PortalPnPreview({
  colors,
  platformName,
  logoSrc,
  bannerSrc,
  bannerCount,
  tieneLogo,
  textos = TEXTOS_VISTA_PREVIA_OT,
  carouselSrc = null,
  carouselCount,
}: PortalPnPreviewProps) {
  const scope = useMemo<CSSProperties | null>(() => {
    if (![colors.primary, colors.secondary, colors.onPrimary].every(isValidHexColor)) return null;
    return {
      ["--pv-primary" as string]: colors.primary,
      ["--pv-action" as string]: colors.secondary,
      ["--pv-on" as string]: colors.onPrimary,
    };
  }, [colors]);

  const name = platformName.trim();

  if (!scope) {
    return (
      <div
        role="status"
        className="rounded-2xl border p-4 text-xs opacity-70"
        style={{ borderColor: "#DFE5ED" }}
      >
        Elige colores válidos para ver cómo quedará el portal.
      </div>
    );
  }

  return (
    <figure
      className="overflow-hidden rounded-2xl border bg-white shadow-sm"
      style={{ ...scope, borderColor: "#DFE5ED" }}
      aria-label={textos.ariaLabel}
      data-testid="portal-pn-preview"
    >
      <div
        className="flex items-center gap-2 border-b px-3 py-2"
        style={{ borderColor: "#DFE5ED" }}
      >
        {logoSrc ? (
          // eslint-disable-next-line @next/next/no-img-element -- imagen del borrador (URL de objeto)
          <img src={logoSrc} alt={textos.logoAlt} className="h-6 max-w-[96px] object-contain" />
        ) : (
          <span className="rounded-md bg-[#EEF5FF] px-1.5 py-0.5 text-xs font-bold text-[#162744]">
            FLIT
          </span>
        )}
        <span
          className="truncate text-xs font-bold"
          style={{ color: "var(--pv-primary)" }}
          data-testid="portal-pn-preview-name"
        >
          {name || textos.nombrePorDefecto}
        </span>
      </div>

      <div className="space-y-3 bg-[#EEF5FF] p-3">
        {bannerCount === undefined ? null : bannerCount === 0 ? (
          <p
            className="rounded-xl border border-dashed px-3 py-2 text-center text-xs opacity-70"
            style={{ borderColor: "#DFE5ED" }}
          >
            {textos.sinBanner ?? ""}
          </p>
        ) : (
          <div data-testid="portal-pn-preview-banner">
            {bannerSrc && (
              // eslint-disable-next-line @next/next/no-img-element -- imagen del borrador (URL de objeto)
              <img
                src={bannerSrc}
                alt="Primer banner del portal"
                className="aspect-[22/5] w-full rounded-xl border bg-white object-cover"
                style={{ borderColor: "#DFE5ED" }}
              />
            )}
            <p className="mt-1 text-center text-xs opacity-70">Banners: {bannerCount}</p>
          </div>
        )}

        <div
          className="rounded-xl p-3 text-center"
          style={{ background: "var(--pv-action)", color: "var(--pv-on)" }}
        >
          <p className="text-sm font-bold">Iniciar mi trámite</p>
          <p className="text-xs">Placa, documento y tipo de trámite.</p>
          <span
            className="mt-2 inline-block rounded-full bg-white px-3 py-1 text-xs font-semibold"
            style={{ color: "var(--pv-primary)" }}
          >
            Iniciar trámite
          </span>
        </div>
        <p className="text-sm font-bold" style={{ color: "var(--pv-primary)" }}>
          Trámite de Traspaso
        </p>
        <div className="flex items-center gap-2">
          <span
            className="rounded-full px-3 py-1.5 text-xs font-semibold"
            style={{ background: "var(--pv-primary)", color: "var(--pv-on)" }}
          >
            Continuar
          </span>
          <span
            className="grid h-7 w-7 place-items-center rounded-full border-2 text-xs font-semibold"
            style={{ borderColor: "var(--pv-primary)", color: "var(--pv-primary)" }}
          >
            1
          </span>
        </div>
        {carouselCount === undefined ? null : carouselCount === 0 ? (
          <p
            className="rounded-xl border border-dashed px-3 py-2 text-center text-xs opacity-70"
            style={{ borderColor: "#DFE5ED" }}
          >
            Sin carrusel: el portal muestra los servicios de siempre.
          </p>
        ) : (
          <div data-testid="portal-pn-preview-carousel">
            {carouselSrc && (
              // eslint-disable-next-line @next/next/no-img-element -- imagen del borrador (URL de objeto)
              <img
                src={carouselSrc}
                alt="Primera imagen del carrusel"
                className="aspect-[3/2] w-full rounded-xl border bg-white object-cover"
                style={{ borderColor: "#DFE5ED" }}
              />
            )}
            <p className="mt-1 text-center text-xs opacity-70">
              Carrusel: {carouselCount} {carouselCount === 1 ? "imagen" : "imágenes"}
            </p>
          </div>
        )}
      </div>
      <figcaption
        className="border-t px-3 py-1.5 text-xs opacity-70"
        style={{ borderColor: "#DFE5ED" }}
      >
        {textoSegunImagenes(textos.pie, {
          tieneLogo: tieneLogo ?? Boolean(logoSrc),
          tieneBanners: Boolean(bannerCount),
        })}
      </figcaption>
    </figure>
  );
}

"use client";

// Marca del botón de Inicio del dock (escritorio y móvil). En host FLIT es el isotipo de siempre
// (paridad: mismo <img>, mismas clases). En una Marca Blanca usa el logo que publicó la red y la
// forma se ajusta sola a su proporción: un símbolo casi cuadrado va en el círculo de siempre; un
// logo con texto (horizontal) convierte el botón en una pastilla con el logo completo, sin
// recortarlo. Sin logo publicado, la inicial de la plataforma sobre el color de la marca.
//
// Uso de ejemplo:
//   const mark = useDockHomeMark("/assets/favicon.svg");
//   <Link className={mark.shape === "pill" ? "h-14 px-3" : "h-14 w-14"}>{mark.content}</Link>
import { useEffect, useState, type ReactNode } from "react";
import { useBrand } from "@flit/brand/BrandProvider";

/** Ancho entre alto a partir del cual el logo se trata como «logo con texto». */
export const WIDE_LOGO_RATIO = 1.3;

export type DockHomeShape = "flit" | "circle" | "pill" | "initial";

export function useDockHomeMark(flitIconSrc: string): { shape: DockHomeShape; content: ReactNode } {
  const brand = useBrand();
  const logoUrl = brand.isFlit ? null : brand.logoUrl;
  const [ratio, setRatio] = useState<{ url: string; value: number } | null>(null);

  useEffect(() => {
    if (!logoUrl) return;
    const img = new Image();
    img.onload = () => {
      if (img.naturalHeight > 0) setRatio({ url: logoUrl, value: img.naturalWidth / img.naturalHeight });
    };
    img.src = logoUrl;
    return () => {
      img.onload = null;
    };
  }, [logoUrl]);

  if (brand.isFlit) {
    return {
      shape: "flit",
      // eslint-disable-next-line @next/next/no-img-element
      content: <img src={flitIconSrc} alt="" aria-hidden="true" className="h-full w-full object-cover" />,
    };
  }

  if (!logoUrl) {
    const initial = brand.platformName.trim().charAt(0).toUpperCase() || "·";
    return {
      shape: "initial",
      content: (
        <span
          aria-hidden="true"
          className="grid h-full w-full place-items-center text-lg font-bold"
          style={{ background: "var(--color-flit-brand)", color: "var(--brand-on-primary, #ffffff)" }}
        >
          {initial}
        </span>
      ),
    };
  }

  // Mientras la imagen no ha cargado se usa el círculo: el logo entra completo igual (contain).
  const wide = ratio?.url === logoUrl && ratio.value > WIDE_LOGO_RATIO;
  return {
    shape: wide ? "pill" : "circle",
    content: (
      <span aria-hidden="true" className="flex h-full w-full items-center justify-center bg-white">
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img
          src={logoUrl}
          alt=""
          className={wide ? "h-[58%] w-auto max-w-full object-contain" : "h-[64%] w-[64%] object-contain"}
        />
      </span>
    ),
  };
}

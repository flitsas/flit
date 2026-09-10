"use client";

// Respaldo del carrusel de banners (HU #12242, Feature #12236) cuando `object-contain` deja
// margen (pantallas angostas, ver `Dashboard.tsx` / `OtDashboard.tsx`): en vez de un color de
// marca fijo — que puede desentonar con banners de cualquier paleta — el fondo usa el color
// promedio del propio banner, para que combine con cualquier imagen. Mismo patrón que Spotify
// Canvas / YouTube ambient mode.
import { useEffect, useState } from "react";

/** Degradado de marca (mismo que el slide fijo de bienvenida) mientras se calcula el color o si falla. */
const FALLBACK_GRADIENT = "linear-gradient(120deg,#00dbd5 0%,#557eff 100%)";

/** Cache por URL: el carrusel rota entre los mismos banners repetidamente, no tiene sentido recalcular. */
const cache = new Map<string, string>();

function averageColor(img: HTMLImageElement): string | null {
  try {
    const size = 16;
    const canvas = document.createElement("canvas");
    canvas.width = size;
    canvas.height = size;
    const ctx = canvas.getContext("2d");
    if (!ctx) {
      return null;
    }
    ctx.drawImage(img, 0, 0, size, size);
    const { data } = ctx.getImageData(0, 0, size, size);

    let r = 0;
    let g = 0;
    let b = 0;
    let count = 0;
    for (let i = 0; i < data.length; i += 4) {
      // Ignora píxeles casi transparentes (no aportan color visible al banner).
      if (data[i + 3] < 32) {
        continue;
      }
      r += data[i];
      g += data[i + 1];
      b += data[i + 2];
      count++;
    }
    if (count === 0) {
      return null;
    }
    return `${Math.round(r / count)},${Math.round(g / count)},${Math.round(b / count)}`;
  } catch {
    // Canvas "tainted" (imagen cross-origin sin CORS) u otro error de seguridad: sin color.
    return null;
  }
}

/** Mezcla un color rgb "r,g,b" hacia blanco (amount > 0) o negro (amount < 0). */
function shade(rgbCsv: string, amount: number): string {
  const [r, g, b] = rgbCsv.split(",").map(Number);
  const target = amount > 0 ? 255 : 0;
  const p = Math.abs(amount);
  const mix = (c: number) => Math.round(c + (target - c) * p);
  return `rgb(${mix(r)}, ${mix(g)}, ${mix(b)})`;
}

/** Arma el degradado ambiental a partir del color promedio calculado por {@link useDominantColor}. */
export function bannerAmbientGradient(rgbCsv: string | null): string {
  if (!rgbCsv) {
    return FALLBACK_GRADIENT;
  }
  return `radial-gradient(130% 140% at 50% 30%, ${shade(rgbCsv, 0.18)} 0%, rgb(${rgbCsv}) 45%, ${shade(rgbCsv, -0.35)} 100%)`;
}

/**
 * Color promedio de una imagen de banner, calculado en el navegador (canvas 16x16, sin
 * librerías nuevas). Devuelve `null` mientras carga o si el cálculo falla — el caller usa
 * {@link bannerAmbientGradient} con ese `null` para caer al degradado de marca.
 *
 * El cache por URL se lee directo durante el render (no hace falta `useState` para ese caso);
 * el estado solo guarda el resultado de un cálculo en curso, etiquetado con la URL a la que
 * pertenece — así, si `imageUrl` cambia antes de que termine, el hook no devuelve por error el
 * color de la imagen anterior mientras la nueva termina de cargar.
 */
export function useDominantColor(imageUrl: string | undefined): string | null {
  const [computed, setComputed] = useState<{ url: string; rgb: string } | null>(null);

  useEffect(() => {
    if (!imageUrl || cache.has(imageUrl)) {
      return;
    }

    let cancelled = false;
    const img = new Image();
    img.crossOrigin = "anonymous";
    img.onload = () => {
      if (cancelled) {
        return;
      }
      const color = averageColor(img);
      if (color) {
        cache.set(imageUrl, color);
        setComputed({ url: imageUrl, rgb: color });
      }
    };
    img.src = imageUrl;

    return () => {
      cancelled = true;
    };
  }, [imageUrl]);

  if (!imageUrl) {
    return null;
  }
  const cached = cache.get(imageUrl);
  if (cached) {
    return cached;
  }
  return computed?.url === imageUrl ? computed.rgb : null;
}

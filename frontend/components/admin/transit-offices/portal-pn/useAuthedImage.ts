"use client";

import { useEffect, useState } from "react";

/** Pide una imagen que exige sesión y la entrega como URL de objeto (la libera quien la recibe). */
export type CargarImagen = (path: string, signal: AbortSignal) => Promise<string>;

/**
 * Las imágenes del borrador exigen sesión: se piden con el token y se muestran como URL de objeto. Si la imagen no
 * carga, devuelve `null` (la vista previa cae al estado «sin imagen») y libera la URL anterior al cambiar o desmontar.
 * Quién la pide (organismo o web) lo decide `cargar`, que debe ser estable entre renders.
 */
export function useAuthedImage(path: string | null, cargar: CargarImagen): string | null {
  const [loaded, setLoaded] = useState<{ path: string; src: string } | null>(null);

  useEffect(() => {
    if (!path) return;
    const controller = new AbortController();
    let objectUrl: string | null = null;
    void cargar(path, controller.signal)
      .then((src) => {
        if (controller.signal.aborted) {
          URL.revokeObjectURL(src);
          return;
        }
        objectUrl = src;
        setLoaded({ path, src });
      })
      .catch(() => undefined);
    return () => {
      controller.abort();
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [path, cargar]);

  return path && loaded?.path === path ? loaded.src : null;
}

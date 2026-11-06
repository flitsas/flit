"use client";

import { useEffect, useState } from "react";
import { fetchPortalMarcaImage, type PortalMarcaScope } from "@/lib/api/admin-ot-portal-marca";

/**
 * Las imágenes del borrador exigen sesión: se piden con el token y se muestran como URL de objeto. Si la imagen no
 * carga, devuelve `null` (la vista previa cae al estado «sin imagen») y libera la URL anterior al cambiar o desmontar.
 */
export function useAuthedImage(path: string | null, scope?: PortalMarcaScope): string | null {
  const [loaded, setLoaded] = useState<{ path: string; src: string } | null>(null);
  const transitOfficeId = scope?.transitOfficeId;

  useEffect(() => {
    if (!path) return;
    const controller = new AbortController();
    let objectUrl: string | null = null;
    void fetchPortalMarcaImage(path, transitOfficeId ? { transitOfficeId } : undefined, controller.signal)
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
  }, [path, transitOfficeId]);

  return path && loaded?.path === path ? loaded.src : null;
}

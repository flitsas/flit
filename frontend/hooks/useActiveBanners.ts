"use client";

// HU #12242 (Feature #12236): banners Activos compartidos por el carrusel del gestor
// (`Dashboard.tsx`) y del organismo de tránsito (`OtDashboard.tsx`) — mismo set global, misma
// lógica de carga, para no duplicarla en los dos componentes.
import { useEffect, useState } from "react";
import { getActiveBanners, type ActiveBanner } from "@/lib/api/public-banners";

/**
 * Banners activos para los slides del carrusel de bienvenida. Un fallo de red degrada en
 * silencio a lista vacía (AC3): sin toast de error, sin reintento automático — el carrusel se
 * queda mostrando solo su slide fijo, tal como si no hubiera banners configurados.
 */
export function useActiveBanners(): ActiveBanner[] {
  const [banners, setBanners] = useState<ActiveBanner[]>([]);

  useEffect(() => {
    const controller = new AbortController();
    getActiveBanners(controller.signal)
      .then((data) => {
        if (controller.signal.aborted) return;
        setBanners(data);
      })
      .catch(() => {
        // Degradación silenciosa (AC3): `banners` se queda en `[]`, sin loguear como error de UI.
      });
    return () => controller.abort();
  }, []);

  return banners;
}

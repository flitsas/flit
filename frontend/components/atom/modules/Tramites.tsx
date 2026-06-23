"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";

/**
 * Track B — Trámites vive ahora en su propia ruta (/tramites/*) con listado,
 * alta y wizard por instancia. Este módulo atom (renderizado por la SPA en /)
 * queda como redirección: al montar lleva a /tramites. Así evitamos dos flujos
 * paralelos (el wizard embebido vs. el server-driven por ruta). La navegación
 * normal del dock ya empuja /tramites directamente; esto es la red de seguridad.
 */
export function Tramites() {
  const router = useRouter();

  useEffect(() => {
    router.replace("/tramites");
  }, [router]);

  return null;
}

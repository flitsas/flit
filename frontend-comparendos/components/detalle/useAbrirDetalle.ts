"use client";

import { useCallback } from "react";
import { useRouter } from "next/navigation";
import { rutaActual, rutaDetalle } from "@/components/detalle/ruta";
import type { SeccionDetalle } from "@/components/detalle/Timeline";

/**
 * Abre el detalle del comparendo en su página (rediseño S1), recordando la pantalla de origen con su query para que
 * «volver» y Escape regresen igual. Acepta `null` para encajar donde antes se cerraba el modal.
 */
export function useAbrirDetalle(): (id: string | null, seccion?: SeccionDetalle | null) => void {
  const router = useRouter();
  return useCallback(
    (id: string | null, seccion: SeccionDetalle | null = null) => {
      if (id) router.push(rutaDetalle(id, { volver: rutaActual(), seccion }));
    },
    [router],
  );
}

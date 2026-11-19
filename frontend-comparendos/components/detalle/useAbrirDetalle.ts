"use client";

import { useCallback } from "react";
import { useRouter } from "next/navigation";
import { rutaActual, rutaDetalle } from "@/components/detalle/ruta";
import type { SeccionDetalle } from "@/components/detalle/Timeline";

/**
 * Abre el detalle del comparendo en su página (rediseño S1), recordando la pantalla de origen con su query para que
 * «volver» y Escape regresen igual. Acepta `null` para encajar donde antes se cerraba el modal.
 */
export function useAbrirDetalle(): (id: string | null, seccion?: SeccionDetalle | null, volver?: string) => void {
  const router = useRouter();
  return useCallback(
    // `volver` explícito para pantallas cuyo estado aún puede no estar en la URL (su `replace` no ha llegado).
    (id: string | null, seccion: SeccionDetalle | null = null, volver?: string) => {
      if (id) router.push(rutaDetalle(id, { volver: volver ?? rutaActual(), seccion }));
    },
    [router],
  );
}

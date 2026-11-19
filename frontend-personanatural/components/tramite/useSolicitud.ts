"use client";

import { useQuery } from "@tanstack/react-query";
import type { ClientePortal } from "@/lib/api/cliente";

export const claveSolicitud = (id: string) => ["pn", "solicitud", id] as const;

/**
 * El borrador de la solicitud (GET /solicitudes/{id}), con la sesión del trámite que guarda el proxy en su cookie. Los datos
 * que escribe el ciudadano viven solo en la caché de memoria de esta pestaña. Se actualiza a mano con cada escritura (que
 * devuelve el borrador completo), así que no se relee por su cuenta.
 */
export function useSolicitud(cliente: ClientePortal, id: string | undefined) {
  return useQuery({
    queryKey: claveSolicitud(id ?? ""),
    enabled: Boolean(id),
    queryFn: ({ signal }) => cliente.solicitudes.obtener(id as string, { signal }),
    staleTime: Infinity,
    gcTime: 0,
  });
}

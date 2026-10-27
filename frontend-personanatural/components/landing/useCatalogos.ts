"use client";

import { useQuery } from "@tanstack/react-query";
import { portal, type ClientePortal, type Organismo, type TipoTramite } from "@/lib/api/cliente";
import { comoLista, tiposDisponibles } from "@/lib/landing/catalogos";

const CINCO_MINUTOS = 5 * 60 * 1000;

/** Organismos de tránsito integrados (GET /organismos). */
export function useOrganismos(cliente: ClientePortal = portal) {
  return useQuery({
    queryKey: ["pn", "organismos"],
    queryFn: async ({ signal }) => comoLista<Organismo>(await cliente.catalogos.organismos({ signal })),
    staleTime: CINCO_MINUTOS,
  });
}

/** Tipos de trámite que el portal puede atender hoy (GET /tipos-tramite, solo los disponibles). */
export function useTiposTramite(cliente: ClientePortal = portal) {
  return useQuery({
    queryKey: ["pn", "tipos-tramite"],
    queryFn: async ({ signal }) => tiposDisponibles(comoLista<TipoTramite>(await cliente.catalogos.tiposTramite({ signal }))),
    staleTime: CINCO_MINUTOS,
  });
}

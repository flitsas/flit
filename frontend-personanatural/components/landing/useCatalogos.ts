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

/**
 * Definición del tipo elegido (GET /tipos-tramite/{code}/definicion): requisitos y modo de identificar el vehículo. Con el
 * organismo, los documentos son los de ese organismo. Sin tipo no pide nada.
 */
export function useDefinicion(cliente: ClientePortal, code: string, organismoId?: string, habilitada = true) {
  return useQuery({
    queryKey: ["pn", "definicion", code, organismoId ?? null],
    enabled: code !== "" && habilitada,
    queryFn: ({ signal }) => cliente.catalogos.definicion(code, { organismoId, signal }),
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

"use client";

import { useQuery } from "@tanstack/react-query";
import { portal, type ClientePortal, type Organismo, type TipoTramite } from "@/lib/api/cliente";
import { comoLista, tiposDisponibles } from "@/lib/landing/catalogos";

const CINCO_MINUTOS = 5 * 60 * 1000;

/**
 * Organismos de tránsito integrados (GET /organismos). Sin `staleTime`: un precio nuevo o cerrado se refleja en la
 * siguiente consulta (HU #14391), no a los cinco minutos.
 */
export function useOrganismos(cliente: ClientePortal = portal) {
  return useQuery({
    queryKey: ["pn", "organismos"],
    queryFn: async ({ signal }) => comoLista<Organismo>(await cliente.catalogos.organismos({ signal })),
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
  });
}

/**
 * Tipos que ofrece el punto de atención: los del organismo de la sesión del punto con tarifa vigente (GET /punto/tipos-tramite, misma
 * forma que GET /tipos-tramite?organismoId=). `organismoId` solo separa la caché entre puntos.
 */
export function useTiposTramitePunto(cliente: ClientePortal = portal, organismoId = "") {
  return useQuery({
    queryKey: ["pn", "punto", "tipos-tramite", organismoId],
    queryFn: async ({ signal }) => tiposDisponibles(comoLista<TipoTramite>(await cliente.punto.tiposTramite({ signal }))),
  });
}

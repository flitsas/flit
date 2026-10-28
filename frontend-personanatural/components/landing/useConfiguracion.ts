"use client";

import { useQuery } from "@tanstack/react-query";
import { useMemo } from "react";
import { portal, type ClientePortal } from "@/lib/api/cliente";
import { resolverContenido, SIN_PUBLICAR } from "@/lib/configuracion";

const UN_MINUTO = 60 * 1000;

/**
 * Configuración publicada del portal (GET /configuracion, anónimo y sin datos del ciudadano). Es contenido de apoyo: si todavía
 * carga, falla (503 sin core-api, red) o no hay nada publicado (versión «0») devuelve el contenido vacío y la landing conserva
 * el suyo fijo. Nunca muestra error por esto. Sin reintentos: el servidor ya cachea 60 s.
 */
export function useConfiguracion(cliente: ClientePortal = portal) {
  const consulta = useQuery({
    queryKey: ["pn", "configuracion"],
    queryFn: ({ signal }) => cliente.configuracion.obtener({ signal }),
    staleTime: UN_MINUTO,
    retry: false,
  });
  const contenido = useMemo(() => (consulta.isSuccess ? resolverContenido(consulta.data) : SIN_PUBLICAR), [consulta.isSuccess, consulta.data]);
  return { ...consulta, contenido };
}

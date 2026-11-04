"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import type { ReactNode } from "react";
import type { ClientePortal, Cotizacion } from "@/lib/api/cliente";
import { ApiError } from "@/lib/api/errores";
import { useBloqueo } from "@/lib/api/useBloqueo";
import { claveCotizacion } from "@/lib/tramite/cotizacion";
import { organismoElegido } from "@/lib/tramite/diagnostico";

const CODIGOS_SIN_TARIFA = new Set(["tarifa_no_vigente", "tipo_no_habilitado"]);

interface Opciones {
  cliente: ClientePortal;
  organismoId: string;
  tipoCodigo: string;
  /** Códigos de las transformaciones activas. Vacío = solo el valor base del trámite. */
  transformaciones: readonly string[];
}

/**
 * Cotiza al montar y cada vez que cambia el organismo, el tipo o las transformaciones.
 * El captcha está desactivado: no se pide verificación de seguridad.
 */
export function useCotizacionAutomatica({ cliente, organismoId, tipoCodigo, transformaciones }: Opciones) {
  const bloqueo = useBloqueo();
  const clave = claveCotizacion(organismoId, tipoCodigo, transformaciones);

  const cotizacion = useQuery<Cotizacion>({
    queryKey: clave,
    enabled: organismoElegido(organismoId) && !bloqueo.bloqueado,
    staleTime: Infinity,
    retry: false,
    placeholderData: keepPreviousData,
    queryFn: () =>
      cliente.cotizaciones.cotizar({
        organismoId,
        tipoCodigo,
        ...(transformaciones.length > 0 ? { adicionales: [...transformaciones] } : {}),
      }),
  });

  const sinTarifa = cotizacion.error instanceof ApiError && CODIGOS_SIN_TARIFA.has(cotizacion.error.code);

  return {
    data: cotizacion.data,
    error: cotizacion.error,
    isError: cotizacion.isError,
    isPending: cotizacion.isPending,
    sinTarifa,
    bloqueo,
    captcha: null as ReactNode,
  };
}

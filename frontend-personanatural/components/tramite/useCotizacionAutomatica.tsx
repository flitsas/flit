"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { Captcha } from "@/components/Captcha";
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
 * El captcha se resuelve solo (en desarrollo no se ve; con un proveedor real aparece la casilla, sin botón de calcular).
 */
export function useCotizacionAutomatica({ cliente, organismoId, tipoCodigo, transformaciones }: Opciones) {
  const bloqueo = useBloqueo();
  const [token, setToken] = useState<string | null>(null);
  const [reinicio, setReinicio] = useState(0);
  // El token solo sirve para la firma con la que se pidió: al cambiar las transformaciones se pide otro y no se reutiliza.
  const [emitidoPara, setEmitidoPara] = useState<string | null>(null);
  const clave = claveCotizacion(organismoId, tipoCodigo, transformaciones);
  const firma = clave.join("|");
  const firmaVista = useRef(firma);

  useEffect(() => {
    if (firmaVista.current === firma) return;
    firmaVista.current = firma;
    setReinicio((n) => n + 1);
  }, [firma]);

  const onToken = (nuevo: string | null) => {
    if (!nuevo) return;
    setEmitidoPara(firma);
    setToken(nuevo);
  };

  const cotizacion = useQuery<Cotizacion>({
    queryKey: clave,
    enabled: organismoElegido(organismoId) && token !== null && emitidoPara === firma && !bloqueo.bloqueado,
    staleTime: Infinity,
    retry: false,
    placeholderData: keepPreviousData,
    queryFn: () =>
      cliente.cotizaciones.cotizar(
        {
          organismoId,
          tipoCodigo,
          ...(transformaciones.length > 0 ? { adicionales: [...transformaciones] } : {}),
        },
        { captcha: token ?? "" },
      ),
  });

  const sinTarifa = cotizacion.error instanceof ApiError && CODIGOS_SIN_TARIFA.has(cotizacion.error.code);

  return {
    data: cotizacion.data,
    error: cotizacion.error,
    isError: cotizacion.isError,
    isPending: cotizacion.isPending,
    sinTarifa,
    bloqueo,
    captcha: <Captcha onToken={onToken} reinicio={reinicio} />,
  };
}

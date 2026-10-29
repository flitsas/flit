"use client";

import { skipToken, useQuery } from "@tanstack/react-query";
import { useEffect, useId } from "react";
import { useCotizacionAutomatica } from "@/components/tramite/useCotizacionAutomatica";
import type { ClientePortal } from "@/lib/api/cliente";
import { esperaDe, mensajeDe } from "@/lib/api/errores";
import { formatearPesos } from "@/lib/formato";
import { claveAdicionalesActivos } from "@/lib/tramite/cotizacion";

export const MENSAJE_SIN_TARIFA = "Este organismo no tiene tarifa para este trámite";

interface Props {
  cliente: ClientePortal;
  organismoId: string;
  tipoCodigo: string;
  /** Avisa si el organismo no tiene tarifa vigente para el tipo: el cascarón no deja continuar. */
  onSinTarifa: (sinTarifa: boolean) => void;
}

/**
 * «Valor del trámite» de la barra lateral. Se cotiza solo, con las transformaciones que el ciudadano active
 * en Requisitos. Sin tarifa vigente avisa y bloquea el avance.
 */
export function ValorTramite({ cliente, organismoId, tipoCodigo, onSinTarifa }: Props) {
  const idValor = useId();
  const { data: adicionales } = useQuery<readonly string[]>({ queryKey: claveAdicionalesActivos, queryFn: skipToken, staleTime: Infinity });
  const cotizacion = useCotizacionAutomatica({ cliente, organismoId, tipoCodigo, transformaciones: adicionales ?? [] });

  useEffect(() => onSinTarifa(cotizacion.sinTarifa), [cotizacion.sinTarifa, onSinTarifa]);

  const total = cotizacion.data ? formatearPesos(cotizacion.data.totalCop) : "—";
  const error = cotizacion.isError ? cotizacion.error : null;
  const calculando = cotizacion.isPending && !cotizacion.sinTarifa;

  return (
    <div>
      <label htmlFor={idValor} className="mb-1 block text-xs font-light text-muted-foreground">
        Valor del trámite
      </label>
      <input id={idValor} className="field" readOnly value={total} aria-describedby={`${idValor}-estado`} />
      <div id={`${idValor}-estado`} aria-live="polite" className="mt-1 text-xs">
        {calculando && <p role="status">Calculando...</p>}
        {cotizacion.sinTarifa && <p role="alert" className="text-alert">{MENSAJE_SIN_TARIFA}</p>}
        {error !== null && !cotizacion.sinTarifa && <p role="alert" className="text-alert">{esperaDe(error) ? mensajeDe(error) : "No pudimos calcular el valor. Intenta de nuevo."}</p>}
        {cotizacion.bloqueo.bloqueado && <p role="status">Podrás intentarlo de nuevo en {cotizacion.bloqueo.segundosRestantes} s.</p>}
        {cotizacion.captcha}
      </div>
    </div>
  );
}

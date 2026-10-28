"use client";

import { useMutation } from "@tanstack/react-query";
import { useEffect, useId, useRef, useState } from "react";
import { Captcha } from "@/components/Captcha";
import { boton } from "@/components/landing/estilos";
import type { ClientePortal, Cotizacion } from "@/lib/api/cliente";
import { ApiError, esperaDe, mensajeDe } from "@/lib/api/errores";
import { useBloqueo } from "@/lib/api/useBloqueo";
import { formatearPesos } from "@/lib/formato";

export const MENSAJE_SIN_TARIFA = "Este organismo no tiene tarifa para este trámite";
const CODIGOS_SIN_TARIFA = new Set(["tarifa_no_vigente", "tipo_no_habilitado"]);

interface Props {
  cliente: ClientePortal;
  organismoId: string;
  tipoCodigo: string;
  /** Avisa si el organismo no tiene tarifa vigente para el tipo: el cascarón no deja continuar. */
  onSinTarifa: (sinTarifa: boolean) => void;
}

/**
 * «Valor del trámite» de la barra lateral: la cotización vigente del organismo para el tipo (el front no tiene precios). El BFF
 * pide captcha para cotizar, así que el valor se calcula a pedido: «Calcular valor» muestra la verificación y, al resolverla,
 * consulta sola. Sin tarifa vigente avisa y bloquea el avance.
 */
export function ValorTramite({ cliente, organismoId, tipoCodigo, onSinTarifa }: Props) {
  const idValor = useId();
  const bloqueo = useBloqueo();
  const [pidiendo, setPidiendo] = useState(false);
  const [captcha, setCaptcha] = useState<string | null>(null);
  const [reinicio, setReinicio] = useState(0);
  const tokenUsado = useRef<string | null>(null);

  const cotizacion = useMutation<Cotizacion, unknown, string>({
    mutationFn: (token) => cliente.cotizaciones.cotizar({ organismoId, tipoCodigo }, { captcha: token }),
    onError: (error) => bloqueo.registrar(error),
    onSettled: () => {
      setCaptcha(null);
      setReinicio((n) => n + 1);
    },
  });

  const sinTarifa = cotizacion.error instanceof ApiError && CODIGOS_SIN_TARIFA.has(cotizacion.error.code);
  useEffect(() => onSinTarifa(sinTarifa), [sinTarifa, onSinTarifa]);

  const mutar = cotizacion.mutate;
  useEffect(() => {
    if (!captcha) {
      tokenUsado.current = null;
      return;
    }
    if (!pidiendo || cotizacion.isPending || cotizacion.isSuccess || bloqueo.bloqueado || tokenUsado.current === captcha) return;
    tokenUsado.current = captcha;
    mutar(captcha);
  }, [captcha, pidiendo, cotizacion.isPending, cotizacion.isSuccess, bloqueo.bloqueado, mutar]);

  const total = cotizacion.data ? formatearPesos(cotizacion.data.totalCop) : "—";
  const error = cotizacion.isError ? cotizacion.error : null;

  return (
    <div>
      <label htmlFor={idValor} className="mb-1 block text-xs font-light text-muted-foreground">
        Valor del trámite
      </label>
      <input id={idValor} className="field" readOnly value={total} aria-describedby={`${idValor}-estado`} />
      <div id={`${idValor}-estado`} aria-live="polite" className="mt-1 text-xs">
        {cotizacion.isPending && <p role="status">Calculando...</p>}
        {sinTarifa && <p role="alert" className="text-alert">{MENSAJE_SIN_TARIFA}</p>}
        {error !== null && !sinTarifa && <p role="alert" className="text-alert">{esperaDe(error) ? mensajeDe(error) : "No pudimos calcular el valor. Intenta de nuevo."}</p>}
        {bloqueo.bloqueado && <p role="status">Podrás intentarlo de nuevo en {bloqueo.segundosRestantes} s.</p>}
      </div>
      {!cotizacion.isSuccess && !cotizacion.isPending && !sinTarifa && !pidiendo && (
        <button type="button" className={boton("back", "mt-2 h-9 px-4", "none")} onClick={() => setPidiendo(true)}>
          Calcular valor
        </button>
      )}
      {pidiendo && !cotizacion.isSuccess && !cotizacion.isPending && !sinTarifa && (
        <div className="mt-2">
          <Captcha onToken={setCaptcha} reinicio={reinicio} />
        </div>
      )}
    </div>
  );
}

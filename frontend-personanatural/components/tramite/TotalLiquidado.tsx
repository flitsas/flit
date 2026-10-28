"use client";

import { skipToken, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { Captcha } from "@/components/Captcha";
import { boton } from "@/components/landing/estilos";
import { MENSAJE_SIN_TARIFA } from "@/components/tramite/ValorTramite";
import type { ClientePortal, Cotizacion } from "@/lib/api/cliente";
import { ApiError, esperaDe, mensajeDe } from "@/lib/api/errores";
import { useBloqueo } from "@/lib/api/useBloqueo";
import { formatearPesos } from "@/lib/formato";
import { claveCotizacion } from "@/lib/tramite/cotizacion";
import { TRANSFORMACIONES } from "@/lib/tramite/requisitos";

interface Props {
  cliente: ClientePortal;
  organismoId: string;
  tipoCodigo: string;
  /** Códigos de las transformaciones activas (se cotizan como conceptos adicionales del tipo). */
  transformaciones: readonly string[];
}

/**
 * «Valor total liquidado: $ … COP» del paso Requisitos: la cotización vigente del organismo para el tipo, con las transformaciones
 * activas como conceptos adicionales (el front no tiene precios). Comparte la caché con el «Valor del trámite» de la barra lateral:
 * si ya se cotizó ese mismo conjunto no pide nada. Una cotización nueva exige la verificación de seguridad del BFF.
 */
export function TotalLiquidado({ cliente, organismoId, tipoCodigo, transformaciones }: Props) {
  const queryClient = useQueryClient();
  const bloqueo = useBloqueo();
  const clave = claveCotizacion(organismoId, tipoCodigo, transformaciones);
  const { data } = useQuery<Cotizacion>({ queryKey: clave, queryFn: skipToken, staleTime: Infinity });
  const [pidiendo, setPidiendo] = useState(false);
  const [captcha, setCaptcha] = useState<string | null>(null);
  const [reinicio, setReinicio] = useState(0);
  const tokenUsado = useRef<string | null>(null);

  const cotizacion = useMutation<Cotizacion, unknown, string>({
    mutationFn: (token) => cliente.cotizaciones.cotizar({ organismoId, tipoCodigo, ...(transformaciones.length > 0 ? { adicionales: [...transformaciones] } : {}) }, { captcha: token }),
    onSuccess: (c) => {
      queryClient.setQueryData(clave, c);
      setPidiendo(false);
    },
    onError: (e) => bloqueo.registrar(e),
    onSettled: () => {
      setCaptcha(null);
      setReinicio((n) => n + 1);
    },
  });

  const mutar = cotizacion.mutate;
  useEffect(() => {
    if (!captcha) {
      tokenUsado.current = null;
      return;
    }
    if (!pidiendo || cotizacion.isPending || bloqueo.bloqueado || tokenUsado.current === captcha) return;
    tokenUsado.current = captcha;
    mutar(captcha);
  }, [captcha, pidiendo, cotizacion.isPending, bloqueo.bloqueado, mutar]);

  const error = cotizacion.isError ? cotizacion.error : null;
  const sinTarifa = error instanceof ApiError && (error.code === "tarifa_no_vigente" || error.code === "tipo_no_habilitado");
  const noAplicados = data?.adicionalesNoAplicados ?? [];
  const etiquetaNoAplicados = noAplicados.map((c) => TRANSFORMACIONES.find((t) => t.codigo === c)?.etiqueta ?? c).join(", ");

  return (
    <div className="space-y-2 text-right">
      <p className="font-semibold" aria-live="polite">
        Valor total liquidado: {data ? formatearPesos(data.totalCop) : "—"}
      </p>
      {noAplicados.length > 0 && <p role="status" className="text-xs text-muted-foreground">No aplican en este organismo: {etiquetaNoAplicados}.</p>}
      {cotizacion.isPending && <p role="status" className="text-xs">Calculando...</p>}
      {sinTarifa && <p role="alert" className="text-xs text-alert">{MENSAJE_SIN_TARIFA}</p>}
      {error !== null && !sinTarifa && <p role="alert" className="text-xs text-alert">{esperaDe(error) ? mensajeDe(error) : "No pudimos calcular el valor. Intenta de nuevo."}</p>}
      {bloqueo.bloqueado && <p role="status" className="text-xs">Podrás intentarlo de nuevo en {bloqueo.segundosRestantes} s.</p>}
      {!data && !pidiendo && !cotizacion.isPending && !sinTarifa && (
        <button type="button" className={boton("back", "h-9 px-4", "none")} onClick={() => setPidiendo(true)}>
          Calcular valor total
        </button>
      )}
      {pidiendo && !data && !cotizacion.isPending && !sinTarifa && (
        <div className="flex justify-end">
          <Captcha onToken={setCaptcha} reinicio={reinicio} />
        </div>
      )}
    </div>
  );
}

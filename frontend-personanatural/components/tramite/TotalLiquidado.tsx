"use client";

import { useCotizacionAutomatica } from "@/components/tramite/useCotizacionAutomatica";
import { MENSAJE_SIN_TARIFA } from "@/components/tramite/ValorTramite";
import type { ClientePortal } from "@/lib/api/cliente";
import { esperaDe, mensajeDe } from "@/lib/api/errores";
import { formatearPesos } from "@/lib/formato";
import { organismoElegido } from "@/lib/tramite/diagnostico";
import { TRANSFORMACIONES } from "@/lib/tramite/requisitos";

interface Props {
  cliente: ClientePortal;
  organismoId: string;
  tipoCodigo: string;
  /** Códigos de las transformaciones activas (se cotizan como conceptos adicionales del tipo). */
  transformaciones: readonly string[];
}

/**
 * «Valor total liquidado» del paso Requisitos. Se actualiza solo al activar o quitar una transformación
 * y comparte la cotización con el «Valor del trámite» de la barra lateral.
 */
export function TotalLiquidado({ cliente, organismoId, tipoCodigo, transformaciones }: Props) {
  const cotizacion = useCotizacionAutomatica({ cliente, organismoId, tipoCodigo, transformaciones });
  const activo = organismoElegido(organismoId);
  const data = cotizacion.data;
  const error = cotizacion.isError ? cotizacion.error : null;
  const noAplicados = data?.adicionalesNoAplicados ?? [];
  const etiquetaNoAplicados = noAplicados.map((c) => TRANSFORMACIONES.find((t) => t.codigo === c)?.etiqueta ?? c).join(", ");

  return (
    <div className="space-y-2 text-right">
      <p className="font-semibold" aria-live="polite">
        Valor total liquidado: {data ? formatearPesos(data.totalCop) : "—"}
      </p>
      {noAplicados.length > 0 && <p role="status" className="text-xs text-muted-foreground">No aplican en este organismo: {etiquetaNoAplicados}.</p>}
      {activo && cotizacion.isPending && !cotizacion.sinTarifa && <p role="status" className="text-xs">Calculando...</p>}
      {activo && cotizacion.sinTarifa && <p role="alert" className="text-xs text-alert">{MENSAJE_SIN_TARIFA}</p>}
      {activo && error !== null && !cotizacion.sinTarifa && <p role="alert" className="text-xs text-alert">{esperaDe(error) ? mensajeDe(error) : "No pudimos calcular el valor. Intenta de nuevo."}</p>}
      {activo && cotizacion.bloqueo.bloqueado && <p role="status" className="text-xs">Podrás intentarlo de nuevo en {cotizacion.bloqueo.segundosRestantes} s.</p>}
      <div className="flex justify-end">{cotizacion.captcha}</div>
    </div>
  );
}

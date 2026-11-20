"use client";

// HU #14465 — Pantalla de cierre del canal OT. Se monta cuando el pago quedó confirmado: el servidor radica solo, aquí no se
// llama a /radicar. Muestra el ID del trámite (código público) y dónde recogerlo; mientras el registro en FLIT 2.0 está en
// curso sondea hasta que quede registrado o rechazado.
import { useQuery } from "@tanstack/react-query";
import { useEffect, useRef } from "react";
import { MobilityLoader } from "@/components/MobilityLoader";
import { boton } from "@/components/landing/estilos";
import { Btn } from "@/components/tramite/piezas";
import { lineasComprobante, ResumenCierre } from "@/components/punto/ResumenCierre";
import type { CierrePuntoRespuesta, ClientePortal } from "@/lib/api/cliente";
import { ApiError, mensajeDeFallo } from "@/lib/api/errores";
import { descargarPdf } from "@/lib/punto/comprobantePdf";

/** Cada cuánto se vuelve a preguntar mientras el trámite está en registro. */
export const INTERVALO_SONDEO_CIERRE_MS = 3000;

export type PropsCierrePunto = {
  cliente: ClientePortal;
  solicitudId: string;
  tipoCodigo: string;
  /** Para la columna «Tipo» y la tarjeta «Vehículo» de la confirmación (opcionales). */
  placa?: string | undefined;
  tipoNombre?: string | undefined;
  /** Sale del wizard (vuelve al inicio del punto). */
  onTerminar: () => void;
  /** La caja anuló el cobro estando en el cierre (409 tras haber mostrado un pago): el asistente vuelve al paso Pago. */
  onPagoAnulado?: () => void;
  /** Llega el radicado de FLIT 2.0 (para la barra lateral del asistente). */
  onRadicado?: (radicado: string) => void;
};

export function CierrePunto({ cliente, solicitudId, placa, tipoNombre, onTerminar, onPagoAnulado, onRadicado }: PropsCierrePunto) {
  const cierre = useQuery<CierrePuntoRespuesta>({
    queryKey: ["pn", "punto", "cierre", solicitudId],
    queryFn: ({ signal }) => cliente.punto.pago.cierre({ signal }),
    refetchInterval: (q) => (q.state.data?.estadoCierre === "en_registro" ? INTERVALO_SONDEO_CIERRE_MS : false),
    retry: false,
  });
  const datos = cierre.data;
  // La hora de envío al organismo viene en el estado del pago (`pago.radicaEn`); solo se pide mientras el trámite sigue en registro.
  const enRegistro = datos?.estadoCierre === "en_registro";
  const estadoPago = useQuery({
    queryKey: ["pn", "punto", "pago-estado", solicitudId],
    queryFn: ({ signal }) => cliente.punto.pago.estado({ signal }),
    enabled: enRegistro,
    retry: false,
  });
  const pagoDelEstado = estadoPago.data?.pago;
  const radicaEn = enRegistro && pagoDelEstado?.enRegistro ? (pagoDelEstado.radicaEn ?? null) : null;
  // Tras haber mostrado un pago, un 409 es que la caja lo anuló: no queda nada que mostrar aquí.
  const pagoAnulado = Boolean(onPagoAnulado) && datos !== undefined && cierre.error instanceof ApiError && cierre.error.status === 409;
  const avisoAnulado = useRef(false);
  useEffect(() => {
    if (!pagoAnulado || avisoAnulado.current) return;
    avisoAnulado.current = true;
    onPagoAnulado?.();
  }, [pagoAnulado, onPagoAnulado]);
  const radicado = datos?.radicadoFlit2 ?? null;
  useEffect(() => {
    if (radicado) onRadicado?.(radicado);
  }, [radicado, onRadicado]);
  if (pagoAnulado) return null;

  return (
    <div className="space-y-6" aria-busy={cierre.isPending}>
      {cierre.isPending && <MobilityLoader mensaje="Registrando tu trámite…" />}
      {cierre.isError && (
        <div role="alert" className="space-y-3 rounded-[9px] bg-alert/10 p-4 text-alert">
          <p>{mensajeDeFallo(cierre.error)}</p>
          <button type="button" className={boton("flit", "h-11 px-6", "none")} onClick={() => void cierre.refetch()}>
            Reintentar
          </button>
        </div>
      )}
      {datos && <ResumenCierre cierre={datos} titulo="h2" placa={placa} tipoNombre={tipoNombre} radicaEn={radicaEn} />}

      <div className="flex flex-wrap items-center justify-end gap-3">
        {datos && datos.estadoCierre !== "rechazado" && (
          <Btn onClick={() => descargarPdf(`comprobante-${datos.codigoPublico}.pdf`, lineasComprobante(datos, placa, tipoNombre))}>Descargar comprobante</Btn>
        )}
        <Btn tone="secondary" onClick={onTerminar}>
          Volver al inicio
        </Btn>
      </div>
    </div>
  );
}

"use client";

// HU #14465 — Pantalla de cierre del canal OT. Se monta cuando el pago quedó confirmado: el servidor radica solo, aquí no se
// llama a /radicar. Muestra el ID del trámite (código público) y dónde recogerlo; mientras el registro en FLIT 2.0 está en
// curso sondea hasta que quede registrado o rechazado.
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { ConsultarModal } from "@/components/modales/ConsultarModal";
import { MobilityLoader } from "@/components/MobilityLoader";
import { boton } from "@/components/landing/estilos";
import { ResumenCierre } from "@/components/punto/ResumenCierre";
import type { CierrePuntoRespuesta, ClientePortal } from "@/lib/api/cliente";
import { mensajeDeFallo } from "@/lib/api/errores";

/** Cada cuánto se vuelve a preguntar mientras el trámite está en registro. */
export const INTERVALO_SONDEO_CIERRE_MS = 3000;

export type PropsCierrePunto = {
  cliente: ClientePortal;
  solicitudId: string;
  tipoCodigo: string;
  /** Sale del wizard (vuelve al inicio del punto). */
  onTerminar: () => void;
};

export function CierrePunto({ cliente, solicitudId, onTerminar }: PropsCierrePunto) {
  const [consultando, setConsultando] = useState(false);
  const cierre = useQuery<CierrePuntoRespuesta>({
    queryKey: ["pn", "punto", "cierre", solicitudId],
    queryFn: ({ signal }) => cliente.punto.pago.cierre({ signal }),
    refetchInterval: (q) => (q.state.data?.estadoCierre === "en_registro" ? INTERVALO_SONDEO_CIERRE_MS : false),
    retry: false,
  });
  const datos = cierre.data;

  return (
    <div className="mx-auto max-w-2xl space-y-6 p-4" aria-busy={cierre.isPending}>
      {cierre.isPending && <MobilityLoader mensaje="Registrando tu trámite…" />}
      {cierre.isError && (
        <div role="alert" className="space-y-3 rounded-[9px] bg-alert/10 p-4 text-alert">
          <p>{mensajeDeFallo(cierre.error)}</p>
          <button type="button" className={boton("flit", "h-11 px-6", "none")} onClick={() => void cierre.refetch()}>
            Reintentar
          </button>
        </div>
      )}
      {datos && <ResumenCierre cierre={datos} titulo="h2" />}

      <div className="flex flex-wrap justify-center gap-3">
        {datos && (
          <button type="button" className={boton("flit", "h-11 px-8", "none")} onClick={() => setConsultando(true)}>
            Consultar trámite
          </button>
        )}
        <button type="button" className={boton("institutional", "h-11 px-8", "none")} onClick={onTerminar}>
          Volver al inicio
        </button>
      </div>

      {datos && (
        <ConsultarModal
          abierto={consultando}
          onCerrar={() => setConsultando(false)}
          cliente={cliente}
          radicadoInicial={datos.codigoPublico}
          consultaPunto
        />
      )}
    </div>
  );
}

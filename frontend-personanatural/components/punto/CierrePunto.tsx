"use client";

// Punto de enganche del CIERRE del punto (HU #14465). Hoy muestra la confirmación/seguimiento de siempre (EnvioTramite vía
// EnvioPunto); la pantalla de cierre definitiva reemplaza el cuerpo de este componente conservando estas props.
// Se monta cuando el pago quedó confirmado: el servidor radica solo, aquí no se llama a /radicar.
import { EnvioPunto } from "@/components/punto/EnvioPunto";
import type { ClientePortal } from "@/lib/api/cliente";

export type PropsCierrePunto = {
  cliente: ClientePortal;
  solicitudId: string;
  tipoCodigo: string;
  /** Sale del wizard (vuelve al inicio del punto). */
  onTerminar: () => void;
};

export function CierrePunto({ cliente, solicitudId, tipoCodigo, onTerminar }: PropsCierrePunto) {
  return (
    <EnvioPunto
      cliente={cliente}
      solicitudId={solicitudId}
      tipoCodigo={tipoCodigo}
      paso="envio"
      onPaso={() => undefined}
      onAtras={onTerminar}
      onIrActores={onTerminar}
      onTerminar={onTerminar}
    />
  );
}

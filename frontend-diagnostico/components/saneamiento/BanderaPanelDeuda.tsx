"use client";

import type { ReactNode } from "react";
import { BotonSanear, SANEAMIENTO_SOLICITADO, SIN_SANEAMIENTO } from "@/components/saneamiento/BotonSanear";
import type { ContextoBandera } from "@/components/saneamiento/PanelDeudaSaneamiento";
import type { EstadoSaneamientoActivo, SaneamientoFila } from "@/lib/saneamiento/api";

/** Vehículo del panel: el identificador visible es la placa (o el VIN, o el identificador). */
export interface VehiculoBandera {
  id: string;
  placa?: string | null;
  vin?: string | null;
  identificador?: string | null;
}

/**
 * Saneamiento de la fila a partir del estado que avisa el panel (`onCambioEstado`): sin saneamiento vuelve a ser elegible;
 * con cualquier estado ya no se puede volver a marcar (motivo «Ya tiene un saneamiento»). El sondeo o la recarga traen
 * después el valor del servidor (D-35).
 */
export function filaDeEstado(estado: EstadoSaneamientoActivo | null): SaneamientoFila {
  if (estado === null) return SIN_SANEAMIENTO;
  if (estado === "SOLICITADO") return SANEAMIENTO_SOLICITADO;
  return { estado, elegible: false, motivo_no_elegible: "YA_SOLICITADO" };
}

/**
 * Bandera de la cabecera de PS-2 (spec-ui §3.1, cableado-fe-c.md §1): `BotonSanear variante="cabecera"` con el contexto que
 * le da el panel. El optimista y su reversión van por `ctx.onCambio`; `ctx.onCambioSaneamiento` se llama solo cuando la API
 * confirma (PUT 200/201 → SOLICITADO; DELETE 204 o 404 → null), para que el panel recargue GET …/saneamiento sin correr
 * contra la petición en vuelo. El anuncio lo hace la región viva del panel (D-45), así que el botón no anuncia por su cuenta.
 * D-101: dentro del panel no se repite «Ver saneamiento» (el panel ya está abierto y el estado está en la sección
 * «Estado»): la cabecera solo pinta la bandera cuando se puede marcar o desmarcar.
 */
export function BanderaPanelDeuda({ contexto, diagnosticoId, vehiculo }: { contexto: ContextoBandera; diagnosticoId: string; vehiculo: VehiculoBandera }) {
  const { saneamiento, puedeSanear } = contexto;
  if (!puedeSanear) return null;
  if (saneamiento.estado !== null && saneamiento.estado !== "SOLICITADO") return null;
  return (
    <BotonSanear
      variante="cabecera"
      diagnosticoId={diagnosticoId}
      vehiculo={vehiculo}
      saneamiento={saneamiento}
      puedeSanear={puedeSanear}
      onCambio={(estado) => contexto.onCambio(estado)}
      onConfirmado={(estado) => contexto.onCambioSaneamiento(estado)}
      onRefrescar={contexto.onRefrescar}
      // Inalcanzable: con un estado que solo se ve, la cabecera no pinta el botón (arriba).
      onAbrirPanel={() => {}}
    />
  );
}

/** `accionBandera` para `PanelDeudaSaneamiento`: `accionBandera={banderaPanelDeuda(diagnosticoId, { id, placa })}`. */
export function banderaPanelDeuda(diagnosticoId: string, vehiculo: VehiculoBandera): (contexto: ContextoBandera) => ReactNode {
  return function bandera(contexto: ContextoBandera) {
    return <BanderaPanelDeuda contexto={contexto} diagnosticoId={diagnosticoId} vehiculo={vehiculo} />;
  };
}

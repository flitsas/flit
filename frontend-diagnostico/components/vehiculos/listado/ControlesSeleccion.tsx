"use client";

import { useId, type ReactNode } from "react";
import { Button } from "@flit/ui/Button";
import { MARCA_CLS } from "@/components/configuracion/estilos";
import { laPlaca } from "@/lib/saneamiento/estados";
import type { SeleccionFila } from "./seleccion";

// PS-3 (CR-5): la casilla de cada fila y el botón de la barra de selección, con su motivo cuando no se pueden usar.

/** Casilla «Seleccionar la placa X» (sin identificador, «Seleccionar el vehículo sin placa»): deshabilitada, el motivo va en un texto `hidden` por `aria-describedby` y en `title`. */
export function Casilla({ placa, seleccion, marcada, onCambiar }: { placa: string; seleccion: SeleccionFila; marcada: boolean; onCambiar: (marcada: boolean) => void }) {
  const motivoId = `${useId()}-motivo`;
  const motivo = seleccion.habilitada ? undefined : seleccion.motivo;
  return (
    <>
      <input
        type="checkbox"
        aria-label={`Seleccionar ${laPlaca(placa)}`}
        aria-describedby={motivo ? motivoId : undefined}
        checked={seleccion.habilitada && marcada}
        disabled={!seleccion.habilitada}
        title={motivo}
        onChange={(e) => onCambiar(e.target.checked)}
        className={`${MARCA_CLS} disabled:cursor-not-allowed disabled:opacity-50`}
      />
      {motivo && (
        <span id={motivoId} hidden>
          {motivo}
        </span>
      )}
    </>
  );
}

/** Botón de la barra: con N = 0 o sobre el tope queda `aria-disabled` (sigue en el Tab) con el motivo como descripción. */
export function AccionBarra(props: {
  variante: "primary" | "secondary";
  cantidad: number;
  enviando: boolean;
  textoEnvio: string;
  texto: string;
  icono: ReactNode;
  bloqueo: string | null;
  onClick: () => void;
}) {
  const { variante, cantidad, enviando, textoEnvio, texto, icono, bloqueo, onClick } = props;
  const bloqueoId = `${useId()}-bloqueo`;
  return (
    <>
      <Button
        variant={variante}
        cargando={enviando}
        aria-disabled={bloqueo ? "true" : undefined}
        aria-describedby={bloqueo ? bloqueoId : undefined}
        className={bloqueo ? "cursor-not-allowed opacity-60" : undefined}
        onClick={() => {
          if (!bloqueo && !enviando) onClick();
        }}
      >
        {enviando ? (
          textoEnvio
        ) : (
          <>
            {icono}
            {`${texto} (${cantidad})`}
          </>
        )}
      </Button>
      {bloqueo && (
        <span id={bloqueoId} hidden>
          {bloqueo}
        </span>
      )}
    </>
  );
}

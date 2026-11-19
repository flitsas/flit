"use client";

import { ChipFiltro, EMBUDO_CLS } from "@flit/ui/ChipsFiltro";
import { rutaKit } from "@flit/ui/iconos-mapas";
import { ESTADO_LEGAL } from "@/lib/etiquetas";

/** Orden de los estados legales en la fila. */
const ESTADOS = ["SinNotificar", "EnTiempo", "Vencido", "Caducado", "Prescrito"] as const;

/** Disco del kit para cada tono del estado legal (el mismo color que su etiqueta en la tabla). */
const DISCO_POR_TONO: Record<string, string> = {
  neutral: rutaKit("estado-borrador"),
  success: rutaKit("estado-aprobado"),
  danger: rutaKit("estado-rechazado"),
  warning: rutaKit("estado-revocado"),
  info: rutaKit("estado-preparado"),
};

/**
 * HU #13869: los 5 estados legales con su conteo. Rediseño de Comparendos: el embudo del kit (`EMBUDO_CLS`), igual que
 * la franja de estados de Trámites: contenedor con borde, una casilla por estado con su disco, nombre y cifra. Clic elige
 * el estado; segundo clic lo suelta. Sin conteo todavía se pinta un guion: un 0 diría que no hay comparendos.
 */
export function TarjetasEstadoLegal({
  conteos,
  elegido,
  onElegir,
}: {
  conteos: Record<string, number> | null;
  elegido: string;
  onElegir: (estado: string) => void;
}) {
  return (
    <div role="group" aria-label="Estados legales de los comparendos" className={EMBUDO_CLS}>
      {ESTADOS.map((estado) => {
        const activo = elegido === estado;
        const { texto, tono } = ESTADO_LEGAL[estado];
        const conteo = conteos?.[estado];
        return (
          <ChipFiltro
            key={estado}
            activo={activo}
            aria-label={conteo === undefined ? texto : `${texto}: ${conteo} comparendo${conteo === 1 ? "" : "s"}`}
            onClick={() => onElegir(activo ? "" : estado)}
            cifra={conteo === undefined ? "—" : conteo.toLocaleString("es-CO")}
            etiqueta={texto}
            icono={DISCO_POR_TONO[tono]}
          />
        );
      })}
    </div>
  );
}

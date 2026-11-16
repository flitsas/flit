"use client";

import { ChipFiltro, CHIPS_FILTRO_FILA_CLS } from "@flit/ui/ChipsFiltro";
import { ESTADO_LEGAL } from "@/lib/etiquetas";

/** Orden de los estados legales en la fila. */
const ESTADOS = ["SinNotificar", "EnTiempo", "Vencido", "Caducado", "Prescrito"] as const;

/**
 * HU #13869: los 5 estados legales con su conteo, como la tira de estados de Trámites. Clic elige el estado; segundo
 * clic lo suelta. Sin conteo todavía se pinta un guion: un 0 diría que no hay comparendos. Con el aspecto de los chips
 * que filtran de la propuesta (HU #14486, `ChipFiltro` de @flit/ui): sin colores de estado, el activo en azul de acción.
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
    <div role="group" aria-label="Estados legales de los comparendos" className={CHIPS_FILTRO_FILA_CLS}>
      {ESTADOS.map((estado) => {
        const activo = elegido === estado;
        const texto = ESTADO_LEGAL[estado].texto;
        const conteo = conteos?.[estado];
        return (
          <ChipFiltro
            key={estado}
            activo={activo}
            aria-label={conteo === undefined ? texto : `${texto}: ${conteo} comparendo${conteo === 1 ? "" : "s"}`}
            onClick={() => onElegir(activo ? "" : estado)}
            cifra={conteo === undefined ? "—" : conteo.toLocaleString("es-CO")}
            etiqueta={texto}
          />
        );
      })}
    </div>
  );
}

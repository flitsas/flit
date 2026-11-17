"use client";

import { reintentarPegadas, type ResultadoReintento } from "@/lib/tablero/api";
import { BarraSeleccionTablero, type AccionCrearTramites, type AccionSanear } from "./BarraSeleccionTablero";
import { BotonReintentar } from "./BotonReintentar";

export interface AccionesBloqueLoteProps {
  diagnosticoId: string;
  /**
   * Hay selección: capacidad `derivar` con filas con datos de derivación, o capacidad `sanear` con filas con datos de
   * saneamiento (HU #14577 AC10).
   */
  haySeleccion: boolean;
  elegibles: number;
  seleccionadas: number;
  onSeleccionarTodas: () => void;
  onQuitar: () => void;
  /** «Crear trámites (N)»; ausente sin la capacidad `derivar`. */
  crear?: AccionCrearTramites;
  /** «Sanear (N)»; ausente sin la capacidad `sanear`. */
  sanear?: AccionSanear;
  /** Placas pegadas del lote; el reintento en bloque solo se ofrece con `puedeReprocesar` y más de 0 (D4(5)). */
  totalPegadas: number;
  puedeReprocesar: boolean;
  onReintentado: (resultado: ResultadoReintento) => void;
  onErrorReintento: (mensaje: string, volverALeer: boolean) => void;
}

/**
 * Acciones en bloque del tablero (HU #14562 AC32; HU #14577 AC10): selección para crear trámites y para sanear, y reintento
 * de las consultas pegadas.
 */
export function AccionesBloqueLote({
  diagnosticoId,
  haySeleccion,
  elegibles,
  seleccionadas,
  onSeleccionarTodas,
  onQuitar,
  crear,
  sanear,
  totalPegadas,
  puedeReprocesar,
  onReintentado,
  onErrorReintento,
}: AccionesBloqueLoteProps) {
  const hayPegadas = puedeReprocesar && totalPegadas > 0;
  if (!haySeleccion && !hayPegadas) return null;
  return (
    <div className="flex flex-col gap-3">
      {haySeleccion && (
        <BarraSeleccionTablero
          elegibles={elegibles}
          seleccionadas={seleccionadas}
          onSeleccionarTodas={onSeleccionarTodas}
          onQuitar={onQuitar}
          crear={crear}
          sanear={sanear}
        />
      )}
      {hayPegadas && (
        <BotonReintentar
          variante="lote"
          etiqueta={`Reintentar las ${totalPegadas} placas pegadas del lote`}
          texto={`Reintentar pegadas (${totalPegadas})`}
          reintentar={() => reintentarPegadas(diagnosticoId)}
          onReintentado={onReintentado}
          onError={onErrorReintento}
        />
      )}
    </div>
  );
}

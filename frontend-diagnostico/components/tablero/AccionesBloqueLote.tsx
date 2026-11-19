"use client";

import { reintentarPegadas, type ResultadoReintento } from "@/lib/tablero/api";
import { BarraSeleccionDerivacion } from "./BarraSeleccionDerivacion";
import { BotonReintentar } from "./BotonReintentar";

export interface AccionesBloqueLoteProps {
  diagnosticoId: string;
  /** Hay derivación (capacidad `derivar` y filas con datos de derivación): selección y «Crear trámites». */
  hayDerivacion: boolean;
  elegibles: number;
  seleccionadas: number;
  encolando: boolean;
  errorMasivo: string | null;
  onSeleccionarTodas: () => void;
  onQuitar: () => void;
  onCrear: () => void;
  /** Placas pegadas del lote; el reintento en bloque solo se ofrece con `puedeReprocesar` y más de 0 (D4(5)). */
  totalPegadas: number;
  puedeReprocesar: boolean;
  onReintentado: (resultado: ResultadoReintento) => void;
  onErrorReintento: (mensaje: string, volverALeer: boolean) => void;
}

/** Acciones en bloque del tablero (HU #14562 AC32): selección para crear trámites y reintento de las consultas pegadas. */
export function AccionesBloqueLote({
  diagnosticoId,
  hayDerivacion,
  elegibles,
  seleccionadas,
  encolando,
  errorMasivo,
  onSeleccionarTodas,
  onQuitar,
  onCrear,
  totalPegadas,
  puedeReprocesar,
  onReintentado,
  onErrorReintento,
}: AccionesBloqueLoteProps) {
  const hayPegadas = puedeReprocesar && totalPegadas > 0;
  if (!hayDerivacion && !hayPegadas) return null;
  return (
    <div className="flex flex-col gap-3">
      {hayDerivacion && (
        <BarraSeleccionDerivacion
          elegibles={elegibles}
          seleccionadas={seleccionadas}
          enviando={encolando}
          onSeleccionarTodas={onSeleccionarTodas}
          onQuitar={onQuitar}
          onCrear={onCrear}
          error={errorMasivo}
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

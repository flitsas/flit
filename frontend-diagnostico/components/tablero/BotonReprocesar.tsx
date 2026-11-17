"use client";

import { useState } from "react";
import { RotateCcw } from "lucide-react";
import { FOCO_CLS } from "@/components/configuracion/estilos";
import { ApiError } from "@/lib/api";
import { mensajeReproceso, reprocesarConsulta, type CeldaGrilla } from "@/lib/tablero/api";

export interface BotonReprocesarProps {
  celda: CeldaGrilla;
  /** Nombre accesible completo: «Reprocesar <fuente> de <vehículo>». */
  etiqueta: string;
  onReprocesada?: (celda: CeldaGrilla) => void;
  /** Mensaje claro del fallo; `refrescar` = la celda cambió en el servidor (409) y conviene volver a leer la grilla. */
  onErrorReproceso?: (mensaje: string, refrescar: boolean) => void;
}

/**
 * «Reprocesar» de una celda roja (HU #13748, F04-C9): POST /consultas/{cid}/reprocesar (HU #13746). Solo lo pinta la grilla
 * para el admin con la capacidad `reprocesarConsultas`; la API vuelve a exigir el permiso (403) y que la celda siga en
 * ERROR y no dependa de un RUNT en error (409).
 */
export function BotonReprocesar({ celda, etiqueta, onReprocesada, onErrorReproceso }: BotonReprocesarProps) {
  const [enviando, setEnviando] = useState(false);

  async function reprocesar() {
    setEnviando(true);
    try {
      await reprocesarConsulta(celda.id);
      onReprocesada?.(celda);
    } catch (e) {
      onErrorReproceso?.(mensajeReproceso(e), e instanceof ApiError && e.status === 409);
    } finally {
      setEnviando(false);
    }
  }

  return (
    <button
      type="button"
      aria-label={etiqueta}
      aria-busy={enviando}
      disabled={enviando}
      onClick={() => void reprocesar()}
      className={`inline-flex items-center gap-1 rounded-full border border-current px-2.5 py-0.5 text-xs font-semibold text-[var(--flit-brand-ink)] transition disabled:cursor-not-allowed disabled:opacity-60 ${FOCO_CLS}`}
    >
      <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
      {enviando ? "Reprocesando…" : "Reprocesar"}
    </button>
  );
}

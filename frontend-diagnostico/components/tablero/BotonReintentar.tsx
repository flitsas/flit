"use client";

import { useState } from "react";
import { RotateCcw } from "lucide-react";
import { BOTON_SECUNDARIO_CLS, FOCO_CLS } from "@/components/configuracion/estilos";
import { ApiError } from "@/lib/api";
import { mensajeReintento, type ResultadoReintento } from "@/lib/tablero/api";

export interface BotonReintentarProps {
  /** Nombre accesible completo («Reintentar consultas de la placa XXX» / «Reintentar las N placas pegadas del lote»). */
  etiqueta: string;
  /** Texto visible: «Reintentar placa» o «Reintentar pegadas (N)». */
  texto: string;
  /** POST del reintento (por placa o por lote). */
  reintentar: () => Promise<ResultadoReintento>;
  onReintentado?: (resultado: ResultadoReintento) => void;
  /** Mensaje claro del fallo; `refrescar` = el estado cambió en el servidor (409) y conviene volver a leer la grilla. */
  onError?: (mensaje: string, refrescar: boolean) => void;
  /** `fila`: pastilla compacta de la celda, como «Reprocesar»; `lote`: botón secundario encima de la grilla. */
  variante?: "fila" | "lote";
}

const FILA_CLS = `inline-flex items-center gap-1 rounded-full border border-current px-2.5 py-0.5 text-xs font-semibold text-[var(--flit-brand-ink)] transition disabled:cursor-not-allowed disabled:opacity-60 ${FOCO_CLS}`;

/**
 * Reintento manual de las consultas pegadas (Punto 3, ola 2026-11-11): por placa o para todo el lote. Mismo patrón que
 * «Reprocesar» de una celda: cargando con aria-busy y deshabilitado mientras responde (no se pulsa dos veces), y el resultado
 * o el error los muestra quien lo pinta. Solo se pinta con la capacidad `reprocesarConsultas`; la API vuelve a exigirla (403).
 */
export function BotonReintentar({ etiqueta, texto, reintentar, onReintentado, onError, variante = "fila" }: BotonReintentarProps) {
  const [enviando, setEnviando] = useState(false);

  async function alPulsar() {
    if (enviando) return;
    setEnviando(true);
    try {
      onReintentado?.(await reintentar());
    } catch (e) {
      onError?.(mensajeReintento(e), e instanceof ApiError && e.status === 409);
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
      onClick={() => void alPulsar()}
      className={variante === "lote" ? `${BOTON_SECUNDARIO_CLS} self-start` : FILA_CLS}
    >
      <RotateCcw className={variante === "lote" ? "h-4 w-4" : "h-3.5 w-3.5"} aria-hidden="true" />
      {enviando ? "Reintentando…" : texto}
    </button>
  );
}

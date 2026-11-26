"use client";

import { useState } from "react";
import { Button } from "@flit/ui/Button";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { Modal } from "@flit/ui/Modal";
import { ApiError } from "@/lib/api";
import { mensajeErrorFijo } from "@/lib/errores";
import { relanzarItem } from "@/lib/liquidaciones/api";
import { ERRORES_LIQUIDACIONES, PANTALLA } from "@/lib/liquidaciones/estados";
import type { LiquidacionConItems } from "@/lib/liquidaciones/tipos";

interface Props {
  open: boolean;
  liquidacionId: string;
  item: { id: string; placa: string };
  empresa: string | null;
  onClose: () => void;
  /** 200 del relanzamiento: la misma liquidación. Quien abrió el diálogo vuelve a leer su pantalla (ítem o lote). */
  onRelanzado: (liquidacion: LiquidacionConItems) => void;
}

/** Códigos que se explican con su literal del contrato dentro del diálogo. */
const CON_LITERAL = new Set(["NO_RELANZABLE", "APROVISIONAMIENTO_NO_LISTO", "PRODUCTO_SUSPENDIDO", "CONFIRMACION_REQUERIDA"]);

/**
 * Relanzar una placa (spec-visual §3.5, contrato 1.3.0): abre un nuevo intento del MISMO registro; no crea otra
 * liquidación ni pide confirmar un posible duplicado. Con el 200 entrega la liquidación a `onRelanzado` (la pantalla
 * vuelve a leer el ítem o el lote y el sondeo sigue al nuevo intento); no navega. Los errores quedan dentro del diálogo,
 * que no se cierra (409 NO_RELANZABLE con el literal del contrato).
 */
export function DialogoRelanzar({ open, liquidacionId, item, empresa, onClose, onRelanzado }: Props) {
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function cerrar() {
    if (enviando) return;
    setError(null);
    onClose();
  }

  async function relanzar() {
    if (enviando) return;
    setEnviando(true);
    setError(null);
    try {
      const misma = await relanzarItem(liquidacionId, item.id, { empresa });
      setEnviando(false);
      onRelanzado(misma);
    } catch (e) {
      setEnviando(false);
      setError(
        e instanceof ApiError && e.code && CON_LITERAL.has(e.code)
          ? mensajeErrorFijo(e, "", ERRORES_LIQUIDACIONES)
          : mensajeErrorFijo(e, "No se pudo relanzar la placa. Intenta de nuevo.", {}),
      );
    }
  }

  return (
    <Modal
      open={open}
      onClose={cerrar}
      title={`Relanzar la placa ${item.placa}`}
      icono="reintento"
      size="md"
      closeOnBackdrop={false}
      busy={enviando}
      footer={
        <div className="flex flex-wrap justify-end gap-2">
          <Button variant="secondary" onClick={cerrar} disabled={enviando} data-autofocus>
            Cancelar
          </Button>
          <Button cargando={enviando} onClick={() => void relanzar()}>
            {PANTALLA.accionRelanzar}
          </Button>
        </div>
      }
    >
      <div className="flex flex-col gap-3">
        <p className="text-sm text-flit-primary">{PANTALLA.confirmarRelanzar}</p>
        {error ? (
          <InlineAlert tone="error" title="No se relanzó la placa">
            {error}
          </InlineAlert>
        ) : null}
      </div>
    </Modal>
  );
}

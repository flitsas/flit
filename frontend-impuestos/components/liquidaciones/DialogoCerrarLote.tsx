"use client";

import { useId, useState } from "react";
import { Button } from "@flit/ui/Button";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { Modal } from "@flit/ui/Modal";
import { AYUDA_CLS, CAMPO_CLS, ETIQUETA_CLS } from "@/components/comun/estilos";
import { ApiError } from "@/lib/api";
import { cerrarLiquidacion } from "@/lib/liquidaciones/api";
import { ERRORES_LIQUIDACIONES, PANTALLA } from "@/lib/liquidaciones/estados";
import type { Liquidacion } from "@/lib/liquidaciones/tipos";

/** Máximo del motivo según el contrato (POST /liquidaciones/{id}/cerrar, motivo maxLength 300). */
export const MAX_MOTIVO = 300;

interface Props {
  open: boolean;
  liquidacionId: string;
  empresa: string | null;
  onClose: () => void;
  onCerrado: (liquidacion: Liquidacion) => void;
}

/**
 * Cierre manual del lote (DA-6b, F3-CF12; spec-visual §3.5). Texto del contrato (confirmarCerrarLote), motivo opcional
 * de hasta 300 caracteres, foco inicial en «Cancelar» y acción destructiva «Cerrar lote» (variant alerta). Los errores
 * quedan dentro del diálogo, que no se cierra: 409 YA_TERMINADA con su literal; otro, texto fijo.
 */
export function DialogoCerrarLote({ open, liquidacionId, empresa, onClose, onCerrado }: Props) {
  const base = useId();
  const [motivo, setMotivo] = useState("");
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function cerrarDialogo() {
    if (enviando) return;
    setMotivo("");
    setError(null);
    onClose();
  }

  async function confirmar() {
    if (enviando) return;
    setEnviando(true);
    setError(null);
    try {
      const cerrada = await cerrarLiquidacion(liquidacionId, motivo.trim() || null, { empresa });
      setEnviando(false);
      setMotivo("");
      onCerrado(cerrada);
    } catch (e) {
      setEnviando(false);
      setError(e instanceof ApiError && e.code === "YA_TERMINADA" ? ERRORES_LIQUIDACIONES.YA_TERMINADA : "No se pudo cerrar el lote. Intenta de nuevo.");
    }
  }

  return (
    <Modal
      open={open}
      onClose={cerrarDialogo}
      title={PANTALLA.accionCerrarLote}
      icono="alerta"
      size="md"
      closeOnBackdrop={false}
      busy={enviando}
      footer={
        <div className="flex flex-wrap justify-end gap-2">
          <Button variant="secondary" onClick={cerrarDialogo} disabled={enviando} data-autofocus>
            Cancelar
          </Button>
          <Button variant="alerta" cargando={enviando} onClick={() => void confirmar()}>
            {PANTALLA.accionCerrarLote}
          </Button>
        </div>
      }
    >
      <div className="flex flex-col gap-3">
        <p className="text-sm text-flit-primary">{PANTALLA.confirmarCerrarLote}</p>
        <div className="flex flex-col gap-1">
          <label htmlFor={`${base}-motivo`} className={ETIQUETA_CLS}>
            Motivo (opcional)
          </label>
          <textarea
            id={`${base}-motivo`}
            value={motivo}
            maxLength={MAX_MOTIVO}
            rows={3}
            disabled={enviando}
            aria-describedby={`${base}-ayuda`}
            onChange={(e) => setMotivo(e.target.value)}
            className={`${CAMPO_CLS} h-auto py-2`}
          />
          <p id={`${base}-ayuda`} className={AYUDA_CLS}>{`Hasta ${MAX_MOTIVO} caracteres.`}</p>
        </div>
        {error ? (
          <InlineAlert tone="error" title="No se cerró el lote">
            {error}
          </InlineAlert>
        ) : null}
      </div>
    </Modal>
  );
}

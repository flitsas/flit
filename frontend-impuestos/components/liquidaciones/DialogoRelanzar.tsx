"use client";

import { useId, useState } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@flit/ui/Button";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { Modal } from "@flit/ui/Modal";
import { AYUDA_CLS, MARCA_CLS } from "@/components/comun/estilos";
import { ApiError } from "@/lib/api";
import { hrefConEmpresa } from "@/lib/empresaSeleccionada";
import { mensajeErrorFijo } from "@/lib/errores";
import { relanzarItem } from "@/lib/liquidaciones/api";
import { ERRORES_LIQUIDACIONES, PANTALLA } from "@/lib/liquidaciones/estados";

interface Props {
  open: boolean;
  liquidacionId: string;
  item: { id: string; placa: string };
  empresa: string | null;
  onClose: () => void;
}

/** Códigos que se explican con su literal del contrato dentro del diálogo. */
const CON_LITERAL = new Set(["NO_RELANZABLE", "APROVISIONAMIENTO_NO_LISTO", "PRODUCTO_SUSPENDIDO", "CONFIRMACION_REQUERIDA"]);

/**
 * Relanzar una placa (spec-visual §3.5, plan §3.2): crea una liquidación por placa nueva con aviso de posible duplicado.
 * La casilla «Entiendo que puede quedar duplicada» es obligatoria: hasta marcarla, «Relanzar» está deshabilitado con el
 * literal CONFIRMACION_REQUERIDA visible y asociado. Con el 201 navega al ítem nuevo. Los errores quedan dentro del
 * diálogo, que no se cierra (409 NO_RELANZABLE con el literal del contrato).
 */
export function DialogoRelanzar({ open, liquidacionId, item, empresa, onClose }: Props) {
  const router = useRouter();
  const base = useId();
  const idMotivo = `${base}-motivo`;
  const [acepta, setAcepta] = useState(false);
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function cerrar() {
    if (enviando) return;
    setAcepta(false);
    setError(null);
    onClose();
  }

  async function relanzar() {
    if (!acepta || enviando) return;
    setEnviando(true);
    setError(null);
    try {
      const nueva = await relanzarItem(liquidacionId, item.id, { empresa });
      const itemNuevo = nueva.items?.[0]?.id;
      router.push(hrefConEmpresa(itemNuevo ? `/liquidaciones/${nueva.id}/items/${itemNuevo}` : `/liquidaciones/${nueva.id}`, empresa));
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
          <Button cargando={enviando} disabled={!acepta} aria-describedby={acepta ? undefined : idMotivo} onClick={() => void relanzar()}>
            {PANTALLA.accionRelanzar}
          </Button>
        </div>
      }
    >
      <div className="flex flex-col gap-3">
        <p className="text-sm text-flit-primary">{PANTALLA.confirmarRelanzar}</p>
        <label className="flex items-center gap-2 text-sm font-semibold text-flit-primary">
          <input type="checkbox" checked={acepta} onChange={(e) => setAcepta(e.target.checked)} className={MARCA_CLS} />
          Entiendo que puede quedar duplicada
        </label>
        {!acepta ? (
          <p id={idMotivo} className={AYUDA_CLS}>
            {ERRORES_LIQUIDACIONES.CONFIRMACION_REQUERIDA}
          </p>
        ) : null}
        {error ? (
          <InlineAlert tone="error" title="No se relanzó la placa">
            {error}
          </InlineAlert>
        ) : null}
      </div>
    </Modal>
  );
}

"use client";

import { useId, useState } from "react";
import { Icono } from "@flit/ui/Icono";
import { BOTON_SECUNDARIO_CLS, ERROR_CLS, TEXTO_SECUNDARIO_CLS } from "@/components/comun/estilos";
import { ChipEstado } from "@/components/liquidaciones/ChipEstado";
import { mensajeErrorFijo } from "@/lib/errores";
import { descargarRecibo } from "@/lib/liquidaciones/api";
import { ERRORES_LIQUIDACIONES, PANTALLA, chipRecibo, motivoRecibo, nombreRecibo } from "@/lib/liquidaciones/estados";
import type { Recibo } from "@/lib/liquidaciones/tipos";

interface Props {
  liquidacionId: string;
  itemId: string;
  recibos: Recibo[];
  empresa: string | null;
  /** impuestos.recibos.descargar: sin él, solo el estado. */
  puedeDescargar: boolean;
}

/**
 * Recibos de la placa (spec-visual §3.6, §1.4). Una fila por recibo con su nombre, el chip del estado y, con GUARDADO y
 * permiso, «Descargar recibo» por el proxy BFF (el PDF nunca viene del proveedor al navegador). Cada botón tiene su
 * estado propio: «Descargando…» con aria-busy y el error debajo con role="alert" y aria-describedby.
 */
export function ListaRecibos({ liquidacionId, itemId, recibos, empresa, puedeDescargar }: Props) {
  if (recibos.length === 0) return <p className="text-sm text-muted-foreground">Esta liquidación no tiene recibos.</p>;
  return (
    <ul className="flex flex-col divide-y divide-[var(--table-row-border)]">
      {recibos.map((r) => (
        <FilaRecibo key={r.id} recibo={r} liquidacionId={liquidacionId} itemId={itemId} empresa={empresa} puedeDescargar={puedeDescargar} />
      ))}
    </ul>
  );
}

function FilaRecibo({ recibo, liquidacionId, itemId, empresa, puedeDescargar }: Omit<Props, "recibos"> & { recibo: Recibo }) {
  const idError = useId();
  const [descargando, setDescargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const nombre = nombreRecibo(recibo);
  const motivo = recibo.estado === "GUARDADO" ? null : motivoRecibo(recibo.motivo, recibo.razon);

  async function bajar() {
    if (descargando) return;
    setDescargando(true);
    setError(null);
    try {
      await descargarRecibo(liquidacionId, itemId, recibo.id, empresa);
    } catch (e) {
      setError(mensajeErrorFijo(e, "No se descargó el recibo. Intenta de nuevo.", ERRORES_LIQUIDACIONES));
    } finally {
      setDescargando(false);
    }
  }

  return (
    <li className="flex flex-wrap items-center justify-between gap-3 py-3">
      <div className="flex min-w-0 flex-col gap-1">
        <span className="text-sm font-semibold text-flit-primary">{nombre}</span>
        <span className="flex flex-wrap items-center gap-2">
          <ChipEstado chip={chipRecibo(recibo.estado)} />
          {motivo ? <span className={TEXTO_SECUNDARIO_CLS}>{motivo}</span> : null}
        </span>
      </div>
      {recibo.estado === "GUARDADO" && puedeDescargar ? (
        <div className="flex flex-col items-end gap-1">
          <button
            type="button"
            onClick={() => void bajar()}
            aria-label={`${PANTALLA.accionDescargarRecibo}: ${nombre}`}
            aria-busy={descargando ? true : undefined}
            aria-describedby={error ? idError : undefined}
            className={BOTON_SECUNDARIO_CLS}
          >
            <Icono nombre="nube-bajar" size={16} />
            {descargando ? "Descargando…" : PANTALLA.accionDescargarRecibo}
          </button>
          {error ? (
            <p id={idError} role="alert" className={ERROR_CLS}>
              {error}
            </p>
          ) : null}
        </div>
      ) : null}
    </li>
  );
}

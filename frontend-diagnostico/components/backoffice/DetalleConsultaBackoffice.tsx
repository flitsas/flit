"use client";

import { useMemo } from "react";
import { PaginaDetalleConsulta } from "@/components/tablero/PaginaDetalleConsulta";
import { TEXTO_SECUNDARIO_CLS } from "@/components/tablero/vistasDetalle/comun";
import { cargadorDetalleBackoffice, type DetalleCeldaBackoffice } from "@/lib/backoffice/api";
import type { DetalleCelda } from "@/lib/tablero/detalle";
import { MarcaManual } from "./GrillaBackoffice";

export interface DetalleConsultaBackofficeProps {
  diagnosticoId: string;
  vehiculoId: string;
  fuente: string;
  /** Compañía elegida (viaja como ?empresa= en cada llamada y en las migas). */
  empresa: string;
}

/** Nota bajo el título: el origen manual, visible solo en el backoffice (Q5). */
function NotaOrigen({ detalle }: { detalle: DetalleCelda }) {
  if ((detalle as DetalleCeldaBackoffice).origen !== "MANUAL") return null;
  return (
    <p className={`flex flex-wrap items-center gap-2 ${TEXTO_SECUNDARIO_CLS}`}>
      <MarcaManual />
      Dato llenado a mano desde el backoffice.
    </p>
  );
}

/**
 * Página del detalle de una consulta en el backoffice FLIT (HU #14087), que el semáforo de su grilla abre en una pestaña nueva:
 * la misma PaginaDetalleConsulta con el cargador del backoffice (?empresa=), migas y «Volver al diagnóstico» que se quedan en
 * /backoffice con la compañía y la nota de origen manual. «Llenar a mano» sigue en la grilla, como modal.
 */
export function DetalleConsultaBackoffice({ diagnosticoId, vehiculoId, fuente, empresa }: DetalleConsultaBackofficeProps) {
  const cargar = useMemo(() => cargadorDetalleBackoffice(empresa), [empresa]);
  const q = new URLSearchParams({ empresa }).toString();
  return (
    <PaginaDetalleConsulta
      loteId={diagnosticoId}
      vehiculoId={vehiculoId}
      fuente={fuente}
      cargar={cargar}
      backoffice
      rutaListado={`/backoffice/diagnosticos?${q}`}
      rutaDiagnostico={`/backoffice/diagnosticos/${encodeURIComponent(diagnosticoId)}?${q}`}
      nota={(d) => <NotaOrigen detalle={d} />}
    />
  );
}

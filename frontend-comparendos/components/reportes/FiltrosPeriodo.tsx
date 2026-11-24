"use client";

import { useState } from "react";
import { InlineAlert } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { SelectorEmpresa } from "@/components/SelectorEmpresa";
import { BOTON_PRIMARIO_CLS, ETIQUETA_CLS, FECHA_CLS } from "@/components/ui/estilos";
import { diaColombia } from "@/lib/format/date";
import { errorRango, inicioDeMes } from "@/lib/reportes";

export interface Periodo {
  desde: string;
  hasta: string;
  empresa: string | null;
}

export function periodoInicial(): Periodo {
  const hoy = diaColombia();
  return { desde: inicioDeMes(hoy), hasta: hoy, empresa: null };
}

/**
 * Filtros comunes de los reportes: periodo (por defecto el mes en curso) y, para el SuperAdmin, la empresa (vacía =
 * todas). Se aplican con «Consultar» para no disparar una consulta por cada tecla.
 */
export function FiltrosPeriodo({
  valor,
  onAplicar,
  extra,
  accion,
}: {
  valor: Periodo;
  onAplicar: (p: Periodo) => void;
  extra?: React.ReactNode;
  /** Acción de la pestaña (p. ej. «Descargar Excel») a la derecha de la fila, como `ExportButtons` en Trámites. */
  accion?: React.ReactNode;
}) {
  const { sesion } = useSesion();
  const [borrador, setBorrador] = useState(valor);
  const error = errorRango(borrador.desde, borrador.hasta);

  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-end gap-3">
        <label className={ETIQUETA_CLS}>
          Desde
          <input type="date" className={`${FECHA_CLS} w-full sm:w-40`} value={borrador.desde} onChange={(e) => setBorrador({ ...borrador, desde: e.target.value })} />
        </label>
        <label className={ETIQUETA_CLS}>
          Hasta
          <input type="date" className={`${FECHA_CLS} w-full sm:w-40`} value={borrador.hasta} onChange={(e) => setBorrador({ ...borrador, hasta: e.target.value })} />
        </label>
        {/* Patrón de GlobalFilters de Trámites: etiqueta arriba, sin texto de ayuda; la opción vacía ya dice que son todas. */}
        {sesion?.usuario.esSuperAdmin && (
          <SelectorEmpresa etiquetaArriba textoVacio="Todas las empresas" valor={borrador.empresa} onCambio={(empresa) => setBorrador({ ...borrador, empresa })} />
        )}
        {extra}
        <button type="button" className={BOTON_PRIMARIO_CLS} disabled={!!error} onClick={() => onAplicar(borrador)}>
          Consultar
        </button>
        {accion && <div className="ml-auto">{accion}</div>}
      </div>
      {error && borrador.desde && borrador.hasta && (
        <InlineAlert tone="warning" compact>
          {error}
        </InlineAlert>
      )}
    </div>
  );
}

"use client";

import { useState } from "react";
import { InlineAlert } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { SelectorEmpresa } from "@/components/SelectorEmpresa";
import { BOTON_PRIMARIO_CLS, ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
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
export function FiltrosPeriodo({ valor, onAplicar, extra }: { valor: Periodo; onAplicar: (p: Periodo) => void; extra?: React.ReactNode }) {
  const { sesion } = useSesion();
  const [borrador, setBorrador] = useState(valor);
  const error = errorRango(borrador.desde, borrador.hasta);

  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-end gap-3">
        <label className={ETIQUETA_CLS}>
          Desde
          <input type="date" className={`${INPUT_CLS} w-40`} value={borrador.desde} onChange={(e) => setBorrador({ ...borrador, desde: e.target.value })} />
        </label>
        <label className={ETIQUETA_CLS}>
          Hasta
          <input type="date" className={`${INPUT_CLS} w-40`} value={borrador.hasta} onChange={(e) => setBorrador({ ...borrador, hasta: e.target.value })} />
        </label>
        {sesion?.usuario.esSuperAdmin && (
          <div className="flex flex-col gap-1">
            <SelectorEmpresa valor={borrador.empresa} onCambio={(empresa) => setBorrador({ ...borrador, empresa })} />
            <span className="text-[11px] opacity-60">Sin empresa elegida se ven todas.</span>
          </div>
        )}
        {extra}
        <button type="button" className={BOTON_PRIMARIO_CLS} disabled={!!error} onClick={() => onAplicar(borrador)}>
          Consultar
        </button>
      </div>
      {error && borrador.desde && borrador.hasta && (
        <InlineAlert tone="warning" compact>
          {error}
        </InlineAlert>
      )}
    </div>
  );
}

"use client";

import { Fragment, useState } from "react";
import { ArrowUpRight, ChevronDown, ChevronRight, Loader2 } from "lucide-react";
import {
  ETIQUETA_ESTADO,
  estaTerminada,
  type EstadoFila,
  type FilaLote,
} from "@/lib/migracion/progreso";
import { claveFila, enlaceTramite, ETIQUETA_TRAMITE } from "@/lib/migracion/types";
import { ReporteMigracion } from "./ReporteMigracion";

/**
 * La tabla del lote: una casilla por trámite y el resultado de cada uno en cuanto llega.
 *
 * Las filas ya terminadas siguen siendo seleccionables pero NO se vuelven a encolar (lo filtra
 * quien ejecuta). Es a propósito: quitarles la casilla impediría el caso legítimo de querer
 * reintentar una que quedó con avisos, y desmarcarlas solas escondería lo que ya se hizo.
 */
export function TablaLote({
  filas,
  seleccion,
  onSeleccion,
  bloqueada,
}: {
  filas: FilaLote[];
  seleccion: Set<string>;
  onSeleccion: (valor: Set<string>) => void;
  bloqueada: boolean;
}) {
  const [abierta, setAbierta] = useState<string | null>(null);

  const seleccionables = filas.filter((f) => !estaTerminada(f));
  const todasMarcadas =
    seleccionables.length > 0 && seleccionables.every((f) => seleccion.has(claveFila(f)));

  function alternar(fila: FilaLote) {
    const clave = claveFila(fila);
    const siguiente = new Set(seleccion);
    if (siguiente.has(clave)) {
      siguiente.delete(clave);
    } else {
      siguiente.add(clave);
    }
    onSeleccion(siguiente);
  }

  function alternarTodas() {
    // La casilla del encabezado gobierna solo lo PENDIENTE: es lo que se va a migrar, y marcar
    // "todo" incluyendo lo ya hecho daría un contador que no coincide con lo que va a correr.
    onSeleccion(todasMarcadas ? new Set() : new Set(seleccionables.map(claveFila)));
  }

  return (
    <div className="overflow-x-auto rounded-2xl border border-[#DFE5ED] dark:border-white/10">
      <table className="w-full min-w-[640px] text-sm">
        <thead>
          <tr className="border-b border-[#DFE5ED] text-left text-xs uppercase tracking-wide opacity-60 dark:border-white/10">
            <th scope="col" className="w-10 p-3">
              <input
                type="checkbox"
                aria-label="Seleccionar todos los pendientes"
                className="h-4 w-4 accent-[#557EFF]"
                checked={todasMarcadas}
                onChange={alternarTodas}
                disabled={bloqueada || seleccionables.length === 0}
              />
            </th>
            <th scope="col" className="w-16 p-3 font-semibold">Fila</th>
            <th scope="col" className="p-3 font-semibold">Trámite</th>
            <th scope="col" className="p-3 font-semibold">Id V1</th>
            <th scope="col" className="p-3 font-semibold">Estado</th>
            <th scope="col" className="p-3 font-semibold">Resultado</th>
          </tr>
        </thead>
        <tbody>
          {filas.map((fila) => {
            const clave = claveFila(fila);
            const desplegada = abierta === clave;

            return (
              <Fragment key={clave}>
                <tr className="border-b border-[#DFE5ED]/60 last:border-0 dark:border-white/5">
                  <td className="p-3">
                    <input
                      type="checkbox"
                      aria-label={`Seleccionar ${ETIQUETA_TRAMITE[fila.tramite]} ${fila.v1Id}`}
                      className="h-4 w-4 accent-[#557EFF]"
                      checked={seleccion.has(clave)}
                      onChange={() => alternar(fila)}
                      disabled={bloqueada}
                    />
                  </td>
                  <td className="p-3 tabular-nums opacity-60">{fila.fila}</td>
                  <td className="p-3">{ETIQUETA_TRAMITE[fila.tramite]}</td>
                  <td className="p-3 font-medium tabular-nums">{fila.v1Id}</td>
                  <td className="p-3">
                    <Estado estado={fila.estado} />
                  </td>
                  <td className="p-3">
                    <div className="flex flex-wrap items-center gap-2">
                      {fila.respuesta?.destino && (
                        <a
                          href={enlaceTramite(fila.respuesta.destino)}
                          target="_blank"
                          rel="noreferrer"
                          className="flex items-center gap-1 text-xs font-semibold"
                          style={{ color: "#557EFF" }}
                        >
                          Ver en V2
                          <ArrowUpRight className="h-3 w-3" aria-hidden="true" />
                        </a>
                      )}

                      {fila.respuesta && (
                        <button
                          type="button"
                          onClick={() => setAbierta(desplegada ? null : clave)}
                          className="flex items-center gap-1 text-xs opacity-70"
                        >
                          {desplegada ? (
                            <ChevronDown className="h-3 w-3" aria-hidden="true" />
                          ) : (
                            <ChevronRight className="h-3 w-3" aria-hidden="true" />
                          )}
                          {desplegada ? "Ocultar" : "Ver reporte"}
                        </button>
                      )}

                      {fila.error && (
                        <span className="text-xs text-red-500" title={fila.error}>
                          {fila.error}
                        </span>
                      )}
                    </div>
                  </td>
                </tr>

                {desplegada && fila.respuesta && (
                  <tr className="border-b border-[#DFE5ED]/60 dark:border-white/5">
                    <td colSpan={6} className="bg-black/[0.02] p-4 dark:bg-white/[0.02]">
                      <ReporteMigracion respuesta={fila.respuesta} />
                    </td>
                  </tr>
                )}
              </Fragment>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

const TONO: Record<EstadoFila, string> = {
  pendiente: "bg-black/5 dark:bg-white/10 opacity-70",
  en_curso: "bg-blue-500/10 text-blue-600 dark:text-blue-400",
  migrado: "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400",
  con_avisos: "bg-amber-500/10 text-amber-600 dark:text-amber-400",
  fallido: "bg-red-500/10 text-red-600 dark:text-red-400",
  ya_estaba: "bg-slate-500/10 text-slate-600 dark:text-slate-300",
};

function Estado({ estado }: { estado: EstadoFila }) {
  return (
    <span
      className={`inline-flex items-center gap-1.5 whitespace-nowrap rounded-md px-2 py-0.5 text-xs font-semibold ${TONO[estado]}`}
    >
      {estado === "en_curso" && <Loader2 className="h-3 w-3 animate-spin" aria-hidden="true" />}
      {ETIQUETA_ESTADO[estado]}
    </span>
  );
}

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
import { Pagination } from "@/components/atom/Pagination";
import { usePaginacion } from "@/components/atom/usePaginacion";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";

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
  // Bug #13055 — tabla homologada con el modelo de trámites: sin tarjeta envolvente, cabecera y
  // filas del modelo y paginación en cliente con filas por página. La selección vive en `seleccion`
  // (por clave), así que cambiar de página no la pierde.
  const pg = usePaginacion();
  const head = { background: TABLA_HEADER_BG, color: TABLA_HEADER_FG } as const;
  const cell = { borderColor: "#DFE5ED" } as const;

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
    <div className="flex flex-col">
    <div className="overflow-x-auto">
      <table
        className="min-w-[640px] text-sm"
        style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}
      >
        <caption className="sr-only">Trámites del lote de migración</caption>
        <thead>
          <tr>
            <th scope="col" className={`${TABLA_HEADER_CELL_CLS} w-10 rounded-l-xl`} style={head}>
              <input
                type="checkbox"
                aria-label="Seleccionar todos los pendientes"
                className="h-4 w-4 accent-[#557EFF]"
                checked={todasMarcadas}
                onChange={alternarTodas}
                disabled={bloqueada || seleccionables.length === 0}
              />
            </th>
            <th scope="col" className={`${TABLA_HEADER_CELL_CLS} w-16`} style={head}>Fila</th>
            <th scope="col" className={TABLA_HEADER_CELL_CLS} style={head}>Trámite</th>
            <th scope="col" className={TABLA_HEADER_CELL_CLS} style={head}>Id V1</th>
            <th scope="col" className={TABLA_HEADER_CELL_CLS} style={head}>Estado</th>
            <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl`} style={head}>Resultado</th>
          </tr>
        </thead>
        <tbody>
          {pg.paginar(filas).map((fila) => {
            const clave = claveFila(fila);
            const desplegada = abierta === clave;

            return (
              <Fragment key={clave}>
                <tr className={`bg-white dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`}>
                  <td className="rounded-l-xl border-y border-l px-4 py-3" style={cell}>
                    <input
                      type="checkbox"
                      aria-label={`Seleccionar ${ETIQUETA_TRAMITE[fila.tramite]} ${fila.v1Id}`}
                      className="h-4 w-4 accent-[#557EFF]"
                      checked={seleccion.has(clave)}
                      onChange={() => alternar(fila)}
                      disabled={bloqueada}
                    />
                  </td>
                  <td className="border-y px-4 py-3 tabular-nums opacity-60" style={cell}>{fila.fila}</td>
                  <td className="border-y px-4 py-3" style={cell}>{ETIQUETA_TRAMITE[fila.tramite]}</td>
                  <td className="border-y px-4 py-3 font-medium tabular-nums" style={cell}>{fila.v1Id}</td>
                  <td className="border-y px-4 py-3" style={cell}>
                    <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                      <Estado estado={fila.estado} />
                      {/*
                        El motivo va AQUÍ y no dentro del reporte. Un id que no existe en V1
                        devuelve 200 con la instancia en cuarentena, así que la fila decía «Falló»
                        y nada más: para leer «No existe en la copia de V1» había que desplegar el
                        reporte y bajar hasta la instancia. Con tres fallos en una ola de veinte,
                        eso son tres despliegues para enterarse de algo que cabe en una línea.
                      */}
                      {motivoDelFallo(fila) && (
                        <span className="text-xs opacity-70">{motivoDelFallo(fila)}</span>
                      )}
                    </div>
                  </td>
                  <td className="rounded-r-xl border-y border-r px-4 py-3" style={cell}>
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
                          className="flex items-center gap-1 text-xs font-semibold opacity-80 hover:opacity-100"
                        >
                          {desplegada ? (
                            <ChevronDown className="h-3 w-3" aria-hidden="true" />
                          ) : (
                            <ChevronRight className="h-3 w-3" aria-hidden="true" />
                          )}
                          {desplegada ? "Ocultar" : "Ver reporte"}
                        </button>
                      )}

                    </div>
                  </td>
                </tr>

                {desplegada && fila.respuesta && (
                  <tr>
                    <td
                      colSpan={6}
                      className="rounded-xl border bg-white p-4 dark:bg-[#0B0F14]"
                      style={cell}
                    >
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
    <Pagination
      page={pg.page}
      pageSize={pg.pageSize}
      totalCount={filas.length}
      onPageChange={pg.setPage}
      onPageSizeChange={pg.setPageSize}
      ariaLabel="Paginación del lote"
      noun="trámites"
    />
    </div>
  );
}

/**
 * Por qué falló una fila, en una línea.
 *
 * Dos orígenes distintos que para quien mira son lo mismo: `error` es lo que reventó del lado del
 * navegador (la petición no llegó, o el host devolvió un ProblemDetails), y `motivo` es lo que el
 * motor dice de la instancia que se quedó en cuarentena, que llega en un 200 perfectamente normal.
 */
function motivoDelFallo(fila: FilaLote): string | null {
  if (fila.error) {
    return fila.error;
  }
  return fila.respuesta?.instancias.find((i) => i.conProblemas)?.motivo ?? null;
}

const TONO: Record<EstadoFila, string> = {
  pendiente: "bg-black/5 dark:bg-white/10 opacity-70",
  en_curso: "bg-blue-500/10 text-blue-600 dark:text-blue-400",
  // Deliberadamente NO verde: una simulación no migró nada, y el verde es la señal universal de
  // «hecho». Se parece más a un pendiente informado que a un logro.
  simulado: "bg-[#557EFF]/10 text-[#557EFF] dark:text-[#8AA6FF]",
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

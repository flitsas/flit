"use client";

import { useId, useState } from "react";
import { AlertTriangle, CheckCircle2, XCircle } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { FOCO_CLS } from "@/components/configuracion/estilos";
import { fechaCorta, formatearPesos, type ResultadoSimitDetalle } from "@/lib/tablero/detalle";
import { CABECERA_TABLA_CLS, CABECERA_TABLA_ESTILO, CELDA_TABLA_CLS, Etiqueta, FILA_TABLA_CLS, TEXTO_SECUNDARIO_CLS, Vacio, type TonoEtiqueta } from "./comun";

/** Comparendos visibles antes de expandir la lista. */
export const LIMITE_COMPARENDOS = 10;

const SEMAFOROS: Record<NonNullable<ResultadoSimitDetalle["semaforo"]>, { tono: TonoEtiqueta; texto: string; Icono: typeof CheckCircle2 }> = {
  VERDE: { tono: "success", texto: "Verde", Icono: CheckCircle2 },
  AMARILLO: { tono: "warning", texto: "Amarillo", Icono: AlertTriangle },
  ROJO: { tono: "danger", texto: "Rojo", Icono: XCircle },
};

const COLUMNAS = ["Número", "Fecha", "Valor", "Estado", "Organismo"];

/**
 * Comparendos del SIMIT (HU #13801, F06-C13): semáforo, total y tabla con número, fecha, valor en pesos, estado y organismo.
 * La lista se limita a {@link LIMITE_COMPARENDOS} y se expande con un botón (aria-expanded); si la API la cortó
 * (`comparendos_truncados`, más de 100) lo avisa. La usan SIMIT del vehículo y la sección SIMIT de cada persona.
 */
export function DetalleSimit({ resultado, titulo = "Comparendos" }: { resultado: ResultadoSimitDetalle; titulo?: string }) {
  const idTabla = useId();
  const [expandida, setExpandida] = useState(false);
  const { comparendos, cantidad_comparendos: total } = resultado;
  const semaforo = resultado.semaforo ? SEMAFOROS[resultado.semaforo] : null;
  const visibles = expandida ? comparendos : comparendos.slice(0, LIMITE_COMPARENDOS);

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        {semaforo && (
          <Etiqueta tono={semaforo.tono}>
            <semaforo.Icono className="h-3.5 w-3.5" aria-hidden="true" />
            {`Semáforo SIMIT: ${semaforo.texto}`}
          </Etiqueta>
        )}
        <span className="text-sm font-semibold text-flit-primary dark:text-white">{`${total} ${total === 1 ? "comparendo" : "comparendos"}`}</span>
      </div>

      {resultado.comparendos_truncados && (
        <InlineAlert tone="info">{`Se muestran ${comparendos.length} de ${total} comparendos. El SIMIT tiene el listado completo.`}</InlineAlert>
      )}

      {comparendos.length === 0 ? (
        <Vacio>Sin comparendos registrados.</Vacio>
      ) : (
        <>
          <div className="overflow-x-auto rounded-xl border border-[var(--color-flit-gray)] dark:border-white/10">
            <table id={idTabla} aria-label={titulo} className="w-full min-w-[520px] border-collapse">
              <thead>
                <tr>
                  {COLUMNAS.map((c) => (
                    <th key={c} scope="col" className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
                      {c}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {visibles.map((c, i) => (
                  <tr key={`${c.numero ?? "sin-numero"}-${i}`} className={FILA_TABLA_CLS}>
                    <td className={`${CELDA_TABLA_CLS} font-mono`}>{c.numero ?? "—"}</td>
                    <td className={CELDA_TABLA_CLS}>{fechaCorta(c.fecha)}</td>
                    <td className={`${CELDA_TABLA_CLS} whitespace-nowrap`}>{formatearPesos(c.valor)}</td>
                    <td className={CELDA_TABLA_CLS}>{c.estado ?? "—"}</td>
                    <td className={CELDA_TABLA_CLS}>{c.organismo ?? "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {comparendos.length > LIMITE_COMPARENDOS && (
            <div className="flex items-center gap-3">
              <button
                type="button"
                aria-expanded={expandida}
                aria-controls={idTabla}
                onClick={() => setExpandida((v) => !v)}
                className={`rounded-full border border-current px-3 py-1 text-xs font-semibold text-[var(--flit-brand-ink)] dark:text-white ${FOCO_CLS}`}
              >
                {expandida ? "Ver menos" : `Ver los ${comparendos.length} comparendos`}
              </button>
              {!expandida && <span className={TEXTO_SECUNDARIO_CLS}>{`Mostrando ${LIMITE_COMPARENDOS} de ${comparendos.length}`}</span>}
            </div>
          )}
        </>
      )}
    </div>
  );
}

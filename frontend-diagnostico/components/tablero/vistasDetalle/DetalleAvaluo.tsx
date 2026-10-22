import { CircleSlash } from "lucide-react";
import { fechaCorta, formatearPesos, type ResultadoAvaluoDetalle } from "@/lib/tablero/detalle";
import { CABECERA_TABLA_CLS, CABECERA_TABLA_ESTILO, CELDA_TABLA_CLS, Etiqueta, FILA_TABLA_CLS, TARJETA_DETALLE_CLS, TEXTO_SECUNDARIO_CLS } from "./comun";

const NOMBRES_FUENTE: Record<string, string> = {
  fasecolda: "Fasecolda",
  mercado_libre: "Mercado Libre",
  base_gravable: "Base gravable",
};

const ESTADOS_FUENTE = {
  OK: { tono: "success", texto: "Con valor" },
  SIN_DATOS: { tono: "neutral", texto: "Sin datos" },
  ERROR: { tono: "danger", texto: "Error" },
} as const;

export const nombreFuenteAvaluo = (f: string | null) => (f ? (NOMBRES_FUENTE[f] ?? f) : "—");

/**
 * Avalúo comercial (HU #13801, F06-C13): valor sugerido y fuente principal, o «Sin valor comercial» cuando ninguna fuente
 * reportó valor (Q4 = b: COMPLETADO, no es un error), y una fila por fuente consultada.
 */
export function DetalleAvaluo({ resultado }: { resultado: ResultadoAvaluoDetalle }) {
  return (
    <div className="flex flex-col gap-3">
      <div className={TARJETA_DETALLE_CLS}>
        {resultado.sin_valor_comercial ? (
          <div className="flex items-start gap-2">
            <CircleSlash className="mt-0.5 h-5 w-5 shrink-0 text-[var(--nav-texto)]" aria-hidden="true" />
            <div>
              <p className="text-base font-semibold text-flit-primary dark:text-white">Sin valor comercial</p>
              <p className={TEXTO_SECUNDARIO_CLS}>Ninguna fuente reportó un valor para este vehículo.</p>
            </div>
          </div>
        ) : (
          <div className="flex flex-col gap-0.5">
            <span className={TEXTO_SECUNDARIO_CLS}>Valor sugerido</span>
            <span className="text-xl font-bold text-flit-primary dark:text-white">{formatearPesos(resultado.valor_sugerido)}</span>
            {resultado.fuente_principal && <span className={TEXTO_SECUNDARIO_CLS}>{`Fuente principal: ${nombreFuenteAvaluo(resultado.fuente_principal)}`}</span>}
          </div>
        )}
      </div>

      {resultado.fuentes.length > 0 && (
        <div className="overflow-x-auto rounded-xl border border-[var(--color-flit-gray)] dark:border-white/10">
          <table aria-label="Fuentes del avalúo" className="w-full min-w-[480px] border-collapse">
            <thead>
              <tr>
                {["Fuente", "Estado", "Valor", "Muestras", "Fecha"].map((c) => (
                  <th key={c} scope="col" className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
                    {c}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {resultado.fuentes.map((f, i) => {
                const estado = f.estado ? ESTADOS_FUENTE[f.estado] : null;
                return (
                  <tr key={`${f.fuente ?? "fuente"}-${i}`} className={FILA_TABLA_CLS}>
                    <td className={CELDA_TABLA_CLS}>{nombreFuenteAvaluo(f.fuente)}</td>
                    <td className={CELDA_TABLA_CLS}>{estado ? <Etiqueta tono={estado.tono}>{estado.texto}</Etiqueta> : "—"}</td>
                    <td className={`${CELDA_TABLA_CLS} whitespace-nowrap`}>{formatearPesos(f.valor)}</td>
                    <td className={CELDA_TABLA_CLS}>{f.muestras ?? "—"}</td>
                    <td className={CELDA_TABLA_CLS}>{fechaCorta(f.fecha)}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

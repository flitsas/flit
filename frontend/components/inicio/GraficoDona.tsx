import type { ReactNode } from "react";

/** Una porción de la dona: clave estable, el texto de la leyenda, la cifra y el color de la porción. */
export interface PorcionDona {
  clave: string;
  etiqueta: ReactNode;
  valor: number;
  /** Color de la porción y del punto de la leyenda (un token, p. ej. el acento del estado). */
  color: string;
  /** Lo que acompaña a la cifra en la leyenda (p. ej. el porcentaje). */
  detalle?: ReactNode;
}

const R = 42;
const CIRCUNFERENCIA = 2 * Math.PI * R;

/**
 * UI Kit 2026 — dona de la propuesta (`dona()` de propuesta.js / `.dona`): anillo de 18 px en un SVG de 150 px con el
 * total al centro y la leyenda en lista a la derecha (puntos de 10 px y la cifra alineada a la derecha).
 *
 * Solo presentación: el SVG es decorativo (`aria-hidden`). Los datos los lee el lector de pantalla en la lista, igual
 * que antes de la dona.
 */
export function GraficoDona({ porciones, total }: { porciones: PorcionDona[]; total: number }) {
  const conValor = porciones.filter((p) => p.valor > 0);
  const suma = conValor.reduce((a, p) => a + p.valor, 0);
  let recorrido = 0;
  return (
    <div className="flex flex-wrap items-center gap-5 max-sm:justify-center">
      <svg viewBox="0 0 120 120" aria-hidden="true" focusable="false" className="h-[150px] w-[150px] shrink-0 max-md:h-[132px] max-md:w-[132px]">
        {suma > 0 &&
          conValor.map((p) => {
            const largo = (CIRCUNFERENCIA * p.valor) / suma;
            const arco = (
              <circle
                key={p.clave}
                cx="60"
                cy="60"
                r={R}
                fill="none"
                strokeWidth="18"
                style={{ stroke: p.color }}
                strokeDasharray={`${largo.toFixed(2)} ${(CIRCUNFERENCIA - largo).toFixed(2)}`}
                strokeDashoffset={(-recorrido).toFixed(2)}
                transform="rotate(-90 60 60)"
              />
            );
            recorrido += largo;
            return arco;
          })}
        <text
          x="60"
          y="65"
          textAnchor="middle"
          className="text-[20px] font-bold tabular-nums"
          style={{ fill: "var(--di-text-primary)" }}
        >
          {total}
        </text>
      </svg>
      <ul className="m-0 flex min-w-[150px] flex-1 list-none flex-col gap-1.5 p-0 text-[0.8125rem]">
        {porciones.map((p) => (
          <li key={p.clave} className="flex items-center gap-2">
            <span aria-hidden="true" className="h-2.5 w-2.5 shrink-0 rounded-full" style={{ background: p.color }} />
            <span className="min-w-0 flex-1 text-[color:var(--di-text-primary)]">{p.etiqueta}</span>
            <strong className="tabular-nums text-[color:var(--di-text-primary)]">{p.valor}</strong>
            {p.detalle != null ? (
              <span className="min-w-[3rem] text-right tabular-nums text-[color:var(--di-text-tertiary)]">{p.detalle}</span>
            ) : null}
          </li>
        ))}
      </ul>
    </div>
  );
}

"use client";

import { ChevronLeft, ChevronRight } from "lucide-react";

/** Tamaños estándar de TODAS las tablas FLIT (HU #13916): 10/20/30/50. */
export const TAMANOS_DE_PAGINA = [10, 20, 30, 50] as const;
export const TAMANO_POR_DEFECTO = 10;
// El tamaño no se recuerda entre visitas: el portal no usa almacenamiento del navegador (datos-personales.test.ts).

/** Números a mostrar: primera, última y la vecindad de la actual, con «…» (null) en los saltos. */
export function paginasVisibles(actual: number, total: number): (number | null)[] {
  const base = new Set([1, total, actual - 1, actual, actual + 1].filter((p) => p >= 1 && p <= total));
  const orden = [...base].sort((a, b) => a - b);
  const salida: (number | null)[] = [];
  orden.forEach((p, i) => {
    if (i > 0 && p - orden[i - 1] > 1) salida.push(null);
    salida.push(p);
  });
  return salida;
}

type Props = {
  pagina: number;
  tamano: number;
  total: number;
  onPagina: (pagina: number) => void;
  onTamano: (tamano: number) => void;
};

const CONTROL = "inline-flex h-9 min-w-9 items-center justify-center rounded-xl border border-border bg-card px-3 text-xs font-semibold transition hover:bg-muted disabled:cursor-not-allowed disabled:opacity-50";

/** Paginación estándar: «Filas por página» a la izquierda y «Mostrando X–Y de N» con la navegación numerada. */
export function PaginacionCaja({ pagina, tamano, total, onPagina, onTamano }: Props) {
  const paginas = Math.max(1, Math.ceil(total / tamano));
  const actual = Math.min(pagina, paginas);
  const desde = total === 0 ? 0 : (actual - 1) * tamano + 1;
  const hasta = Math.min(actual * tamano, total);

  return (
    <div className="flex flex-wrap items-center justify-between gap-4 pt-3">
      <label className="flex items-center gap-2 text-xs">
        Filas por página
        <select
          value={tamano}
          onChange={(e) => onTamano(Number(e.target.value))}
          className="h-9 rounded-xl border border-border bg-card px-3 text-xs font-semibold"
        >
          {TAMANOS_DE_PAGINA.map((n) => (
            <option key={n} value={n}>
              {n}
            </option>
          ))}
        </select>
      </label>
      <nav aria-label="Paginación de cobros" className="flex flex-1 flex-wrap items-center justify-end gap-3">
        <span className="text-xs" aria-live="polite">
          {total === 0 ? "Sin cobros que mostrar" : `Mostrando ${desde}–${hasta} de ${total}`}
        </span>
        {paginas > 1 && (
          <ul className="flex items-center gap-1">
            <li>
              <button type="button" className={CONTROL} aria-label="Página anterior" disabled={actual <= 1} onClick={() => onPagina(actual - 1)}>
                <ChevronLeft aria-hidden className="h-4 w-4" />
              </button>
            </li>
            {paginasVisibles(actual, paginas).map((p, i) => (
              <li key={p ?? `salto-${i}`}>
                {p === null ? (
                  <span aria-hidden className="px-1 text-xs">
                    …
                  </span>
                ) : (
                  <button
                    type="button"
                    className={`${CONTROL} ${p === actual ? "border-primary bg-primary text-primary-foreground hover:bg-primary" : ""}`}
                    aria-label={`Página ${p}`}
                    aria-current={p === actual ? "page" : undefined}
                    onClick={() => onPagina(p)}
                  >
                    {p}
                  </button>
                )}
              </li>
            ))}
            <li>
              <button type="button" className={CONTROL} aria-label="Página siguiente" disabled={actual >= paginas} onClick={() => onPagina(actual + 1)}>
                <ChevronRight aria-hidden className="h-4 w-4" />
              </button>
            </li>
          </ul>
        )}
      </nav>
    </div>
  );
}

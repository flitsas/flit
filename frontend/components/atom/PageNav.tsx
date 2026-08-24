'use client';

/**
 * Paginación numerada del diseño FLIT: ‹ 1 2 … N ›, con la página activa rellena en azul.
 *
 * Nació dentro de `TramitesTable` y vive aquí porque la usa más de una pantalla. Es distinta de
 * `components/atom/Pagination`, que solo ofrece Anterior / Siguiente: cuando el listado tiene
 * decenas de páginas, saltar a una concreta exige los números.
 *
 * La línea de conteo se pinta SIEMPRE —es la única pieza que dice cuántos registros hay—, pero
 * los botones se ocultan con una sola página: no tiene sentido paginar lo que no se pagina. El
 * texto lo pone quien la usa, porque no es el mismo en un listado que cuenta en cliente
 * («Mostrando 25 de 300») que en uno paginado en servidor («Mostrando 26–50 de 300»).
 */
export interface PageNavProps {
  /** Página actual, 1-based. */
  page: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  /** Línea de conteo ya redactada, p. ej. «Mostrando 1–25 de 300». */
  resumen: string;
  /** Nombre accesible de la navegación; distingue varias paginaciones en una misma pantalla. */
  ariaLabel: string;
  className?: string;
}

export function PageNav({
  page,
  totalPages,
  onPageChange,
  resumen,
  ariaLabel,
  className = '',
}: PageNavProps) {
  const hayVariasPaginas = totalPages > 1;

  // La ventana se calcula en vez de dibujarse fija para que «…» solo aparezca cuando de verdad
  // hay páginas ocultas.
  const paginas: (number | 'gap')[] = [];
  for (let p = 1; p <= totalPages; p += 1) {
    const cerca = Math.abs(p - page) <= 1;
    if (p === 1 || p === totalPages || cerca) paginas.push(p);
    else if (paginas[paginas.length - 1] !== 'gap') paginas.push('gap');
  }

  return (
    <nav
      className={`flex flex-wrap items-center justify-end gap-4 pt-3 ${className}`}
      aria-label={ariaLabel}
    >
      <p className="text-xs opacity-70" role="status" aria-live="polite">
        {resumen}
      </p>
      {hayVariasPaginas ? (
        <div className="flex items-center gap-1">
          <button
            type="button"
            onClick={() => onPageChange(page - 1)}
            disabled={page <= 1}
            className="grid h-7 min-w-7 place-items-center rounded-lg px-2 text-xs font-semibold transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:opacity-40"
            style={{ color: '#557EFF', background: 'rgba(85,126,255,0.08)' }}
            aria-label="Página anterior"
          >
            ‹
          </button>
          {paginas.map((p, i) =>
            p === 'gap' ? (
              <span key={`gap-${i}`} className="px-1 text-xs opacity-50" aria-hidden="true">
                …
              </span>
            ) : (
              <button
                key={p}
                type="button"
                onClick={() => onPageChange(p)}
                aria-label={`Página ${p}`}
                aria-current={p === page ? 'page' : undefined}
                className="grid h-7 min-w-7 place-items-center rounded-lg px-2 text-xs font-semibold tabular-nums transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
                style={
                  p === page
                    ? { background: '#557EFF', color: '#fff' }
                    : { color: '#557EFF', background: 'rgba(85,126,255,0.08)' }
                }
              >
                {p}
              </button>
            ),
          )}
          <button
            type="button"
            onClick={() => onPageChange(page + 1)}
            disabled={page >= totalPages}
            className="grid h-7 min-w-7 place-items-center rounded-lg px-2 text-xs font-semibold transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:opacity-40"
            style={{ color: '#557EFF', background: 'rgba(85,126,255,0.08)' }}
            aria-label="Página siguiente"
          >
            ›
          </button>
        </div>
      ) : null}
    </nav>
  );
}

'use client';

/**
 * Paginación numerada del diseño FLIT: ‹ 1 2 … N ›, con la página activa rellena en tinta.
 *
 * Nació dentro de `TramitesTable`; vive en @flit/ui porque es la paginación ESTÁNDAR de todas las
 * tablas (Bug #13055). La compone `Pagination` junto al selector «Filas por página»: cuando el listado tiene
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

/**
 * UI Kit 2026 (propuesta-2, paginación): números en círculos de 36 px (44 px en celular), la página actual en azul con
 * texto blanco y el resto con fondo --kit-hover al pasar el puntero; flechas y extremos en gris. Se conservan
 * «Primera»/«Última» (HU #13916).
 */
const BOTON_FLECHA_CLS =
  'inline-grid h-9 min-w-9 place-items-center rounded-full px-3 text-[0.8125rem] font-semibold ' +
  'text-[color:var(--di-text-tertiary)] transition-colors hover:bg-[var(--kit-hover)] hover:text-[color:var(--di-action)] focus:outline-none ' +
  'focus-visible:ring-2 focus-visible:ring-flit-brand disabled:cursor-default disabled:opacity-45 ' +
  'disabled:hover:bg-transparent max-sm:h-11 max-sm:min-w-11';
/** Primera/Última llevan su nombre a la vista: un «»» suelto no se lee como «ir al final». */
const BOTON_EXTREMO_CLS = `${BOTON_FLECHA_CLS} inline-flex gap-1`;
const NUMERO_CLS =
  'grid h-9 w-9 place-items-center rounded-full border border-transparent text-[0.8125rem] ' +
  'font-semibold tabular-nums transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand ' +
  'max-sm:h-11 max-sm:min-w-11';
const NUMERO_INACTIVO_CLS =
  'text-[color:var(--di-text-primary)] hover:bg-[var(--kit-hover)] hover:text-[color:var(--di-action)]';
const NUMERO_ACTUAL_CLS = 'bg-[var(--kit-azul-relleno)] text-white';

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
      className={`flex flex-wrap items-center justify-between gap-3 pt-4 max-sm:justify-center ${className}`}
      aria-label={ariaLabel}
    >
      <p
        className="m-0 text-[0.8125rem] tabular-nums text-[color:var(--di-text-tertiary)]"
        role="status"
        aria-live="polite"
      >
        {resumen}
      </p>
      {hayVariasPaginas ? (
        <div className="flex flex-wrap items-center gap-1">
          {/* HU #13916 — los extremos van aparte de los números: en un listado de 40 páginas el «40»
              se pierde entre los demás y no se lee como «ir al final». */}
          <button
            type="button"
            onClick={() => onPageChange(1)}
            disabled={page <= 1}
            className={BOTON_EXTREMO_CLS}
            aria-label="Primera página"
            title="Primera página"
          >
            <span aria-hidden="true">«</span>
            <span aria-hidden="true">Primera</span>
          </button>
          <button
            type="button"
            onClick={() => onPageChange(page - 1)}
            disabled={page <= 1}
            className={BOTON_FLECHA_CLS}
            aria-label="Página anterior"
            title="Página anterior"
          >
            ‹
          </button>
          {paginas.map((p, i) =>
            p === 'gap' ? (
              <span
                key={`gap-${i}`}
                className="min-w-5 text-center text-[color:var(--di-text-tertiary)]"
                aria-hidden="true"
              >
                …
              </span>
            ) : (
              <button
                key={p}
                type="button"
                onClick={() => onPageChange(p)}
                aria-label={`Página ${p}`}
                aria-current={p === page ? 'page' : undefined}
                className={`${NUMERO_CLS} ${p === page ? NUMERO_ACTUAL_CLS : NUMERO_INACTIVO_CLS}`}
              >
                {p}
              </button>
            ),
          )}
          <button
            type="button"
            onClick={() => onPageChange(page + 1)}
            disabled={page >= totalPages}
            className={BOTON_FLECHA_CLS}
            aria-label="Página siguiente"
            title="Página siguiente"
          >
            ›
          </button>
          <button
            type="button"
            onClick={() => onPageChange(totalPages)}
            disabled={page >= totalPages}
            className={BOTON_EXTREMO_CLS}
            aria-label="Última página"
            title="Última página"
          >
            <span aria-hidden="true">Última</span>
            <span aria-hidden="true">»</span>
          </button>
        </div>
      ) : null}
    </nav>
  );
}

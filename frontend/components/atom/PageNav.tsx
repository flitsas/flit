'use client';

import { ChevronLeft, ChevronRight } from 'lucide-react';

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
  /**
   * Separación propia respecto de lo que tiene encima. Por defecto sí, que es como la usan los
   * listados que la cuelgan directamente de la tabla. Se apaga cuando la paginación vive DENTRO de
   * una barra de pie con su propio relleno: ahí el `pt-3` descuadraba la línea de base contra el
   * resto de los controles de la barra.
   */
  espacioSuperior?: boolean;
  className?: string;
}

export function PageNav({
  page,
  totalPages,
  onPageChange,
  resumen,
  ariaLabel,
  espacioSuperior = true,
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
      className={`flex flex-wrap items-center justify-end gap-3 ${espacioSuperior ? 'pt-3' : ''} ${className}`}
      aria-label={ariaLabel}
    >
      <p
        className="text-xs tabular-nums text-[#162744]/55 dark:text-white/45"
        role="status"
        aria-live="polite"
      >
        {resumen}
      </p>
      {hayVariasPaginas ? (
        /*
         * Un solo control, no una fila de fichas sueltas.
         *
         * Antes cada número llevaba su propio fondo azul claro, así que la página activa —el único
         * dato que la navegación tiene que comunicar— competía contra otras seis manchas del mismo
         * color y había que buscarla. Ahora el reposo es TRANSPARENTE y el relleno azul queda
         * reservado para la página en la que se está; el resto aparece al pasar el puntero.
         *
         * El grupo va dentro de una cápsula con borde para que se lea como una pieza —el mismo
         * borde y radio que los controles de la barra de filtros—, en vez de como botones
         * desperdigados sobre el fondo de la página.
         */
        <div className="inline-flex items-center gap-0.5 rounded-xl border border-[#DFE5ED] bg-white p-1 dark:border-white/15 dark:bg-white/[0.04]">
          <Flecha
            direccion="anterior"
            disabled={page <= 1}
            onClick={() => onPageChange(page - 1)}
          />
          {paginas.map((p, i) =>
            p === 'gap' ? (
              <span
                key={`gap-${i}`}
                className="grid h-8 w-5 place-items-center text-xs text-[#162744]/35 dark:text-white/30"
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
                className={
                  'grid h-8 min-w-8 place-items-center rounded-lg px-2 text-xs font-semibold tabular-nums transition ' +
                  'focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-1 ' +
                  (p === page
                    ? 'bg-[#557EFF] text-white'
                    : 'text-[#162744]/70 hover:bg-[#557EFF]/10 hover:text-[#3B4FD6] dark:text-white/60 dark:hover:text-white')
                }
              >
                {p}
              </button>
            ),
          )}
          <Flecha
            direccion="siguiente"
            disabled={page >= totalPages}
            onClick={() => onPageChange(page + 1)}
          />
        </div>
      ) : null}
    </nav>
  );
}

/**
 * Flecha de paso. Va con icono y no con los caracteres «‹ ›»: esos dependen de la fuente, se
 * pintan finísimos y no quedan a la misma altura óptica que los números de al lado.
 */
function Flecha({
  direccion,
  disabled,
  onClick,
}: {
  direccion: 'anterior' | 'siguiente';
  disabled: boolean;
  onClick: () => void;
}) {
  const Icono = direccion === 'anterior' ? ChevronLeft : ChevronRight;
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-label={direccion === 'anterior' ? 'Página anterior' : 'Página siguiente'}
      className={
        'grid h-8 w-8 place-items-center rounded-lg text-[#162744]/70 transition ' +
        'hover:bg-[#557EFF]/10 hover:text-[#3B4FD6] focus:outline-none focus-visible:ring-2 ' +
        'focus-visible:ring-[#557EFF] focus-visible:ring-offset-1 disabled:pointer-events-none ' +
        'disabled:text-[#162744]/20 dark:text-white/60 dark:hover:text-white dark:disabled:text-white/20'
      }
    >
      <Icono className="h-4 w-4" aria-hidden />
    </button>
  );
}

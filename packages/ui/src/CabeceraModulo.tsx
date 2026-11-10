import type { ReactNode } from "react";
import { cn } from "./cn";

/**
 * Cabecera de módulo de la propuesta «Diseño Inteligente» (`.page-header` de propuesta.css), HU #14476: tarjeta con
 * degradado blanco → cielo, la ilustración del módulo a la izquierda, antetítulo opcional, título con su línea de
 * acento, bajada, los dos círculos decorativos (turquesa lleno y aro) a la derecha y las acciones dentro, a la derecha.
 *
 * Es la misma pieza en las 4 apps. No sabe qué ilustración le toca a cada pantalla: la ruta la pasa quien la usa (cada
 * app tiene su tabla de título → ilustración, p. ej. `ModuleTitle` de Trámites).
 */
export interface CabeceraModuloProps {
  titulo: ReactNode;
  /** Bajada bajo el título. Va en un `div`: algunas pantallas pasan bloques (chip, barra de progreso). */
  descripcion?: ReactNode;
  /** Antetítulo en versalitas sobre el título (p. ej. «Gestión integral»). */
  antetitulo?: ReactNode;
  /** Ruta de la ilustración (`rutaIlustracion(...)` o `ilustracionDe(...)`). Es decorativa: va con `alt=""`. */
  ilustracion?: string;
  /** Lo que va a la derecha, dentro de la cabecera: indicadores y acciones, en ese orden. */
  acciones?: ReactNode;
  /** Contenido sobre el título y el antetítulo, dentro de la columna del texto (p. ej. migas de pan). */
  encima?: ReactNode;
  /** Contenido bajo la descripción, dentro de la columna del texto (p. ej. una nota). */
  debajo?: ReactNode;
  /** Elemento raíz: `header` por defecto; `section` donde la pantalla la etiqueta con `aria-labelledby`. */
  as?: "header" | "section";
  /** `id` del `h1`, para un `aria-labelledby`. */
  tituloId?: string;
  "aria-labelledby"?: string;
  /** Clases extra del título (p. ej. `break-all` para un nombre largo sin espacios). */
  tituloClassName?: string;
  className?: string;
}

export function CabeceraModulo({
  titulo,
  descripcion,
  antetitulo,
  ilustracion,
  acciones,
  encima,
  debajo,
  as: Raiz = "header",
  tituloId,
  "aria-labelledby": ariaLabelledby,
  tituloClassName,
  className,
}: CabeceraModuloProps) {
  return (
    <Raiz
      aria-labelledby={ariaLabelledby}
      className={cn(
        "relative flex shrink-0 flex-wrap items-center gap-6 overflow-clip rounded-[var(--di-radius-header)] border border-[var(--di-border)] py-8 pl-[132px] pr-8 shadow-[var(--shadow-card)] max-md:gap-3.5 max-md:rounded-[20px] max-md:py-5 max-md:pl-[84px] max-md:pr-4",
        className,
      )}
      style={{ background: "linear-gradient(120deg, var(--di-surface-elevated) 35%, var(--di-sky) 100%)" }}
    >
      <span
        aria-hidden="true"
        className="pointer-events-none absolute -right-[60px] -top-[110px] h-[230px] w-[230px] rounded-full opacity-90 max-md:-right-12 max-md:-top-[60px] max-md:h-[120px] max-md:w-[120px]"
        style={{ background: "var(--di-highlight-decor)" }}
      />
      <span
        aria-hidden="true"
        className="pointer-events-none absolute -right-[150px] -top-[60px] h-[420px] w-[420px] rounded-full border-[1.5px] opacity-50 max-md:hidden"
        style={{ borderColor: "var(--di-sky-line)" }}
      />
      {ilustracion ? (
        <img
          src={ilustracion}
          alt=""
          aria-hidden="true"
          className="absolute left-6 top-1/2 h-[92px] w-[92px] -translate-y-1/2 object-contain max-md:left-3.5 max-md:top-5 max-md:h-[58px] max-md:w-[58px] max-md:translate-y-0"
        />
      ) : null}
      <div className="relative z-[1] min-w-0 flex-[1_1_320px]">
        {encima && <div className="mb-2">{encima}</div>}
        {antetitulo && (
          <p className="mb-1 text-xs font-bold uppercase tracking-[0.06em]" style={{ color: "var(--di-highlight)" }}>
            {antetitulo}
          </p>
        )}
        <h1
          id={tituloId}
          className={cn("text-[2rem] font-bold leading-tight max-md:text-2xl", tituloClassName)}
          style={{ color: "var(--di-heading)" }}
        >
          {titulo}
        </h1>
        <span
          aria-hidden="true"
          className="mt-2.5 block h-[5px] w-16 rounded-full"
          style={{ background: "linear-gradient(90deg, var(--di-sky-line), var(--di-sky))" }}
        />
        {descripcion && (
          <div className="mt-2.5 max-w-[60ch] text-sm max-md:text-[13px]" style={{ color: "var(--di-text-secondary)" }}>
            {descripcion}
          </div>
        )}
        {debajo}
      </div>
      {acciones && <div className="relative z-[1] flex flex-wrap items-center gap-3">{acciones}</div>}
    </Raiz>
  );
}

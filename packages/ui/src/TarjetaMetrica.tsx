"use client";

import type { CSSProperties, MouseEventHandler, ReactNode } from "react";
import { cn } from "./cn";

/**
 * Tarjeta de cifra del prototipo «Diseño Inteligente» (`.metrica` de `propuesta.css`, HU #14476): etiqueta
 * arriba, cifra grande debajo, ilustración a la derecha y un pie opcional con la acción («Ver el detalle»).
 *
 * Es genérica: no sabe de trámites ni de reportes. Con `href` se pinta como enlace y con `onClick` como botón
 * (la tarjeta entera es la zona de pulsación, como en el prototipo); sin ninguno de los dos es una tarjeta
 * quieta, sin realce al pasar el puntero, para no prometer una acción que no existe.
 */
export interface TarjetaMetricaProps {
  etiqueta: ReactNode;
  cifra: ReactNode;
  /** Ruta de la ilustración (p. ej. `ilustracionDe("reportes")`). Es decorativa: va con `alt=""`. */
  ilustracion?: string;
  /** Pie de la tarjeta: la acción («Ver el detalle») o una línea secundaria (variación, aclaración). */
  pie?: ReactNode;
  href?: string;
  onClick?: MouseEventHandler<HTMLElement>;
  /** Resalta la tarjeta con el borde turquesa del prototipo (`.metrica--pendiente`): algo espera atención. */
  pendiente?: boolean;
  /** Color de la cifra; por defecto el texto principal. */
  colorCifra?: string;
  title?: string;
  className?: string;
  style?: CSSProperties;
  "aria-label"?: string;
  "data-testid"?: string;
}

/** Rejilla de tarjetas de cifra (`.metricas` del prototipo): columnas de 220 px como mínimo. */
export const METRICAS_GRID_CLS = "grid grid-cols-[repeat(auto-fit,minmax(220px,1fr))] gap-4";

const BASE_CLS =
  "grid grid-cols-[1fr_auto] items-center gap-x-3 gap-y-1 rounded-[20px] border p-5 text-left " +
  "text-inherit no-underline border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] " +
  "shadow-[var(--shadow-card)]";

const ACCIONABLE_CLS =
  "cursor-pointer transition-[box-shadow,transform] duration-150 hover:-translate-y-0.5 " +
  "hover:shadow-[var(--shadow-card-hover)] focus:outline-none focus-visible:ring-2 " +
  "focus-visible:ring-flit-brand focus-visible:ring-offset-2 motion-reduce:transition-none motion-reduce:hover:translate-y-0";

export function TarjetaMetrica({
  etiqueta,
  cifra,
  ilustracion,
  pie,
  href,
  onClick,
  pendiente = false,
  colorCifra,
  title,
  className,
  style,
  "aria-label": ariaLabel,
  "data-testid": testId,
}: TarjetaMetricaProps) {
  const accionable = href !== undefined || onClick !== undefined;
  const cls = cn(
    BASE_CLS,
    accionable && ACCIONABLE_CLS,
    pendiente && "border-[color:var(--di-highlight-decor)]",
    className,
  );

  const contenido = (
    <>
      <span className="text-[0.8125rem] font-medium text-[color:var(--di-text-secondary)]">{etiqueta}</span>
      <span
        className="row-start-2 text-[length:var(--di-text-cifra)] font-bold leading-[1.1] tabular-nums text-[color:var(--di-text-primary)]"
        style={colorCifra ? { color: colorCifra } : undefined}
      >
        {cifra}
      </span>
      {ilustracion ? (
        <img
          src={ilustracion}
          alt=""
          aria-hidden="true"
          width={44}
          height={44}
          className="col-start-2 row-span-3 row-start-1 h-11 w-11"
        />
      ) : null}
      {pie != null && pie !== false ? (
        <span
          className={cn(
            "row-start-3 text-xs",
            accionable ? "font-semibold text-[color:var(--di-action)]" : "text-[color:var(--di-text-tertiary)]",
          )}
        >
          {pie}
        </span>
      ) : null}
    </>
  );

  const comunes = { className: cls, style, title, "aria-label": ariaLabel, "data-testid": testId };

  if (href !== undefined) {
    return (
      <a href={href} onClick={onClick} {...comunes}>
        {contenido}
      </a>
    );
  }
  if (onClick !== undefined) {
    return (
      <button type="button" onClick={onClick} {...comunes}>
        {contenido}
      </button>
    );
  }
  return <div {...comunes}>{contenido}</div>;
}

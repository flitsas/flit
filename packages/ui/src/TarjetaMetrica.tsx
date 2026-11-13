"use client";

import type { CSSProperties, ElementType, MouseEventHandler, ReactNode } from "react";
import { cn } from "./cn";
import { IconoModulo } from "./Icono";

/**
 * Tarjeta de cifra del Manual UI Kit 2026 (propuesta-2, `.metrica`): etiqueta arriba, cifra de 24 px en azul, ícono de
 * 40 px en disco a la derecha y un pie opcional con la acción subrayada. Borde de 1 px, radio de 9 px, sin sombra; al
 * pasar el puntero el borde se pone azul. `pendiente` pinta el borde, la cifra y el disco en naranja.
 *
 * Es genérica: no sabe de trámites ni de reportes. Con `href` se pinta como enlace y con `onClick` como botón
 * (la tarjeta entera es la zona de pulsación, como en el prototipo); sin ninguno de los dos es una tarjeta
 * quieta, sin realce al pasar el puntero, para no prometer una acción que no existe.
 */
export interface TarjetaMetricaProps {
  etiqueta: ReactNode;
  cifra: ReactNode;
  /** Clave del ícono en MAPA_ICONO (p. ej. "finalizados", "tiempo"). Decorativo. */
  icono?: string;
  /** @deprecated Usa `icono`. Ilustración WebP del diseño anterior. */
  ilustracion?: string;
  /** Pie de la tarjeta: la acción («Ver el detalle») o una línea secundaria (variación, aclaración). */
  pie?: ReactNode;
  href?: string;
  /** Componente de enlace para `href` (p. ej. `Link` de next, para navegar sin recargar). Por defecto `<a>`. */
  enlace?: ElementType;
  onClick?: MouseEventHandler<HTMLElement>;
  /**
   * HU #14562 · UI-2: modo filtro. Con `onAlternar` la tarjeta es un botón con aria-pressed que alterna `activa`
   * (p. ej. las tarjetas de conteo del tablero). Sin la prop, nada cambia.
   */
  activa?: boolean;
  onAlternar?: (siguiente: boolean) => void;
  /** Resalta la tarjeta en naranja (`.metrica--pendiente` del kit): algo espera atención. */
  pendiente?: boolean;
  /** Color de la cifra; por defecto el texto principal. */
  colorCifra?: string;
  title?: string;
  className?: string;
  style?: CSSProperties;
  "aria-label"?: string;
  "data-testid"?: string;
}

/** Rejilla de tarjetas de cifra (`.metricas` del kit): columnas de 190 px como mínimo. */
export const METRICAS_GRID_CLS = "grid grid-cols-[repeat(auto-fit,minmax(190px,1fr))] gap-4";

const BASE_CLS =
  "grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-2 gap-y-1 rounded-[9px] border p-4 text-left " +
  "text-inherit no-underline border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] shadow-none";

const ACCIONABLE_CLS =
  "cursor-pointer transition-colors duration-150 hover:border-[color:var(--kit-azul-texto)] " +
  "focus:outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--kit-azul-texto)]";

export function TarjetaMetrica({
  etiqueta,
  cifra,
  icono,
  ilustracion,
  pie,
  href,
  enlace: Enlace = "a",
  onClick,
  activa = false,
  onAlternar,
  pendiente = false,
  colorCifra,
  title,
  className,
  style,
  "aria-label": ariaLabel,
  "data-testid": testId,
}: TarjetaMetricaProps) {
  const filtro = onAlternar !== undefined && href === undefined;
  const accionable = href !== undefined || onClick !== undefined || filtro;
  const cls = cn(
    BASE_CLS,
    accionable && ACCIONABLE_CLS,
    pendiente && "border-[color:var(--kit-naranja)] hover:border-[color:var(--kit-naranja)]",
    filtro && activa && "border-[color:var(--kit-azul-texto)] bg-[var(--di-accent-soft)]",
    className,
  );

  const contenido = (
    <>
      <span className="text-sm font-medium text-[color:var(--di-text-primary)]">{etiqueta}</span>
      <span
        className="row-start-2 text-[length:var(--di-text-cifra)] font-bold leading-[1.1] tabular-nums"
        style={{ color: colorCifra ?? (pendiente ? "var(--badge-warning-fg)" : "var(--kit-azul-texto)") }}
      >
        {cifra}
      </span>
      {icono ? (
        <IconoModulo
          clave={icono}
          disco
          size={40}
          className="col-start-2 row-span-3 row-start-1"
          style={pendiente ? { background: "#ff4e001a", color: "var(--kit-naranja)" } : undefined}
        />
      ) : ilustracion ? (
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
            accionable ? "text-[color:var(--di-action)] underline underline-offset-2" : "text-[color:var(--di-text-tertiary)]",
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
      <Enlace href={href} onClick={onClick} {...comunes}>
        {contenido}
      </Enlace>
    );
  }
  if (filtro) {
    return (
      <button
        type="button"
        aria-pressed={activa}
        data-activa={activa ? "true" : "false"}
        onClick={(e) => {
          onClick?.(e);
          onAlternar?.(!activa);
        }}
        {...comunes}
      >
        {contenido}
      </button>
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

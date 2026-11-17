'use client';

import { CHIP_EMBUDO_CLS, EMBUDO_CLS } from '@flit/ui/ChipsFiltro';
import { cn } from '@flit/ui/cn';

/**
 * Epic #12686 — la tira de tarjetas con conteo que comparten el listado del gestor
 * (`EstadoFunnel`) y la bandeja del OT (`OtBandejaCountersStrip`). Presentacional: cada pantalla
 * decide qué tarjetas hay y qué filtra cada una; aquí solo se pinta, para que un estado se vea
 * igual en las dos. Clic en una tarjeta la elige; segundo clic la suelta.
 */
export interface FranjaItem {
  key: string;
  label: string;
  /** SVG del estado; trae su propio círculo de color. */
  icon: string;
  /** `null` = sin dato todavía: se pinta un guion, porque un 0 afirmaría que no hay trabajo. */
  count: number | null;
  ariaLabel: string;
  title?: string;
  /** Fondo de la tarjeta elegida y color de su subrayado. */
  activeBg: string;
  activeColor: string;
  /** HU #14482 — color propio del estado (`var(--estado-<x>-acento)`). UI Kit 2026: el embudo del kit ya no pinta la
   * barra del pie (el estado lo dice su disco); se conserva en el contrato para quien lo arma. */
  acento: string;
}

/**
 * UI Kit 2026 — el embudo de estados del kit (`EMBUDO_CLS` / `CHIP_EMBUDO_CLS` de `@flit/ui/ChipsFiltro`): una franja
 * con una columna por estado —el disco del estado arriba, el nombre y la cifra— y la elegida en --kit-hover con aro azul.
 * En celular se desliza en horizontal. Mismos botones, nombres accesibles y filtro de siempre; solo cambia el aspecto.
 */
const FRANJA_CLS = EMBUDO_CLS;

/** El chip del embudo en columna normal: aquí el DOM ya va en el orden visual (disco, nombre, cifra). */
const BALDOSA_CLS = cn(CHIP_EMBUDO_CLS, 'flex-col justify-start');

// Columnas en pantalla ancha: una por tarjeta. Clases literales para que Tailwind las genere.

export interface FranjaEstadosProps {
  items: readonly FranjaItem[];
  /** Tarjeta elegida; vacío = ninguna. */
  selected: string;
  onSelect?: (key: string) => void;
  ariaLabel: string;
  disabled?: boolean;
  /** Fondo en modo oscuro: cada pantalla conserva el de su superficie. */
  darkBgClassName?: string;
}

export function FranjaEstados({
  items,
  selected,
  onSelect,
  ariaLabel,
  disabled = false,
  darkBgClassName = '',
}: FranjaEstadosProps) {
  return (
    <div
      role="group"
      aria-label={ariaLabel}
      // HU #14482: franja de baldosas con el color de cada estado en la barra. Mismo grupo, mismos
      // botones y mismo filtro; solo cambia el aspecto.
      className={`${FRANJA_CLS} ${darkBgClassName}`}
    >
      {items.map((item) => {
        const activo = selected === item.key;
        return (
          <button
            key={item.key}
            type="button"
            aria-label={item.ariaLabel}
            aria-pressed={activo}
            title={item.title}
            disabled={disabled}
            onClick={() => onSelect?.(activo ? '' : item.key)}
            className={`group ${BALDOSA_CLS}`}
          >
            {/* Decorativo: el nombre accesible del botón ya dice la tarjeta y el conteo. */}
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              src={item.icon}
              alt=""
              aria-hidden="true"
              width={32}
              height={32}
              className="mb-0.5 h-8 w-8"
            />
            {/* Hasta dos líneas: «Solicitud de revocatoria» no cabe en una y truncarla la
                confundía con la tarjeta de al lado. */}
            <span className="max-w-full text-xs font-medium leading-tight text-[color:var(--di-text-primary)]">
              {item.label}
            </span>
            <span
              className="text-xl font-bold leading-none tabular-nums group-aria-pressed:text-[color:var(--kit-azul-texto)]"
              aria-hidden="true"
            >
              {item.count ?? '—'}
            </span>
          </button>
        );
      })}
    </div>
  );
}

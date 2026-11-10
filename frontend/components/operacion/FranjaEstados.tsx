'use client';

import { CHIP_FILTRO_CLS, CHIPS_FILTRO_FILA_CLS } from '@flit/ui/ChipsFiltro';

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
}

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
      // Propuesta (HU #14482): la franja pasa a la fila de chips segmentados del prototipo
      // (`.embudo` + `.chip`, `ChipsFiltro` de @flit/ui). Mismo grupo, mismos botones y mismo filtro;
      // solo cambia el aspecto.
      className={`${CHIPS_FILTRO_FILA_CLS} ${darkBgClassName}`}
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
            className={`${CHIP_FILTRO_CLS} group`}
          >
            {/* Decorativo: el nombre accesible del botón ya dice la tarjeta y el conteo. */}
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              src={item.icon}
              alt=""
              aria-hidden="true"
              width={28}
              height={28}
              className="hidden"
            />
            {/* Hasta dos líneas: «Solicitud de revocatoria» no cabe en una y truncarla la
                confundía con la tarjeta de al lado. */}
            <span className="max-w-full text-[0.8125rem] font-medium leading-tight">
              {item.label}
            </span>
            <span
              // Primero la cifra, como el chip del prototipo («15 Todos»), solo con `order`.
              className="order-first text-base font-bold leading-none tabular-nums text-[color:var(--di-text-primary)] group-aria-pressed:text-[color:var(--di-action)]"
              aria-hidden="true"
            >
              {item.count ?? '—'}
            </span>
            <span
              className="hidden"
              aria-hidden="true"
            />
          </button>
        );
      })}
    </div>
  );
}

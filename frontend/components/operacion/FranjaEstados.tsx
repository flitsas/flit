'use client';

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
  /** HU #14482 — color propio del estado para la barra del pie (`var(--estado-<x>-acento)`). */
  acento: string;
}

/**
 * HU #14482 — tira de baldosas: franja blanca con una baldosa por estado (nombre arriba, cifra
 * grande y una barra corta con el color del estado al pie). El color va solo en la barra; el texto
 * queda en tinta. En anchos menores las baldosas pasan a varias filas.
 */
const FRANJA_CLS =
  'flex flex-wrap gap-2 rounded-2xl bg-[var(--di-surface-elevated)] p-2 shadow-[var(--shadow-card)]';

const BALDOSA_CLS =
  'flex min-w-[7.5rem] flex-1 cursor-pointer flex-col items-center justify-start gap-1 rounded-xl border ' +
  'border-transparent bg-[var(--di-surface-elevated)] px-3 pb-2.5 pt-3 text-center transition-colors ' +
  'hover:bg-[var(--di-accent-soft)] focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand ' +
  'focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-60 ' +
  'aria-pressed:border-[color:color-mix(in_srgb,var(--di-action)_35%,transparent)] aria-pressed:bg-[var(--di-accent-soft)]';

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
            className={BALDOSA_CLS}
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
            <span className="max-w-full text-[0.8125rem] font-medium leading-tight text-[color:var(--di-text-primary)]">
              {item.label}
            </span>
            <span
              className="text-[1.375rem] font-bold leading-none tabular-nums text-[color:var(--di-heading)]"
              aria-hidden="true"
            >
              {item.count ?? '—'}
            </span>
            <span
              className="mt-1.5 h-1 w-3/5 rounded-full"
              style={{ background: item.acento }}
              aria-hidden="true"
            />
          </button>
        );
      })}
    </div>
  );
}

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
}

// Columnas en pantalla ancha: una por tarjeta. Clases literales para que Tailwind las genere.
const XL_COLS: Record<number, string> = {
  4: 'xl:grid-cols-4',
  5: 'xl:grid-cols-5',
  6: 'xl:grid-cols-6',
  7: 'xl:grid-cols-7',
  8: 'xl:grid-cols-8',
  9: 'xl:grid-cols-9',
  10: 'xl:grid-cols-10',
  11: 'xl:grid-cols-11',
};

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
  darkBgClassName = 'dark:bg-[#162744]',
}: FranjaEstadosProps) {
  return (
    <div
      role="group"
      aria-label={ariaLabel}
      className={`grid grid-cols-2 divide-[#EEF2F7] overflow-hidden rounded-2xl border border-[#DFE5ED] bg-white shadow-[0_4px_12px_rgba(0,0,0,0.04)] sm:grid-cols-4 sm:divide-x lg:grid-cols-6 ${XL_COLS[items.length] ?? 'xl:grid-cols-11'} dark:divide-white/5 dark:border-white/10 ${darkBgClassName}`}
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
            className="flex flex-col items-center gap-1 px-2 py-2 transition hover:bg-[#557EFF]/[0.06] focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[#557EFF] disabled:cursor-not-allowed disabled:opacity-60"
            style={activo ? { background: item.activeBg } : undefined}
          >
            {/* Decorativo: el nombre accesible del botón ya dice la tarjeta y el conteo. */}
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              src={item.icon}
              alt=""
              aria-hidden="true"
              width={28}
              height={28}
              className="h-7 w-7 shrink-0"
            />
            {/* Hasta dos líneas: «Solicitud de revocatoria» no cabe en una y truncarla la
                confundía con la tarjeta de al lado. */}
            <span className="line-clamp-2 max-w-full text-center text-xs font-medium leading-tight opacity-70 text-[#162744] dark:text-white/70">
              {item.label}
            </span>
            <span
              className="text-lg font-bold leading-none tabular-nums text-[#1E293B] dark:text-white"
              aria-hidden="true"
            >
              {item.count ?? '—'}
            </span>
            <span
              className="h-0.5 w-6 rounded-full"
              style={{ background: activo ? item.activeColor : 'transparent' }}
              aria-hidden="true"
            />
          </button>
        );
      })}
    </div>
  );
}

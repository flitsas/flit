"use client";

import type { OtBandejaCounters as Counters } from "@/lib/api/types-ot";

/** Clave de la tarjeta pulsada; el contenedor la traduce a filtros del listado. */
export type OtCounterKey =
  | "sinAsignarPlaca"
  | "conPlacaAsignada"
  | "aprobados"
  | "rechazados"
  | "sinGestion";

interface TarjetaDef {
  key: OtCounterKey;
  label: string;
  icon: string;
  /** Qué mide, para el título del control: la etiqueta sola no basta para dos de ellas. */
  hint: string;
}

/**
 * Orden de lectura: primero la cola de placa (lo que el organismo tiene que despachar), luego los
 * desenlaces, y al final lo que nadie ha tocado. No es el orden del ciclo de vida sino el del
 * trabajo: la tira se lee de izquierda a derecha buscando dónde hay algo que hacer.
 */
const TARJETAS: TarjetaDef[] = [
  {
    key: "sinAsignarPlaca",
    label: "Sin asignar placa",
    icon: "/assets/ot-estados/sin-placa.svg",
    hint: "Entregados en ruta de placa que todavía no la tienen",
  },
  {
    key: "conPlacaAsignada",
    label: "Con placa asignada",
    icon: "/assets/ot-estados/con-placa.svg",
    hint: "Entregados con la placa ya puesta",
  },
  {
    key: "aprobados",
    label: "Aprobados",
    icon: "/assets/ot-estados/aprobados.svg",
    hint: "Trámites que el organismo aprobó",
  },
  {
    key: "rechazados",
    label: "Rechazados",
    icon: "/assets/ot-estados/rechazados.svg",
    hint: "Trámites que el organismo rechazó",
  },
  {
    key: "sinGestion",
    label: "Sin gestión",
    icon: "/assets/ot-estados/sin-gestion.svg",
    hint: "Entregados que nadie ha empezado a trabajar",
  },
];

export interface OtBandejaCountersStripProps {
  counters: Counters | null;
  /** Tarjeta activa; vacío = ninguna. */
  selected: OtCounterKey | "";
  onSelect: (key: OtCounterKey | "") => void;
  loading?: boolean;
}

/**
 * Tira de contadores de la bandeja del OT: una tarjeta única dividida en columnas, con icono,
 * etiqueta y cifra. Pulsar una filtra el listado; pulsarla de nuevo quita el filtro.
 *
 * Las cifras vienen del backend (`/client-procedures/counters`), NO de las filas cargadas: la
 * bandeja está paginada, así que contar lo que hay en pantalla diría "cuántos de estos veinte" en
 * vez de "cuántos hay", que es la pregunta que el operador se hace al entrar.
 */
export function OtBandejaCountersStrip({
  counters,
  selected,
  onSelect,
  loading = false,
}: OtBandejaCountersStripProps) {
  return (
    <div
      role="group"
      aria-label="Carga de trabajo del organismo"
      className="grid grid-cols-2 divide-[#EEF2F7] overflow-hidden rounded-2xl border border-[#DFE5ED] bg-white shadow-[0_4px_12px_rgba(0,0,0,0.04)] sm:grid-cols-3 sm:divide-x lg:grid-cols-5 dark:divide-white/5 dark:border-white/10 dark:bg-[#0B0F14]"
    >
      {TARJETAS.map((t) => {
        const valor = counters ? counters[t.key] : null;
        const activo = selected === t.key;
        return (
          <button
            key={t.key}
            type="button"
            aria-pressed={activo}
            aria-label={`${t.label}: ${valor ?? "sin dato"}. ${t.hint}`}
            title={t.hint}
            disabled={loading}
            onClick={() => onSelect(activo ? "" : t.key)}
            className="flex flex-col items-center gap-1 px-2 py-2 transition hover:bg-[#557EFF]/[0.06] focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[#557EFF] disabled:cursor-not-allowed disabled:opacity-60"
            style={activo ? { background: "rgba(85,126,255,0.08)" } : undefined}
          >
            {/* El SVG trae su propio círculo de color: se pinta entero, sin pastilla detrás. */}
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img src={t.icon} alt="" aria-hidden="true" width={28} height={28} className="h-7 w-7" />
            <span className="max-w-full truncate text-[10px] font-medium opacity-70">
              {t.label}
            </span>
            <span
              className="text-lg font-bold leading-none tabular-nums text-[#1E293B] dark:text-white"
              aria-hidden="true"
            >
              {/* Guion mientras no hay cifra: un 0 afirmaría que no hay trabajo, que es distinto. */}
              {valor ?? "—"}
            </span>
            <span
              className="h-0.5 w-6 rounded-full"
              style={{ background: activo ? "#557EFF" : "transparent" }}
              aria-hidden="true"
            />
          </button>
        );
      })}
    </div>
  );
}

/**
 * Filtros del listado que corresponden a cada tarjeta. Es el punto donde las cinco clases se
 * traducen al contrato del API — dos van por sub-estado de placa y tres por estado del trámite—, y
 * vive junto a la tira para que contar y filtrar no puedan divergir.
 */
export function filtrosDeContador(key: OtCounterKey | ""): {
  status: string;
  plateFlowStatus: string;
} {
  switch (key) {
    case "sinAsignarPlaca":
      return { status: "entregado", plateFlowStatus: "preasignado" };
    case "conPlacaAsignada":
      return { status: "entregado", plateFlowStatus: "asignado,terminado" };
    case "aprobados":
      return { status: "aprobado", plateFlowStatus: "" };
    case "rechazados":
      return { status: "rechazado", plateFlowStatus: "" };
    case "sinGestion":
      return { status: "entregado", plateFlowStatus: "sin_ruta" };
    default:
      return { status: "", plateFlowStatus: "" };
  }
}

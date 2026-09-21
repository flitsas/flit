"use client";

import { ESTADO_ICONO } from "@/lib/tramites/estados";
import type { OtBandejaCounters as Counters } from "@/lib/api/types-ot";

/** Clave de la tarjeta pulsada; el contenedor la traduce al filtro de estado del listado. */
export type OtCounterKey =
  | "preasignacion"
  | "asignados"
  | "porDecidir"
  | "aprobados"
  | "rechazados"
  | "revocados"
  | "solicitudesRevocatoria";

interface TarjetaDef {
  key: OtCounterKey;
  label: string;
  icon: string;
  /** Qué mide, para el título del control. */
  hint: string;
  /**
   * Estado real del ciclo de vida que la tarjeta cuenta y filtra (ADR-0059). Vacío en la única
   * tarjeta que no es un estado: ver {@link hasActiveRevocationRequest}.
   */
  status: string;
  /**
   * Feature #12565 — ÚNICA excepción a ADR-0059: la tarjeta no cuenta un estado sino el
   * sub-flujo de revocatoria, ortogonal al `status` (ADR-0022: el trámite sigue `aprobado`
   * mientras se decide). Por eso no es excluyente con las demás y filtra por su propio flag.
   */
  hasActiveRevocationRequest?: boolean;
}

/**
 * Orden de lectura: primero la cola de placa (lo que el organismo tiene que despachar), luego la
 * cola de decisión y al final los desenlaces. No es el orden del ciclo de vida sino el del trabajo:
 * la tira se lee de izquierda a derecha buscando dónde hay algo que hacer.
 *
 * ADR-0059 — cada tarjeta ES un estado real (preasignacion, asignado, entregado, aprobado,
 * rechazado, revocado): pulsarla equivale a filtrar por ese estado, y las seis son excluyentes.
 * El icono (y con él el color) es el MISMO del catálogo del gestor (`ESTADO_ICONO`): un estado se
 * ve igual en las dos pantallas. «Por decidir» conserva el suyo porque no nombra el estado sino
 * la cola de trabajo del organismo.
 */
const TARJETAS: TarjetaDef[] = [
  {
    key: "preasignacion",
    label: "Preasignación",
    icon: ESTADO_ICONO.preasignacion,
    hint: "Radicados sin placa: el organismo debe asignarla",
    status: "preasignacion",
  },
  {
    key: "asignados",
    label: "Asignados",
    icon: ESTADO_ICONO.asignado,
    hint: "Con placa asignada; el gestor gestiona SOAT e impuestos y envía al OT",
    status: "asignado",
  },
  {
    key: "porDecidir",
    label: "Por decidir",
    icon: "/assets/ot-estados/sin-gestion.svg",
    hint: "Entregados a la espera de la decisión del organismo",
    status: "entregado",
  },
  {
    key: "aprobados",
    label: "Aprobados",
    icon: ESTADO_ICONO.aprobado,
    hint: "Trámites que el organismo aprobó",
    status: "aprobado",
  },
  {
    // Feature #12565 — Aprobados con una solicitud de revocatoria ACTIVA: sin esta tarjeta, la
    // única forma de notarlos era entrar a "Aprobados" y leer fila por fila. Va justo al lado de
    // "Aprobados" (de donde sale) y antes de "Rechazados": es una decisión pendiente, no un
    // desenlace ya cerrado como "Revocados".
    key: "solicitudesRevocatoria",
    label: "Solicitudes de revocatoria",
    icon: "/assets/ot-estados/solicitud-revocatoria.svg",
    hint: "Aprobados con una solicitud de revocatoria esperando decisión",
    status: "",
    hasActiveRevocationRequest: true,
  },
  {
    key: "rechazados",
    label: "Rechazados",
    icon: ESTADO_ICONO.rechazado,
    hint: "Trámites que el organismo rechazó (desde entregado o desde preasignación)",
    status: "rechazado",
  },
  {
    key: "revocados",
    label: "Revocados",
    icon: ESTADO_ICONO.revocado,
    hint: "Trámites Aprobados que el organismo revocó (HU #12166)",
    status: "revocado",
  },
];

export const OT_BANDEJA_TARJETAS: readonly TarjetaDef[] = TARJETAS;

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
      className="grid grid-cols-2 divide-[#EEF2F7] overflow-hidden rounded-2xl border border-[#DFE5ED] bg-white shadow-[0_4px_12px_rgba(0,0,0,0.04)] sm:grid-cols-4 sm:divide-x lg:grid-cols-7 dark:divide-white/5 dark:border-white/10 dark:bg-[#0B0F14]"
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
 * Estado del listado que corresponde a cada tarjeta. Vive junto a la tira para que contar y filtrar
 * no puedan divergir: la tarjeta dice «N» y, al pulsarla, el filtro es exactamente ese estado.
 */
export function estadoDeContador(key: OtCounterKey | ""): string {
  return TARJETAS.find((t) => t.key === key)?.status ?? "";
}

/**
 * Feature #12565 — flag del sub-flujo de revocatoria de la tarjeta pulsada; `undefined` en las
 * que filtran por estado. Helper aparte de {@link estadoDeContador} porque son dos ejes de
 * filtrado distintos, no dos valores del mismo.
 */
export function revocatoriaActivaDeContador(key: OtCounterKey | ""): boolean | undefined {
  return TARJETAS.find((t) => t.key === key)?.hasActiveRevocationRequest;
}

/** Tarjeta que corresponde a un estado del filtro (para marcar la activa cuando el filtro cambia por el desplegable). */
export function contadorDeEstado(status: string): OtCounterKey | "" {
  // Se exige status no vacío: la tarjeta de revocatoria lo tiene vacío (no filtra por estado) y
  // si no, empataría con "sin filtro" y se marcaría activa sin haberse pulsado.
  return (status ? TARJETAS.find((t) => t.status === status)?.key : "") ?? "";
}

"use client";

// Controles de filtro de Reportes del organismo.
//
// Cada pestaña arma los suyos. Esto NO es cosmético: el panel operativo describe el estado actual de
// la cola y el movimiento de hoy, así que un rango de fechas encima suyo era una promesa falsa —
// moverlo no cambiaba un solo número. Al partir los filtros por pestaña, cada control queda al lado
// de lo que de verdad gobierna.

import {
  TipoTramiteSelect,
  type GrupoTiposTramite,
  type SeleccionTipoTramite,
} from "@/components/consultas/tipo-tramite";
import type { OtClientCompanyOption } from "@/lib/api/ot-metrics";
import { FIELD_CLS } from "./shared";

export interface DateRange {
  from: string;
  to: string;
}

function toIsoDate(d: Date): string {
  return d.toISOString().slice(0, 10);
}

/** Ventana de los últimos `dias` días contando hoy. */
export function lastDaysRange(dias: number, reference: Date = new Date()): DateRange {
  const start = new Date(reference);
  start.setDate(start.getDate() - (dias - 1));
  return { from: toIsoDate(start), to: toIsoDate(reference) };
}

/** Rango por defecto: últimos 30 días, la ventana con la que se mira una operación. */
export function defaultRange(reference: Date = new Date()): DateRange {
  return lastDaysRange(30, reference);
}

/**
 * Atajos de rango. Existen porque el caso real del informe es «lo del mes pasado» o «la semana que
 * cerró», y llegar ahí con dos selectores de fecha son cuatro clics y una oportunidad de equivocarse
 * de año.
 */
export const RANGE_PRESETS: { id: string; label: string; build: (today: Date) => DateRange }[] = [
  {
    id: "7d",
    label: "Últimos 7 días",
    build: (today) => {
      const start = new Date(today);
      start.setDate(start.getDate() - 6);
      return { from: toIsoDate(start), to: toIsoDate(today) };
    },
  },
  { id: "30d", label: "Últimos 30 días", build: (today) => defaultRange(today) },
  {
    id: "mes",
    label: "Mes actual",
    build: (today) => ({
      from: toIsoDate(new Date(today.getFullYear(), today.getMonth(), 1)),
      to: toIsoDate(today),
    }),
  },
  {
    id: "mes-pasado",
    label: "Mes pasado",
    build: (today) => ({
      from: toIsoDate(new Date(today.getFullYear(), today.getMonth() - 1, 1)),
      to: toIsoDate(new Date(today.getFullYear(), today.getMonth(), 0)),
    }),
  },
  {
    id: "trimestre",
    label: "Últimos 90 días",
    build: (today) => {
      const start = new Date(today);
      start.setDate(start.getDate() - 89);
      return { from: toIsoDate(start), to: toIsoDate(today) };
    },
  },
];

export function RangePresets({
  range,
  onChange,
}: {
  range: DateRange;
  onChange: (range: DateRange) => void;
}) {
  const today = new Date();

  return (
    <div className="flex flex-wrap items-center gap-1.5" data-testid="ot-report-presets">
      {RANGE_PRESETS.map((preset) => {
        const target = preset.build(today);
        const active = target.from === range.from && target.to === range.to;
        return (
          <button
            key={preset.id}
            type="button"
            aria-pressed={active}
            onClick={() => onChange(target)}
            className={`rounded-full border px-3 py-1 text-[11px] font-semibold transition ${
              active
                ? "border-[#557EFF] bg-[#557EFF]/10 text-[#557EFF]"
                : "border-[#DFE5ED] text-[#6B7280] hover:border-[#557EFF] dark:border-white/10 dark:text-white/50"
            }`}
          >
            {preset.label}
          </button>
        );
      })}
    </div>
  );
}

export function DateRangeFields({
  range,
  onChange,
}: {
  range: DateRange;
  onChange: (range: DateRange) => void;
}) {
  return (
    <>
      <label className="flex flex-col gap-1 text-xs font-semibold">
        Desde
        <input
          type="date"
          value={range.from}
          max={range.to}
          onChange={(e) => onChange({ ...range, from: e.target.value })}
          className={FIELD_CLS}
        />
      </label>
      <label className="flex flex-col gap-1 text-xs font-semibold">
        Hasta
        <input
          type="date"
          value={range.to}
          min={range.from}
          onChange={(e) => onChange({ ...range, to: e.target.value })}
          className={FIELD_CLS}
        />
      </label>
    </>
  );
}

/**
 * Filtro por tipo de trámite: familias como encabezado y sus tipos debajo.
 *
 * <p>Ofrecía solo las tres familias, así que el informe no distinguía una matrícula inicial de una
 * de leasing —dos trámites distintos para quien los decide— ni un traspaso bilateral de uno
 * unilateral. Los veintiún tipos en una lista plana arreglaban eso y estropeaban otra cosa: quien
 * consulta tendría que saberse de memoria a qué familia pertenece cada uno. Los dos niveles en un
 * solo control resuelven las dos.</p>
 */
export function TipoTramiteFiltro({
  value,
  grupos,
  onChange,
}: {
  value: SeleccionTipoTramite;
  grupos: GrupoTiposTramite[];
  onChange: (seleccion: SeleccionTipoTramite) => void;
}) {
  return (
    <label className="flex flex-col gap-1 text-xs font-semibold">
      Tipo de trámite
      <TipoTramiteSelect grupos={grupos} value={value} onChange={onChange} className={FIELD_CLS} />
    </label>
  );
}

export function EmpresaSelect({
  value,
  companies,
  onChange,
}: {
  value: string;
  companies: OtClientCompanyOption[];
  onChange: (value: string) => void;
}) {
  return (
    <label className="flex flex-col gap-1 text-xs font-semibold">
      Empresa
      <select
        value={value}
        onChange={(e) => onChange(e.target.value)}
        disabled={companies.length === 0}
        className={FIELD_CLS}
        aria-label="Filtrar por empresa"
      >
        <option value="">Todas las empresas</option>
        {companies.map((c) => (
          <option key={c.tenantId} value={c.tenantId}>
            {c.name}
          </option>
        ))}
      </select>
    </label>
  );
}

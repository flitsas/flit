"use client";

import { useMemo, useState } from "react";
import { ArrowRight, CheckCircle2, Eye, MinusCircle, ShieldAlert } from "lucide-react";
import type { AuditLogEntry } from "@/lib/api/types";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { DateRangePicker } from "@/components/atom/DateRangePicker";
import { Modal } from "@/components/atom/Modal";
import { SearchInput } from "@/components/atom/SearchInput";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { esRangoVacio, sinRango, type DateRange } from "@/components/atom/modules/_reportes/range";
import { formatFechaHora } from "@/lib/format/date";
import {
  auditValueText,
  eventLabel,
  parseAuditValue,
  technicalKey,
  type ParsedAuditValue,
} from "./auditLogLabels";

// Tabla del historial de auditoría (HU #10194, AC5). Columnas: Fecha, Campo modificado, Valor
// anterior, Valor nuevo, Operador. El orden DESC por fecha lo garantiza el backend; la tabla
// preserva el orden recibido. Los nombres técnicos se traducen a etiquetas legibles (el nombre de
// base de datos queda en el tooltip y en el detalle). El API no filtra: los filtros (fechas, tipo
// de evento y texto) se aplican en cliente sobre la página cargada.
export interface AuditLogTableProps {
  entries: AuditLogEntry[];
  totalCount: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  /** Filas por página (Bug #13055). */
  onPageSizeChange?: (pageSize: number) => void;
}

const FIELD_CLS =
  "h-10 rounded-[10px] border border-[#DFE5ED] bg-white px-3 text-xs font-medium text-[#162744] " +
  "focus:outline-none focus-visible:border-[#557EFF] focus-visible:ring-2 focus-visible:ring-[#557EFF]/30 " +
  "dark:border-white/15 dark:bg-[#0B0F14] dark:text-white";

const ALL_EVENTS = "";

export function AuditLogTable({
  entries,
  totalCount,
  page,
  pageSize,
  onPageChange,
  onPageSizeChange,
}: AuditLogTableProps) {
  const [range, setRange] = useState<DateRange>(sinRango());
  const [eventFilter, setEventFilter] = useState(ALL_EVENTS);
  const [query, setQuery] = useState("");
  const [detail, setDetail] = useState<AuditLogEntry | null>(null);

  const eventOptions = useMemo(
    () =>
      Array.from(new Set(entries.map((e) => eventLabel(e.entityName, e.fieldName)))).sort((a, b) =>
        a.localeCompare(b, "es"),
      ),
    [entries],
  );

  const filtersActive = !esRangoVacio(range) || eventFilter !== ALL_EVENTS || query.trim() !== "";

  const rows = useMemo(() => {
    const needle = query.trim().toLowerCase();
    return entries.filter((e) => {
      if (eventFilter !== ALL_EVENTS && eventLabel(e.entityName, e.fieldName) !== eventFilter) return false;
      if (!esRangoVacio(range)) {
        const day = localIsoDay(e.changedAt);
        if (!day) return false;
        if (range.from && day < range.from) return false;
        if (range.to && day > range.to) return false;
      }
      if (needle) {
        const haystack = [
          eventLabel(e.entityName, e.fieldName),
          technicalKey(e.entityName, e.fieldName),
          auditValueText(e.oldValue, e.fieldName),
          auditValueText(e.newValue, e.fieldName),
          e.changedBy ?? "",
        ]
          .join(" ")
          .toLowerCase();
        if (!haystack.includes(needle)) return false;
      }
      return true;
    });
  }, [entries, eventFilter, range, query]);

  const clearFilters = () => {
    setRange(sinRango());
    setEventFilter(ALL_EVENTS);
    setQuery("");
  };

  const columns: DataTableColumn<AuditLogEntry>[] = [
    {
      key: "date",
      header: "Fecha",
      cellClassName: "whitespace-nowrap opacity-80",
      render: (entry) => formatDateTime(entry.changedAt),
    },
    {
      key: "field",
      header: "Campo modificado",
      render: (entry) => (
        <span
          className="font-medium text-[#162744] dark:text-white"
          title={technicalKey(entry.entityName, entry.fieldName)}
        >
          {eventLabel(entry.entityName, entry.fieldName)}
        </span>
      ),
    },
    {
      key: "old",
      header: "Valor anterior",
      render: (entry) => (
        <AuditValue raw={entry.oldValue} fieldName={entry.fieldName} onDetail={() => setDetail(entry)} />
      ),
    },
    {
      key: "new",
      header: (
        <span className="inline-flex items-center gap-1.5">
          <ArrowRight className="h-3.5 w-3.5" aria-hidden />
          Valor nuevo
        </span>
      ),
      render: (entry) => (
        <AuditValue raw={entry.newValue} fieldName={entry.fieldName} onDetail={() => setDetail(entry)} />
      ),
    },
    {
      key: "operator",
      header: "Operador",
      align: "right",
      render: (entry) => <Operator changedBy={entry.changedBy} />,
    },
  ];

  return (
    <div className="flex flex-col gap-3">
      <div role="search" aria-label="Filtros del historial" className="flex flex-wrap items-end gap-3">
        <DateRangePicker
          label="Fechas"
          value={range}
          onChange={setRange}
          placeholder="Todas las fechas"
          className="w-auto"
        />
        <div className="flex flex-col gap-1">
          <label htmlFor="audit-filter-event" className="text-xs font-semibold">
            Tipo de cambio
          </label>
          <select
            id="audit-filter-event"
            value={eventFilter}
            onChange={(e) => setEventFilter(e.target.value)}
            className={FIELD_CLS}
          >
            <option value={ALL_EVENTS}>Todos</option>
            {eventOptions.map((label) => (
              <option key={label} value={label}>
                {label}
              </option>
            ))}
          </select>
        </div>
        <SearchInput
          value={query}
          onChange={setQuery}
          label="Buscar en el historial"
          placeholder="Buscar cambio, valor u operador"
          className="h-10 flex-1"
        />
        {filtersActive ? (
          <button
            type="button"
            onClick={clearFilters}
            className="h-10 rounded-xl border border-[#DFE5ED] px-4 text-xs font-semibold hover:bg-[#F4F7FC] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:border-white/15 dark:hover:bg-white/5"
          >
            Limpiar filtros
          </button>
        ) : null}
      </div>
      {filtersActive ? (
        <p role="status" className="text-xs opacity-70">
          Los filtros se aplican a los {entries.length} registros de esta página ({rows.length} coinciden).
        </p>
      ) : null}

      <DataTable
        ariaLabel="Historial de auditoría"
        columns={columns}
        rows={rows}
        getRowKey={(entry) => `${entry.changedAt}|${entry.entityName}|${entry.fieldName}|${entry.newValue ?? ""}|${entry.changedBy ?? ""}`}
        emptyMessage={
          filtersActive ? "Ningún cambio de esta página coincide con los filtros." : "Aún no hay cambios registrados."
        }
        minWidth={720}
        pagination={{
          page,
          pageSize,
          totalCount,
          onPageChange,
          onPageSizeChange,
        }}
      />

      <AuditDetailModal entry={detail} onClose={() => setDetail(null)} />
    </div>
  );
}

function AuditValue({
  raw,
  fieldName,
  onDetail,
}: {
  raw?: string | null;
  fieldName: string;
  onDetail: () => void;
}) {
  const parsed = parseAuditValue(raw, fieldName);
  return <ParsedValue parsed={parsed} onDetail={onDetail} />;
}

function ParsedValue({ parsed, onDetail }: { parsed: ParsedAuditValue; onDetail?: () => void }) {
  switch (parsed.kind) {
    case "empty":
      return <span aria-label="Sin valor">—</span>;
    case "boolean": {
      const Icon = parsed.tone === "success" ? CheckCircle2 : parsed.tone === "warning" ? ShieldAlert : MinusCircle;
      const label = parsed.value ? parsed.positive : parsed.negative;
      return (
        <StatusBadge
          tone={parsed.tone}
          ariaLabel={label}
          label={
            <span className="inline-flex items-center gap-1.5">
              <Icon className="h-3.5 w-3.5" aria-hidden />
              {label}
            </span>
          }
        />
      );
    }
    case "text":
      return <span className="break-words">{parsed.text}</span>;
    case "complex":
      return (
        <button
          type="button"
          onClick={onDetail}
          className="inline-flex items-center gap-1.5 rounded-lg px-2 py-1 text-xs font-semibold text-[#557EFF] hover:bg-[#557EFF]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          title={parsed.summary}
        >
          <Eye className="h-4 w-4" aria-hidden />
          Ver detalle
        </button>
      );
  }
}

function Operator({ changedBy }: { changedBy?: string | null }) {
  if (!changedBy) return <span aria-label="Sin operador">—</span>;
  const short = changedBy.length > 8 ? `${changedBy.slice(0, 8)}…` : changedBy;
  return (
    <span className="font-mono opacity-80" title={`Usuario ${changedBy}`} aria-label={`Usuario ${changedBy}`}>
      {short}
    </span>
  );
}

function AuditDetailModal({ entry, onClose }: { entry: AuditLogEntry | null; onClose: () => void }) {
  if (!entry) return null;
  const oldParsed = parseAuditValue(entry.oldValue, entry.fieldName);
  const newParsed = parseAuditValue(entry.newValue, entry.fieldName);
  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      title="Detalle del cambio"
      description={`${eventLabel(entry.entityName, entry.fieldName)} · ${formatDateTime(entry.changedAt)}`}
      footer={
        <div className="flex justify-end">
          <button
            type="button"
            onClick={onClose}
            className="rounded-xl border border-[#DFE5ED] px-4 py-2 text-xs font-semibold hover:bg-[#F4F7FC] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:border-white/15 dark:hover:bg-white/5"
          >
            Cerrar
          </button>
        </div>
      }
    >
      <div className="space-y-4 text-xs">
        <div>
          <p className="font-semibold">Campo técnico</p>
          <p className="font-mono opacity-80">{technicalKey(entry.entityName, entry.fieldName)}</p>
        </div>
        <div>
          <p className="font-semibold">Operador</p>
          <p className="font-mono opacity-80">{entry.changedBy ?? "—"}</p>
        </div>
        <DetailBlock title="Valor anterior" parsed={oldParsed} />
        <DetailBlock title="Valor nuevo" parsed={newParsed} />
      </div>
    </Modal>
  );
}

function DetailBlock({ title, parsed }: { title: string; parsed: ParsedAuditValue }) {
  return (
    <div>
      <p className="mb-1 font-semibold">{title}</p>
      {parsed.kind === "complex" ? (
        <pre className="max-h-64 overflow-auto rounded-xl border border-[#DFE5ED] bg-[#F4F7FC] p-3 font-mono text-xs dark:border-white/15 dark:bg-white/5">
          {parsed.pretty}
        </pre>
      ) : (
        <ParsedValue parsed={parsed} />
      )}
    </div>
  );
}

/** Día local `YYYY-MM-DD` de un instante ISO (mismo criterio que muestra la columna Fecha). */
function localIsoDay(iso: string): string | null {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return null;
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${d.getFullYear()}-${m}-${day}`;
}

function formatDateTime(iso: string): string {
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) {
    return iso;
  }
  return formatFechaHora(parsed);
}

"use client";

import { useState } from "react";
import { ArrowRight, CheckCircle2, Eye, MinusCircle, ShieldAlert } from "lucide-react";
import type { AuditLogEntry } from "@/lib/api/types";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { Modal } from "@/components/atom/Modal";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { formatFechaHora } from "@/lib/format/date";
import { eventLabel, parseAuditValue, technicalKey, type ParsedAuditValue } from "./auditLogLabels";

// Tabla del historial de auditoría (HU #10194, AC5). Columnas: Fecha, Campo modificado, Valor
// anterior, Valor nuevo, Operador. El orden DESC por fecha lo garantiza el backend; la tabla
// preserva el orden recibido. Los nombres técnicos se traducen a etiquetas legibles (el nombre de
// base de datos queda en el tooltip y en el detalle). Los filtros por columna (HU #14005) viven en
// AuditLogPanel y los resuelve el SERVIDOR; esta tabla solo pinta la página recibida.
export interface AuditLogTableProps {
  entries: AuditLogEntry[];
  totalCount: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  /** Filas por página (Bug #13055). */
  onPageSizeChange?: (pageSize: number) => void;
}

export function AuditLogTable({
  entries,
  totalCount,
  page,
  pageSize,
  onPageChange,
  onPageSizeChange,
}: AuditLogTableProps) {
  const [detail, setDetail] = useState<AuditLogEntry | null>(null);

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
          className="font-medium text-flit-primary dark:text-white"
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
        <AuditValue
          raw={entry.oldValue}
          fieldName={entry.fieldName}
          onDetail={() => setDetail(entry)}
        />
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
        <AuditValue
          raw={entry.newValue}
          fieldName={entry.fieldName}
          onDetail={() => setDetail(entry)}
        />
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
      <DataTable
        ariaLabel="Historial de auditoría"
        columns={columns}
        rows={entries}
        getRowKey={(entry) =>
          `${entry.changedAt}|${entry.entityName}|${entry.fieldName}|${entry.newValue ?? ""}|${entry.changedBy ?? ""}`
        }
        emptyMessage="Aún no hay cambios registrados."
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
      const Icon =
        parsed.tone === "success"
          ? CheckCircle2
          : parsed.tone === "warning"
            ? ShieldAlert
            : MinusCircle;
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
          className="inline-flex items-center gap-1.5 rounded-lg px-2 py-1 text-xs font-semibold text-flit-brand hover:bg-flit-brand/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand"
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
    <span
      className="font-mono opacity-80"
      title={`Usuario ${changedBy}`}
      aria-label={`Usuario ${changedBy}`}
    >
      {short}
    </span>
  );
}

function AuditDetailModal({
  entry,
  onClose,
}: {
  entry: AuditLogEntry | null;
  onClose: () => void;
}) {
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
            className="rounded-xl border border-[#DFE5ED] px-4 py-2 text-xs font-semibold hover:bg-[#F4F7FC] focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand dark:border-white/15 dark:hover:bg-white/5"
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

function formatDateTime(iso: string): string {
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) {
    return iso;
  }
  return formatFechaHora(parsed);
}

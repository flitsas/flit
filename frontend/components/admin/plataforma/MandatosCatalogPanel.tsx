"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Eye, FileText, RotateCcw, Search, Users } from "lucide-react";
import { ActionsMenu } from "@/components/atom/ActionsMenu";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { StatusBadge } from "@/components/atom/StatusBadge";
import {
  MandatoOtConfigForm,
  type MandatoOtConfigPanelMode,
} from "@/components/admin/plataforma/MandatoOtConfigForm";
import {
  deleteMandateOtConfig,
  fetchMandatoTemplatePreview,
  listMandateOtConfigs,
  type MandateOtConfigView,
} from "@/lib/api/admin-plataforma-mandatos";
import { openPdfBlobInNewTab } from "@/lib/documents/open-document-tab";
import {
  MANDATO_TEMPLATES,
  systemTemplateLabel,
  tipoNegocioLabel,
  type MandatoTemplateCode,
  type MandatoTemplateDefinition,
} from "@/lib/plataforma/mandato-templates";
import { useToast } from "@/components/admin/Toast";

/** Filas por página — mismo patrón que OTConfigTablePanel (HU #10495 / design guardian). */
const PAGE_SIZE = 10;

/**
 * Configurador SuperAdmin — plantillas + config por OT (Plataforma → Mandatos).
 */
export function MandatosCatalogPanel() {
  const { show: showToast } = useToast();
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [rows, setRows] = useState<MandateOtConfigView[]>([]);
  const [status, setStatus] = useState<"loading" | "ready" | "error">("loading");
  const [previewing, setPreviewing] = useState<string | null>(null);
  const [actingId, setActingId] = useState<string | null>(null);
  const [editing, setEditing] = useState<{
    office: MandateOtConfigView;
    mode: MandatoOtConfigPanelMode;
  } | null>(null);

  const load = useCallback(async () => {
    setStatus("loading");
    try {
      const items = await listMandateOtConfigs();
      setRows(items);
      setStatus("ready");
    } catch {
      setStatus("error");
    }
  }, []);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API
    void load();
  }, [load]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return rows;
    return rows.filter(
      (row) =>
        row.code.toLowerCase().includes(q) ||
        row.name.toLowerCase().includes(q) ||
        row.templateCode.toLowerCase().includes(q) ||
        systemTemplateLabel(row.templateCode).toLowerCase().includes(q),
    );
  }, [rows, search]);

  const lastPage = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const safePage = Math.min(page, lastPage);
  const pageRows = useMemo(
    () => filtered.slice((safePage - 1) * PAGE_SIZE, safePage * PAGE_SIZE),
    [filtered, safePage],
  );

  const handlePreviewTemplate = async (code: MandatoTemplateCode) => {
    setPreviewing(code);
    try {
      await openPdfBlobInNewTab(() => fetchMandatoTemplatePreview(code));
    } catch {
      showToast(
        "No se pudo abrir el mandato. Verifica la sesión SuperAdmin e inténtalo de nuevo.",
        "error",
      );
    } finally {
      setPreviewing(null);
    }
  };

  const handleReset = async (row: MandateOtConfigView) => {
    if (!row.hasExplicitConfig) return;
    setActingId(row.officeId);
    try {
      await deleteMandateOtConfig(row.officeId);
      await load();
      showToast(`Se restableció el default implícito (genérico) para ${row.name}.`, "success");
    } catch {
      showToast("No se pudo restablecer la configuración.", "error");
    } finally {
      setActingId(null);
    }
  };

  const columns: DataTableColumn<MandateOtConfigView>[] = [
    {
      key: "office",
      header: "Organismo",
      render: (row) => (
        <div className="flex flex-col gap-0.5">
          <span className="font-semibold text-[#162244] dark:text-white">{row.name}</span>
          <span className="font-mono text-[11px] text-[#59677D] dark:text-white/55">{row.code}</span>
        </div>
      ),
    },
    {
      key: "template",
      header: "Plantilla",
      render: (row) => (
        <div className="flex flex-col gap-0.5">
          <span className="text-sm font-medium text-[#162244] dark:text-white">
            {row.hasCustomTemplate ? "Propia" : systemTemplateLabel(row.templateCode)}
          </span>
          {!row.hasCustomTemplate ? (
            <span className="font-mono text-[11px] text-[#59677D] dark:text-white/55">
              {row.templateCode}
            </span>
          ) : null}
        </div>
      ),
    },
    {
      key: "tipo",
      header: "Tipo",
      render: () => (
        <span className="text-sm text-[#59677D] dark:text-white/65">Por compañía</span>
      ),
    },
    {
      key: "origen",
      header: "Origen",
      render: (row) => (
        <StatusBadge
          label={row.hasExplicitConfig ? "Config OT" : "Default"}
          tone={row.hasExplicitConfig ? "success" : "neutral"}
        />
      ),
    },
    {
      key: "actions",
      header: "Acciones",
      align: "right",
      render: (row) => (
        <ActionsMenu
          ariaLabel={`Acciones de mandato para ${row.name}`}
          items={[
            {
              key: "mandato",
              label: "Configuración del mandato",
              icon: FileText,
              onSelect: () => setEditing({ office: row, mode: "mandato" }),
              disabled: actingId !== null || previewing !== null,
              disabledReason: "Hay otra acción en curso.",
            },
            {
              key: "mandatario",
              label: "Configuración del mandatario",
              icon: Users,
              onSelect: () => setEditing({ office: row, mode: "mandatario" }),
              disabled: actingId !== null || previewing !== null,
              disabledReason: "Hay otra acción en curso.",
            },
            ...(row.hasExplicitConfig
              ? [
                  {
                    key: "default",
                    label: "Restablecer default",
                    icon: RotateCcw,
                    onSelect: () => void handleReset(row),
                    disabled: actingId !== null,
                    disabledReason: "Hay otra acción en curso.",
                  },
                ]
              : []),
          ]}
        />
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-6" data-testid="mandatos-catalog-panel">
      <section aria-labelledby="mandatos-plantillas-heading" className="flex flex-col gap-3">
        <div className="flex flex-col gap-1">
          <h2
            id="mandatos-plantillas-heading"
            className="text-sm font-semibold text-[#162244] dark:text-white"
          >
            Plantillas del sistema ({MANDATO_TEMPLATES.length})
          </h2>
          <p className="text-xs text-[#59677D] dark:text-white/65">
            Texto del contrato que FLIT genera por organismo. El Genérico es el respaldo; las demás
            están ligadas a OTs concretos. El tipo de mandatario (Persona/RL, Institucional o Abierto)
            se configura aparte por organismo.
          </p>
        </div>
        <ul className="grid gap-3 md:grid-cols-3">
          {MANDATO_TEMPLATES.map((template) => (
            <li key={template.code}>
              <TemplateCard
                template={template}
                busy={previewing === template.code}
                disabled={previewing !== null}
                onPreview={handlePreviewTemplate}
              />
            </li>
          ))}
        </ul>
      </section>

      <section aria-labelledby="mandatos-aplicacion-heading" className="flex flex-col gap-3">
        <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
          <h2
            id="mandatos-aplicacion-heading"
            className="text-sm font-semibold text-[#162244] dark:text-white"
          >
            Configuración por organismo
          </h2>
          <label className="relative block w-full sm:max-w-xs">
            <span className="sr-only">Buscar organismo o plantilla</span>
            <Search
              className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-[#59677D]"
              aria-hidden="true"
            />
            <input
              type="search"
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                setPage(1);
              }}
              placeholder="Buscar OT o plantilla…"
              className="w-full rounded-xl border border-[#DFE5ED] bg-white py-2 pr-3 pl-9 text-sm text-[#162244] outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
            />
          </label>
        </div>

        <DataTable
          columns={columns}
          rows={pageRows}
          getRowKey={(row) => row.officeId}
          status={status === "loading" ? "loading" : status === "error" ? "error" : undefined}
          onRetry={() => void load()}
          errorMessage="No se pudo cargar la configuración de mandatos."
          ariaLabel="Configuración de mandato por organismo"
          emptyMessage="No hay organismos activos en FLIT, o ninguno coincide con la búsqueda."
          minWidth={860}
          pagination={{
            page: safePage,
            pageSize: PAGE_SIZE,
            totalCount: filtered.length,
            onPageChange: setPage,
          }}
        />
      </section>

      {editing ? (
        <MandatoOtConfigForm
          office={editing.office}
          mode={editing.mode}
          onClose={() => setEditing(null)}
          onSaved={(view) => {
            setRows((prev) => prev.map((r) => (r.officeId === view.officeId ? view : r)));
            setEditing(null);
            showToast(
              editing.mode === "mandatario"
                ? `Mandatario actualizado para ${view.name}.`
                : `Configuración guardada para ${view.name}.`,
              "success",
            );
          }}
        />
      ) : null}
    </div>
  );
}

function TemplateCard({
  template,
  busy,
  disabled,
  onPreview,
}: {
  template: MandatoTemplateDefinition;
  busy: boolean;
  disabled: boolean;
  onPreview: (code: MandatoTemplateCode) => void;
}) {
  return (
    <article
      className="flex h-full flex-col gap-3 rounded-2xl border border-[#DFE5ED] bg-white p-4 dark:border-white/10 dark:bg-[#0B0F14]"
      data-testid={`mandato-template-${template.code}`}
    >
      <div className="flex items-start gap-3">
        <div
          className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-[#557EFF]/10"
          style={{ color: "#557EFF" }}
          aria-hidden="true"
        >
          <FileText className="h-5 w-5" strokeWidth={1.8} />
        </div>
        <div className="min-w-0 flex-1">
          <h3 className="text-sm font-semibold text-[#162244] dark:text-white">{template.label}</h3>
          <p className="font-mono text-[11px] text-[#59677D] dark:text-white/55">{template.code}</p>
        </div>
      </div>
      <p className="text-xs leading-relaxed text-[#59677D] dark:text-white/65">{template.summary}</p>
      <p className="text-[11px] text-[#59677D] dark:text-white/55">
        Tipo típico: {tipoNegocioLabel(template.tipoTipico)}
      </p>
      <button
        type="button"
        onClick={() => onPreview(template.code)}
        disabled={disabled}
        aria-busy={busy}
        aria-label={`Ver documento de mandato ${template.code} en una pestaña nueva`}
        className="mt-auto inline-flex w-full items-center justify-center gap-1.5 rounded-full px-4 py-2 text-xs font-semibold text-white transition hover:opacity-95 disabled:opacity-50"
        style={{ background: "linear-gradient(90deg,#557EFF 0%,#00DBD5 100%)" }}
      >
        <Eye className="h-3.5 w-3.5" aria-hidden="true" />
        {busy ? "Abriendo…" : "Ver documento"}
      </button>
    </article>
  );
}

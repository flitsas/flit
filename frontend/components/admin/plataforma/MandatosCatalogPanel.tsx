"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Eye, FileText, RotateCcw, Search, Settings2 } from "lucide-react";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { MandatoOtConfigForm } from "@/components/admin/plataforma/MandatoOtConfigForm";
import {
  deleteMandateOtConfig,
  fetchMandatoTemplatePreview,
  listMandateOtConfigs,
  type MandateOtConfigView,
} from "@/lib/api/admin-plataforma-mandatos";
import { openPdfBlobInNewTab } from "@/lib/documents/open-document-tab";
import {
  MANDATO_TEMPLATES,
  tipoNegocioLabel,
  type MandatoTemplateCode,
  type MandatoTemplateDefinition,
} from "@/lib/plataforma/mandato-templates";

/**
 * Configurador SuperAdmin — plantillas + config por OT (Plataforma → Mandatos).
 */
export function MandatosCatalogPanel() {
  const [search, setSearch] = useState("");
  const [rows, setRows] = useState<MandateOtConfigView[]>([]);
  const [status, setStatus] = useState<"loading" | "ready" | "error">("loading");
  const [previewing, setPreviewing] = useState<string | null>(null);
  const [actingId, setActingId] = useState<string | null>(null);
  const [editing, setEditing] = useState<MandateOtConfigView | null>(null);
  const [banner, setBanner] = useState<string | null>(null);

  const load = useCallback(async () => {
    setStatus("loading");
    setBanner(null);
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
        row.templateCode.toLowerCase().includes(q),
    );
  }, [rows, search]);

  const handlePreviewTemplate = async (code: MandatoTemplateCode) => {
    setBanner(null);
    setPreviewing(code);
    try {
      await openPdfBlobInNewTab(() => fetchMandatoTemplatePreview(code));
    } catch {
      setBanner("No se pudo abrir el mandato. Verifica la sesión SuperAdmin e inténtalo de nuevo.");
    } finally {
      setPreviewing(null);
    }
  };

  const handleReset = async (row: MandateOtConfigView) => {
    if (!row.hasExplicitConfig) return;
    setActingId(row.officeId);
    setBanner(null);
    try {
      await deleteMandateOtConfig(row.officeId);
      await load();
      setBanner(`Se restableció el default implícito (genérico) para ${row.name}.`);
    } catch {
      setBanner("No se pudo restablecer la configuración.");
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
        <span className="font-mono text-sm text-[#162244] dark:text-white">
          {row.hasCustomTemplate ? "propia" : row.templateCode}
        </span>
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
      render: (row) => (
        <div className="flex flex-wrap gap-1.5">
          <button
            type="button"
            disabled={actingId !== null || previewing !== null}
            onClick={() => setEditing(row)}
            className="inline-flex items-center gap-1 rounded-full px-2.5 py-1 text-[11px] font-semibold text-white disabled:opacity-50"
            style={{ background: "linear-gradient(90deg,#557EFF 0%,#00DBD5 100%)" }}
            aria-label={`Configurar mandato de ${row.name}`}
          >
            <Settings2 className="h-3 w-3" aria-hidden="true" />
            Configurar
          </button>
          {row.hasExplicitConfig ? (
            <button
              type="button"
              disabled={actingId !== null}
              onClick={() => void handleReset(row)}
              className="inline-flex items-center gap-1 rounded-full border border-[#DFE5ED] px-2.5 py-1 text-[11px] font-semibold text-[#162244] disabled:opacity-50 dark:border-white/15 dark:text-white"
              aria-label={`Restablecer default de ${row.name}`}
            >
              <RotateCcw className="h-3 w-3" aria-hidden="true" />
              {actingId === row.officeId ? "…" : "Default"}
            </button>
          ) : null}
        </div>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-6" data-testid="mandatos-catalog-panel">
      {banner ? (
        <div
          role="status"
          className="rounded-xl border border-[#557EFF]/30 bg-[#557EFF]/5 px-4 py-3 text-sm text-[#162244] dark:text-white/80"
        >
          {banner}
        </div>
      ) : null}

      <section aria-labelledby="mandatos-plantillas-heading" className="flex flex-col gap-3">
        <div className="flex flex-col gap-1">
          <h2
            id="mandatos-plantillas-heading"
            className="text-sm font-semibold text-[#162244] dark:text-white"
          >
            Redacciones del sistema ({MANDATO_TEMPLATES.length})
          </h2>
          <p className="text-xs text-[#59677D] dark:text-white/65">
            La redacción es independiente del tipo de mandatario (Persona/RL, Institucional u Abierto)
            que configures por organismo.
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
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Buscar OT o plantilla…"
              className="w-full rounded-xl border border-[#DFE5ED] bg-white py-2 pr-3 pl-9 text-sm text-[#162244] outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
            />
          </label>
        </div>

        <DataTable
          columns={columns}
          rows={filtered}
          getRowKey={(row) => row.officeId}
          status={status === "loading" ? "loading" : status === "error" ? "error" : undefined}
          onRetry={() => void load()}
          errorMessage="No se pudo cargar la configuración de mandatos."
          ariaLabel="Configuración de mandato por organismo"
          emptyMessage="No hay organismos que coincidan con la búsqueda."
          minWidth={860}
        />
      </section>

      {editing ? (
        <MandatoOtConfigForm
          office={editing}
          onClose={() => setEditing(null)}
          onSaved={(view) => {
            setRows((prev) => prev.map((r) => (r.officeId === view.officeId ? view : r)));
            setEditing(null);
            setBanner(`Configuración guardada para ${view.name}.`);
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

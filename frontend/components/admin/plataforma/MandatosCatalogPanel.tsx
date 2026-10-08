"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { AlertTriangle, FileText, RotateCcw, Search, UserRound, Users } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { SectionTabs } from "@/components/atom/SectionTabs";
import { ActionsMenu } from "@/components/atom/ActionsMenu";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { StatusBadge } from "@/components/atom/StatusBadge";
import {
  MandatoOtConfigForm,
  type MandatoOtConfigPanelMode,
} from "@/components/admin/plataforma/MandatoOtConfigForm";
import { MandatoFormatoEditor } from "@/components/admin/plataforma/MandatoFormatoEditor";
import { MandatosFormatosTable } from "@/components/admin/plataforma/MandatosFormatosTable";
import { MandatoSimuladorPanel } from "@/components/admin/plataforma/MandatoSimuladorPanel";
import {
  deleteMandateOtConfig,
  fetchMandatoTemplatePreview,
  listMandateOtConfigs,
  type MandateOtConfigView,
} from "@/lib/api/admin-plataforma-mandatos";
import { openPdfBlobInNewTab } from "@/lib/documents/open-document-tab";
import { useMandatoFormatos } from "@/hooks/useMandatoFormatos";
import type { MandatoFormatView } from "@/lib/api/admin-plataforma-mandatos";
import {
  mandatoFormatName,
  resumenTiposPorCompania,
} from "@/lib/plataforma/mandato-templates";
import { useToast } from "@/components/admin/Toast";

/**
 * Configurador SuperAdmin — plantillas + config por OT (Plataforma → Mandatos).
 */
export function MandatosCatalogPanel() {
  const router = useRouter();
  const [seccion, setSeccion] = useState<"formatos" | "organismos" | "simulador">("formatos");
  const { show: showToast } = useToast();
  const formatos = useMandatoFormatos();
  const [search, setSearch] = useState("");
  // Bug #13055 — tabla homologada con el modelo de trámites: filas por página elegibles.
  const pg = usePaginacion();
  const [rows, setRows] = useState<MandateOtConfigView[]>([]);
  const [status, setStatus] = useState<"loading" | "ready" | "error">("loading");
  const [previewing, setPreviewing] = useState<string | null>(null);
  // HU #13175 — formato abierto en el editor (nombre, tipo y plantilla).
  const [editingFormat, setEditingFormat] = useState<string | null>(null);
  const [actingId, setActingId] = useState<string | null>(null);
  // HU #13153 — confirmación detallada antes de restablecer.
  const [resetTarget, setResetTarget] = useState<MandateOtConfigView | null>(null);
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
        mandatoFormatName(formatos.formatos, row.templateCode).toLowerCase().includes(q),
    );
  }, [rows, search, formatos.formatos]);

  const pageRows = pg.paginar(filtered);

  const handlePreviewTemplate = async (code: string) => {
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
      setResetTarget(null);
      await load();
      showToast(`Se restableció el default implícito (genérico) para ${row.name}.`, "success");
    } catch {
      setResetTarget(null);
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
            {row.hasCustomTemplate ? "Propia" : mandatoFormatName(formatos.formatos, row.templateCode)}
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
      header: "Tipos por compañía",
      render: (row) => (
        <span
          className="text-sm text-[#162244] dark:text-white"
          data-testid={`mandato-tipos-${row.officeId}`}
        >
          {resumenTiposPorCompania(row.explicitPersonaJuridica, row.explicitMandatoAbierto)}
        </span>
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
            {
              key: "personas",
              label: "Mandatarios del organismo",
              icon: UserRound,
              onSelect: () => router.push(`/admin/transit-offices/${row.officeId}/mandatos`),
            },
            ...(row.hasExplicitConfig
              ? [
                  {
                    key: "default",
                    label: "Restablecer default",
                    icon: RotateCcw,
                    onSelect: () => setResetTarget(row),
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
      <SectionTabs
        ariaLabel="Secciones de mandatos de la plataforma"
        active={seccion}
        onChange={setSeccion}
        tabs={[
          {
            id: "formatos",
            label: "Formatos de contrato",
            count: formatos.status === "ready" ? formatos.formatos.length : undefined,
            content: (
      <section aria-labelledby="mandatos-plantillas-heading" className="flex flex-col gap-3">
        <div className="flex flex-col gap-1">
          <h2
            id="mandatos-plantillas-heading"
            className="text-sm font-semibold text-[#162244] dark:text-white"
          >
            Formatos de contrato{formatos.status === "ready" ? ` (${formatos.formatos.length})` : ""}
          </h2>
          <p className="text-xs text-[#59677D] dark:text-white/65">
            Texto del contrato que FLIT genera por organismo. El Genérico es el respaldo. El tipo
            por defecto de un organismo nuevo es Mandatario de la compañía. Institucional y Abierto solo aplican si la
            plantilla del organismo lo implica (p. ej. Sabaneta). Esta pantalla convive con el hub
            del organismo → Mandatos: es la misma configuración.
          </p>
        </div>
        {formatos.status === "loading" ? (
          <p role="status" aria-live="polite" className="text-xs text-[#59677D] dark:text-white/65" data-testid="mandatos-formatos-loading">
            Cargando formatos de contrato…
          </p>
        ) : formatos.status === "error" ? (
          <div
            role="alert"
            data-testid="mandatos-formatos-error"
            className="flex flex-wrap items-center gap-2 rounded-xl border border-[#FF4E00]/40 bg-[rgba(255,78,0,0.06)] px-3 py-2 text-xs text-[#FF4E00]"
          >
            <span>No se pudo cargar la lista de formatos de contrato.</span>
            <button
              type="button"
              onClick={formatos.reload}
              className="rounded-full border border-[#FF4E00]/40 px-3 py-1 font-semibold"
            >
              Reintentar
            </button>
          </div>
        ) : (
          <MandatosFormatosTable
            formatos={formatos.formatos}
            previewing={previewing}
            onEdit={setEditingFormat}
            onPreview={handlePreviewTemplate}
          />
        )}
      </section>
            ),
          },
          {
            id: "organismos",
            label: "Configuración por organismo",
            count: filtered.length,
            content: (
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
                pg.setPage(1);
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
            page: Math.min(pg.page, Math.max(1, Math.ceil(filtered.length / pg.pageSize))),
            pageSize: pg.pageSize,
            totalCount: filtered.length,
            onPageChange: pg.setPage,
            onPageSizeChange: pg.setPageSize,
          }}
        />
      </section>
            ),
          },
          {
            id: "simulador",
            label: "Simulador",
            content: (
      <MandatoSimuladorPanel offices={rows} />
            ),
          },
        ]}
      />

      {resetTarget ? (
        <ResetConfirmDialog
          row={resetTarget}
          formatos={formatos.formatos}
          busy={actingId !== null}
          onConfirm={() => void handleReset(resetTarget)}
          onCancel={() => setResetTarget(null)}
        />
      ) : null}

      {editingFormat ? (
        <MandatoFormatoEditor
          code={editingFormat}
          onClose={() => setEditingFormat(null)}
          onConflict={formatos.reload}
          onSaved={(format, info) => {
            setEditingFormat(null);
            formatos.reload();
            // Las filas por organismo muestran el nombre del formato: se recargan con el catálogo.
            void load();
            showToast(
              info.published
                ? `Se publicó la versión ${info.published} de «${format.name}».`
                : info.changed
                  ? `Se guardó el formato «${format.name}».`
                  : `No había cambios en «${format.name}».`,
              "success",
            );
          }}
        />
      ) : null}

      {editing ? (
        <MandatoOtConfigForm
          office={editing.office}
          mode={editing.mode}
          editableCompanyType
          formatos={formatos}
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

function ResetConfirmDialog({
  row,
  formatos,
  busy,
  onConfirm,
  onCancel,
}: {
  row: MandateOtConfigView;
  formatos: readonly MandatoFormatView[];
  busy: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  const loses: string[] = [
    `La redacción elegida (${
      !row.configuredTemplateCode || row.configuredTemplateCode === "auto"
        ? "Automática"
        : mandatoFormatName(formatos, row.configuredTemplateCode)
    }).`,
  ];
  if (row.defaultMandateSignerId) {
    loses.push(
      `El mandatario general del OT${row.defaultMandateSignerName ? ` (${row.defaultMandateSignerName})` : ""}.`,
    );
  }
  if (row.hasCustomTemplate) loses.push("La plantilla propia del organismo.");
  return (
    <Modal
      open
      onClose={onCancel}
      busy={busy}
      icon={AlertTriangle}
      iconBg="#F9AC00"
      title="Restablecer configuración"
      titleClassName="text-base font-bold text-[#162744]"
      size="md"
    >
      <div className="space-y-3 text-xs" data-testid="mandatos-reset-dialog">
        <p>
          Vas a restablecer <strong>{row.name}</strong> al default. Se perderá:
        </p>
        <ul className="list-disc space-y-1 pl-5" data-testid="mandatos-reset-lista">
          {loses.map((t) => (
            <li key={t}>{t}</li>
          ))}
        </ul>
        <p
          className="rounded-xl border px-3 py-2 leading-relaxed"
          style={{ borderColor: "#F9AC00", background: "rgba(249,172,0,0.08)", color: "#8a6000" }}
          role="note"
        >
          Las reglas por compañía no se eliminan.
        </p>
        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            onClick={onCancel}
            disabled={busy}
            className="rounded-xl border px-4 py-2 text-xs font-semibold disabled:opacity-50"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={onConfirm}
            disabled={busy}
            className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            {busy ? "Restableciendo…" : "Restablecer"}
          </button>
        </div>
      </div>
    </Modal>
  );
}

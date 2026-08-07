"use client";

import { useCallback, useEffect, useId, useMemo, useRef, useState } from "react";
import { Eye, FileText, Search, Trash2, Upload, X } from "lucide-react";
import {
  deleteCompanyOtMandateRule,
  deleteMandateOtCustomTemplate,
  fetchMandateOtPreview,
  fetchMandatoTemplatePreview,
  listCompanyOtMandateRules,
  saveMandateOtEditorBody,
  uploadMandateOtPdfTemplate,
  upsertCompanyOtMandateRule,
  upsertMandateOtConfig,
  type CompanyOtMandateRuleView,
  type MandateOtConfigView,
  type UpsertMandateOtConfigBody,
} from "@/lib/api/admin-plataforma-mandatos";
import { ApiError } from "@/lib/api/types";
import { openPdfBlobInNewTab } from "@/lib/documents/open-document-tab";
import {
  MANDATO_TIPOS,
  resolveAssignmentMode,
  resolveTipoNegocio,
  suggestedFamilyForTipo,
  systemTemplateLabel,
  type MandatoTipoNegocio,
} from "@/lib/plataforma/mandato-templates";

const EDITOR_PLACEHOLDER = `Entre las partes, EL MANDANTE {{mandante_nombre}} identificado con {{mandante_documento}}, y EL MANDATARIO.

Objeto: radicar el trámite {{tramite}} del vehículo placa {{placa}} ante {{organismo}}.

Placeholders: {{placa}} {{tramite}} {{organismo}} {{ciudad}} {{fecha}} {{mandante_nombre}} {{mandante_documento}} {{mandatario_nombre}} {{mandatario_documento}} {{mandatario_institucional}} {{mandatario_nit}}

Las firmas se agregan automáticamente al pie del documento.`;

export interface MandatoOtConfigFormProps {
  office: MandateOtConfigView;
  onClose: () => void;
  onSaved: (view: MandateOtConfigView) => void;
}

export function MandatoOtConfigForm({ office, onClose, onSaved }: MandatoOtConfigFormProps) {
  const titleId = useId();
  const pdfRef = useRef<HTMLInputElement>(null);

  const [view, setView] = useState(office);
  const [templateCode, setTemplateCode] = useState(office.templateCode || "generico");
  const [family, setFamily] = useState(office.mandataryFamily || "individuo");
  const [instName, setInstName] = useState(office.institutionalMandataryName ?? "");
  const [instNit, setInstNit] = useState(office.institutionalMandataryNit ?? "");
  const [chamberCity, setChamberCity] = useState(office.chamberCity ?? "");
  const [sigla, setSigla] = useState(office.mandatarySigla ?? "");
  const [rowVersion, setRowVersion] = useState(office.rowVersion);
  const [editorBody, setEditorBody] = useState(office.customTemplateBody ?? EDITOR_PLACEHOLDER);
  const [showEditor, setShowEditor] = useState(office.customTemplateKind === "editor");

  const [companyRules, setCompanyRules] = useState<CompanyOtMandateRuleView[]>([]);
  const [rulesStatus, setRulesStatus] = useState<"loading" | "ready" | "error">("loading");
  const [savingCompanyId, setSavingCompanyId] = useState<string | null>(null);
  const [companySearch, setCompanySearch] = useState("");
  const [companyTipoFilter, setCompanyTipoFilter] = useState<"all" | MandatoTipoNegocio>("all");

  const [saving, setSaving] = useState(false);
  const [previewing, setPreviewing] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const hasCustom = view.hasCustomTemplate;
  const showInstitutionalMeta =
    templateCode === "sabaneta" ||
    templateCode === "bello" ||
    family === "organismo_transito";

  const filteredCompanyRules = useMemo(() => {
    const q = companySearch.trim().toLowerCase();
    return companyRules.filter((row) => {
      const tipo = resolveTipoNegocio(row.assignmentMode);
      if (companyTipoFilter !== "all" && tipo !== companyTipoFilter) return false;
      if (!q) return true;
      return row.companyName.toLowerCase().includes(q);
    });
  }, [companyRules, companySearch, companyTipoFilter]);

  const explicitRulesCount = useMemo(
    () => companyRules.filter((r) => r.hasExplicitRule).length,
    [companyRules],
  );

  const loadCompanyRules = useCallback(async () => {
    setRulesStatus("loading");
    setError(null);
    try {
      const items = await listCompanyOtMandateRules(office.officeId);
      setCompanyRules(items);
      setRulesStatus("ready");
    } catch (err) {
      setRulesStatus("error");
      const status = err instanceof ApiError ? err.status : null;
      if (status === 404) {
        setError(
          "El API no reconoce el endpoint de compañías (¿Flit.Api desactualizado?). Reinicia la API con el código nuevo y aplica la migración 61.",
        );
      } else if (status === 500) {
        setError(
          "Error del servidor al listar compañías. Suele faltar la tabla company_ot_mandate_rules (migración 61).",
        );
      } else {
        setError("No se pudieron cargar las compañías. Reintentar.");
      }
    }
  }, [office.officeId]);

  useEffect(() => {
    void loadCompanyRules();
  }, [loadCompanyRules]);

  const applyView = (next: MandateOtConfigView) => {
    setView(next);
    setRowVersion(next.rowVersion);
    setTemplateCode(next.templateCode || "generico");
    setFamily(next.mandataryFamily || "individuo");
    setInstName(next.institutionalMandataryName ?? "");
    setInstNit(next.institutionalMandataryNit ?? "");
    setChamberCity(next.chamberCity ?? "");
    setSigla(next.mandatarySigla ?? "");
    if (next.customTemplateBody) setEditorBody(next.customTemplateBody);
    setShowEditor(next.customTemplateKind === "editor");
  };

  const buildBody = (): UpsertMandateOtConfigBody => ({
    templateCode,
    requiresForNaturalPerson: true,
    mandataryFamily: family,
    assignmentMode: "signer",
    institutionalMandataryName: showInstitutionalMeta ? instName || null : null,
    institutionalMandataryNit: showInstitutionalMeta ? instNit || null : null,
    chamberCity: chamberCity || null,
    mandatarySigla: sigla || null,
    rowVersion,
  });

  const handleSaveMeta = async () => {
    setError(null);
    if (showInstitutionalMeta && !instName.trim()) {
      setError("El nombre del mandatario institucional (texto de plantilla) es obligatorio.");
      return;
    }
    setSaving(true);
    try {
      const saved = await upsertMandateOtConfig(office.officeId, buildBody());
      applyView(saved);
      onSaved(saved);
    } catch {
      setError("No se pudo guardar la configuración del OT.");
    } finally {
      setSaving(false);
    }
  };

  const handleUploadPdf = async (file: File) => {
    setError(null);
    setUploading(true);
    try {
      await upsertMandateOtConfig(office.officeId, buildBody());
      const saved = await uploadMandateOtPdfTemplate(office.officeId, file);
      applyView(saved);
      onSaved(saved);
    } catch {
      setError("No se pudo subir la plantilla PDF. Debe ser un PDF válido (máx. 10 MB).");
    } finally {
      setUploading(false);
      if (pdfRef.current) pdfRef.current.value = "";
    }
  };

  const handleSaveEditor = async () => {
    setError(null);
    if (!editorBody.trim()) {
      setError("El cuerpo del editor no puede estar vacío.");
      return;
    }
    setSaving(true);
    try {
      await upsertMandateOtConfig(office.officeId, buildBody());
      const saved = await saveMandateOtEditorBody(office.officeId, editorBody, rowVersion);
      applyView(saved);
      onSaved(saved);
    } catch {
      setError("No se pudo guardar el editor de plantilla.");
    } finally {
      setSaving(false);
    }
  };

  const handleRemoveCustom = async () => {
    setError(null);
    setSaving(true);
    try {
      const saved = await deleteMandateOtCustomTemplate(office.officeId);
      applyView(saved);
      onSaved(saved);
    } catch {
      setError("No se pudo quitar la plantilla propia.");
    } finally {
      setSaving(false);
    }
  };

  const handleCompanyTipoChange = async (
    row: CompanyOtMandateRuleView,
    nextTipo: MandatoTipoNegocio,
  ) => {
    setError(null);
    setSavingCompanyId(row.companyTenantId);
    try {
      const mode = resolveAssignmentMode(nextTipo);
      if (mode === "signer" && !row.hasExplicitRule) {
        return;
      }
      if (mode === "signer" && row.hasExplicitRule) {
        await deleteCompanyOtMandateRule(office.officeId, row.companyTenantId);
        await loadCompanyRules();
        return;
      }
      const familyForTipo = suggestedFamilyForTipo(nextTipo, templateCode);
      await upsertCompanyOtMandateRule(office.officeId, row.companyTenantId, {
        assignmentMode: mode,
        mandataryFamily: familyForTipo,
        institutionalMandataryName:
          mode === "institutional"
            ? row.institutionalMandataryName || instName || office.name
            : null,
        institutionalMandataryNit:
          mode === "institutional" ? row.institutionalMandataryNit || instNit || null : null,
        chamberCity: row.chamberCity || chamberCity || null,
        mandatarySigla: row.mandatarySigla || sigla || null,
      });
      await loadCompanyRules();
    } catch {
      setError("No se pudo guardar el tipo de mandato para esa compañía.");
    } finally {
      setSavingCompanyId(null);
    }
  };

  const handlePreview = async () => {
    setError(null);
    setPreviewing(true);
    try {
      await openPdfBlobInNewTab(async () => {
        try {
          return await fetchMandateOtPreview(office.officeId);
        } catch {
          return fetchMandatoTemplatePreview(templateCode);
        }
      });
    } catch {
      setError("No se pudo abrir la vista previa del mandato.");
    } finally {
      setPreviewing(false);
    }
  };

  const busy = saving || previewing || uploading || savingCompanyId !== null;

  return (
    <div
      className="fixed inset-0 z-50 flex items-end justify-center bg-[#162244]/40 p-4 sm:items-center"
      role="presentation"
      onClick={(e) => {
        if (e.target === e.currentTarget && !busy) onClose();
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="flex max-h-[min(92vh,48rem)] w-full max-w-xl flex-col overflow-hidden rounded-2xl border border-[#DFE5ED] bg-white shadow-xl dark:border-white/10 dark:bg-[#0B0F14]"
        data-testid="mandato-ot-config-form"
      >
        <header className="flex items-start justify-between gap-3 border-b border-[#DFE5ED] px-5 py-4 dark:border-white/10">
          <div>
            <h2 id={titleId} className="text-base font-semibold text-[#162244] dark:text-white">
              Configurar mandato
            </h2>
            <p className="mt-0.5 text-xs text-[#59677D] dark:text-white/65">
              {office.name} · <span className="font-mono">{office.code}</span>
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            disabled={busy}
            className="rounded-lg p-1.5 text-[#59677D] hover:bg-[#F4F7FC] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:opacity-50 dark:hover:bg-white/5"
            aria-label="Cerrar"
          >
            <X className="h-4 w-4" />
          </button>
        </header>

        <div className="flex-1 space-y-4 overflow-y-auto px-5 py-4">
          {error ? (
            <p
              role="alert"
              className="rounded-xl border border-[#FF4E00]/40 bg-[rgba(255,78,0,0.06)] px-3 py-2 text-xs text-[#FF4E00]"
            >
              {error}
            </p>
          ) : null}

          <section className="space-y-3" aria-labelledby="mandato-plantilla-heading">
            <h3
              id="mandato-plantilla-heading"
              className="text-xs font-semibold text-[#162244] dark:text-white"
            >
              Plantilla del mandato (por OT)
            </h3>

            {!hasCustom ? (
              <div
                className="relative overflow-hidden rounded-2xl border border-[#DFE5ED] bg-gradient-to-br from-[#F4F7FC] via-white to-[rgba(0,219,213,0.08)] px-4 py-3 dark:border-white/10 dark:from-white/5 dark:via-[#0B0F14] dark:to-[rgba(85,126,255,0.12)]"
                data-testid="mandato-system-template-badge"
                role="status"
              >
                <div className="flex flex-wrap items-center gap-2">
                  <span className="inline-flex items-center rounded-full bg-[#162244] px-2.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-white dark:bg-[#557EFF]">
                    Sistema
                  </span>
                  <span className="inline-flex items-center rounded-full border border-[#557EFF]/35 bg-white/80 px-2.5 py-0.5 text-[11px] font-semibold text-[#162244] dark:border-[#00DBD5]/40 dark:bg-white/10 dark:text-white">
                    {systemTemplateLabel(templateCode)}
                  </span>
                </div>
                <p className="mt-2 text-[11px] leading-relaxed text-[#59677D] dark:text-white/65">
                  Este OT usa la redacción{" "}
                  <span className="font-semibold text-[#162244] dark:text-white">
                    {systemTemplateLabel(templateCode)}
                  </span>{" "}
                  del sistema para todas las compañías. Sube un PDF o abre el editor para plantilla
                  propia.
                </p>
              </div>
            ) : (
              <div
                className="rounded-2xl border border-[#557EFF]/35 bg-[rgba(85,126,255,0.06)] px-4 py-3 dark:border-[#00DBD5]/35 dark:bg-[rgba(0,219,213,0.08)]"
                data-testid="mandato-custom-template-banner"
              >
                <div className="flex flex-wrap items-center gap-2">
                  <span className="inline-flex items-center rounded-full bg-gradient-to-r from-[#557EFF] to-[#00DBD5] px-2.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-white">
                    Propia
                  </span>
                  <span className="inline-flex items-center gap-1.5 text-xs font-semibold text-[#162244] dark:text-white">
                    <FileText className="h-3.5 w-3.5 shrink-0 text-[#557EFF]" aria-hidden="true" />
                    {view.customTemplateKind === "pdf"
                      ? (view.customTemplateFileName ?? "plantilla.pdf")
                      : "Editor rellenable"}
                  </span>
                </div>
                <p className="mt-2 text-[11px] leading-relaxed text-[#59677D] dark:text-white/65">
                  En el trámite se usa este documento del OT (no la redacción del sistema). Las firmas
                  van al pie con layout FLIT.
                </p>
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => void handleRemoveCustom()}
                  className="mt-2.5 inline-flex items-center gap-1 text-[11px] font-semibold text-[#FF4E00] disabled:opacity-50"
                >
                  <Trash2 className="h-3 w-3" aria-hidden="true" />
                  Quitar y volver a {systemTemplateLabel(templateCode)}
                </button>
              </div>
            )}

            <div className="space-y-3 rounded-xl border border-dashed border-[#DFE5ED] p-3 dark:border-white/15">
              <p className="text-[11px] text-[#59677D] dark:text-white/65">
                {hasCustom
                  ? "Puedes reemplazar el documento subiendo otro PDF o editando el cuerpo."
                  : "PDF blank o editor rellenable. Al guardar, reemplaza la redacción del sistema."}
              </p>
              <input
                ref={pdfRef}
                type="file"
                accept="application/pdf"
                className="sr-only"
                disabled={busy}
                onChange={(e) => {
                  const f = e.target.files?.[0];
                  if (f) void handleUploadPdf(f);
                }}
              />
              <div className="flex flex-wrap gap-2">
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => pdfRef.current?.click()}
                  className="inline-flex items-center gap-1.5 rounded-full border border-[#DFE5ED] px-3 py-1.5 text-[11px] font-semibold text-[#162244] hover:bg-[#F4F7FC] disabled:opacity-50 dark:border-white/15 dark:text-white"
                >
                  <Upload className="h-3.5 w-3.5" aria-hidden="true" />
                  {uploading ? "Subiendo…" : hasCustom ? "Reemplazar PDF" : "Subir PDF"}
                </button>
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => setShowEditor((v) => !v)}
                  className="inline-flex items-center gap-1.5 rounded-full border border-[#DFE5ED] px-3 py-1.5 text-[11px] font-semibold text-[#162244] hover:bg-[#F4F7FC] disabled:opacity-50 dark:border-white/15 dark:text-white"
                  data-testid="mandato-open-editor"
                >
                  <FileText className="h-3.5 w-3.5" aria-hidden="true" />
                  {showEditor ? "Ocultar editor" : "Abrir editor"}
                </button>
              </div>

              {showEditor ? (
                <div className="space-y-2" data-testid="mandato-editor-panel">
                  <textarea
                    value={editorBody}
                    onChange={(e) => setEditorBody(e.target.value)}
                    disabled={busy}
                    rows={8}
                    className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 font-mono text-[11px] text-[#162244] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
                    aria-label="Cuerpo de la plantilla del editor"
                  />
                  <p className="text-[10px] text-[#59677D] dark:text-white/55">
                    Las firmas no se editan aquí: siempre se agregan al pie.
                  </p>
                  <button
                    type="button"
                    disabled={busy}
                    onClick={() => void handleSaveEditor()}
                    className="rounded-full px-3 py-1.5 text-[11px] font-semibold text-white disabled:opacity-50"
                    style={{ background: "linear-gradient(90deg,#557EFF 0%,#00DBD5 100%)" }}
                  >
                    {saving ? "Guardando editor…" : "Guardar plantilla del editor"}
                  </button>
                </div>
              ) : null}
            </div>
          </section>

          {showInstitutionalMeta ? (
            <div className="space-y-3 rounded-xl border border-[#DFE5ED] bg-[#F8FAFC] p-3 dark:border-white/10 dark:bg-white/5">
              <p className="text-[11px] text-[#59677D] dark:text-white/65">
                Datos de texto para la plantilla del OT (no definen el tipo por compañía).
              </p>
              <label className="block space-y-1.5">
                <span className="text-xs font-semibold text-[#162244] dark:text-white">
                  Mandatario institucional / UT
                </span>
                <input
                  value={instName}
                  onChange={(e) => setInstName(e.target.value)}
                  disabled={busy}
                  className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
                />
              </label>
              <label className="block space-y-1.5">
                <span className="text-xs font-semibold text-[#162244] dark:text-white">NIT</span>
                <input
                  value={instNit}
                  onChange={(e) => setInstNit(e.target.value)}
                  disabled={busy}
                  className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 font-mono text-sm dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
                />
              </label>
              <div className="grid gap-3 sm:grid-cols-2">
                <label className="block space-y-1.5">
                  <span className="text-xs font-semibold text-[#162244] dark:text-white">
                    Ciudad cámara
                  </span>
                  <input
                    value={chamberCity}
                    onChange={(e) => setChamberCity(e.target.value)}
                    disabled={busy}
                    className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
                  />
                </label>
                <label className="block space-y-1.5">
                  <span className="text-xs font-semibold text-[#162244] dark:text-white">Sigla</span>
                  <input
                    value={sigla}
                    onChange={(e) => setSigla(e.target.value)}
                    disabled={busy}
                    className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 font-mono text-sm dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
                  />
                </label>
              </div>
            </div>
          ) : null}

          <section className="space-y-2" aria-labelledby="mandato-company-rules-heading">
            <div className="flex flex-wrap items-end justify-between gap-2">
              <div className="min-w-0">
                <h3
                  id="mandato-company-rules-heading"
                  className="text-xs font-semibold text-[#162244] dark:text-white"
                >
                  Tipo de mandatario por compañía
                </h3>
                <p className="mt-0.5 text-[11px] leading-relaxed text-[#59677D] dark:text-white/65">
                  Default sin regla: Persona/RL.{" "}
                  {rulesStatus === "ready" ? (
                    <span className="font-medium text-[#162244] dark:text-white/80">
                      {companyRules.length} compañía{companyRules.length === 1 ? "" : "s"}
                      {explicitRulesCount > 0 ? ` · ${explicitRulesCount} con regla` : ""}
                    </span>
                  ) : null}
                </p>
              </div>
            </div>

            {rulesStatus === "loading" ? (
              <p className="text-[11px] text-[#59677D]" role="status">
                Cargando compañías…
              </p>
            ) : null}
            {rulesStatus === "error" ? (
              <p role="alert" className="text-[11px] text-[#FF4E00]">
                {error?.includes("compañías") || error?.includes("API") || error?.includes("migración")
                  ? error
                  : "No se pudieron cargar las compañías."}{" "}
                <button
                  type="button"
                  className="font-semibold underline"
                  onClick={() => void loadCompanyRules()}
                >
                  Reintentar
                </button>
              </p>
            ) : null}
            {rulesStatus === "ready" && companyRules.length === 0 ? (
              <p
                className="rounded-xl border border-dashed border-[#DFE5ED] px-3 py-2 text-[11px] text-[#59677D] dark:border-white/15"
                data-testid="mandato-company-rules-empty"
              >
                No hay compañías con grant habilitado en este OT.
              </p>
            ) : null}

            {companyRules.length > 0 ? (
              <div
                className="overflow-hidden rounded-xl border border-[#DFE5ED] dark:border-white/10"
                data-testid="mandato-company-rules-list"
              >
                <div className="flex flex-wrap items-center gap-2 border-b border-[#DFE5ED] bg-[#F8FAFC] px-2.5 py-2 dark:border-white/10 dark:bg-white/5">
                  <label className="relative min-w-[10rem] flex-1">
                    <span className="sr-only">Buscar compañía</span>
                    <Search
                      className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-[#59677D]"
                      aria-hidden="true"
                    />
                    <input
                      value={companySearch}
                      onChange={(e) => setCompanySearch(e.target.value)}
                      placeholder="Buscar compañía…"
                      disabled={busy}
                      className="w-full rounded-lg border border-[#DFE5ED] bg-white py-1.5 pl-8 pr-2 text-[11px] text-[#162244] placeholder:text-[#59677D]/70 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
                      data-testid="mandato-company-search"
                    />
                  </label>
                  <label className="shrink-0">
                    <span className="sr-only">Filtrar por tipo</span>
                    <select
                      value={companyTipoFilter}
                      onChange={(e) =>
                        setCompanyTipoFilter(e.target.value as "all" | MandatoTipoNegocio)
                      }
                      disabled={busy}
                      className="rounded-lg border border-[#DFE5ED] bg-white px-2 py-1.5 text-[11px] text-[#162244] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
                      data-testid="mandato-company-tipo-filter"
                    >
                      <option value="all">Todos los tipos</option>
                      {MANDATO_TIPOS.map((t) => (
                        <option key={t.value} value={t.value}>
                          {t.label}
                        </option>
                      ))}
                    </select>
                  </label>
                </div>

                <div
                  className="grid grid-cols-[minmax(0,1fr)_9.5rem] gap-x-2 border-b border-[#DFE5ED] bg-white px-2.5 py-1.5 text-[10px] font-semibold uppercase tracking-wide text-[#59677D] dark:border-white/10 dark:bg-[#0B0F14]"
                  role="row"
                >
                  <span>Compañía</span>
                  <span>Tipo</span>
                </div>

                <ul className="max-h-52 divide-y divide-[#DFE5ED] overflow-y-auto dark:divide-white/10">
                  {filteredCompanyRules.length === 0 ? (
                    <li className="px-2.5 py-3 text-[11px] text-[#59677D]">
                      Ninguna compañía coincide con la búsqueda o el filtro.
                    </li>
                  ) : (
                    filteredCompanyRules.map((row) => {
                      const tipo = resolveTipoNegocio(row.assignmentMode);
                      const rowBusy = savingCompanyId === row.companyTenantId;
                      return (
                        <li
                          key={row.companyTenantId}
                          className="grid grid-cols-[minmax(0,1fr)_9.5rem] items-center gap-x-2 px-2.5 py-1.5 hover:bg-[#F4F7FC]/80 dark:hover:bg-white/[0.04]"
                        >
                          <div className="min-w-0">
                            <p className="truncate text-[12px] font-medium text-[#162244] dark:text-white">
                              {row.companyName}
                            </p>
                            <p className="truncate text-[10px] text-[#59677D] dark:text-white/50">
                              {rowBusy
                                ? "Guardando…"
                                : row.hasExplicitRule
                                  ? "Regla propia"
                                  : "Default"}
                            </p>
                          </div>
                          <select
                            value={tipo}
                            disabled={busy || rowBusy}
                            onChange={(e) =>
                              void handleCompanyTipoChange(
                                row,
                                e.target.value as MandatoTipoNegocio,
                              )
                            }
                            className="w-full rounded-md border border-[#DFE5ED] bg-white px-1.5 py-1 text-[11px] text-[#162244] disabled:opacity-50 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
                            aria-label={`Tipo de mandato para ${row.companyName}`}
                            data-testid={`mandato-company-tipo-${row.companyTenantId}`}
                          >
                            {MANDATO_TIPOS.map((t) => (
                              <option key={t.value} value={t.value}>
                                {t.label}
                              </option>
                            ))}
                          </select>
                        </li>
                      );
                    })
                  )}
                </ul>

                {filteredCompanyRules.length > 0 &&
                filteredCompanyRules.length < companyRules.length ? (
                  <p className="border-t border-[#DFE5ED] px-2.5 py-1.5 text-[10px] text-[#59677D] dark:border-white/10">
                    Mostrando {filteredCompanyRules.length} de {companyRules.length}
                  </p>
                ) : null}
              </div>
            ) : null}
          </section>
        </div>

        <footer className="flex flex-wrap items-center justify-end gap-2 border-t border-[#DFE5ED] px-5 py-3 dark:border-white/10">
          <button
            type="button"
            disabled={busy}
            onClick={() => void handlePreview()}
            className="inline-flex items-center gap-1.5 rounded-full border border-[#DFE5ED] px-4 py-2 text-xs font-semibold text-[#162244] disabled:opacity-50 dark:border-white/15 dark:text-white"
          >
            <Eye className="h-3.5 w-3.5" aria-hidden="true" />
            {previewing ? "Abriendo…" : "Vista previa"}
          </button>
          <button
            type="button"
            disabled={busy}
            onClick={onClose}
            className="rounded-full px-4 py-2 text-xs font-semibold text-[#59677D] disabled:opacity-50"
          >
            Cancelar
          </button>
          <button
            type="button"
            disabled={busy}
            onClick={() => void handleSaveMeta()}
            className="rounded-full px-4 py-2 text-xs font-semibold text-white disabled:opacity-50"
            style={{ background: "linear-gradient(90deg,#557EFF 0%,#00DBD5 100%)" }}
          >
            {saving ? "Guardando…" : "Guardar plantilla"}
          </button>
        </footer>
      </div>
    </div>
  );
}

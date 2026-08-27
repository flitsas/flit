"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Eye, FileText, Search, Trash2, Upload } from "lucide-react";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { OtSidePanel } from "@/components/admin/transit-offices/OtSidePanel";
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
import { fetchMandateSigners, type MandateSigner } from "@/lib/api/admin-mandate-signers";
import { ApiError } from "@/lib/api/types";
import { openPdfBlobInNewTab } from "@/lib/documents/open-document-tab";
import {
  MANDATO_TIPOS,
  mandatoTemplateOptions,
  resolveAssignmentMode,
  resolveTipoNegocio,
  suggestedFamilyForTipo,
  systemTemplateLabel,
  terceroAjenoEnPlantilla,
  type MandatoTipoNegocio,
} from "@/lib/plataforma/mandato-templates";

const EDITOR_PLACEHOLDER = `Entre las partes, EL MANDANTE {{mandante_nombre}} identificado con {{mandante_documento}}, y EL MANDATARIO.

Objeto: radicar el trámite {{tramite}} del vehículo placa {{placa}} ante {{organismo}}.

Placeholders: {{placa}} {{tramite}} {{organismo}} {{ciudad}} {{fecha}} {{mandante_nombre}} {{mandante_documento}} {{mandatario_nombre}} {{mandatario_documento}} {{mandatario_institucional}} {{mandatario_nit}}

Las firmas se agregan automáticamente al pie del documento.`;

/** Filas por página en reglas compañía×OT (DataTable + Pagination del design system). */
const COMPANY_PAGE_SIZE = 10;

/**
 * HU #11705 — el cargue de un PDF propio y el editor de texto libre quedan OCULTOS: cargar un
 * contrato ajeno al estándar se presta para malas prácticas, y la parametrización debe limitarse a
 * escoger cuál de las redacciones del sistema aplica cada organismo.
 *
 * <p>Es un interruptor de interfaz, no un borrado: los endpoints, el almacenamiento y el generador
 * siguen intactos, de modo que los OT que YA tienen plantilla propia la conservan y se siguen
 * mostrando como tal. Volver a exponer la función es cambiar esta constante a `true`.</p>
 */
const MOSTRAR_PLANTILLA_PROPIA = false;

export type MandatoOtConfigPanelMode = "mandato" | "mandatario";

export interface MandatoOtConfigFormProps {
  office: MandateOtConfigView;
  /** Panel: plantilla del mandato (OT) o tipo/mandatario por compañía. */
  mode: MandatoOtConfigPanelMode;
  /** Al abrir desde una tarjeta de compañía, centra el listado en esa empresa. */
  highlightCompanyId?: string | null;
  /**
   * Si se informa, el panel de compañías solo muestra esa empresa. Plataforma no lo pasa: el
   * catálogo sigue listando todas las que radican en el OT.
   */
  lockToCompanyId?: string | null;
  /** Abre el alta de mandatario de esa empresa (hub OT). */
  onRegisterSigner?: (companyTenantId: string) => void;
  /** Tras un alta, recarga el listado de mandatarios del OT sin cerrar el panel. */
  signersRevision?: number;
  /** Mandatario recién creado: se preselecciona como default del OT. */
  lastCreatedSignerId?: string | null;
  onClose: () => void;
  onSaved: (view: MandateOtConfigView) => void;
}

export function MandatoOtConfigForm({
  office,
  mode,
  highlightCompanyId,
  lockToCompanyId,
  onRegisterSigner,
  signersRevision = 0,
  lastCreatedSignerId,
  onClose,
  onSaved,
}: MandatoOtConfigFormProps) {
  const pdfRef = useRef<HTMLInputElement>(null);

  const [view, setView] = useState(office);
  // La ELEGIDA (puede ser "auto"), no la efectiva: si se preseleccionara con la efectiva, abrir y
  // guardar sin tocar nada convertiría un "automática" en una redacción fija.
  const [templateCode, setTemplateCode] = useState(office.configuredTemplateCode || "auto");
  const [family, setFamily] = useState(office.mandataryFamily || "individuo");
  const [instName, setInstName] = useState(office.institutionalMandataryName ?? "");
  const [instNit, setInstNit] = useState(office.institutionalMandataryNit ?? "");
  const [chamberCity, setChamberCity] = useState(office.chamberCity ?? "");
  const [sigla, setSigla] = useState(office.mandatarySigla ?? "");
  const [rowVersion, setRowVersion] = useState(office.rowVersion);
  const [editorBody, setEditorBody] = useState(office.customTemplateBody ?? EDITOR_PLACEHOLDER);
  const [showEditor, setShowEditor] = useState(office.customTemplateKind === "editor");

  const [companyRules, setCompanyRules] = useState<CompanyOtMandateRuleView[]>([]);
  const [otSigners, setOtSigners] = useState<MandateSigner[]>([]);
  const [rulesStatus, setRulesStatus] = useState<"loading" | "ready" | "error">("loading");
  const [savingCompanyId, setSavingCompanyId] = useState<string | null>(null);
  const [companySearch, setCompanySearch] = useState("");
  const [companyTipoFilter, setCompanyTipoFilter] = useState<"all" | MandatoTipoNegocio>("all");
  const [companyPage, setCompanyPage] = useState(1);

  const [saving, setSaving] = useState(false);
  const [previewing, setPreviewing] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [otDefaultSignerId, setOtDefaultSignerId] = useState(office.defaultMandateSignerId ?? "");
  const [hostCompanyId, setHostCompanyId] = useState("");

  const hasCustom = view.hasCustomTemplate;
  // Redacción que se emite hoy: con "auto" elegido, la del sistema para este organismo.
  const effectiveTemplate = view.templateCode || "generico";
  const terceroAjeno = terceroAjenoEnPlantilla(templateCode, office.code);
  const showInstitutionalMeta =
    effectiveTemplate === "sabaneta" ||
    effectiveTemplate === "bello" ||
    family === "organismo_transito";

  const filteredCompanyRules = useMemo(() => {
    const scoped = lockToCompanyId
      ? companyRules.filter((row) => row.companyTenantId === lockToCompanyId)
      : companyRules;
    const q = companySearch.trim().toLowerCase();
    return scoped.filter((row) => {
      const tipo = resolveTipoNegocio(row.assignmentMode);
      if (companyTipoFilter !== "all" && tipo !== companyTipoFilter) return false;
      if (!q) return true;
      return row.companyName.toLowerCase().includes(q);
    });
  }, [companyRules, companySearch, companyTipoFilter, lockToCompanyId]);

  const companyLastPage = Math.max(1, Math.ceil(filteredCompanyRules.length / COMPANY_PAGE_SIZE));
  const safeCompanyPage = Math.min(companyPage, companyLastPage);
  const companyPageRows = useMemo(
    () =>
      filteredCompanyRules.slice(
        (safeCompanyPage - 1) * COMPANY_PAGE_SIZE,
        safeCompanyPage * COMPANY_PAGE_SIZE,
      ),
    [filteredCompanyRules, safeCompanyPage],
  );

  const explicitRulesCount = useMemo(
    () => companyRules.filter((r) => r.hasExplicitRule).length,
    [companyRules],
  );

  const loadCompanyRules = useCallback(async () => {
    setRulesStatus("loading");
    setError(null);
    try {
      const [items, signers] = await Promise.all([
        listCompanyOtMandateRules(office.officeId),
        fetchMandateSigners(office.officeId).catch(() => [] as MandateSigner[]),
      ]);
      setCompanyRules(items);
      setOtSigners(signers.filter((s) => s.isActive));
      if (highlightCompanyId) {
        const focused = items.find((row) => row.companyTenantId === highlightCompanyId);
        if (focused) {
          setCompanySearch(focused.companyName);
          setCompanyPage(1);
        }
      }
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
  }, [office.officeId, highlightCompanyId]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API
    void loadCompanyRules();
  }, [loadCompanyRules, signersRevision]);

  useEffect(() => {
    if (!hostCompanyId && companyRules[0]) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- preselección al cargar empresas
      setHostCompanyId(companyRules[0].companyTenantId);
    }
  }, [companyRules, hostCompanyId]);

  useEffect(() => {
    if (lastCreatedSignerId) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- alta desde este panel
      setOtDefaultSignerId(lastCreatedSignerId);
    }
  }, [lastCreatedSignerId]);

  const applyView = (next: MandateOtConfigView) => {
    setView(next);
    setRowVersion(next.rowVersion);
    setTemplateCode(next.configuredTemplateCode || "auto");
    setFamily(next.mandataryFamily || "individuo");
    setInstName(next.institutionalMandataryName ?? "");
    setInstNit(next.institutionalMandataryNit ?? "");
    setChamberCity(next.chamberCity ?? "");
    setSigla(next.mandatarySigla ?? "");
    setOtDefaultSignerId(next.defaultMandateSignerId ?? "");
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
    defaultMandateSignerId: otDefaultSignerId || null,
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
      onClose();
    } catch (err) {
      setError(messageFromSaveError(err, "No se pudo guardar la configuración del OT."));
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
      onClose();
    } catch (err) {
      setError(
        messageFromSaveError(err, "No se pudo subir la plantilla PDF. Debe ser un PDF válido (máx. 10 MB)."),
      );
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
      onClose();
    } catch (err) {
      setError(messageFromSaveError(err, "No se pudo guardar el editor de plantilla."));
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
    } catch (err) {
      setError(messageFromSaveError(err, "No se pudo quitar la plantilla propia."));
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
      // Volver a Persona/RL sin default ⇒ quitar regla (default implícito).
      if (mode === "signer" && !row.hasExplicitRule && !row.defaultMandateSignerId) {
        return;
      }
      if (mode === "signer" && row.hasExplicitRule && !row.defaultMandateSignerId) {
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
        defaultMandateSignerId: mode === "signer" ? row.defaultMandateSignerId : null,
      });
      await loadCompanyRules();
    } catch {
      setError("No se pudo guardar el tipo de mandato para esa compañía.");
    } finally {
      setSavingCompanyId(null);
    }
  };

  const handleDefaultSignerChange = async (
    row: CompanyOtMandateRuleView,
    nextSignerId: string,
  ) => {
    setError(null);
    setSavingCompanyId(row.companyTenantId);
    try {
      const tipo = resolveTipoNegocio(row.assignmentMode);
      if (tipo !== "persona_rl") return;

      const signerId = nextSignerId.trim() || null;
      // Sin default y sin otros motivos de regla ⇒ volver al implícito.
      if (!signerId && row.hasExplicitRule) {
        await deleteCompanyOtMandateRule(office.officeId, row.companyTenantId);
        await loadCompanyRules();
        return;
      }
      if (!signerId && !row.hasExplicitRule) return;

      await upsertCompanyOtMandateRule(office.officeId, row.companyTenantId, {
        assignmentMode: "signer",
        mandataryFamily: suggestedFamilyForTipo("persona_rl", templateCode),
        institutionalMandataryName: null,
        institutionalMandataryNit: null,
        chamberCity: row.chamberCity || chamberCity || null,
        mandatarySigla: row.mandatarySigla || sigla || null,
        defaultMandateSignerId: signerId,
      });
      await loadCompanyRules();
    } catch {
      setError("No se pudo guardar el mandatario por defecto. Verifica que esté activo en este OT.");
    } finally {
      setSavingCompanyId(null);
    }
  };

  const signersForCompany = (companyTenantId: string) =>
    otSigners.filter((s) => {
      const inCompany = s.companyTenantIds?.includes(companyTenantId);
      if (!inCompany) return false;
      const offices = s.transitOfficeIds?.length ? s.transitOfficeIds : [s.transitOfficeId];
      return offices.includes(office.officeId);
    });

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

  const companyColumns: DataTableColumn<CompanyOtMandateRuleView>[] = useMemo(
    () => [
      {
        key: "company",
        header: "Compañía",
        cellClassName: "!px-2.5",
        headerClassName: "!px-2.5",
        render: (row) => {
          const rowBusy = savingCompanyId === row.companyTenantId;
          return (
            <div className="min-w-0">
              <p className="truncate text-sm font-medium text-[#162244] dark:text-white">
                {row.companyName}
              </p>
              <p
                className="truncate text-[11px] text-[#59677D] dark:text-white/50"
                title={
                  rowBusy
                    ? "Guardando cambios…"
                    : row.hasExplicitRule
                      ? "Esta compañía tiene una regla propia de mandato para este OT (tipo o mandatario default distinto al implícito)."
                      : "Sin regla propia: usa Persona/RL por defecto del sistema para este OT."
                }
              >
                {rowBusy ? "Guardando…" : row.hasExplicitRule ? "Regla propia" : "Default"}
              </p>
            </div>
          );
        },
      },
      {
        key: "tipo",
        header: "Tipo",
        cellClassName: "!px-2.5 w-[30%]",
        headerClassName: "!px-2.5",
        render: (row) => {
          const tipo = resolveTipoNegocio(row.assignmentMode);
          const rowBusy = savingCompanyId === row.companyTenantId;
          return (
            <select
              value={tipo}
              disabled={busy || rowBusy}
              onChange={(e) =>
                void handleCompanyTipoChange(row, e.target.value as MandatoTipoNegocio)
              }
              className="w-full max-w-full rounded-lg border border-[#DFE5ED] bg-white px-1.5 py-1.5 text-[11px] text-[#162244] disabled:opacity-50 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
              aria-label={`Tipo de mandato para ${row.companyName}`}
              data-testid={`mandato-company-tipo-${row.companyTenantId}`}
            >
              {MANDATO_TIPOS.map((t) => (
                <option key={t.value} value={t.value}>
                  {t.label}
                </option>
              ))}
            </select>
          );
        },
      },
      {
        key: "defaultSigner",
        header: "Mandatario default",
        cellClassName: "!px-2.5 w-[32%]",
        headerClassName: "!px-2.5",
        render: (row) => {
          const tipo = resolveTipoNegocio(row.assignmentMode);
          const rowBusy = savingCompanyId === row.companyTenantId;
          const candidates = signersForCompany(row.companyTenantId);
          const defaultValue = row.defaultMandateSignerId ?? "";
          if (tipo !== "persona_rl") {
            return (
              <span className="text-xs text-[#59677D] dark:text-white/45">No aplica</span>
            );
          }
          return (
            <div className="flex flex-col gap-1">
              <select
                value={defaultValue}
                disabled={busy || rowBusy || candidates.length === 0}
                onChange={(e) => void handleDefaultSignerChange(row, e.target.value)}
                className="w-full max-w-full rounded-lg border border-[#DFE5ED] bg-white px-1.5 py-1.5 text-xs text-[#162244] disabled:opacity-50 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
                aria-label={`Mandatario por defecto para ${row.companyName}`}
                data-testid={`mandato-company-default-signer-${row.companyTenantId}`}
              >
                <option value="">
                  {candidates.length === 0 ? "Sin mandatarios" : "Sin default"}
                </option>
                {candidates.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.fullName}
                  </option>
                ))}
              </select>
              {onRegisterSigner ? (
                <button
                  type="button"
                  className="text-left text-xs font-semibold text-[#557EFF] underline-offset-2 hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
                  onClick={() => onRegisterSigner(row.companyTenantId)}
                >
                  Registrar mandatario
                </button>
              ) : null}
            </div>
          );
        },
      },
    ],
    // Handlers son estables por cierre de render; deps cubren estado que cambia las celdas.
    // eslint-disable-next-line react-hooks/exhaustive-deps -- handlers del mismo render
    [busy, savingCompanyId, otSigners, office.officeId, templateCode, onRegisterSigner],
  );

  return (
    <OtSidePanel
      open
      title={mode === "mandatario" ? "Configurar mandatario" : "Configurar mandato"}
      ariaLabel={
        mode === "mandatario"
          ? `Configurar mandatario de ${office.name}`
          : `Configurar mandato de ${office.name}`
      }
      onClose={onClose}
      disabled={busy}
      width="xl"
      surface="modal"
      zClassName="z-[60]"
      footer={
        <div className="flex flex-wrap items-center justify-end gap-2">
          {mode === "mandato" ? (
            <>
              <button
                type="button"
                disabled={busy}
                onClick={() => void handlePreview()}
                className="inline-flex items-center gap-1.5 rounded-full border border-[#DFE5ED] bg-white px-4 py-2 text-xs font-semibold text-[#162244] disabled:opacity-50 dark:border-white/15 dark:bg-transparent dark:text-white"
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
            </>
          ) : (
            <button
              type="button"
              disabled={busy}
              onClick={onClose}
              className="rounded-full px-4 py-2 text-xs font-semibold text-white disabled:opacity-50"
              style={{ background: "linear-gradient(90deg,#557EFF 0%,#00DBD5 100%)" }}
            >
              Cerrar
            </button>
          )}
        </div>
      }
    >
      <div className="space-y-4" data-testid="mandato-ot-config-form" data-mode={mode}>
        <p className="text-xs text-[#59677D] dark:text-white/65">
          {office.name} · <span className="font-mono">{office.code}</span>
        </p>
        <p className="text-[11px] leading-relaxed text-[#59677D] dark:text-white/65">
          Convive con Plataforma → Mandatos (mismos datos). El modelo de cada empresa que radica
          aplica a todas las familias. Mandato cliente = Persona o RL + plantilla genérica.
        </p>

        {error ? (
          <p
            role="alert"
            className="rounded-xl border border-[#FF4E00]/40 bg-[rgba(255,78,0,0.06)] px-3 py-2 text-xs text-[#FF4E00]"
          >
            {error}
          </p>
        ) : null}

        {mode === "mandato" ? (
          <>
        <section className="space-y-3" aria-labelledby="mandato-plantilla-heading">
            <h3
              id="mandato-plantilla-heading"
              className="text-xs font-semibold text-[#162244] dark:text-white"
            >
              Plantilla del mandato (por OT)
            </h3>

            <label className="block space-y-1.5">
              <span className="text-xs font-semibold text-[#162244] dark:text-white">
                Redacción que aplica este OT
              </span>
              <select
                value={templateCode}
                onChange={(e) => setTemplateCode(e.target.value)}
                disabled={busy}
                data-testid="mandato-template-select"
                className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm text-[#162244] disabled:opacity-50 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
              >
                {mandatoTemplateOptions().map((opt) => (
                  <option key={opt.code} value={opt.code}>
                    {opt.label}
                  </option>
                ))}
              </select>
              <span className="block text-[11px] leading-relaxed text-[#59677D] dark:text-white/65">
                {mandatoTemplateOptions().find((o) => o.code === templateCode)?.summary ?? ""}
              </span>
            </label>

            <label className="block space-y-1.5">
              <span className="text-xs font-semibold text-[#162244] dark:text-white">
                Mandatario por defecto de este OT
              </span>
              <select
                value={otDefaultSignerId}
                onChange={(e) => setOtDefaultSignerId(e.target.value)}
                disabled={busy}
                data-testid="mandato-ot-default-signer"
                className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm text-[#162244] disabled:opacity-50 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
              >
                <option value="">
                  {otSigners.length === 0 ? "Sin mandatarios" : "Sin default (vacío al nacer)"}
                </option>
                {otSigners.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.fullName}
                  </option>
                ))}
              </select>
              <span className="block text-[11px] leading-relaxed text-[#59677D] dark:text-white/65">
                Si la empresa no tiene mandatario propio en este OT, los trámites usan esta persona
                (aunque no esté vinculada a esa empresa). El default cliente×OT prima sobre este. Sin
                ninguno de los dos, el mandato sale vacío de firmante persona.
              </span>
              {onRegisterSigner ? (
                <div className="flex flex-col gap-1.5 pt-1">
                  {companyRules.length > 1 ? (
                    <label className="block space-y-1">
                      <span className="text-[11px] font-semibold text-[#162244] dark:text-white">
                        Empresa que registra a la persona
                      </span>
                      <select
                        value={hostCompanyId}
                        onChange={(e) => setHostCompanyId(e.target.value)}
                        disabled={busy}
                        data-testid="mandato-ot-register-host-company"
                        className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm text-[#162244] disabled:opacity-50 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
                      >
                        {companyRules.map((row) => (
                          <option key={row.companyTenantId} value={row.companyTenantId}>
                            {row.companyName}
                          </option>
                        ))}
                      </select>
                    </label>
                  ) : null}
                  <button
                    type="button"
                    className="w-fit text-left text-xs font-semibold text-[#557EFF] underline-offset-2 hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:opacity-50"
                    disabled={busy}
                    data-testid="mandato-ot-register-signer"
                    onClick={() => {
                      const companyId = hostCompanyId || companyRules[0]?.companyTenantId;
                      if (!companyId) {
                        setError(
                          "Para registrar el mandatario del OT hace falta al menos una empresa habilitada en este organismo.",
                        );
                        return;
                      }
                      onRegisterSigner(companyId);
                    }}
                  >
                    Registrar mandatario
                  </button>
                </div>
              ) : null}
            </label>

            {/* HU #11718 — la redacción elegida puede nombrar a un tercero ajeno al organismo:
                las plantillas del sistema llevan su municipio y su mandatario institucional
                quemados. Advierte, no bloquea: restringir contradiría la libertad que introdujo
                el Feature #11702. */}
            {terceroAjeno ? (
              <div
                className="rounded-2xl border px-4 py-3"
                style={{ borderColor: "#F9AC00", background: "rgba(249,172,0,0.08)" }}
                data-testid="mandato-template-ajena-warning"
                role="alert"
              >
                <p className="text-[11px] font-semibold leading-relaxed text-[#8a6000]">
                  Esta redacción es de otro organismo.
                </p>
                <p className="mt-1 text-[11px] leading-relaxed text-[#8a6000]">
                  El contrato de {office.name} quedaría nombrando a{" "}
                  <span className="font-semibold">{terceroAjeno}</span>, que no interviene en sus
                  trámites. Puedes guardarlo igual si es lo que quieres.
                </p>
              </div>
            ) : null}

            {templateCode === "auto" ? (
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
                    {systemTemplateLabel(effectiveTemplate)}
                  </span>
                </div>
                <p className="mt-2 text-[11px] leading-relaxed text-[#59677D] dark:text-white/65">
                  En automática, este OT emite hoy la redacción{" "}
                  <span className="font-semibold text-[#162244] dark:text-white">
                    {systemTemplateLabel(effectiveTemplate)}
                  </span>{" "}
                  para todas las compañías.
                </p>
              </div>
            ) : null}

            {hasCustom ? (
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
                  Este OT tiene un documento propio cargado y es el que se usa en el trámite, por
                  encima de la redacción seleccionada arriba. Las firmas van al pie con layout FLIT.
                </p>
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => void handleRemoveCustom()}
                  className="mt-2.5 inline-flex items-center gap-1 text-[11px] font-semibold text-[#FF4E00] disabled:opacity-50"
                >
                  <Trash2 className="h-3 w-3" aria-hidden="true" />
                  Quitar y volver a {systemTemplateLabel(effectiveTemplate)}
                </button>
              </div>
            ) : null}

            {MOSTRAR_PLANTILLA_PROPIA ? (
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
            ) : null}
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
          </>
        ) : (
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
                  Sin regla propia la empresa usa el modelo del organismo. En Persona/RL puedes
                  fijar un mandatario preferido (preselección en el paso FUR).{" "}
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
              <div data-testid="mandato-company-rules-list" className="flex flex-col gap-2">
                {!lockToCompanyId ? (
                <div className="flex flex-wrap items-center gap-2">
                  <label className="relative min-w-[10rem] flex-1">
                    <span className="sr-only">Buscar compañía</span>
                    <Search
                      className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-[#59677D]"
                      aria-hidden="true"
                    />
                    <input
                      value={companySearch}
                      onChange={(e) => {
                        setCompanySearch(e.target.value);
                        setCompanyPage(1);
                      }}
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
                      onChange={(e) => {
                        setCompanyTipoFilter(e.target.value as "all" | MandatoTipoNegocio);
                        setCompanyPage(1);
                      }}
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
                ) : null}

                <DataTable
                  columns={companyColumns}
                  rows={companyPageRows}
                  getRowKey={(row) => row.companyTenantId}
                  emptyMessage="Ninguna compañía coincide con la búsqueda o el filtro."
                  ariaLabel="Reglas de mandato por compañía"
                  allowHorizontalScroll={false}
                  pagination={{
                    page: safeCompanyPage,
                    pageSize: COMPANY_PAGE_SIZE,
                    totalCount: filteredCompanyRules.length,
                    onPageChange: setCompanyPage,
                  }}
                />
              </div>
            ) : null}
          </section>
        )}
      </div>
    </OtSidePanel>
  );
}

function messageFromSaveError(err: unknown, fallback: string): string {
  if (!(err instanceof ApiError)) return fallback;
  if (err.status === 409) {
    return "La configuración fue modificada en otro lugar. Cierra el panel, vuelve a abrirlo e inténtalo de nuevo.";
  }
  const code =
    err.body && typeof err.body === "object" && "error" in err.body
      ? String((err.body as { error?: unknown }).error ?? "")
      : "";
  switch (code) {
    case "template_code_invalido":
      return "El código de plantilla no es válido.";
    case "mandatary_family_invalida":
      return "La familia de mandatario no es válida.";
    case "assignment_mode_invalido":
      return "El modo de asignación no es válido.";
    case "mandatario_institucional_requerido":
      return "El nombre del mandatario institucional es obligatorio.";
    case "plantilla_pdf_invalida":
      return "El PDF de plantilla no es válido.";
    case "editor_cuerpo_invalido":
      return "El cuerpo del editor no es válido.";
    case "row_version_conflict":
      return "La configuración fue modificada en otro lugar. Cierra el panel, vuelve a abrirlo e inténtalo de nuevo.";
    default:
      if (err.status === 404) return "No se encontró el organismo de tránsito.";
      if (err.status >= 500) return "Error del servidor al guardar. Revisa que la API esté actualizada.";
      return fallback;
  }
}

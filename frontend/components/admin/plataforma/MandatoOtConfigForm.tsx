"use client";

import { useId, useRef, useState } from "react";
import { Eye, Upload, X } from "lucide-react";
import {
  extractMandateConfigFromFile,
  fetchMandateOtPreview,
  fetchMandatoTemplatePreview,
  upsertMandateOtConfig,
  type MandateOtConfigView,
  type UpsertMandateOtConfigBody,
} from "@/lib/api/admin-plataforma-mandatos";
import { openPdfBlobInNewTab } from "@/lib/documents/open-document-tab";
import type { MandatoTemplateCode } from "@/lib/plataforma/mandato-templates";

const TEMPLATES: { value: MandatoTemplateCode; label: string }[] = [
  { value: "generico", label: "Genérico" },
  { value: "sabaneta", label: "Sabaneta (UT-SETSA)" },
  { value: "bello", label: "Bello (UT-MAB)" },
];

export interface MandatoOtConfigFormProps {
  office: MandateOtConfigView;
  onClose: () => void;
  onSaved: (view: MandateOtConfigView) => void;
}

export function MandatoOtConfigForm({ office, onClose, onSaved }: MandatoOtConfigFormProps) {
  const titleId = useId();
  const fileRef = useRef<HTMLInputElement>(null);

  const [templateCode, setTemplateCode] = useState(office.templateCode || "generico");
  const [family, setFamily] = useState(office.mandataryFamily || "individuo");
  const [instName, setInstName] = useState(office.institutionalMandataryName ?? "");
  const [instNit, setInstNit] = useState(office.institutionalMandataryNit ?? "");
  const [chamberCity, setChamberCity] = useState(office.chamberCity ?? "");
  const [sigla, setSigla] = useState(office.mandatarySigla ?? "");
  const [rowVersion, setRowVersion] = useState(office.rowVersion);

  /** Archivo de referencia cargado: Vista previa lo abre tal cual. */
  const [referenceFile, setReferenceFile] = useState<File | null>(null);

  const [saving, setSaving] = useState(false);
  const [previewing, setPreviewing] = useState(false);
  const [extracting, setExtracting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [extractNote, setExtractNote] = useState<string | null>(null);

  const showInstitutional =
    family === "organismo_transito" || templateCode === "sabaneta" || templateCode === "bello";

  const buildBody = (): UpsertMandateOtConfigBody => ({
    templateCode,
    // Legacy API field: el mandato aplica siempre (PN y PJ).
    requiresForNaturalPerson: true,
    mandataryFamily: family,
    institutionalMandataryName: showInstitutional ? instName || null : null,
    institutionalMandataryNit: showInstitutional ? instNit || null : null,
    chamberCity: chamberCity || null,
    mandatarySigla: sigla || null,
    rowVersion,
  });

  const handleSave = async () => {
    setError(null);
    if (showInstitutional && !instName.trim()) {
      setError("El nombre del mandatario institucional es obligatorio para esta familia.");
      return;
    }
    setSaving(true);
    try {
      const saved = await upsertMandateOtConfig(office.officeId, buildBody());
      setRowVersion(saved.rowVersion);
      onSaved(saved);
    } catch {
      setError("No se pudo guardar la configuración. Verifica permisos SuperAdmin e inténtalo de nuevo.");
    } finally {
      setSaving(false);
    }
  };

  const handlePreview = async () => {
    setError(null);
    setPreviewing(true);
    try {
      // Si hay archivo de referencia cargado → mostrar ESE documento (no el PDF FLIT genérico).
      if (referenceFile) {
        await openPdfBlobInNewTab(async () => {
          const type =
            referenceFile.type ||
            (referenceFile.name.toLowerCase().endsWith(".pdf")
              ? "application/pdf"
              : "application/octet-stream");
          return new Blob([await referenceFile.arrayBuffer()], { type });
        });
        return;
      }

      await openPdfBlobInNewTab(async () => {
        try {
          return await fetchMandateOtPreview(office.officeId);
        } catch {
          return fetchMandatoTemplatePreview(templateCode);
        }
      });
    } catch {
      setError(
        referenceFile
          ? "No se pudo abrir el archivo cargado."
          : "No se pudo abrir la vista previa del mandato FLIT.",
      );
    } finally {
      setPreviewing(false);
    }
  };

  const handleExtract = async (file: File) => {
    setError(null);
    setExtractNote(null);
    setExtracting(true);
    setReferenceFile(file);
    try {
      const extracted = await extractMandateConfigFromFile(file);
      setTemplateCode(extracted.suggestedTemplateCode || "generico");
      setFamily(extracted.mandataryFamily || "individuo");
      setInstName(extracted.institutionalMandataryName ?? "");
      setInstNit(extracted.institutionalMandataryNit ?? "");
      setChamberCity(extracted.chamberCity ?? "");
      setSigla(extracted.mandatarySigla ?? "");
      setExtractNote(
        extracted.notes?.trim()
          ? `Datos extraídos. ${extracted.notes} «Vista previa» abre el archivo que cargaste; al guardar, el mandato oficial del trámite usará el diseño FLIT.`
          : "Datos extraídos. «Vista previa» abre el archivo que cargaste; al guardar, el mandato oficial del trámite usará el diseño FLIT.",
      );
    } catch {
      // Conservamos el archivo para poder previsualizarlo aunque falle el OCR.
      setError(
        "No se pudo extraer información automáticamente. Puedes ver el archivo con «Vista previa» y completar los campos a mano.",
      );
    } finally {
      setExtracting(false);
      if (fileRef.current) fileRef.current.value = "";
    }
  };

  const clearReferenceFile = () => {
    setReferenceFile(null);
    setExtractNote(null);
    if (fileRef.current) fileRef.current.value = "";
  };

  const busy = saving || previewing || extracting;

  return (
    <div
      className="fixed inset-0 z-50 flex items-end justify-center bg-[#162744]/40 p-4 sm:items-center"
      role="presentation"
      onClick={(e) => {
        if (e.target === e.currentTarget && !busy) onClose();
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="flex max-h-[min(90vh,40rem)] w-full max-w-lg flex-col overflow-hidden rounded-2xl border border-[#DFE5ED] bg-white shadow-xl dark:border-white/10 dark:bg-[#0B0F14]"
        data-testid="mandato-ot-config-form"
      >
        <header className="flex items-start justify-between gap-3 border-b border-[#DFE5ED] px-5 py-4 dark:border-white/10">
          <div>
            <h2 id={titleId} className="text-base font-semibold text-[#162744] dark:text-white">
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
            <p role="alert" className="rounded-xl border border-[#FF4E00]/40 bg-[rgba(255,78,0,0.06)] px-3 py-2 text-xs text-[#FF4E00]">
              {error}
            </p>
          ) : null}
          {extractNote ? (
            <p role="status" className="rounded-xl border border-[#557EFF]/30 bg-[#557EFF]/5 px-3 py-2 text-xs text-[#162744] dark:text-white/80">
              {extractNote}
            </p>
          ) : null}

          <label className="block space-y-1.5">
            <span className="text-xs font-semibold text-[#162744] dark:text-white">Plantilla</span>
            <select
              value={templateCode}
              onChange={(e) => setTemplateCode(e.target.value)}
              disabled={busy}
              className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
            >
              {TEMPLATES.map((t) => (
                <option key={t.value} value={t.value}>
                  {t.label}
                </option>
              ))}
            </select>
          </label>

          <label className="block space-y-1.5">
            <span className="text-xs font-semibold text-[#162744] dark:text-white">Familia del mandatario</span>
            <select
              value={family}
              onChange={(e) => setFamily(e.target.value)}
              disabled={busy}
              className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
            >
              <option value="individuo">Individuo</option>
              <option value="organismo_transito">Organismo de tránsito</option>
            </select>
          </label>

          {showInstitutional ? (
            <div className="space-y-3 rounded-xl border border-[#DFE5ED] bg-[#F8FAFC] p-3 dark:border-white/10 dark:bg-white/5">
              <label className="block space-y-1.5">
                <span className="text-xs font-semibold text-[#162744] dark:text-white">
                  Mandatario institucional
                </span>
                <input
                  value={instName}
                  onChange={(e) => setInstName(e.target.value)}
                  disabled={busy}
                  className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
                />
              </label>
              <label className="block space-y-1.5">
                <span className="text-xs font-semibold text-[#162744] dark:text-white">NIT</span>
                <input
                  value={instNit}
                  onChange={(e) => setInstNit(e.target.value)}
                  disabled={busy}
                  className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 font-mono text-sm dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
                />
              </label>
              <div className="grid gap-3 sm:grid-cols-2">
                <label className="block space-y-1.5">
                  <span className="text-xs font-semibold text-[#162744] dark:text-white">Ciudad cámara</span>
                  <input
                    value={chamberCity}
                    onChange={(e) => setChamberCity(e.target.value)}
                    disabled={busy}
                    className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
                  />
                </label>
                <label className="block space-y-1.5">
                  <span className="text-xs font-semibold text-[#162744] dark:text-white">Sigla</span>
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

          <div className="rounded-xl border border-dashed border-[#DFE5ED] p-3 dark:border-white/15">
            <p className="text-xs font-semibold text-[#162744] dark:text-white">Cargar mandato de referencia</p>
            <p className="mt-1 text-[11px] text-[#59677D] dark:text-white/65">
              Se extraen datos al formulario. Con archivo cargado, «Vista previa» muestra ese documento;
              sin archivo, muestra el PDF FLIT de la plantilla.
            </p>
            <input
              ref={fileRef}
              type="file"
              accept="application/pdf,image/png,image/jpeg,image/webp"
              className="sr-only"
              disabled={busy}
              onChange={(e) => {
                const f = e.target.files?.[0];
                if (f) void handleExtract(f);
              }}
            />
            <div className="mt-2 flex flex-wrap items-center gap-2">
              <button
                type="button"
                disabled={busy}
                onClick={() => fileRef.current?.click()}
                className="inline-flex items-center gap-1.5 rounded-full border border-[#DFE5ED] px-3 py-1.5 text-[11px] font-semibold text-[#162744] hover:bg-[#F4F7FC] disabled:opacity-50 dark:border-white/15 dark:text-white dark:hover:bg-white/5"
              >
                <Upload className="h-3.5 w-3.5" aria-hidden="true" />
                {extracting ? "Extrayendo…" : referenceFile ? "Cambiar archivo" : "Seleccionar archivo"}
              </button>
              {referenceFile ? (
                <>
                  <span
                    className="max-w-[12rem] truncate text-[11px] text-[#59677D] dark:text-white/65"
                    title={referenceFile.name}
                    data-testid="mandato-reference-file-name"
                  >
                    {referenceFile.name}
                  </span>
                  <button
                    type="button"
                    disabled={busy}
                    onClick={clearReferenceFile}
                    className="text-[11px] font-semibold text-[#FF4E00] disabled:opacity-50"
                  >
                    Quitar
                  </button>
                </>
              ) : null}
            </div>
          </div>
        </div>

        <footer className="flex flex-wrap items-center justify-end gap-2 border-t border-[#DFE5ED] px-5 py-3 dark:border-white/10">
          <button
            type="button"
            disabled={busy}
            onClick={() => void handlePreview()}
            className="inline-flex items-center gap-1.5 rounded-full border border-[#DFE5ED] px-4 py-2 text-xs font-semibold text-[#162744] disabled:opacity-50 dark:border-white/15 dark:text-white"
            aria-label={
              referenceFile
                ? "Vista previa del archivo de referencia cargado"
                : "Vista previa del mandato FLIT"
            }
          >
            <Eye className="h-3.5 w-3.5" aria-hidden="true" />
            {previewing
              ? "Abriendo…"
              : referenceFile
                ? "Vista previa (archivo)"
                : "Vista previa (FLIT)"}
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
            onClick={() => void handleSave()}
            className="rounded-full px-4 py-2 text-xs font-semibold text-white disabled:opacity-50"
            style={{ background: "linear-gradient(90deg,#557EFF 0%,#00DBD5 100%)" }}
          >
            {saving ? "Guardando…" : "Guardar"}
          </button>
        </footer>
      </div>
    </div>
  );
}

"use client";

import { useCallback, useEffect, useId, useRef, useState } from "react";
import { AlertTriangle, Eye, FileSignature, History } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import {
  getMandatoFormat,
  getMandatoFormatVersion,
  previewMandatoFormatDraft,
  readFormatError,
  updateMandatoFormat,
  type MandatoFormatDetail,
  type MandatoFormatView,
  type MandatoUnknownVariable,
  type UpdateMandatoFormatBody,
} from "@/lib/api/admin-plataforma-mandatos";
import { ApiError } from "@/lib/api/types";
import { openPdfBlobInNewTab } from "@/lib/documents/open-document-tab";
import { formatFechaHora } from "@/lib/format/date";
import {
  MANDATO_TIPOS,
  resolveAssignmentMode,
  resolveTipoNegocio,
  tipoNegocioLabel,
  type MandatoTipoNegocio,
} from "@/lib/plataforma/mandato-templates";
import {
  MANDATO_FORMATO_ERRORES,
  MANDATO_VARIABLES_INSERTABLES,
} from "@/lib/plataforma/mandato-variables";

const FIELD =
  "w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm text-[#162244] disabled:opacity-50 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white";
const BTN_OUTLINE =
  "rounded-xl border px-4 py-2 text-xs font-semibold disabled:opacity-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]";
const BTN_PRIMARY =
  "rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2";
const GRADIENT = { background: "linear-gradient(135deg,#557EFF,#00DBD5)" };

export interface MandatoFormatoEditorProps {
  /** Código del formato a editar (el catálogo no permite crear ni eliminar). */
  code: string;
  /** Tras guardar: el panel recarga el catálogo y avisa. */
  onSaved: (format: MandatoFormatView, info: { published: number | null; changed: boolean }) => void;
  /** El PUT respondió 409: el panel recarga la fila del catálogo. */
  onConflict: () => void;
  onClose: () => void;
}

type LoadState = "loading" | "ready" | "error";

/** HU #13175 — edición del nombre, el tipo de mandato y la plantilla de un formato existente. */
export function MandatoFormatoEditor({ code, onSaved, onConflict, onClose }: MandatoFormatoEditorProps) {
  const uid = useId();
  const bodyRef = useRef<HTMLTextAreaElement>(null);
  const [state, setState] = useState<LoadState>("loading");
  const [detail, setDetail] = useState<MandatoFormatDetail | null>(null);
  const [name, setName] = useState("");
  const [tipo, setTipo] = useState<MandatoTipoNegocio>("persona_rl");
  const [body, setBody] = useState("");
  const [saving, setSaving] = useState(false);
  const [previewing, setPreviewing] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const [unknownVars, setUnknownVars] = useState<MandatoUnknownVariable[]>([]);
  const [showHistory, setShowHistory] = useState(false);
  const [oldVersion, setOldVersion] = useState<{ number: number; body: string } | null>(null);
  const [loadingVersion, setLoadingVersion] = useState<number | null>(null);

  const load = useCallback(
    async (keepDrafts: boolean) => {
      if (!keepDrafts) setState("loading");
      try {
        const d = await getMandatoFormat(code);
        setDetail(d);
        if (!keepDrafts) {
          setName(d.format.name);
          setTipo(resolveTipoNegocio(d.format.assignmentMode));
          setBody(d.body ?? "");
        }
        setState("ready");
      } catch {
        if (!keepDrafts) setState("error");
        else setError("No se pudo recargar el formato. Cierra e inténtalo de nuevo.");
      }
    },
    [code],
  );

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API
    void load(false);
  }, [load]);

  const format = detail?.format ?? null;
  const baseline = {
    name: format?.name ?? "",
    tipo: resolveTipoNegocio(format?.assignmentMode),
    body: (detail?.body ?? "").trim(),
  };
  const redaction = format?.selectableAsRedaction ?? false;
  const nameChanged = name.trim() !== baseline.name;
  const tipoChanged = tipo !== baseline.tipo;
  const bodyChanged =
    redaction && body.trim() !== baseline.body && (body.trim() !== "" || baseline.body !== "");
  const dirty = nameChanged || tipoChanged || bodyChanged;
  const busy = saving || previewing;

  const explain = (err: unknown): string => {
    if (err instanceof ApiError) {
      const { error: errorCode, unknownVariables } = readFormatError(err.body);
      setUnknownVars(unknownVariables);
      if (err.status === 409) return MANDATO_FORMATO_ERRORES.row_version_conflict;
      if (err.status === 404) return "El formato ya no existe en el catálogo.";
      if (errorCode && MANDATO_FORMATO_ERRORES[errorCode]) return MANDATO_FORMATO_ERRORES[errorCode];
      return err.message;
    }
    return "No se pudo completar la operación. Inténtalo de nuevo.";
  };

  const send = async () => {
    if (!format) return;
    setConfirming(false);
    setError(null);
    setConflict(false);
    setUnknownVars([]);
    const payload: UpdateMandatoFormatBody = { rowVersion: format.rowVersion };
    if (nameChanged) payload.name = name.trim();
    if (tipoChanged) payload.assignmentMode = resolveAssignmentMode(tipo);
    if (bodyChanged) payload.body = body;
    setSaving(true);
    try {
      const result = await updateMandatoFormat(code, payload);
      onSaved(result.format, { published: result.publishedVersion, changed: result.changed });
    } catch (err) {
      const message = explain(err);
      setError(message);
      if (err instanceof ApiError && err.status === 409) {
        // Se recarga la fila y la versión vigente; lo escrito se conserva para no perderlo.
        setConflict(true);
        onConflict();
        await load(true);
      }
    } finally {
      setSaving(false);
    }
  };

  const handlePublish = () => {
    if (!dirty) return;
    if (bodyChanged) {
      setConfirming(true);
      return;
    }
    void send();
  };

  const handlePreview = async () => {
    setError(null);
    setUnknownVars([]);
    setPreviewing(true);
    try {
      await openPdfBlobInNewTab(() => previewMandatoFormatDraft(code, body));
    } catch (err) {
      setError(explain(err));
    } finally {
      setPreviewing(false);
    }
  };

  const insertVariable = (variable: string) => {
    const token = `{{${variable}}}`;
    const el = bodyRef.current;
    const start = el?.selectionStart ?? body.length;
    const end = el?.selectionEnd ?? body.length;
    const next = body.slice(0, start) + token + body.slice(end);
    setBody(next);
    requestAnimationFrame(() => {
      el?.focus();
      el?.setSelectionRange(start + token.length, start + token.length);
    });
  };

  const openVersion = async (versionNumber: number) => {
    setLoadingVersion(versionNumber);
    try {
      const v = await getMandatoFormatVersion(code, versionNumber);
      setOldVersion({ number: versionNumber, body: v.body });
    } catch {
      setError("No se pudo cargar el texto de esa versión.");
    } finally {
      setLoadingVersion(null);
    }
  };

  const title = format ? `Editar formato: ${format.name}` : "Editar formato";

  return (
    <>
      <Modal
        open
        onClose={onClose}
        busy={busy}
        icon={FileSignature}
        iconBg="#557EFF"
        title={title}
        titleClassName="text-base font-bold text-[#162744]"
        size="xl"
      >
        <div className="space-y-4 text-xs" data-testid="mandato-formato-editor">
          {state === "loading" ? (
            <p
              role="status"
              aria-live="polite"
              data-testid="mandato-formato-loading"
              className="text-[#59677D]"
            >
              Cargando el formato…
            </p>
          ) : state === "error" || !format ? (
            <div
              role="alert"
              className="flex flex-wrap items-center gap-2 text-[#FF4E00]"
              data-testid="mandato-formato-error"
            >
              <span>No se pudo cargar el formato.</span>
              <button type="button" onClick={() => void load(false)} className="font-semibold underline">
                Reintentar
              </button>
            </div>
          ) : (
            <>
              <div className="grid gap-3 sm:grid-cols-2">
                <label className="block space-y-1.5" htmlFor={`${uid}-nombre`}>
                  <span className="font-semibold text-[#162244]">Nombre del formato</span>
                  <input
                    id={`${uid}-nombre`}
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    disabled={busy}
                    required
                    aria-required="true"
                    maxLength={200}
                    className={FIELD}
                  />
                </label>
                <label className="block space-y-1.5" htmlFor={`${uid}-tipo`}>
                  <span className="font-semibold text-[#162244]">Tipo de mandato</span>
                  <select
                    id={`${uid}-tipo`}
                    value={tipo}
                    onChange={(e) => setTipo(e.target.value as MandatoTipoNegocio)}
                    disabled={busy}
                    className={FIELD}
                    data-testid="mandato-formato-tipo"
                  >
                    {MANDATO_TIPOS.map((t) => (
                      <option key={t.value} value={t.value}>
                        {tipoNegocioLabel(t.value)}
                      </option>
                    ))}
                  </select>
                </label>
              </div>
              <p className="text-[11px] text-[#59677D]">
                El tipo es el valor por defecto del formato; la regla por compañía y organismo sigue
                mandando sobre él.
              </p>

              {redaction ? (
                <section aria-labelledby={`${uid}-plantilla`} className="space-y-2">
                  <h3 id={`${uid}-plantilla`} className="font-semibold text-[#162244]">
                    Plantilla del contrato{" "}
                    <span className="font-normal text-[#59677D]">
                      {format.currentVersion > 0
                        ? `(versión vigente ${format.currentVersion})`
                        : "(redacción de fábrica)"}
                    </span>
                  </h3>
                  <div
                    role="group"
                    aria-label="Variables permitidas"
                    className="flex flex-wrap gap-1.5"
                    data-testid="mandato-formato-variables"
                  >
                    {MANDATO_VARIABLES_INSERTABLES.map((v) => (
                      <button
                        key={v.name}
                        type="button"
                        title={v.description}
                        onClick={() => insertVariable(v.name)}
                        disabled={busy}
                        aria-label={`Insertar variable ${v.name}: ${v.description}`}
                        className="rounded-full border border-[#557EFF]/40 px-2.5 py-1 font-mono text-[11px] text-[#162244] hover:bg-[#557EFF]/10 disabled:opacity-50 dark:text-white"
                      >
                        {`{{${v.name}}}`}
                      </button>
                    ))}
                  </div>
                  <label className="block" htmlFor={`${uid}-cuerpo`}>
                    <span className="sr-only">Texto de la plantilla</span>
                    <textarea
                      id={`${uid}-cuerpo`}
                      ref={bodyRef}
                      value={body}
                      onChange={(e) => setBody(e.target.value)}
                      disabled={busy}
                      rows={12}
                      spellCheck={false}
                      placeholder="Aún usa la redacción de fábrica. Escribe aquí el texto para publicar una plantilla propia."
                      aria-invalid={unknownVars.length > 0 || undefined}
                      aria-describedby={unknownVars.length > 0 ? `${uid}-vars-err` : undefined}
                      className={`${FIELD} font-mono text-[12px] leading-relaxed`}
                      data-testid="mandato-formato-cuerpo"
                    />
                  </label>
                </section>
              ) : (
                <p className="rounded-xl border border-[#DFE5ED] px-3 py-2 text-[#59677D]" role="note">
                  Este formato no tiene plantilla propia: usa la redacción de cada organismo. Aquí solo
                  puedes cambiar su nombre y su tipo de mandato.
                </p>
              )}

              {unknownVars.length > 0 ? (
                <div
                  id={`${uid}-vars-err`}
                  role="alert"
                  data-testid="mandato-formato-variables-invalidas"
                  className="rounded-xl border border-[#FF4E00]/40 bg-[rgba(255,78,0,0.06)] px-3 py-2 text-[#FF4E00]"
                >
                  <p className="font-semibold">Variables que no existen:</p>
                  <ul className="mt-1 list-disc space-y-0.5 pl-5">
                    {unknownVars.map((v, i) => (
                      <li key={`${v.name}-${i}`}>
                        <span className="font-mono">{`{{${v.name}}}`}</span>
                        {v.line ? ` (línea ${v.line}${v.column ? `, columna ${v.column}` : ""})` : ""}
                      </li>
                    ))}
                  </ul>
                </div>
              ) : null}

              {error ? (
                <div
                  role="alert"
                  data-testid="mandato-formato-mensaje"
                  className="space-y-1 text-[11px] text-[#FF4E00]"
                >
                  <p>{error}</p>
                  {conflict ? (
                    <p>
                      Se recargó el formato con la versión vigente y se conservó lo que escribiste.
                      Revísalo y vuelve a guardar.
                    </p>
                  ) : null}
                </div>
              ) : null}

              <section aria-labelledby={`${uid}-hist`} className="space-y-2">
                <h3 id={`${uid}-hist`} className="sr-only">
                  Historial de versiones
                </h3>
                <button
                  type="button"
                  onClick={() => setShowHistory((v) => !v)}
                  aria-expanded={showHistory}
                  disabled={busy}
                  className="inline-flex items-center gap-1.5 font-semibold text-[#557EFF] disabled:opacity-50"
                  data-testid="mandato-formato-historial-toggle"
                >
                  <History className="h-3.5 w-3.5" aria-hidden="true" />
                  Historial de versiones ({detail?.versions.length ?? 0})
                </button>
                {showHistory ? (
                  detail && detail.versions.length > 0 ? (
                    <table
                      className="w-full border-collapse text-left"
                      data-testid="mandato-formato-historial"
                    >
                      <caption className="sr-only">Versiones publicadas de la plantilla</caption>
                      <thead>
                        <tr className="text-[11px] text-[#59677D]">
                          <th scope="col" className="py-1 pr-3 font-semibold">
                            Versión
                          </th>
                          <th scope="col" className="py-1 pr-3 font-semibold">
                            Autor
                          </th>
                          <th scope="col" className="py-1 pr-3 font-semibold">
                            Fecha
                          </th>
                          <th scope="col" className="py-1 text-right font-semibold">
                            Texto
                          </th>
                        </tr>
                      </thead>
                      <tbody>
                        {detail.versions.map((v) => (
                          <tr key={v.versionNumber} className="border-t border-[#DFE5ED]">
                            <td className="py-1.5 pr-3 font-semibold">v{v.versionNumber}</td>
                            <td
                              className="py-1.5 pr-3 font-mono text-[11px]"
                              title={v.createdBy ?? undefined}
                            >
                              {v.createdBy ? v.createdBy.slice(0, 8) : "Sistema"}
                            </td>
                            <td className="py-1.5 pr-3">{formatFechaHora(v.createdAt)}</td>
                            <td className="py-1.5 text-right">
                              <button
                                type="button"
                                onClick={() => void openVersion(v.versionNumber)}
                                disabled={busy || loadingVersion !== null}
                                aria-label={`Ver el texto de la versión ${v.versionNumber}`}
                                className="inline-flex items-center gap-1 font-semibold text-[#557EFF] disabled:opacity-50"
                              >
                                <Eye className="h-3.5 w-3.5" aria-hidden="true" />
                                Ver
                              </button>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  ) : (
                    <p className="text-[#59677D]">Este formato aún no tiene versiones publicadas.</p>
                  )
                ) : null}
                {oldVersion ? (
                  <div className="space-y-1" data-testid="mandato-formato-version-texto">
                    <p className="font-semibold text-[#162244]">
                      Texto de la versión {oldVersion.number} (solo lectura; no se restaura desde aquí)
                    </p>
                    <textarea
                      readOnly
                      value={oldVersion.body}
                      rows={8}
                      aria-label={`Texto de la versión ${oldVersion.number}`}
                      className={`${FIELD} font-mono text-[12px] leading-relaxed`}
                    />
                  </div>
                ) : null}
              </section>

              <div className="flex flex-wrap justify-end gap-2 pt-1">
                <button type="button" onClick={onClose} disabled={busy} className={BTN_OUTLINE}>
                  Cancelar
                </button>
                {redaction ? (
                  <button
                    type="button"
                    onClick={() => void handlePreview()}
                    disabled={busy || body.trim() === ""}
                    className={BTN_OUTLINE}
                  >
                    Vista previa
                  </button>
                ) : null}
                <button
                  type="button"
                  onClick={handlePublish}
                  disabled={busy || !dirty}
                  className={BTN_PRIMARY}
                  style={GRADIENT}
                >
                  {bodyChanged ? "Publicar" : "Guardar"}
                </button>
              </div>
            </>
          )}
        </div>
      </Modal>

      {confirming ? (
        <Modal
          open
          onClose={() => setConfirming(false)}
          icon={AlertTriangle}
          iconBg="#F9AC00"
          title="Confirmar publicación de la plantilla"
          titleClassName="text-base font-bold text-[#162744]"
          size="md"
          zClassName="z-[110]"
        >
          <div className="space-y-3 text-xs" data-testid="mandato-formato-confirmacion">
            <ul className="list-disc space-y-1 pl-5">
              <li>El cambio aplica a los trámites nuevos.</li>
              <li>Los contratos ya emitidos no cambian.</li>
              <li>Requiere validación del PO y de jurídico antes de usarse en producción.</li>
            </ul>
            <div className="flex justify-end gap-2 pt-1">
              <button type="button" onClick={() => setConfirming(false)} className={BTN_OUTLINE}>
                Cancelar
              </button>
              <button type="button" onClick={() => void send()} className={BTN_PRIMARY} style={GRADIENT}>
                Confirmar y publicar
              </button>
            </div>
          </div>
        </Modal>
      ) : null}

      {saving ? <CarLoaderModal label="Guardando…" /> : null}
    </>
  );
}

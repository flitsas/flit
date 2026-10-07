"use client";

// HU #10523 (RF31) — Consola de parámetros documentales por compañía gestora.
// Permite fijar el estado (OCULTO/OBLIGATORIO/OPCIONAL) de un tipo de documento por gestora,
// consumiendo el endpoint admin de HU #10521. Sin parámetros, el checklist queda en su estado base.
// Rediseño: tabla del modelo único + modal FLIT para agregar/editar; el error de carga muestra el
// motivo real (antes un `catch {}` lo tragaba y siempre salía el mismo texto).
import { useCallback, useEffect, useId, useMemo, useState } from "react";
import { AlertTriangle, CheckCircle2, EyeOff, FilePlus, MinusCircle, Pencil, type LucideIcon } from "lucide-react";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { CreateButton } from "@/components/atom/CreateButton";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { Modal } from "@/components/atom/Modal";
import { RowActions } from "@/components/atom/RowActions";
import { StatusBadge, type StatusTone } from "@/components/atom/StatusBadge";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import {
  fetchCompanyDocumentParams,
  upsertCompanyDocumentParam,
  type CompanyDocumentParam,
  type CompanyDocumentParamState,
} from "@/lib/api/admin-company-document-params";
import { fetchDocumentTypes } from "@/lib/api/admin-document-types";
import { ApiError, ApiValidationError } from "@/lib/api/types";
import type { DocumentType } from "@/lib/api/types-documents";
import { catalogDocumentName } from "@/lib/tramites/document-labels";

interface StateMeta {
  label: string;
  hint: string;
  tone: StatusTone;
  icon: LucideIcon;
}

const STATE_META: Record<CompanyDocumentParamState, StateMeta> = {
  OBLIGATORIO: {
    label: "Obligatorio",
    hint: "El gestor debe cargarlo para continuar.",
    tone: "danger",
    icon: AlertTriangle,
  },
  OPCIONAL: {
    label: "Opcional",
    hint: "El gestor puede cargarlo, pero no es requisito.",
    tone: "neutral",
    icon: MinusCircle,
  },
  OCULTO: {
    label: "Oculto",
    hint: "No se le pide ni se le muestra al gestor.",
    tone: "neutral",
    icon: EyeOff,
  },
};

const STATES: CompanyDocumentParamState[] = ["OBLIGATORIO", "OPCIONAL", "OCULTO"];

const FIELD_CLS =
  "w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-xs text-[#162744] " +
  "focus:outline-none focus-visible:border-[#557EFF] focus-visible:ring-2 focus-visible:ring-[#557EFF]/30 " +
  "disabled:opacity-60 dark:border-white/15 dark:bg-[#0B0F14] dark:text-white";

/** Motivo legible de un fallo de red/HTTP: nunca un texto genérico que oculte el 403/500 real. */
export function describeDocumentParamsError(err: unknown, action: "cargar" | "guardar"): string {
  if (err instanceof ApiError) {
    if (err.status === 401) return "Tu sesión expiró. Vuelve a iniciar sesión.";
    if (err.status === 403) {
      return action === "cargar"
        ? "No tienes permiso para ver los parámetros documentales de esta compañía."
        : "No tienes permiso para modificar los parámetros documentales de esta compañía.";
    }
    if (err.status === 404) return "No se encontró la ruta de parámetros documentales de esta compañía.";
    if (err.status >= 500) return `El servidor falló al ${action} los parámetros (error ${err.status}). Inténtalo de nuevo.`;
    return err.message;
  }
  if (err instanceof ApiValidationError) {
    const message = err.errors[0]?.message;
    if (message) return message;
    return "Revisa el código y el estado del documento e inténtalo de nuevo.";
  }
  if (err instanceof Error && err.message === FORMAT_ERROR) {
    return "La respuesta del servidor no tiene el formato esperado.";
  }
  if (err instanceof TypeError) return "No hay conexión con el servidor. Revisa tu red e inténtalo de nuevo.";
  return `No se pudieron ${action === "cargar" ? "cargar" : "guardar"} los parámetros documentales.`;
}

const FORMAT_ERROR = "unexpected_shape";

type ModalState =
  | { mode: "closed" }
  | { mode: "add" }
  | { mode: "edit"; item: CompanyDocumentParam };

export interface CompanyDocumentParamsPanelProps {
  tenantId: string;
  networkHeadId?: string | null;
}

export function CompanyDocumentParamsPanel({
  tenantId,
  networkHeadId,
}: CompanyDocumentParamsPanelProps) {
  const [items, setItems] = useState<CompanyDocumentParam[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [catalog, setCatalog] = useState<DocumentType[]>([]);
  const [modal, setModal] = useState<ModalState>({ mode: "closed" });

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setLoading(true);
      setLoadError(null);
      try {
        const data = await fetchCompanyDocumentParams(tenantId, signal, networkHeadId);
        if (signal?.aborted) return;
        if (!Array.isArray(data)) throw new Error(FORMAT_ERROR);
        setItems(data);
      } catch (err) {
        if (signal?.aborted) return;
        setLoadError(describeDocumentParamsError(err, "cargar"));
      } finally {
        if (!signal?.aborted) setLoading(false);
      }
    },
    [tenantId, networkHeadId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: los setState ocurren tras el await (no síncronos)
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  // Catálogo de tipos de documento: solo mejora la UI (nombres legibles y selector). Si falla o el
  // rol no lo puede leer, el panel sigue funcionando con el código en crudo.
  useEffect(() => {
    const controller = new AbortController();
    fetchDocumentTypes({ page: 1, pageSize: 100 }, controller.signal)
      .then((res) => {
        if (!controller.signal.aborted && Array.isArray(res?.data)) setCatalog(res.data);
      })
      .catch(() => undefined);
    return () => controller.abort();
  }, []);

  const nameByCode = useMemo(() => {
    const map = new Map<string, string>();
    for (const d of catalog) map.set(d.codigo, catalogDocumentName(d.codigo, d.nombre));
    return map;
  }, [catalog]);

  const documentName = useCallback(
    (code: string) => nameByCode.get(code) ?? catalogDocumentName(code),
    [nameByCode],
  );

  const handleSaved = useCallback((saved: CompanyDocumentParam) => {
    setItems((prev) => {
      const rest = prev.filter((p) => p.documentTypeCode !== saved.documentTypeCode);
      return [...rest, saved].sort((a, b) => a.documentTypeCode.localeCompare(b.documentTypeCode));
    });
    setModal({ mode: "closed" });
  }, []);

  const columns: DataTableColumn<CompanyDocumentParam>[] = [
    {
      key: "document",
      header: "Documento",
      render: (item) => (
        <div className="min-w-0">
          <p className="font-medium text-[#162744] dark:text-white">{documentName(item.documentTypeCode)}</p>
          <p className="font-mono text-xs opacity-70">{item.documentTypeCode}</p>
        </div>
      ),
    },
    {
      key: "state",
      header: "Estado",
      render: (item) => {
        const meta = STATE_META[item.state] ?? STATE_META.OPCIONAL;
        const Icon = meta.icon;
        return (
          <StatusBadge
            tone={meta.tone}
            ariaLabel={`Estado de ${item.documentTypeCode}: ${meta.label}`}
            label={
              <span className="inline-flex items-center gap-1.5">
                <Icon className="h-3.5 w-3.5" aria-hidden />
                {meta.label}
              </span>
            }
          />
        );
      },
    },
    {
      key: "actions",
      header: "Acciones",
      align: "right",
      render: (item) => (
        <RowActions
          actions={[
            {
              icon: Pencil,
              label: `Editar ${item.documentTypeCode}`,
              tone: "primary",
              onClick: () => setModal({ mode: "edit", item }),
            },
          ]}
        />
      ),
    },
  ];

  const addButton = (
    <CreateButton label="Agregar parámetro" icon={FilePlus} onClick={() => setModal({ mode: "add" })} />
  );

  return (
    <section aria-label="Parámetros documentales por gestora" className="space-y-4">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold">Parámetros documentales por gestora</h2>
          <p className="text-sm opacity-70">
            Define si cada tipo de documento es obligatorio, opcional u oculto para esta compañía.
          </p>
        </div>
        {!loading && !loadError ? addButton : null}
      </header>

      {loading ? (
        <CarLoaderModal label="Cargando parámetros documentales…" />
      ) : (
        <UiStateBoundary
          status={loadError ? "error" : items.length === 0 ? "empty" : "ready"}
          errorMessage={loadError ?? undefined}
          onRetry={() => void load()}
          emptyMessage="Sin parámetros: se aplica el comportamiento base."
          emptyCta={addButton}
        >
          {/* Parámetros por gestora: lista corta, sin paginar */}
          <DataTable
            ariaLabel="Parámetros documentales"
            columns={columns}
            rows={items}
            getRowKey={(item) => item.id}
            allowHorizontalScroll
          />
        </UiStateBoundary>
      )}

      {modal.mode !== "closed" ? (
        <DocumentParamModal
          key={modal.mode === "edit" ? modal.item.id : "add"}
          tenantId={tenantId}
          networkHeadId={networkHeadId}
          item={modal.mode === "edit" ? modal.item : null}
          catalog={catalog}
          existingCodes={items.map((i) => i.documentTypeCode)}
          documentName={documentName}
          onClose={() => setModal({ mode: "closed" })}
          onSaved={handleSaved}
        />
      ) : null}
    </section>
  );
}

interface DocumentParamModalProps {
  tenantId: string;
  networkHeadId?: string | null;
  /** Parámetro a editar; `null` = alta. */
  item: CompanyDocumentParam | null;
  catalog: DocumentType[];
  existingCodes: string[];
  documentName: (code: string) => string;
  onClose: () => void;
  onSaved: (saved: CompanyDocumentParam) => void;
}

function DocumentParamModal({
  tenantId,
  networkHeadId,
  item,
  catalog,
  existingCodes,
  documentName,
  onClose,
  onSaved,
}: DocumentParamModalProps) {
  const codeId = useId();
  const [code, setCode] = useState(item?.documentTypeCode ?? "");
  const [state, setState] = useState<CompanyDocumentParamState>(item?.state ?? "OBLIGATORIO");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isEdit = item !== null;
  const available = useMemo(
    () => catalog.filter((d) => d.estado === "activo" && !existingCodes.includes(d.codigo)),
    [catalog, existingCodes],
  );
  const useSelector = !isEdit && available.length > 0;
  const trimmed = code.trim();

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!trimmed || saving) return;
    setSaving(true);
    setError(null);
    try {
      const saved = await upsertCompanyDocumentParam(
        tenantId,
        { documentTypeCode: trimmed, state },
        networkHeadId,
      );
      onSaved(saved);
    } catch (err) {
      setError(describeDocumentParamsError(err, "guardar"));
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      busy={saving}
      icon={FilePlus}
      title={isEdit ? "Editar parámetro" : "Agregar parámetro"}
      description={
        isEdit ? documentName(item.documentTypeCode) : "Elige el documento y cómo se le pedirá al gestor."
      }
    >
      <form onSubmit={(e) => void submit(e)} className="space-y-4" aria-label="Parámetro documental">
        <div>
          <label htmlFor={codeId} className="mb-1 block text-xs font-semibold">
            {useSelector ? "Documento" : "Código de documento"}
          </label>
          {useSelector ? (
            <select
              id={codeId}
              value={code}
              onChange={(e) => setCode(e.target.value)}
              disabled={saving}
              className={FIELD_CLS}
            >
              <option value="">Selecciona un documento…</option>
              {available.map((d) => (
                <option key={d.id} value={d.codigo}>
                  {catalogDocumentName(d.codigo, d.nombre)} ({d.codigo})
                </option>
              ))}
            </select>
          ) : (
            <input
              id={codeId}
              value={code}
              onChange={(e) => setCode(e.target.value)}
              readOnly={isEdit}
              disabled={saving}
              autoComplete="off"
              className={`${FIELD_CLS} font-mono`}
            />
          )}
        </div>

        <fieldset disabled={saving} className="min-w-0">
          <legend className="mb-1 text-xs font-semibold">Estado</legend>
          <div role="radiogroup" aria-label="Estado del parámetro" className="grid gap-2 sm:grid-cols-3">
            {STATES.map((s) => {
              const meta = STATE_META[s];
              const Icon = meta.icon;
              const selected = state === s;
              return (
                <button
                  key={s}
                  type="button"
                  role="radio"
                  aria-checked={selected}
                  onClick={() => setState(s)}
                  className={`flex flex-col items-start gap-1 rounded-xl border p-3 text-left transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] ${
                    selected
                      ? "border-[#557EFF] bg-[#557EFF]/10"
                      : "border-[#DFE5ED] hover:bg-[#F4F7FC] dark:border-white/15 dark:hover:bg-white/5"
                  }`}
                >
                  <span className="flex items-center gap-1.5 text-xs font-semibold">
                    {selected ? (
                      <CheckCircle2 className="h-4 w-4 text-[#557EFF]" aria-hidden />
                    ) : (
                      <Icon className="h-4 w-4" aria-hidden />
                    )}
                    {meta.label}
                  </span>
                  <span className="text-xs opacity-70">{meta.hint}</span>
                </button>
              );
            })}
          </div>
        </fieldset>

        {error ? (
          <p role="alert" className="flex items-start gap-2 text-xs font-medium text-[#B42318]">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
            {error}
          </p>
        ) : null}

        <div className="flex justify-end gap-2 pt-2">
          <button
            type="button"
            onClick={onClose}
            disabled={saving}
            className="rounded-xl border border-[#DFE5ED] px-4 py-2 text-xs font-semibold hover:bg-[#F4F7FC] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:opacity-50 dark:border-white/15 dark:hover:bg-white/5"
          >
            Cancelar
          </button>
          <button
            type="submit"
            disabled={saving || !trimmed}
            className="rounded-xl px-5 py-2 text-xs font-semibold text-white shadow-sm focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            {saving ? "Guardando…" : "Guardar"}
          </button>
        </div>
      </form>
    </Modal>
  );
}

"use client";

// HU #10523 (RF31) — Consola de parámetros documentales por compañía gestora.
// Permite fijar el estado (OCULTO/OBLIGATORIO/OPCIONAL) de un tipo de documento por gestora,
// consumiendo el endpoint admin de HU #10521. Sin parámetros, el checklist queda en su estado base.
// Rediseño: tabla del modelo único + modal FLIT para editar; el error de carga muestra el
// motivo real (antes un `catch {}` lo tragaba y siempre salía el mismo texto).
// HU #14000 (Feature #13996): la tabla lista TODOS los documentos activos del catálogo cruzados con los
// parámetros (sin parámetro = «Base») y lleva filtros por columna (Documento, Estado). «Agregar
// parámetro» se retiró: editar la fila del documento es un paso menos que abrir un selector.
import { useCallback, useEffect, useId, useMemo, useState } from "react";
import {
  AlertTriangle,
  CheckCircle2,
  CircleDashed,
  EyeOff,
  FilePlus,
  MinusCircle,
  Pencil,
  type LucideIcon,
} from "lucide-react";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { Modal } from "@/components/atom/Modal";
import { RowActions } from "@/components/atom/RowActions";
import { StatusBadge, type StatusTone } from "@/components/atom/StatusBadge";
import { TablaFiltrosBar, TablaFiltrosVacio } from "@/components/atom/TablaFiltrosBar";
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
import { filtrarFilas, type ColumnaFiltro } from "@/lib/tablas/filtrar-filas";
import { useTablaFiltros } from "@/lib/tablas/useTablaFiltros";
import { catalogDocumentName } from "@/lib/tramites/document-labels";

interface StateMeta {
  label: string;
  hint: string;
  tone: StatusTone;
  icon: LucideIcon;
}

/** Estado de una fila: el del parámetro, o «BASE» cuando la compañía no lo parametrizó. */
type RowState = CompanyDocumentParamState | "BASE";

const STATE_META: Record<RowState, StateMeta> = {
  BASE: {
    label: "Base",
    hint: "Sin parámetro: aplica el comportamiento base.",
    tone: "neutral",
    icon: CircleDashed,
  },
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
  "w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-xs text-flit-primary " +
  "focus:outline-none focus-visible:border-flit-brand focus-visible:ring-2 focus-visible:ring-flit-brand/30 " +
  "disabled:opacity-60 dark:border-white/15 dark:bg-[#0B0F14] dark:text-white";

/** Motivo legible de un fallo de red/HTTP: nunca un texto genérico que oculte el 403/500 real. */
export function describeDocumentParamsError(
  err: unknown,
  action: "cargar" | "guardar" | "catalogo",
): string {
  if (action === "catalogo") return describeCatalogError(err);
  if (err instanceof ApiError) {
    if (err.status === 401) return "Tu sesión expiró. Vuelve a iniciar sesión.";
    if (err.status === 403) {
      return action === "cargar"
        ? "No tienes permiso para ver los parámetros documentales de esta compañía."
        : "No tienes permiso para modificar los parámetros documentales de esta compañía.";
    }
    if (err.status === 404)
      return "No se encontró la ruta de parámetros documentales de esta compañía.";
    if (err.status >= 500)
      return `El servidor falló al ${action} los parámetros (error ${err.status}). Inténtalo de nuevo.`;
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
  if (err instanceof TypeError)
    return "No hay conexión con el servidor. Revisa tu red e inténtalo de nuevo.";
  return `No se pudieron ${action === "cargar" ? "cargar" : "guardar"} los parámetros documentales.`;
}

const FORMAT_ERROR = "unexpected_shape";

function describeCatalogError(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 401) return "Tu sesión expiró. Vuelve a iniciar sesión.";
    if (err.status === 403) return "No tienes permiso para ver el catálogo de documentos.";
    if (err.status >= 500) {
      return `El servidor falló al cargar el catálogo de documentos (error ${err.status}). Inténtalo de nuevo.`;
    }
    return err.message;
  }
  if (err instanceof Error && err.message === FORMAT_ERROR) {
    return "La respuesta del catálogo de documentos no tiene el formato esperado.";
  }
  if (err instanceof TypeError)
    return "No hay conexión con el servidor. Revisa tu red e inténtalo de nuevo.";
  return "No se pudo cargar el catálogo de documentos.";
}

const PAGE_SIZE_CATALOGO = 100;
const MAX_PAGINAS_CATALOGO = 50;

/** Trae TODAS las páginas del catálogo (no solo la primera). Cualquier fallo aborta: sin tabla parcial. */
async function fetchAllDocumentTypes(signal?: AbortSignal): Promise<DocumentType[]> {
  const all: DocumentType[] = [];
  for (let page = 1; page <= MAX_PAGINAS_CATALOGO; page++) {
    const res = await fetchDocumentTypes({ page, pageSize: PAGE_SIZE_CATALOGO }, signal);
    if (!Array.isArray(res?.data)) throw new Error(FORMAT_ERROR);
    all.push(...res.data);
    if (res.data.length < PAGE_SIZE_CATALOGO || all.length >= (res.totalCount ?? 0)) break;
  }
  return all;
}

interface DocRow {
  code: string;
  name: string;
  state: RowState;
}

const COLUMNAS_FILTRO: ColumnaFiltro[] = [
  { id: "documento", label: "Documento", tipo: "texto" },
  {
    id: "estado",
    label: "Estado",
    tipo: "seleccion",
    opciones: (["OBLIGATORIO", "OPCIONAL", "OCULTO", "BASE"] as RowState[]).map((value) => ({
      value,
      label: STATE_META[value].label,
    })),
  },
];

type ModalState = { mode: "closed" } | { mode: "edit"; row: DocRow };

export interface CompanyDocumentParamsPanelProps {
  tenantId: string;
  networkHeadId?: string | null;
}

export function CompanyDocumentParamsPanel({
  tenantId,
  networkHeadId,
}: CompanyDocumentParamsPanelProps) {
  const [items, setItems] = useState<CompanyDocumentParam[]>([]);
  const [catalog, setCatalog] = useState<DocumentType[]>([]);
  const pg = usePaginacion("documents.CompanyDocumentParamsPanel");
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [modal, setModal] = useState<ModalState>({ mode: "closed" });

  const { setPage } = pg;
  const filtros = useTablaFiltros({ columnas: COLUMNAS_FILTRO, onCambio: () => setPage(1) });

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setLoading(true);
      setLoadError(null);
      let step: "cargar" | "catalogo" = "cargar";
      try {
        const data = await fetchCompanyDocumentParams(tenantId, signal, networkHeadId);
        if (signal?.aborted) return;
        if (!Array.isArray(data)) throw new Error(FORMAT_ERROR);
        step = "catalogo";
        const types = await fetchAllDocumentTypes(signal);
        if (signal?.aborted) return;
        setItems(data);
        setCatalog(types);
      } catch (err) {
        if (signal?.aborted) return;
        setLoadError(describeDocumentParamsError(err, step));
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

  // Catálogo activo + parámetros. Un parámetro cuyo código ya no está activo se muestra igual.
  const rows = useMemo<DocRow[]>(() => {
    const paramByCode = new Map(items.map((p) => [p.documentTypeCode, p]));
    const result: DocRow[] = [];
    const seen = new Set<string>();
    for (const d of catalog) {
      if (d.estado !== "activo" || seen.has(d.codigo)) continue;
      seen.add(d.codigo);
      result.push({
        code: d.codigo,
        name: catalogDocumentName(d.codigo, d.nombre),
        state: paramByCode.get(d.codigo)?.state ?? "BASE",
      });
    }
    for (const p of items) {
      if (seen.has(p.documentTypeCode)) continue;
      const inactive = catalog.find((d) => d.codigo === p.documentTypeCode);
      result.push({
        code: p.documentTypeCode,
        name: catalogDocumentName(p.documentTypeCode, inactive?.nombre),
        state: p.state,
      });
    }
    return result.sort(
      (a, b) => a.name.localeCompare(b.name, "es") || a.code.localeCompare(b.code),
    );
  }, [catalog, items]);

  const visibles = useMemo(
    () =>
      filtrarFilas(rows, filtros.condicionesTotales, filtros.busqueda, {
        columnas: { documento: (r) => [r.name, r.code], estado: (r) => r.state },
      }),
    [rows, filtros.condicionesTotales, filtros.busqueda],
  );

  const handleSaved = useCallback((saved: CompanyDocumentParam) => {
    setItems((prev) => {
      const rest = prev.filter((p) => p.documentTypeCode !== saved.documentTypeCode);
      return [...rest, saved].sort((a, b) => a.documentTypeCode.localeCompare(b.documentTypeCode));
    });
    setModal({ mode: "closed" });
  }, []);

  const columns: DataTableColumn<DocRow>[] = [
    {
      key: "document",
      header: "Documento",
      render: (row) => (
        <div className="min-w-0">
          <p className="font-medium text-flit-primary dark:text-white">{row.name}</p>
          <p className="font-mono text-xs opacity-70">{row.code}</p>
        </div>
      ),
    },
    {
      key: "state",
      header: "Estado",
      render: (row) => {
        const meta = STATE_META[row.state] ?? STATE_META.OPCIONAL;
        const Icon = meta.icon;
        return (
          <StatusBadge
            tone={meta.tone}
            ariaLabel={`Estado de ${row.code}: ${meta.label}`}
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
      render: (row) => (
        <RowActions
          actions={[
            {
              icon: Pencil,
              label: `Editar ${row.code}`,
              tone: "primary",
              onClick: () => setModal({ mode: "edit", row }),
            },
          ]}
        />
      ),
    },
  ];

  const filtradoVacio = visibles.length === 0 && filtros.hayFiltros;

  return (
    <section aria-label="Parámetros documentales por gestora" className="space-y-4">
      <header>
        <h2 className="text-lg font-semibold">Parámetros documentales por gestora</h2>
        <p className="text-sm opacity-70">
          Define si cada tipo de documento es obligatorio, opcional u oculto para esta compañía. Los
          documentos en «Base» aplican el comportamiento base.
        </p>
      </header>

      {loading ? (
        <CarLoaderModal label="Cargando parámetros documentales…" />
      ) : (
        <UiStateBoundary
          status={loadError ? "error" : rows.length === 0 ? "empty" : "ready"}
          errorMessage={loadError ?? undefined}
          onRetry={() => void load()}
          emptyMessage="No hay documentos activos en el catálogo."
        >
          <div className="space-y-3">
            <TablaFiltrosBar filtros={filtros} />
            {filtradoVacio ? (
              <TablaFiltrosVacio onLimpiar={filtros.limpiar} />
            ) : (
              /* HU #13918 — paginación estándar: con una sola página solo se ven el resumen y
                 «Filas por página». */
              <DataTable
                ariaLabel="Parámetros documentales"
                columns={columns}
                rows={pg.paginar(visibles)}
                getRowKey={(row) => row.code}
                allowHorizontalScroll
                pagination={{
                  page: pg.page,
                  pageSize: pg.pageSize,
                  totalCount: visibles.length,
                  onPageChange: pg.setPage,
                  onPageSizeChange: pg.setPageSize,
                }}
              />
            )}
          </div>
        </UiStateBoundary>
      )}

      {modal.mode === "edit" ? (
        <DocumentParamModal
          key={modal.row.code}
          tenantId={tenantId}
          networkHeadId={networkHeadId}
          row={modal.row}
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
  /** Documento a parametrizar (ya elegido: viene de la fila). */
  row: DocRow;
  onClose: () => void;
  onSaved: (saved: CompanyDocumentParam) => void;
}

function DocumentParamModal({
  tenantId,
  networkHeadId,
  row,
  onClose,
  onSaved,
}: DocumentParamModalProps) {
  const codeId = useId();
  const [state, setState] = useState<CompanyDocumentParamState>(
    row.state === "BASE" ? "OBLIGATORIO" : row.state,
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (saving) return;
    setSaving(true);
    setError(null);
    try {
      const saved = await upsertCompanyDocumentParam(
        tenantId,
        { documentTypeCode: row.code, state },
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
      title="Editar parámetro"
      description={row.name}
    >
      <form
        onSubmit={(e) => void submit(e)}
        className="space-y-4"
        aria-label="Parámetro documental"
      >
        <div>
          <label htmlFor={codeId} className="mb-1 block text-xs font-semibold">
            Código de documento
          </label>
          <input id={codeId} value={row.code} readOnly className={`${FIELD_CLS} font-mono`} />
        </div>

        <fieldset disabled={saving} className="min-w-0">
          <legend className="mb-1 text-xs font-semibold">Estado</legend>
          <div
            role="radiogroup"
            aria-label="Estado del parámetro"
            className="grid gap-2 sm:grid-cols-3"
          >
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
                  className={`flex flex-col items-start gap-1 rounded-xl border p-3 text-left transition focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand ${
                    selected
                      ? "border-flit-brand bg-flit-brand/10"
                      : "border-[#DFE5ED] hover:bg-[#F4F7FC] dark:border-white/15 dark:hover:bg-white/5"
                  }`}
                >
                  <span className="flex items-center gap-1.5 text-xs font-semibold">
                    {selected ? (
                      <CheckCircle2 className="h-4 w-4 text-flit-brand" aria-hidden />
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
            className="rounded-xl border border-[#DFE5ED] px-4 py-2 text-xs font-semibold hover:bg-[#F4F7FC] focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand disabled:opacity-50 dark:border-white/15 dark:hover:bg-white/5"
          >
            Cancelar
          </button>
          <button
            type="submit"
            disabled={saving}
            className="rounded-xl px-5 py-2 text-xs font-semibold text-white shadow-sm focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
            style={{
              background: "linear-gradient(135deg,var(--color-flit-brand),var(--color-flit-tech))",
            }}
          >
            {saving ? "Guardando…" : "Guardar"}
          </button>
        </div>
      </form>
    </Modal>
  );
}

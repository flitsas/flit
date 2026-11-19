"use client";

/**
 * Panel SuperAdmin — Overrides de persona RUNT por tenant (HU #14642).
 *
 * Permite crear, editar y desactivar registros que substituyen la respuesta del RUNT para
 * una persona específica dentro de un tenant, útil para manejar casos donde el RUNT no
 * devuelve datos correctos o completos.
 */
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { EncabezadoFiltrable, TablaFiltrosVacio } from "@/components/atom/TablaFiltrosBar";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { CompanySelector } from "@/components/atom/modules/_reportes/CompanySelector";
import { filtrarFilas, type ColumnaFiltro } from "@/lib/tablas/filtrar-filas";
import { useTablaFiltros } from "@/lib/tablas/useTablaFiltros";
import { fetchAllCompanies } from "@/lib/api/admin-companies";
import {
  listRuntPersonOverrides,
  createRuntPersonOverride,
  updateRuntPersonOverride,
  deactivateRuntPersonOverride,
  type RuntPersonOverride,
  type CreateRuntPersonOverrideInput,
  type UpdateRuntPersonOverrideInput,
  type OverrideDocumentType,
} from "@/lib/api/admin-runt-person-overrides";
import type { CompanyListItem } from "@/lib/api/types";
import { formatFechaHora } from "@/lib/format/date";

/** Clave para recordar «Filas por página» en la sesión (estándar HU #13917). */
const CLAVE_TABLA = "plataforma.runtPersonaOverrides";

const DOC_OPTIONS: { value: OverrideDocumentType; label: string }[] = [
  { value: "CC", label: "Cédula de ciudadanía (CC)" },
  { value: "CE", label: "Cédula de extranjería (CE)" },
  { value: "TI", label: "Tarjeta de identidad (TI)" },
  { value: "PAS", label: "Pasaporte (PAS)" },
  { value: "PPT", label: "Permiso por Protección Temporal (PPT)" },
];

const COLUMNAS_FILTRO: ColumnaFiltro[] = [
  { id: "documento", label: "Documento", tipo: "texto" },
  { id: "nombre", label: "Nombre", tipo: "texto" },
  {
    id: "activo",
    label: "Estado",
    tipo: "seleccion",
    opciones: [
      { value: "activo", label: "Activo" },
      { value: "inactivo", label: "Inactivo" },
    ],
  },
];

// ── Formulario de creación/edición ────────────────────────────────────────────

interface FormState {
  documentType: OverrideDocumentType;
  documentNumber: string;
  firstName: string;
  secondName: string;
  firstLastName: string;
  secondLastName: string;
  citizenStatus: string;
  driverStatus: string;
  hasActiveLicense: string; // "true" | "false" | ""
  licenseCategories: string;
  hasPendingFines: string; // "true" | "false" | ""
  nroPazYSalvo: string;
  reason: string;
  expiresAt: string;
  isActive: string; // solo en edición
}

function emptyForm(): FormState {
  return {
    documentType: "CC",
    documentNumber: "",
    firstName: "",
    secondName: "",
    firstLastName: "",
    secondLastName: "",
    citizenStatus: "",
    driverStatus: "",
    hasActiveLicense: "",
    licenseCategories: "",
    hasPendingFines: "",
    nroPazYSalvo: "",
    reason: "",
    expiresAt: "",
    isActive: "true",
  };
}

function overrideToForm(o: RuntPersonOverride): FormState {
  return {
    documentType: o.documentType,
    documentNumber: o.documentNumber,
    firstName: o.firstName,
    secondName: o.secondName ?? "",
    firstLastName: o.firstLastName,
    secondLastName: o.secondLastName ?? "",
    citizenStatus: o.citizenStatus ?? "",
    driverStatus: o.driverStatus ?? "",
    hasActiveLicense: o.hasActiveLicense == null ? "" : o.hasActiveLicense ? "true" : "false",
    licenseCategories: o.licenseCategories ?? "",
    hasPendingFines: o.hasPendingFines == null ? "" : o.hasPendingFines ? "true" : "false",
    nroPazYSalvo: o.nroPazYSalvo ?? "",
    reason: o.reason,
    expiresAt: o.expiresAt ? o.expiresAt.slice(0, 16) : "",
    isActive: o.isActive ? "true" : "false",
  };
}

function formToCreateInput(f: FormState): CreateRuntPersonOverrideInput {
  return {
    documentType: f.documentType,
    documentNumber: f.documentNumber.trim(),
    firstName: f.firstName.trim(),
    ...(f.secondName.trim() ? { secondName: f.secondName.trim() } : {}),
    firstLastName: f.firstLastName.trim(),
    ...(f.secondLastName.trim() ? { secondLastName: f.secondLastName.trim() } : {}),
    ...(f.citizenStatus.trim() ? { citizenStatus: f.citizenStatus.trim() } : {}),
    ...(f.driverStatus.trim() ? { driverStatus: f.driverStatus.trim() } : {}),
    ...(f.hasActiveLicense ? { hasActiveLicense: f.hasActiveLicense === "true" } : {}),
    ...(f.licenseCategories.trim() ? { licenseCategories: f.licenseCategories.trim() } : {}),
    ...(f.hasPendingFines ? { hasPendingFines: f.hasPendingFines === "true" } : {}),
    ...(f.nroPazYSalvo.trim() ? { nroPazYSalvo: f.nroPazYSalvo.trim() } : {}),
    reason: f.reason.trim(),
    ...(f.expiresAt ? { expiresAt: new Date(f.expiresAt).toISOString() } : {}),
  };
}

function formToUpdateInput(f: FormState): UpdateRuntPersonOverrideInput {
  return {
    firstName: f.firstName.trim() || undefined,
    secondName: f.secondName.trim() || undefined,
    firstLastName: f.firstLastName.trim() || undefined,
    secondLastName: f.secondLastName.trim() || undefined,
    citizenStatus: f.citizenStatus.trim() || undefined,
    driverStatus: f.driverStatus.trim() || undefined,
    hasActiveLicense: f.hasActiveLicense ? f.hasActiveLicense === "true" : undefined,
    licenseCategories: f.licenseCategories.trim() || undefined,
    hasPendingFines: f.hasPendingFines ? f.hasPendingFines === "true" : undefined,
    nroPazYSalvo: f.nroPazYSalvo.trim() || undefined,
    reason: f.reason.trim() || undefined,
    isActive: f.isActive ? f.isActive === "true" : undefined,
    expiresAt: f.expiresAt ? new Date(f.expiresAt).toISOString() : null,
  };
}

function validateForm(f: FormState, isEdit: boolean): string | null {
  if (!f.documentNumber.trim()) return "El número de documento es obligatorio.";
  if (!f.firstName.trim()) return "El primer nombre es obligatorio.";
  if (!f.firstLastName.trim()) return "El primer apellido es obligatorio.";
  if (!f.reason.trim()) return "El motivo es obligatorio.";
  if (!isEdit && !f.documentType) return "El tipo de documento es obligatorio.";
  return null;
}

// ── Estilos comunes ───────────────────────────────────────────────────────────

const INPUT_CLS =
  "block w-full rounded-lg border border-[var(--border-subtle)] bg-[var(--surface-default)] px-3 py-2 text-sm text-[var(--content-primary)] placeholder:text-[var(--content-disabled)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-default)] disabled:opacity-60";

const LABEL_CLS = "block text-xs font-medium text-[var(--content-secondary)] mb-1";

const BTN_PRIMARY =
  "inline-flex items-center gap-1.5 rounded-lg bg-[var(--brand-default)] px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--brand-hover)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-default)] disabled:opacity-60";

const BTN_SECONDARY =
  "inline-flex items-center gap-1.5 rounded-lg border border-[var(--border-default)] bg-[var(--surface-default)] px-4 py-2 text-sm font-medium text-[var(--content-primary)] hover:bg-[var(--surface-hover)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-default)] disabled:opacity-60";

const BTN_DANGER =
  "inline-flex items-center gap-1.5 rounded-lg border border-[var(--status-error)] bg-transparent px-3 py-1.5 text-xs font-medium text-[var(--status-error)] hover:bg-[var(--status-error-subtle)] focus:outline-none focus:ring-2 focus:ring-[var(--status-error)] disabled:opacity-60";

// ── Diálogo de creación/edición ───────────────────────────────────────────────

interface DialogProps {
  override: RuntPersonOverride | null; // null = crear
  onClose: () => void;
  onSaved: (o: RuntPersonOverride) => void;
  tenantId: string;
}

function OverrideDialog({ override, onClose, onSaved, tenantId }: DialogProps) {
  const isEdit = override !== null;
  const [form, setForm] = useState<FormState>(
    isEdit ? overrideToForm(override) : emptyForm(),
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const firstRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    firstRef.current?.focus();
  }, []);

  const set = (key: keyof FormState) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) => {
    setForm((prev) => ({ ...prev, [key]: e.target.value }));
  };

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const err = validateForm(form, isEdit);
    if (err) { setError(err); return; }
    setSaving(true);
    setError(null);
    try {
      let saved: RuntPersonOverride;
      if (isEdit) {
        saved = await updateRuntPersonOverride(tenantId, override.id, formToUpdateInput(form));
      } else {
        saved = await createRuntPersonOverride(tenantId, formToCreateInput(form));
      }
      onSaved(saved);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : "Error al guardar. Intenta de nuevo.";
      setError(msg);
    } finally {
      setSaving(false);
    }
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label={isEdit ? "Editar override de persona RUNT" : "Crear override de persona RUNT"}
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
    >
      <div className="relative w-full max-w-2xl max-h-[90vh] overflow-y-auto rounded-2xl bg-[var(--surface-default)] shadow-xl">
        {/* Encabezado */}
        <div className="sticky top-0 z-10 flex items-center justify-between border-b border-[var(--border-subtle)] bg-[var(--surface-default)] px-6 py-4">
          <h2 className="text-base font-semibold text-[var(--content-primary)]">
            {isEdit ? "Editar override" : "Nuevo override de persona RUNT"}
          </h2>
          <button
            type="button"
            aria-label="Cerrar"
            onClick={onClose}
            className="rounded-lg p-1.5 hover:bg-[var(--surface-hover)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-default)]"
          >
            <svg aria-hidden="true" className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        <form onSubmit={handleSubmit} className="px-6 py-5 space-y-5" noValidate>
          {error && (
            <p role="alert" className="rounded-lg bg-[var(--status-error-subtle)] px-3 py-2 text-sm text-[var(--status-error)]">
              {error}
            </p>
          )}

          {/* Identificación */}
          <fieldset className="space-y-3">
            <legend className="text-xs font-semibold uppercase tracking-wide text-[var(--content-secondary)]">
              Identificación
            </legend>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label htmlFor="dlg-doc-type" className={LABEL_CLS}>
                  Tipo de documento <span aria-hidden="true">*</span>
                </label>
                <select
                  id="dlg-doc-type"
                  value={form.documentType}
                  onChange={set("documentType")}
                  disabled={isEdit}
                  aria-required="true"
                  className={INPUT_CLS}
                >
                  {DOC_OPTIONS.map((o) => (
                    <option key={o.value} value={o.value}>{o.label}</option>
                  ))}
                </select>
              </div>
              <div>
                <label htmlFor="dlg-doc-num" className={LABEL_CLS}>
                  Número de documento <span aria-hidden="true">*</span>
                </label>
                <input
                  ref={firstRef}
                  id="dlg-doc-num"
                  type="text"
                  value={form.documentNumber}
                  onChange={set("documentNumber")}
                  disabled={isEdit}
                  aria-required="true"
                  className={INPUT_CLS}
                  placeholder="Ej. 1234567890"
                  maxLength={30}
                />
              </div>
            </div>
          </fieldset>

          {/* Nombre */}
          <fieldset className="space-y-3">
            <legend className="text-xs font-semibold uppercase tracking-wide text-[var(--content-secondary)]">
              Nombre
            </legend>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label htmlFor="dlg-first-name" className={LABEL_CLS}>
                  Primer nombre <span aria-hidden="true">*</span>
                </label>
                <input
                  id="dlg-first-name"
                  type="text"
                  value={form.firstName}
                  onChange={set("firstName")}
                  aria-required="true"
                  className={INPUT_CLS}
                  maxLength={100}
                />
              </div>
              <div>
                <label htmlFor="dlg-second-name" className={LABEL_CLS}>Segundo nombre</label>
                <input
                  id="dlg-second-name"
                  type="text"
                  value={form.secondName}
                  onChange={set("secondName")}
                  className={INPUT_CLS}
                  maxLength={100}
                />
              </div>
              <div>
                <label htmlFor="dlg-first-last" className={LABEL_CLS}>
                  Primer apellido <span aria-hidden="true">*</span>
                </label>
                <input
                  id="dlg-first-last"
                  type="text"
                  value={form.firstLastName}
                  onChange={set("firstLastName")}
                  aria-required="true"
                  className={INPUT_CLS}
                  maxLength={100}
                />
              </div>
              <div>
                <label htmlFor="dlg-second-last" className={LABEL_CLS}>Segundo apellido</label>
                <input
                  id="dlg-second-last"
                  type="text"
                  value={form.secondLastName}
                  onChange={set("secondLastName")}
                  className={INPUT_CLS}
                  maxLength={100}
                />
              </div>
            </div>
          </fieldset>

          {/* Estado RUNT */}
          <fieldset className="space-y-3">
            <legend className="text-xs font-semibold uppercase tracking-wide text-[var(--content-secondary)]">
              Estado RUNT (opcional)
            </legend>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label htmlFor="dlg-citizen" className={LABEL_CLS}>Estado ciudadano</label>
                <input
                  id="dlg-citizen"
                  type="text"
                  value={form.citizenStatus}
                  onChange={set("citizenStatus")}
                  className={INPUT_CLS}
                  placeholder="Ej. ACTIVA"
                  maxLength={50}
                />
              </div>
              <div>
                <label htmlFor="dlg-driver" className={LABEL_CLS}>Estado conductor</label>
                <input
                  id="dlg-driver"
                  type="text"
                  value={form.driverStatus}
                  onChange={set("driverStatus")}
                  className={INPUT_CLS}
                  placeholder="Ej. ACTIVO"
                  maxLength={50}
                />
              </div>
              <div>
                <label htmlFor="dlg-license" className={LABEL_CLS}>Licencia activa</label>
                <select
                  id="dlg-license"
                  value={form.hasActiveLicense}
                  onChange={set("hasActiveLicense")}
                  className={INPUT_CLS}
                >
                  <option value="">Sin definir</option>
                  <option value="true">Sí</option>
                  <option value="false">No</option>
                </select>
              </div>
              <div>
                <label htmlFor="dlg-categories" className={LABEL_CLS}>Categorías de licencia</label>
                <input
                  id="dlg-categories"
                  type="text"
                  value={form.licenseCategories}
                  onChange={set("licenseCategories")}
                  className={INPUT_CLS}
                  placeholder="Ej. A2, B1"
                  maxLength={100}
                />
              </div>
              <div>
                <label htmlFor="dlg-fines" className={LABEL_CLS}>Multas pendientes</label>
                <select
                  id="dlg-fines"
                  value={form.hasPendingFines}
                  onChange={set("hasPendingFines")}
                  className={INPUT_CLS}
                >
                  <option value="">Sin definir</option>
                  <option value="true">Sí</option>
                  <option value="false">No</option>
                </select>
              </div>
              <div>
                <label htmlFor="dlg-paz" className={LABEL_CLS}>Nro. paz y salvo</label>
                <input
                  id="dlg-paz"
                  type="text"
                  value={form.nroPazYSalvo}
                  onChange={set("nroPazYSalvo")}
                  className={INPUT_CLS}
                  maxLength={50}
                />
              </div>
            </div>
          </fieldset>

          {/* Meta del override */}
          <fieldset className="space-y-3">
            <legend className="text-xs font-semibold uppercase tracking-wide text-[var(--content-secondary)]">
              Vigencia y motivo
            </legend>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label htmlFor="dlg-expires" className={LABEL_CLS}>Vence el (opcional)</label>
                <input
                  id="dlg-expires"
                  type="datetime-local"
                  value={form.expiresAt}
                  onChange={set("expiresAt")}
                  className={INPUT_CLS}
                />
              </div>
              {isEdit && (
                <div>
                  <label htmlFor="dlg-is-active" className={LABEL_CLS}>Estado del override</label>
                  <select
                    id="dlg-is-active"
                    value={form.isActive}
                    onChange={set("isActive")}
                    className={INPUT_CLS}
                  >
                    <option value="true">Activo</option>
                    <option value="false">Inactivo</option>
                  </select>
                </div>
              )}
            </div>
            <div>
              <label htmlFor="dlg-reason" className={LABEL_CLS}>
                Motivo <span aria-hidden="true">*</span>
              </label>
              <textarea
                id="dlg-reason"
                value={form.reason}
                onChange={set("reason")}
                aria-required="true"
                rows={2}
                maxLength={500}
                className={`${INPUT_CLS} resize-none`}
                placeholder="Describe la razón de este override…"
              />
            </div>
          </fieldset>

          {/* Acciones */}
          <div className="flex justify-end gap-3 pt-2">
            <button type="button" onClick={onClose} className={BTN_SECONDARY} disabled={saving}>
              Cancelar
            </button>
            <button type="submit" className={BTN_PRIMARY} disabled={saving}>
              {saving ? "Guardando…" : isEdit ? "Guardar cambios" : "Crear override"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

// ── Panel principal ───────────────────────────────────────────────────────────

export function RuntPersonaOverridesPanel() {
  const { show } = useToast();

  // Tenant
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [tenantId, setTenantId] = useState("");

  // Datos
  const [overrides, setOverrides] = useState<RuntPersonOverride[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");

  // Diálogo
  const [dialog, setDialog] = useState<"create" | RuntPersonOverride | null>(null);

  // Tabla
  const pg = usePaginacion(CLAVE_TABLA);
  const { setPage } = pg;
  const volverAPrimera = useCallback(() => setPage(1), [setPage]);
  const filtros = useTablaFiltros({ columnas: COLUMNAS_FILTRO, onCambio: volverAPrimera });

  // Cargar compañías al montar
  useEffect(() => {
    const ctrl = new AbortController();
    fetchAllCompanies({ estadoActivo: true }, ctrl.signal)
      .then((data) => { if (!ctrl.signal.aborted) setCompanies(data); })
      .catch(() => { /* silencioso */ });
    return () => ctrl.abort();
  }, []);

  // Cargar overrides cuando cambia el tenant
  const load = useCallback(async (tenant: string, signal?: AbortSignal) => {
    if (!tenant) { setOverrides([]); setStatus("empty"); return; }
    setStatus("loading");
    try {
      const data = await listRuntPersonOverrides(tenant, undefined, signal);
      if (signal?.aborted) return;
      setOverrides(data);
      setStatus(data.length === 0 ? "empty" : "ready");
    } catch {
      if (!signal?.aborted) setStatus("error");
    }
  }, []);

  useEffect(() => {
    const ctrl = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga al cambiar tenant: skeleton intencional
    void load(tenantId, ctrl.signal);
    return () => ctrl.abort();
  }, [tenantId, load]);

  // Filtrado cliente
  const visibles = useMemo(
    () =>
      filtrarFilas(overrides, filtros.condicionesTotales, filtros.busqueda, {
        columnas: {
          documento: (o) => [o.documentType, o.documentNumber],
          nombre: (o) => [o.firstName, o.secondName ?? "", o.firstLastName, o.secondLastName ?? ""],
          activo: (o) => (o.isActive ? "activo" : "inactivo"),
        },
      }),
    [overrides, filtros.condicionesTotales, filtros.busqueda],
  );

  // Acciones
  async function handleDeactivate(override: RuntPersonOverride) {
    if (!tenantId) return;
    try {
      const updated = await deactivateRuntPersonOverride(tenantId, override.id);
      setOverrides((prev) => prev.map((o) => (o.id === updated.id ? updated : o)));
      show("Override desactivado.", "success");
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : "Error al desactivar.";
      show(msg, "error");
    }
  }

  function handleSaved(saved: RuntPersonOverride) {
    setOverrides((prev) => {
      const idx = prev.findIndex((o) => o.id === saved.id);
      if (idx >= 0) {
        const copy = [...prev];
        copy[idx] = saved;
        return copy;
      }
      return [saved, ...prev];
    });
    setStatus("ready");
    setDialog(null);
    show(dialog === "create" ? "Override creado." : "Override actualizado.", "success");
  }

  // Columnas
  const columns: DataTableColumn<RuntPersonOverride>[] = [
    {
      key: "documento",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="documento">
          Documento
        </EncabezadoFiltrable>
      ),
      render: (o) => (
        <span className="font-mono text-xs">
          {o.documentType} {o.documentNumber}
        </span>
      ),
    },
    {
      key: "nombre",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="nombre">
          Nombre
        </EncabezadoFiltrable>
      ),
      render: (o) =>
        [o.firstName, o.secondName, o.firstLastName, o.secondLastName]
          .filter(Boolean)
          .join(" "),
    },
    {
      key: "activo",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="activo">
          Estado
        </EncabezadoFiltrable>
      ),
      render: (o) => (
        <StatusBadge
          label={o.isActive ? "Activo" : "Inactivo"}
          tone={o.isActive ? "success" : "neutral"}
        />
      ),
    },
    {
      key: "expires",
      header: "Vence",
      render: (o) =>
        o.expiresAt ? (
          <span className="text-xs text-[var(--content-secondary)]">
            {formatFechaHora(o.expiresAt)}
          </span>
        ) : (
          <span className="text-xs text-[var(--content-disabled)]">Sin vencimiento</span>
        ),
    },
    {
      key: "reason",
      header: "Motivo",
      render: (o) => (
        <span className="max-w-xs truncate text-xs text-[var(--content-secondary)]" title={o.reason}>
          {o.reason}
        </span>
      ),
    },
    {
      key: "acciones",
      header: "Acciones",
      render: (o) => (
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => setDialog(o)}
            className={BTN_SECONDARY.replace("px-4 py-2", "px-3 py-1.5").replace("text-sm", "text-xs")}
            aria-label={`Editar override de ${o.documentType} ${o.documentNumber}`}
          >
            Editar
          </button>
          {o.isActive && (
            <button
              type="button"
              onClick={() => void handleDeactivate(o)}
              className={BTN_DANGER}
              aria-label={`Desactivar override de ${o.documentType} ${o.documentNumber}`}
            >
              Desactivar
            </button>
          )}
        </div>
      ),
    },
  ];

  const hasNoTenant = !tenantId;

  return (
    <div className="space-y-6">
      {/* Selector de tenant */}
      <div className="flex flex-wrap items-end gap-4">
        <div className="min-w-[280px]">
          <CompanySelector
            id="rpo-company"
            companies={companies}
            value={tenantId}
            onChange={(id) => { setTenantId(id); setPage(1); }}
            defaultLabel="Selecciona una compañía…"
            hideLabel={false}
          />
        </div>
        <button
          type="button"
          onClick={() => setDialog("create")}
          disabled={hasNoTenant}
          className={BTN_PRIMARY}
          aria-label="Crear nuevo override de persona RUNT"
        >
          <svg aria-hidden="true" className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 4v16m8-8H4" />
          </svg>
          Nuevo override
        </button>
      </div>

      {/* Tabla */}
      {hasNoTenant ? (
        <p className="text-sm text-[var(--content-secondary)]">
          Selecciona una compañía para ver y gestionar sus overrides de persona RUNT.
        </p>
      ) : (
        <UiStateBoundary
          status={status}
          emptyMessage="Esta compañía no tiene overrides de persona RUNT. Usa «Nuevo override» para crear el primero."
          errorMessage="No se pudieron cargar los overrides de persona RUNT."
          onRetry={() => void load(tenantId)}
        >
          <DataTable<RuntPersonOverride>
            ariaLabel="Overrides de persona RUNT"
            columns={columns}
            rows={pg.paginar(visibles)}
            getRowKey={(o) => o.id}
            emptyContent={<TablaFiltrosVacio onLimpiar={filtros.limpiar} />}
            pagination={{
              page: pg.page,
              pageSize: pg.pageSize,
              totalCount: visibles.length,
              onPageChange: pg.setPage,
              onPageSizeChange: pg.setPageSize,
            }}
          />
        </UiStateBoundary>
      )}

      {/* Diálogo */}
      {dialog !== null && tenantId && (
        <OverrideDialog
          override={dialog === "create" ? null : dialog}
          tenantId={tenantId}
          onClose={() => setDialog(null)}
          onSaved={handleSaved}
        />
      )}
    </div>
  );
}

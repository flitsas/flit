"use client";

import { useEffect, useState } from "react";
import { Loader2, Pencil } from "lucide-react";
import { OtSidePanel } from "@/components/admin/transit-offices/OtSidePanel";
import { OT_INPUT_CLS } from "@/components/admin/transit-offices/ot-form-styles";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { ApiValidationError } from "@/lib/api/types";
import type {
  AssignableProcedureType,
  LegalRepresentativeCompanyInput,
  LegalRepresentativeInput,
  LegalRepresentativeItem,
  LegalRepresentativeSaved,
} from "@/lib/api/admin-legal-representatives";
import { fetchLegalRepresentative } from "@/lib/api/admin-legal-representatives";
import { digitsOnly } from "@/lib/format/currency";
import { sanitizeDocNumber } from "@/lib/validation/fieldRules";
import { formatFecha } from "@/lib/format/date";
import { procedureTypeLabels } from "./legalRepresentativesDisplay";
import { RepresentativeCompaniesAccordion } from "./RepresentativeCompaniesAccordion";
import { SignatureVaultSelector } from "./SignatureVaultSelector";
import { IdentityActionsBlock } from "./IdentityActionsBlock";

/** Modo del panel: consulta (solo lectura), alta (formulario en blanco) o edición (formulario precargado). */
export type PanelMode = "view" | "create" | "edit";

// Tipos de documento del representante — mismos que en el resto de la app (ActorsForm / Baúl).
const DOC_TYPE_OPTIONS: { value: string; label: string }[] = [
  { value: "CC", label: "Cédula de ciudadanía (CC)" },
  { value: "CE", label: "Cédula de extranjería (CE)" },
  { value: "PAS", label: "Pasaporte (PAS)" },
  { value: "TI", label: "Tarjeta de identidad (TI)" },
];

export interface LegalRepresentativesFormPanelProps {
  open: boolean;
  /** Modo del panel: consulta, alta o edición. */
  mode: PanelMode;
  /** ID del representante a cargar (view/edit); null en alta. */
  representativeId: string | null;
  /** TenantId necesario para la llamada a GET /{id}. */
  tenantId: string;
  /** Catálogo de tipos de trámite asignables (activos + publicados). */
  procedureTypes: AssignableProcedureType[];
  onClose: () => void;
  onSubmit: (input: LegalRepresentativeInput) => Promise<LegalRepresentativeSaved>;
  onSaved: (saved: LegalRepresentativeSaved) => void;
  onError: (message: string) => void;
  /** AC2: el usuario eligió editar desde la vista de consulta; el padre cambia el modo sin cerrar. */
  onSwitchToEdit: () => void;
}

// Una fila de empresa dentro del formulario (HU #10934). Exportada para RepresentativeCompaniesAccordion.
export interface CompanyRow {
  nit: string;
  name: string;
  email: string;
  address: string;
  city: string;
  phone: string;
}

interface FormState {
  companies: CompanyRow[];
  documentType: string;
  documentNumber: string;
  firstLastName: string;
  secondLastName: string;
  name: string;
  email: string;
  address: string;
  city: string;
  phone: string;
  procedureTypeIds: string[];
  /** HU #11180 — firma del baúl elegida explícitamente por el administrador. */
  signatureVaultId: string | null;
}

const EMPTY_COMPANY: CompanyRow = { nit: "", name: "", email: "", address: "", city: "", phone: "" };

const EMPTY: FormState = {
  companies: [{ ...EMPTY_COMPANY }],
  documentType: "CC",
  documentNumber: "",
  firstLastName: "",
  secondLastName: "",
  name: "",
  email: "",
  address: "",
  city: "",
  phone: "",
  procedureTypeIds: [],
  signatureVaultId: null,
};

// HU #11058 — la precarga tiene que traer TODAS las compañías del representante y el contacto COMPLETO
// de cada una. El guardado reenvía esta lista y el backend hace upsert con lo que reciba: un campo que
// llegue en blanco se persiste como null.
function fromItem(item: LegalRepresentativeItem): FormState {
  const companies: CompanyRow[] =
    item.companies && item.companies.length > 0
      ? item.companies.map((c) => ({
          nit: c.nit,
          name: c.name,
          email: c.email ?? "",
          address: c.address ?? "",
          city: c.city ?? "",
          phone: c.phone ?? "",
        }))
      : [{ ...EMPTY_COMPANY, nit: item.companyDocumentNumber, name: item.companyName }];
  return {
    companies,
    documentType: item.documentType || "CC",
    documentNumber: item.documentNumber,
    firstLastName: item.firstLastName,
    secondLastName: item.secondLastName ?? "",
    name: item.name,
    email: item.email ?? "",
    address: item.address ?? "",
    city: item.city ?? "",
    phone: item.phone ?? "",
    procedureTypeIds: [...item.procedureTypeIds],
    // HU #11180 — precargar la firma seleccionada previamente (AC2).
    signatureVaultId: item.signatureVaultId ?? null,
  };
}

/**
 * Panel lateral unificado del representante legal (HU #11178): una sola superficie con tres modos:
 * - `view`: consulta con toda la información (identidad, firma, compañías, persona, trámites).
 *   El botón «Editar» llama a `onSwitchToEdit` para pasar a `edit` SIN cerrar (AC2).
 * - `create`: formulario en blanco para el alta. Tras guardar emite `onSaved` y el padre decide
 *   (típicamente: no cierra y pasa a `edit` sobre el recién creado — AC5).
 * - `edit`: formulario precargado con `GET /{id}` completo (compañías, identidad, firma — AC3).
 *   Muestra skeleton de carga mientras obtiene el detalle.
 */
export function LegalRepresentativesFormPanel({
  open,
  mode,
  representativeId,
  tenantId,
  procedureTypes,
  onClose,
  onSubmit,
  onSaved,
  onError,
  onSwitchToEdit,
}: LegalRepresentativesFormPanelProps) {
  const [detail, setDetail] = useState<LegalRepresentativeItem | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState(false);
  const [form, setForm] = useState<FormState>(EMPTY);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [banner, setBanner] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // AC3 — al abrir en view/edit se carga el representante completo desde GET /{id}. Si ya se cargó
  // en view y el usuario pulsa «Editar» (AC2), el padre solo cambia el modo (mode: view → edit); el
  // detalle ya está en caché y no se vuelve a pedir, evitando el parpadeo que AC2 prohíbe.
  useEffect(() => {
    if (!open) {
      setDetail(null);
      setDetailLoading(false);
      setDetailError(false);
      return;
    }

    setFieldErrors({});
    setBanner(null);

    if (mode === "create") {
      setForm(EMPTY);
      setDetail(null);
      return;
    }

    if (!representativeId) return;

    // Detalle ya en caché para este representante → no re-pedir.
    if (detail?.id === representativeId) {
      if (mode === "edit") {
        setForm(fromItem(detail));
      }
      return;
    }

    setDetailLoading(true);
    setDetailError(false);
    const controller = new AbortController();

    fetchLegalRepresentative(tenantId, representativeId, controller.signal)
      .then((full) => {
        if (controller.signal.aborted) return;
        setDetail(full);
        setDetailLoading(false);
        if (mode === "edit") {
          setForm(fromItem(full));
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setDetailLoading(false);
          setDetailError(true);
        }
      });

    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, mode, representativeId, tenantId]);

  // ── Helpers de formulario ────────────────────────────────────────────────────

  const patch = (p: Partial<FormState>) => setForm((f) => ({ ...f, ...p }));

  const patchCompany = (index: number, p: Partial<CompanyRow>) =>
    setForm((f) => ({
      ...f,
      companies: f.companies.map((c, i) => (i === index ? { ...c, ...p } : c)),
    }));

  const addCompany = () =>
    setForm((f) => ({ ...f, companies: [...f.companies, { ...EMPTY_COMPANY }] }));

  const removeCompany = (index: number) =>
    setForm((f) => ({
      ...f,
      companies: f.companies.length <= 1 ? [{ ...EMPTY_COMPANY }] : f.companies.filter((_, i) => i !== index),
    }));

  const toggleProcedureType = (id: string) =>
    setForm((f) => ({
      ...f,
      procedureTypeIds: f.procedureTypeIds.includes(id)
        ? f.procedureTypeIds.filter((x) => x !== id)
        : [...f.procedureTypeIds, id],
    }));

  const companiesValid =
    form.companies.length > 0 &&
    form.companies.every((c) => c.nit.trim() !== "" && c.name.trim() !== "");
  const isValid =
    companiesValid &&
    form.documentType.trim() !== "" &&
    form.documentNumber.trim() !== "" &&
    form.firstLastName.trim() !== "" &&
    form.name.trim() !== "";

  const canSubmit = isValid && !submitting;

  const handleSubmit = async () => {
    setSubmitting(true);
    setBanner(null);
    setFieldErrors({});
    try {
      const trimmed = (v: string) => (v.trim() === "" ? null : v.trim());
      const companies: LegalRepresentativeCompanyInput[] = form.companies.map((c) => ({
        nit: c.nit.trim(),
        name: c.name.trim(),
        email: trimmed(c.email),
        address: trimmed(c.address),
        city: trimmed(c.city),
        phone: trimmed(c.phone),
      }));
      const primary = companies[0];
      const saved = await onSubmit({
        companies,
        // Retrocompatibilidad: la primera compañía también viaja en los campos planos.
        companyNit: primary.nit,
        companyName: primary.name,
        companyEmail: primary.email,
        companyAddress: primary.address,
        companyCity: primary.city,
        companyPhone: primary.phone,
        documentType: form.documentType,
        documentNumber: form.documentNumber.trim(),
        firstLastName: form.firstLastName.trim(),
        secondLastName: trimmed(form.secondLastName),
        name: form.name.trim(),
        email: trimmed(form.email),
        address: trimmed(form.address),
        city: trimmed(form.city),
        phone: trimmed(form.phone),
        procedureTypeIds: form.procedureTypeIds,
        // HU #11180 — firma del baúl elegida (null = usar el resolver automático del backend).
        signatureVaultId: form.signatureVaultId ?? null,
      });
      onSaved(saved);
    } catch (err) {
      if (err instanceof ApiValidationError) {
        const mapped: Record<string, string> = {};
        for (const e of err.errors) {
          if (e.field) mapped[e.field] = e.message;
        }
        setFieldErrors(mapped);
        setBanner("Revisa los campos marcados: hay valores inválidos.");
      } else {
        onError(
          mode === "edit"
            ? "No se pudo actualizar el representante. Intenta de nuevo."
            : "No se pudo registrar el representante. Intenta de nuevo.",
        );
      }
    } finally {
      setSubmitting(false);
    }
  };

  const errStyle = (field: string) =>
    fieldErrors[field] ? { borderColor: "#FF4E00" } : undefined;

  // HU #11179 — AC4: tras guardar una escritura, re-carga el detalle completo para refrescar la
  // lista de escrituras en el acordeón. Sin recarga de página: solo actualiza el estado local.
  const refreshDetail = () => {
    if (!representativeId) return;
    fetchLegalRepresentative(tenantId, representativeId)
      .then((full) => setDetail(full))
      .catch(() => {
        // Fallo silencioso: la escritura ya se guardó; el gestor puede reabrir el panel.
      });
  };

  // ── Metadatos del panel ──────────────────────────────────────────────────────

  const title =
    mode === "view"
      ? "Detalle del representante"
      : mode === "edit"
        ? "Editar representante legal"
        : "Nuevo representante legal";

  const ariaLabel =
    mode === "view"
      ? "Ver representante legal"
      : mode === "edit"
        ? "Editar representante legal"
        : "Registrar representante legal";

  const footer =
    mode === "view" ? (
      <button
        type="button"
        onClick={onSwitchToEdit}
        className="flex w-full items-center justify-center gap-2 rounded-xl py-2.5 text-xs font-semibold text-white"
        style={{ background: "#557EFF" }}
        aria-label="Pasar a modo edición"
      >
        <Pencil className="h-4 w-4" aria-hidden="true" />
        Editar
      </button>
    ) : (
      <button
        type="button"
        disabled={!canSubmit}
        onClick={() => void handleSubmit()}
        className="flex w-full items-center justify-center gap-2 rounded-xl py-2.5 text-xs font-semibold text-white disabled:opacity-50"
        style={{ background: "#557EFF" }}
      >
        {submitting && <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />}
        {mode === "edit" ? "Guardar cambios" : "Registrar representante"}
      </button>
    );

  // ── Render ───────────────────────────────────────────────────────────────────

  return (
    <OtSidePanel
      open={open}
      title={title}
      ariaLabel={ariaLabel}
      onClose={onClose}
      disabled={submitting}
      footer={footer}
    >
      {mode === "view" ? renderView() : renderForm()}
    </OtSidePanel>
  );

  // ── Vista de consulta (modo view) ────────────────────────────────────────────

  function renderView() {
    if (detailLoading) return <PanelSkeleton />;
    if (detailError) {
      return (
        <p
          role="alert"
          className="rounded-xl border px-3 py-2 text-[11px] font-medium"
          style={{ borderColor: "#FF4E00", color: "#FF4E00" }}
        >
          No se pudo cargar la información del representante. Cierra e inténtalo de nuevo.
        </p>
      );
    }
    if (!detail) return <PanelSkeleton />;

    const tramites = procedureTypeLabels(detail.procedureTypeIds, procedureTypes);

    return (
      <div className="space-y-5">
        {/* HU #11180 — Bloque de identidad con acciones (AC5, AC6) */}
        <div data-testid="rl-identidad">
          <IdentityActionsBlock
            tenantId={tenantId}
            representativeId={representativeId}
            identityStatus={detail.identityStatus}
            identityValidUntil={detail.identityValidUntil}
            firmaBaulVigente={detail.firmaBaulVigente}
            firmaBaulVigenteHasta={detail.firmaBaulVigenteHasta}
            email={detail.email}
            onRefresh={refreshDetail}
          />
        </div>

        {/* Firma del baúl — solo lectura en modo view */}
        <div
          className="rounded-xl border border-[#DFE5ED] p-3"
          data-testid="rl-firma-baul"
        >
          <p className="text-[11px] font-bold uppercase tracking-wide opacity-60">
            Firma del baúl
          </p>
          <div className="mt-1.5">
            <span
              className="inline-flex items-center rounded-full px-2.5 py-1 text-[10px] font-semibold"
              style={
                detail.firmaBaulVigente
                  ? { background: "rgba(112,207,58,0.14)", color: "#3f7a15" }
                  : { background: "rgba(245,158,11,0.16)", color: "#b45309" }
              }
            >
              {detail.firmaBaulVigente
                ? "Firma vigente"
                : detail.signatureVaultId
                  ? "Firma vencida"
                  : "Sin firma registrada"}
            </span>
            {detail.firmaBaulVigente && detail.firmaBaulVigenteHasta && (
              <p className="mt-1 text-[10px] opacity-60">
                Válida hasta {formatFecha(detail.firmaBaulVigenteHasta)}
              </p>
            )}
          </div>
        </div>

        {/* Datos personales del representante */}
        <section aria-label="Datos del representante">
          <h3 className="mb-2 text-[11px] font-bold uppercase tracking-wide opacity-60">
            Representante legal
          </h3>
          <dl className="grid grid-cols-1 gap-2 text-xs sm:grid-cols-2">
            <DlField label="Tipo de documento" value={detail.documentType} />
            <DlField label="Número de documento" value={detail.documentNumber} />
            <DlField label="Nombres" value={detail.name} />
            <DlField label="Primer apellido" value={detail.firstLastName} />
            {detail.secondLastName && (
              <DlField label="Segundo apellido" value={detail.secondLastName} />
            )}
            {detail.email && <DlField label="Correo" value={detail.email} />}
            {detail.phone && <DlField label="Teléfono" value={detail.phone} />}
            {detail.city && <DlField label="Ciudad" value={detail.city} />}
          </dl>
        </section>

        {/* Empresas representadas — acordeón HU #11179 (AC1–AC3) */}
        {detail.companies.length > 0 && (
          <section aria-label="Empresas representadas">
            <RepresentativeCompaniesAccordion
              mode="view"
              companies={detail.companies}
              formCompanies={[]}
              onContactChange={() => undefined}
              onAddCompany={() => undefined}
              onRemoveCompany={() => undefined}
              fieldErrors={{}}
              tenantId={tenantId}
              representativeId={representativeId}
              onDeedSaved={refreshDetail}
              onError={onError}
            />
          </section>
        )}

        {/* Tipos de trámite */}
        <section aria-label="Tipos de trámite">
          <h3 className="mb-2 text-[11px] font-bold uppercase tracking-wide opacity-60">
            Tipos de trámite que puede firmar
          </h3>
          {tramites.length === 0 ? (
            <p className="text-[11px] opacity-60">Ninguno asignado.</p>
          ) : (
            <div className="flex flex-wrap gap-1.5">
              {tramites.map((t, i) => (
                <StatusBadge key={`${t}-${i}`} tone="info" label={t} />
              ))}
            </div>
          )}
        </section>
      </div>
    );
  }

  // ── Formulario (modos create y edit) ─────────────────────────────────────────

  function renderForm() {
    // En edit: skeleton mientras se carga el detalle completo (AC3).
    if (mode === "edit" && detailLoading) return <PanelSkeleton />;
    if (mode === "edit" && detailError) {
      return (
        <p
          role="alert"
          className="rounded-xl border px-3 py-2 text-[11px] font-medium"
          style={{ borderColor: "#FF4E00", color: "#FF4E00" }}
        >
          No se pudo cargar la información del representante. Cierra el panel e inténtalo de nuevo.
        </p>
      );
    }

    return (
      <div className="space-y-5">
        {banner && (
          <p
            role="alert"
            className="rounded-xl border px-3 py-2 text-[11px] font-medium"
            style={{ borderColor: "#FF4E00", color: "#FF4E00" }}
          >
            {banner}
          </p>
        )}

        {/* Empresas representadas — acordeón HU #11179 (AC1–AC5) */}
        <section className="space-y-3">
          <RepresentativeCompaniesAccordion
            mode={mode}
            companies={detail?.companies ?? []}
            formCompanies={form.companies}
            onContactChange={patchCompany}
            onAddCompany={addCompany}
            onRemoveCompany={removeCompany}
            fieldErrors={fieldErrors}
            tenantId={tenantId}
            representativeId={representativeId}
            onDeedSaved={refreshDetail}
            onError={onError}
          />
        </section>

        {/* Datos del representante-persona */}
        <section className="space-y-3">
          <h3 className="text-[11px] font-bold uppercase tracking-wide opacity-60">
            Representante legal
          </h3>

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <Field id="lr-doctype" label="Tipo de documento" error={fieldErrors.documentType}>
              <select
                id="lr-doctype"
                value={form.documentType}
                onChange={(e) => {
                  const documentType = e.target.value;
                  patch({
                    documentType,
                    documentNumber: sanitizeDocNumber(form.documentNumber, documentType),
                  });
                }}
                className={OT_INPUT_CLS}
                style={errStyle("documentType")}
              >
                {DOC_TYPE_OPTIONS.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.label}
                  </option>
                ))}
              </select>
            </Field>
            <Field
              id="lr-docnumber"
              label="Número de documento"
              error={fieldErrors.documentNumber}
            >
              <input
                id="lr-docnumber"
                value={form.documentNumber}
                onChange={(e) =>
                  patch({ documentNumber: sanitizeDocNumber(e.target.value, form.documentType) })
                }
                className={OT_INPUT_CLS}
                style={errStyle("documentNumber")}
                inputMode={form.documentType === "PAS" ? "text" : "numeric"}
                autoComplete="off"
              />
            </Field>
          </div>

          <Field id="lr-name" label="Nombres" error={fieldErrors.name}>
            <input
              id="lr-name"
              value={form.name}
              onChange={(e) => patch({ name: e.target.value })}
              className={OT_INPUT_CLS}
              style={errStyle("name")}
              placeholder="Nombres del representante"
            />
          </Field>

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <Field id="lr-firstLastName" label="Primer apellido" error={fieldErrors.firstLastName}>
              <input
                id="lr-firstLastName"
                value={form.firstLastName}
                onChange={(e) => patch({ firstLastName: e.target.value })}
                className={OT_INPUT_CLS}
                style={errStyle("firstLastName")}
              />
            </Field>
            <Field
              id="lr-secondLastName"
              label="Segundo apellido (opcional)"
              error={fieldErrors.secondLastName}
            >
              <input
                id="lr-secondLastName"
                value={form.secondLastName}
                onChange={(e) => patch({ secondLastName: e.target.value })}
                className={OT_INPUT_CLS}
                style={errStyle("secondLastName")}
              />
            </Field>
          </div>

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <Field id="lr-email" label="Correo (opcional)" error={fieldErrors.email}>
              <input
                id="lr-email"
                type="email"
                value={form.email}
                onChange={(e) => patch({ email: e.target.value })}
                className={OT_INPUT_CLS}
                style={errStyle("email")}
                placeholder="Para la validación de identidad"
              />
            </Field>
            <Field id="lr-phone" label="Teléfono (opcional)" error={fieldErrors.phone}>
              <input
                id="lr-phone"
                type="tel"
                inputMode="numeric"
                pattern="[0-9]*"
                autoComplete="tel"
                value={form.phone}
                onChange={(e) => patch({ phone: digitsOnly(e.target.value) })}
                className={OT_INPUT_CLS}
                style={errStyle("phone")}
              />
            </Field>
            <Field id="lr-address" label="Dirección (opcional)" error={fieldErrors.address}>
              <input
                id="lr-address"
                value={form.address}
                onChange={(e) => patch({ address: e.target.value })}
                className={OT_INPUT_CLS}
                style={errStyle("address")}
              />
            </Field>
            <Field id="lr-city" label="Ciudad (opcional)" error={fieldErrors.city}>
              <input
                id="lr-city"
                value={form.city}
                onChange={(e) => patch({ city: e.target.value })}
                className={OT_INPUT_CLS}
                style={errStyle("city")}
              />
            </Field>
          </div>
        </section>

        {/* HU #11180 — Firma del baúl (AC1, AC2, AC3) */}
        <section className="space-y-2">
          <h3 className="text-[11px] font-bold uppercase tracking-wide opacity-60">
            Firma del baúl
          </h3>
          <SignatureVaultSelector
            tenantId={tenantId}
            documentType={form.documentType}
            documentNumber={form.documentNumber}
            value={form.signatureVaultId}
            onChange={(id) => patch({ signatureVaultId: id })}
          />
        </section>

        {/* HU #11180 — Identidad (AC4, AC5, AC6) — disponible solo en modo edit */}
        {mode === "edit" && (
          <IdentityActionsBlock
            tenantId={tenantId}
            representativeId={representativeId}
            identityStatus={detail?.identityStatus}
            identityValidUntil={detail?.identityValidUntil}
            firmaBaulVigente={detail?.firmaBaulVigente}
            firmaBaulVigenteHasta={detail?.firmaBaulVigenteHasta}
            email={form.email}
            onRefresh={refreshDetail}
          />
        )}

        {/* AC4: en modo create, aviso de identidad automática */}
        {mode === "create" && (
          <IdentityActionsBlock
            tenantId={tenantId}
            representativeId={null}
            email={form.email}
            onRefresh={() => undefined}
          />
        )}

        {/* Tipos de trámite */}
        <fieldset className="space-y-2">
          <legend className="text-[11px] font-bold uppercase tracking-wide opacity-60">
            Tipos de trámite que puede firmar
          </legend>
          {fieldErrors.procedureTypeIds && (
            <p
              className="text-[11px] font-medium"
              style={{ color: "#FF4E00" }}
              role="alert"
            >
              {fieldErrors.procedureTypeIds}
            </p>
          )}
          {procedureTypes.length === 0 ? (
            <p className="text-[11px] opacity-60">
              No hay tipos de trámite habilitados en el módulo de trámites. Publica al menos un tipo
              (activo y publicado) para poder asignarlo al representante.
            </p>
          ) : (
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
              {procedureTypes.map((pt) => {
                const checked = form.procedureTypeIds.includes(pt.id);
                return (
                  <label
                    key={pt.id}
                    className="flex cursor-pointer items-center gap-2 rounded-xl border px-3 py-2 text-xs"
                    style={checked ? { borderColor: "#557EFF" } : undefined}
                  >
                    <input
                      type="checkbox"
                      checked={checked}
                      onChange={() => toggleProcedureType(pt.id)}
                      className="h-3.5 w-3.5 accent-[#557EFF]"
                    />
                    <span className="font-medium">{pt.name}</span>
                  </label>
                );
              })}
            </div>
          )}
        </fieldset>
      </div>
    );
  }
}

// ── Componentes auxiliares ───────────────────────────────────────────────────

function Field({
  id,
  label,
  error,
  children,
}: {
  id: string;
  label: string;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <label htmlFor={id} className="mb-1 block text-xs font-semibold">
        {label}
      </label>
      {children}
      {error && (
        <p className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }} role="alert">
          {error}
        </p>
      )}
    </div>
  );
}

function DlField({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="font-semibold opacity-60">{label}</dt>
      <dd className="mt-0.5">{value}</dd>
    </div>
  );
}

/** Skeleton de carga para el panel (vista/edición mientras llega el GET /{id}). */
function PanelSkeleton() {
  return (
    <div className="space-y-4" aria-busy="true" aria-label="Cargando información del representante">
      {[1, 2, 3, 4].map((n) => (
        <div
          key={n}
          className="h-10 animate-pulse rounded-xl"
          style={{ background: "#DFE5ED" }}
        />
      ))}
    </div>
  );
}

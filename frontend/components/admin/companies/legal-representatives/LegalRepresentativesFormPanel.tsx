"use client";

import { useEffect, useState } from "react";
import { Loader2, Pencil, X } from "lucide-react";
import type { ReactNode } from "react";
import { OtSidePanel } from "@/components/admin/transit-offices/OtSidePanel";
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
import {
  RL_COLOR,
  RL_INPUT_CLS,
  rlGhostBrandStyle,
  rlPrimaryCtaClass,
  rlPrimaryCtaStyle,
} from "./rl-flit-styles";

/** Modos del panel RL. */
export type PanelMode = "view" | "create" | "edit" | "companies";

// Tipos de documento del representante — mismos que en el resto de la app (ActorsForm / Baúl).
const DOC_TYPE_OPTIONS: { value: string; label: string }[] = [
  { value: "CC", label: "Cédula de ciudadanía (CC)" },
  { value: "CE", label: "Cédula de extranjería (CE)" },
  { value: "PAS", label: "Pasaporte (PAS)" },
  { value: "TI", label: "Tarjeta de identidad (TI)" },
];

export interface LegalRepresentativesFormPanelProps {
  open: boolean;
  /** Modo del panel. */
  mode: PanelMode;
  /** ID del representante a cargar (view/edit/companies); null en alta. */
  representativeId: string | null;
  /** TenantId necesario para la llamada a GET /{id}. */
  tenantId: string;
  /** Catálogo de tipos de trámite asignables (activos + publicados). */
  procedureTypes: AssignableProcedureType[];
  onClose: () => void;
  onSubmit: (input: LegalRepresentativeInput) => Promise<LegalRepresentativeSaved>;
  onSaved: (saved: LegalRepresentativeSaved) => void;
  onError: (message: string) => void;
  /**
   * Tras auto-guardar empresas (p. ej. al asociar escritura) sin cerrar el panel:
   * refresca el listado en segundo plano.
   */
  onCompaniesPersisted?: () => void;
  /** Desde la vista completa: pasar a editar persona/firma/trámites. */
  onSwitchToEdit: () => void;
  /** Desde la vista completa: pasar a asociar empresas/escrituras. */
  onSwitchToCompanies: () => void;
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
  companies: [],
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
// llegue en blanco se persiste como null. Sin compañías → lista vacía (persona sin NITs).
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
      : item.companyDocumentNumber
        ? [{ ...EMPTY_COMPANY, nit: item.companyDocumentNumber, name: item.companyName }]
        : [];
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
 * Panel del representante legal:
 * - `create` / `edit`: persona + tipos de trámite + firma/identidad (sin empresas).
 * - `companies`: asociar NITs y escrituras.
 * - `view`: pantalla completa de lectura con todo lo asociado.
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
  onCompaniesPersisted,
  onSwitchToEdit,
  onSwitchToCompanies,
}: LegalRepresentativesFormPanelProps) {
  const [detail, setDetail] = useState<LegalRepresentativeItem | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState(false);
  const [form, setForm] = useState<FormState>(EMPTY);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [banner, setBanner] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Al abrir en view/edit/companies se carga GET /{id}. Si el detalle ya está en caché
  // (p. ej. view → edit), no se vuelve a pedir.
  useEffect(() => {
    if (!open) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
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
      if (mode === "edit" || mode === "companies") {
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
        if (mode === "edit" || mode === "companies") {
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
      companies: f.companies.filter((_, i) => i !== index),
    }));

  const toggleProcedureType = (id: string) =>
    setForm((f) => ({
      ...f,
      procedureTypeIds: f.procedureTypeIds.includes(id)
        ? f.procedureTypeIds.filter((x) => x !== id)
        : [...f.procedureTypeIds, id],
    }));

  // NITs opcionales: lista vacía OK; si hay filas, cada una exige NIT + razón social.
  const companiesValid =
    form.companies.length === 0 ||
    form.companies.every((c) => c.nit.trim() !== "" && c.name.trim() !== "");
  const isValid =
    companiesValid &&
    form.documentType.trim() !== "" &&
    form.documentNumber.trim() !== "" &&
    form.firstLastName.trim() !== "" &&
    form.name.trim() !== "";

  const canSubmit = isValid && !submitting;

  const buildInput = (companiesSource: CompanyRow[]): LegalRepresentativeInput => {
    const trimmed = (v: string) => (v.trim() === "" ? null : v.trim());
    const companies: LegalRepresentativeCompanyInput[] = companiesSource
      .filter((c) => c.nit.trim() !== "" && c.name.trim() !== "")
      .map((c) => ({
        nit: c.nit.trim(),
        name: c.name.trim(),
        email: trimmed(c.email),
        address: trimmed(c.address),
        city: trimmed(c.city),
        phone: trimmed(c.phone),
      }));
    const primary = companies[0];
    return {
      companies,
      companyNit: primary?.nit,
      companyName: primary?.name,
      companyEmail: primary?.email ?? null,
      companyAddress: primary?.address ?? null,
      companyCity: primary?.city ?? null,
      companyPhone: primary?.phone ?? null,
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
      signatureVaultId: form.signatureVaultId ?? null,
    };
  };

  const handleSubmit = async () => {
    setSubmitting(true);
    setBanner(null);
    setFieldErrors({});
    try {
      const saved = await onSubmit(buildInput(form.companies));
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

  /**
   * Persiste las empresas del formulario sin cerrar el panel (para poder asociar escritura
   * enseguida). Devuelve el id de la compañía en `companyIndex`, o null si falló/validación.
   */
  const ensureCompanySaved = async (companyIndex: number): Promise<string | null> => {
    if (!representativeId) {
      onError("Guarda el representante antes de asociar empresas o escrituras.");
      return null;
    }

    const target = form.companies[companyIndex];
    if (!target || target.nit.trim() === "" || target.name.trim() === "") {
      setBanner("Completa NIT y razón social de la empresa antes de asociar la escritura.");
      const errs: Record<string, string> = {};
      if (!target || target.nit.trim() === "") {
        errs[`companies[${companyIndex}].nit`] = "El NIT es obligatorio.";
      }
      if (!target || target.name.trim() === "") {
        errs[`companies[${companyIndex}].name`] = "La razón social es obligatoria.";
      }
      setFieldErrors(errs);
      return null;
    }

    // Filas a medio llenar (solo NIT o solo nombre) bloquean el upsert.
    const incomplete = form.companies.filter(
      (c, i) =>
        i !== companyIndex &&
        ((c.nit.trim() !== "" && c.name.trim() === "") ||
          (c.nit.trim() === "" && c.name.trim() !== "")),
    );
    if (incomplete.length > 0) {
      setBanner("Hay empresas incompletas: completa NIT y razón social o quítalas.");
      return null;
    }

    const targetNit = digitsOnly(target.nit);
    setSubmitting(true);
    setBanner(null);
    setFieldErrors({});
    try {
      await onSubmit(buildInput(form.companies));
      const full = await fetchLegalRepresentative(tenantId, representativeId);
      setDetail(full);
      setForm(fromItem(full));
      onCompaniesPersisted?.();

      const matched =
        full.companies.find((c) => digitsOnly(c.nit) === targetNit) ??
        full.companies[companyIndex] ??
        null;
      if (!matched?.id) {
        onError("La empresa se guardó, pero no se pudo obtener su identificador. Reintenta.");
        return null;
      }
      return matched.id;
    } catch (err) {
      if (err instanceof ApiValidationError) {
        const mapped: Record<string, string> = {};
        for (const e of err.errors) {
          if (e.field) mapped[e.field] = e.message;
        }
        setFieldErrors(mapped);
        setBanner("Revisa los campos marcados: hay valores inválidos.");
      } else {
        onError("No se pudo guardar la empresa para asociar la escritura. Intenta de nuevo.");
      }
      return null;
    } finally {
      setSubmitting(false);
    }
  };

  const errStyle = (field: string) =>
    fieldErrors[field] ? { borderColor: RL_COLOR.danger } : undefined;

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
      ? "Ficha completa del representante"
      : mode === "edit"
        ? "Editar persona, firma y trámites"
        : mode === "companies"
          ? "Empresas y escrituras"
          : "Nuevo representante legal";

  const ariaLabel =
    mode === "view"
      ? "Ver representante legal"
      : mode === "edit"
        ? "Editar representante legal"
        : mode === "companies"
          ? "Asociar empresas y escrituras"
          : "Registrar representante legal";

  const footer =
    mode === "view" ? (
      <div className="flex flex-col gap-2 sm:flex-row">
        <button
          type="button"
          onClick={onSwitchToEdit}
          className={`flex-1 ${rlPrimaryCtaClass} py-2.5`}
          style={rlPrimaryCtaStyle}
          aria-label="Pasar a modo edición"
        >
          <Pencil className="h-4 w-4" aria-hidden="true" />
          Editar persona / firma
        </button>
        <button
          type="button"
          onClick={onSwitchToCompanies}
          className="flex flex-1 items-center justify-center gap-2 rounded-xl border py-2.5 text-xs font-semibold"
          style={rlGhostBrandStyle}
          aria-label="Asociar empresas y escrituras"
        >
          Asociar empresas
        </button>
      </div>
    ) : (
      <button
        type="button"
        disabled={!canSubmit}
        onClick={() => void handleSubmit()}
        className={`w-full ${rlPrimaryCtaClass} py-2.5`}
        style={rlPrimaryCtaStyle}
      >
        {submitting && <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />}
        {mode === "edit"
          ? "Guardar cambios"
          : mode === "companies"
            ? "Guardar empresas"
            : "Registrar representante"}
      </button>
    );

  // ── Render ───────────────────────────────────────────────────────────────────

  if (mode === "view") {
    return (
      <FullScreenShell
        open={open}
        title={title}
        ariaLabel={ariaLabel}
        onClose={onClose}
        footer={footer}
      >
        {renderView()}
      </FullScreenShell>
    );
  }

  return (
    <OtSidePanel
      open={open}
      title={title}
      ariaLabel={ariaLabel}
      onClose={onClose}
      disabled={submitting}
      footer={footer}
      width="2xl"
      surface="modal"
    >
      {mode === "companies" ? renderCompaniesForm() : renderPersonForm()}
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
          style={{ borderColor: RL_COLOR.danger, color: RL_COLOR.danger }}
        >
          No se pudo cargar la información del representante. Cierra e inténtalo de nuevo.
        </p>
      );
    }
    if (!detail) return <PanelSkeleton />;

    const tramites = procedureTypeLabels(detail.procedureTypeIds, procedureTypes);

    return (
      <div className="space-y-5">
        {/* 1. Persona */}
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
            {detail.address && <DlField label="Dirección" value={detail.address} />}
            {detail.city && <DlField label="Ciudad" value={detail.city} />}
          </dl>
        </section>

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

        {/* Firma + identidad */}
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
          <div
            className="rounded-xl border p-3"
            style={{ borderColor: RL_COLOR.border }}
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
                    ? { background: RL_COLOR.successBg, color: RL_COLOR.successText }
                    : { background: RL_COLOR.warningBg, color: RL_COLOR.warningText }
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
        </div>

        {/* Empresas + escrituras */}
        <section aria-label="Empresas representadas">
          <h3 className="mb-2 text-[11px] font-bold uppercase tracking-wide opacity-60">
            Empresas y escrituras
          </h3>
          {detail.companies.length === 0 ? (
            <p className="text-[11px] opacity-60">Sin empresas asociadas todavía.</p>
          ) : (
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
          )}
        </section>
      </div>
    );
  }

  // ── Formulario persona / firma / trámites (create + edit) ────────────────────

  function renderPersonForm() {
    // En edit: skeleton mientras se carga el detalle completo (AC3).
    if (mode === "edit" && detailLoading) return <PanelSkeleton />;
    if (mode === "edit" && detailError) {
      return (
        <p
          role="alert"
          className="rounded-xl border px-3 py-2 text-[11px] font-medium"
          style={{ borderColor: RL_COLOR.danger, color: RL_COLOR.danger }}
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
            style={{ borderColor: RL_COLOR.danger, color: RL_COLOR.danger }}
          >
            {banner}
          </p>
        )}

        {mode === "create" && (
          <p className="text-[11px] opacity-60">
            Registra a la persona, los tipos de trámite y, si quieres, su firma o validación de
            identidad. Las empresas y escrituras se asocian después desde el listado.
          </p>
        )}

        {/* 1. Persona */}
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
                className={RL_INPUT_CLS}
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
                className={RL_INPUT_CLS}
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
              className={RL_INPUT_CLS}
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
                className={RL_INPUT_CLS}
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
                className={RL_INPUT_CLS}
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
                className={RL_INPUT_CLS}
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
                className={RL_INPUT_CLS}
                style={errStyle("phone")}
              />
            </Field>
            <Field id="lr-address" label="Dirección (opcional)" error={fieldErrors.address}>
              <input
                id="lr-address"
                value={form.address}
                onChange={(e) => patch({ address: e.target.value })}
                className={RL_INPUT_CLS}
                style={errStyle("address")}
              />
            </Field>
            <Field id="lr-city" label="Ciudad (opcional)" error={fieldErrors.city}>
              <input
                id="lr-city"
                value={form.city}
                onChange={(e) => patch({ city: e.target.value })}
                className={RL_INPUT_CLS}
                style={errStyle("city")}
              />
            </Field>
          </div>
        </section>

        {/* Tipos de trámite — junto a la persona en el alta/edición */}
        <fieldset className="space-y-2">
          <legend className="text-[11px] font-bold uppercase tracking-wide opacity-60">
            Tipos de trámite que puede firmar
          </legend>
          {fieldErrors.procedureTypeIds && (
            <p
              className="text-[11px] font-medium"
              style={{ color: RL_COLOR.danger }}
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
                    style={checked ? { borderColor: RL_COLOR.brand } : undefined}
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

        {/* Firma + identidad */}
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <section className="space-y-2">
            <h3 className="text-[11px] font-bold uppercase tracking-wide opacity-60">
              Firma del baúl (opcional)
            </h3>
            <SignatureVaultSelector
              tenantId={tenantId}
              documentType={form.documentType}
              documentNumber={form.documentNumber}
              value={form.signatureVaultId}
              onChange={(id) => patch({ signatureVaultId: id })}
              fullName={[form.name, form.firstLastName, form.secondLastName]
                .map((p) => p?.trim() ?? "")
                .filter((p) => p !== "")
                .join(" ")}
              nitEmpresa={form.companies[0]?.nit ?? null}
            />
          </section>

          <section className="space-y-2">
            <h3 className="text-[11px] font-bold uppercase tracking-wide opacity-60">
              Validación de identidad (opcional)
            </h3>
            {mode === "edit" ? (
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
            ) : (
              <IdentityActionsBlock
                tenantId={tenantId}
                representativeId={null}
                email={form.email}
                onRefresh={() => undefined}
              />
            )}
          </section>
        </div>
      </div>
    );
  }

  // ── Formulario empresas / escrituras ─────────────────────────────────────────

  function renderCompaniesForm() {
    if (detailLoading) return <PanelSkeleton />;
    if (detailError) {
      return (
        <p
          role="alert"
          className="rounded-xl border px-3 py-2 text-[11px] font-medium"
          style={{ borderColor: RL_COLOR.danger, color: RL_COLOR.danger }}
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
            style={{ borderColor: RL_COLOR.danger, color: RL_COLOR.danger }}
          >
            {banner}
          </p>
        )}
        <p className="text-[11px] opacity-60">
          Asocia los NIT que representa esta persona y, en cada uno, su escritura vigente si aplica.
        </p>
        <RepresentativeCompaniesAccordion
          mode="edit"
          companies={detail?.companies ?? []}
          formCompanies={form.companies}
          onContactChange={patchCompany}
          onAddCompany={addCompany}
          onRemoveCompany={removeCompany}
          fieldErrors={fieldErrors}
          tenantId={tenantId}
          representativeId={representativeId}
          onDeedSaved={refreshDetail}
          onEnsureCompanySaved={ensureCompanySaved}
          onError={onError}
        />
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
        <p className="mt-1 text-[11px] font-medium" style={{ color: RL_COLOR.danger }} role="alert">
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
          style={{ background: RL_COLOR.tableHeader }}
        />
      ))}
    </div>
  );
}

/** Pantalla completa para la ficha de lectura del representante. */
function FullScreenShell({
  open,
  title,
  ariaLabel,
  onClose,
  footer,
  children,
}: {
  open: boolean;
  title: string;
  ariaLabel: string;
  onClose: () => void;
  footer?: ReactNode;
  children: ReactNode;
}) {
  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 flex flex-col bg-slate-900/40 backdrop-blur-sm">
      <button
        type="button"
        className="absolute inset-0"
        aria-label="Cerrar ficha"
        onClick={onClose}
      />
      <div
        className="relative m-0 flex h-full w-full flex-col shadow-2xl sm:m-4 sm:h-[calc(100%-2rem)] sm:w-[calc(100%-2rem)] sm:rounded-2xl sm:border"
        style={{
          background: RL_COLOR.modal,
          borderColor: RL_COLOR.border,
          boxShadow: "0 24px 60px rgba(22, 39, 68, 0.18)",
        }}
        role="dialog"
        aria-modal="true"
        aria-label={ariaLabel}
      >
        <div
          className="flex items-center justify-between border-b px-4 py-3 sm:px-6"
          style={{ borderColor: RL_COLOR.border }}
        >
          <h2 className="text-sm font-bold sm:text-base" style={{ color: RL_COLOR.navy }}>
            {title}
          </h2>
          <button type="button" aria-label="Cerrar" onClick={onClose}>
            <X className="h-4 w-4" style={{ color: RL_COLOR.navy }} />
          </button>
        </div>
        <div className="flex-1 overflow-y-auto p-4 sm:p-6">{children}</div>
        {footer && (
          <div className="border-t p-4 sm:px-6" style={{ borderColor: RL_COLOR.border }}>
            {footer}
          </div>
        )}
      </div>
    </div>
  );
}

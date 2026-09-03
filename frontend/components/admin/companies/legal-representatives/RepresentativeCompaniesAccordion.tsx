"use client";

import { useId, useState } from "react";
import {
  Building2,
  Eye,
  FileText,
  Loader2,
  Pencil,
  Star,
} from "lucide-react";
import { StatusBadge, type StatusTone } from "@/components/atom/StatusBadge";
import { DeedsFormPanel, type DeedEditingRef } from "../deeds/DeedsFormPanel";
import { saveDeed, fetchDeedDetail, type DeedFormInput } from "@/lib/api/admin-deeds";
import { formatFecha } from "@/lib/format/date";
import { digitsOnly } from "@/lib/format/currency";
import type {
  LegalRepresentativeCompanySummary,
  RepresentativeDeedEstado,
} from "@/lib/api/admin-legal-representatives";
import type { PanelMode, CompanyRow } from "./LegalRepresentativesFormPanel";
import {
  RL_COLOR,
  RL_INPUT_CLS,
  rlOutlinedActionClass,
  rlOutlinedActionStyle,
  rlTextDangerActionClass,
  rlTextDangerActionStyle,
} from "./rl-flit-styles";

// ── Badge helpers ─────────────────────────────────────────────────────────────

function deedTone(estado: RepresentativeDeedEstado): StatusTone {
  switch (estado) {
    case "vigente":
      return "success";
    case "vencida":
      return "danger";
    case "futura":
      return "info";
    case "inactiva":
      return "neutral";
  }
}

function deedEstadoLabel(estado: RepresentativeDeedEstado): string {
  switch (estado) {
    case "vigente":
      return "Vigente";
    case "vencida":
      return "Vencida";
    case "futura":
      return "Futura";
    case "inactiva":
      return "Inactiva";
  }
}

// ── Component props ───────────────────────────────────────────────────────────

export interface RepresentativeCompaniesAccordionProps {
  mode: PanelMode;
  /**
   * Datos completos de las compañías del representante (incluyendo escrituras e isPrimary).
   * Disponible en `view`/`edit`; vacío en `create` (el representante aún no fue guardado).
   */
  companies: LegalRepresentativeCompanySummary[];
  /**
   * Estado editable del formulario — alineado por índice con `companies`.
   * En `create` puede tener entradas sin id de compañía; lista vacía = sin NITs aún.
   */
  formCompanies: CompanyRow[];
  onContactChange: (index: number, patch: Partial<CompanyRow>) => void;
  onAddCompany: () => void;
  onRemoveCompany: (index: number) => void;
  fieldErrors: Record<string, string>;
  tenantId: string;
  /** ID del representante para asociar escrituras (null en modo alta). */
  representativeId: string | null;
  /** Llamada tras guardar una escritura — el padre re-fetcha el detalle completo. */
  onDeedSaved: () => void;
  /**
   * Si la empresa del índice aún no tiene id, el padre la persiste y devuelve el id.
   * Usado al hacer clic en «Asociar escritura».
   */
  onEnsureCompanySaved?: (companyIndex: number) => Promise<string | null>;
  onError: (message: string) => void;
}

/**
 * Grid de compañías del representante legal.
 *
 * Cada tarjeta muestra NIT/contacto + zona de escrituras (asociar / historial).
 * Las empresas y las escrituras son opcionales: se pueden agregar después de crear la persona.
 * Conserva el nombre histórico `RepresentativeCompaniesAccordion` por compatibilidad de imports.
 */
export function RepresentativeCompaniesAccordion({
  mode,
  companies,
  formCompanies,
  onContactChange,
  onAddCompany,
  onRemoveCompany,
  fieldErrors,
  tenantId,
  representativeId,
  onDeedSaved,
  onEnsureCompanySaved,
  onError,
}: RepresentativeCompaniesAccordionProps) {
  const [deedPanel, setDeedPanel] = useState<{
    companyIndex: number;
    companyId: string;
    editing: DeedEditingRef | null;
  } | null>(null);

  const [viewing, setViewing] = useState<string | null>(null);
  const [persistingIndex, setPersistingIndex] = useState<number | null>(null);
  const baseId = useId();

  const closeDeedPanel = () => setDeedPanel(null);

  const handleDeedSubmit = async (input: DeedFormInput): Promise<import("@/lib/api/admin-deeds").DeedSaved> => {
    if (deedPanel === null) throw new Error("no company context");
    const companyId = deedPanel.companyId;
    if (!companyId) {
      onError("Guarda la empresa antes de asociar una escritura.");
      throw new Error("company not persisted");
    }
    const editingId = deedPanel.editing ? deedPanel.editing.id : null;
    return saveDeed(
      tenantId,
      editingId,
      { ...input, companyIds: [companyId] },
      representativeId ?? undefined,
    );
  };

  const handleDeedSaved = () => {
    closeDeedPanel();
    onDeedSaved();
  };

  const openDeedPanel = (index: number, companyId: string, editing: DeedEditingRef | null) => {
    setDeedPanel({ companyIndex: index, companyId, editing });
  };

  const handleAsociar = async (index: number) => {
    const existingId = companies[index]?.id;
    if (existingId) {
      openDeedPanel(index, existingId, null);
      return;
    }

    if (!onEnsureCompanySaved) {
      onError("Guarda la empresa antes de asociar una escritura.");
      return;
    }

    setPersistingIndex(index);
    try {
      const companyId = await onEnsureCompanySaved(index);
      if (!companyId) return;
      openDeedPanel(index, companyId, null);
    } finally {
      setPersistingIndex(null);
    }
  };

  const handleVer = async (deedId: string) => {
    setViewing(deedId);
    try {
      const detail = await fetchDeedDetail(tenantId, deedId);
      if (detail.viewUrl) {
        window.open(detail.viewUrl, "_blank", "noopener,noreferrer");
      } else {
        onError("La escritura no tiene un PDF disponible para ver.");
      }
    } catch {
      onError("No se pudo abrir la escritura.");
    } finally {
      setViewing(null);
    }
  };

  const deedPanelCompany =
    deedPanel !== null
      ? (() => {
          const fromDetail = companies.find((c) => c.id === deedPanel.companyId);
          if (fromDetail) {
            return { id: fromDetail.id, name: fromDetail.name, nit: fromDetail.nit };
          }
          const formRow = formCompanies[deedPanel.companyIndex];
          if (formRow) {
            return {
              id: deedPanel.companyId,
              name: formRow.name,
              nit: formRow.nit,
            };
          }
          return null;
        })()
      : null;

  const count =
    mode === "view" ? companies.length : Math.max(formCompanies.length, companies.length);

  return (
    <>
      <div className="flex items-center justify-between gap-2">
        <h3 className="text-[11px] font-bold uppercase tracking-wide opacity-60">
          Empresas representadas (opcional)
        </h3>
        {mode !== "view" && (
          <button
            type="button"
            onClick={onAddCompany}
            className={rlOutlinedActionClass}
            style={rlOutlinedActionStyle}
          >
            Agregar empresa
          </button>
        )}
      </div>

      {mode !== "view" && (
        <p className="text-[11px] opacity-60">
          Puedes registrar la persona sin empresas y asociar NITs después. La escritura de cada
          compañía también es opcional.
        </p>
      )}

      {count === 0 ? (
        <p
          className="rounded-xl border border-dashed px-3 py-6 text-center text-[11px] opacity-60"
          style={{ borderColor: RL_COLOR.border }}
          data-testid="rl-companies-empty"
        >
          {mode === "view"
            ? "Sin empresas asociadas."
            : "Sin empresas aún. Usa «Agregar empresa» cuando quieras vincular un NIT."}
        </p>
      ) : (
        <div
          className="grid grid-cols-1 gap-3 lg:grid-cols-2"
          data-testid="rl-companies-grid"
        >
          {Array.from({ length: count }).map((_, index) => {
            const companySummary: LegalRepresentativeCompanySummary | undefined = companies[index];
            const formRow: CompanyRow = formCompanies[index] ?? {
              nit: "",
              name: "",
              email: "",
              address: "",
              city: "",
              phone: "",
            };

            const displayName =
              mode === "view"
                ? (companySummary?.name ?? "")
                : (formRow.name || `Empresa ${index + 1}`);
            const displayNit =
              mode === "view" ? (companySummary?.nit ?? "") : (formRow.nit || "—");
            const isPrimary =
              mode === "create" ? index === 0 : (companySummary?.isPrimary ?? index === 0);
            const deeds = companySummary?.deeds ?? [];
            const vigentesCount = deeds.filter((d) => d.estado === "vigente").length;

            const cardId = `${baseId}-card-${index}`;

            const fieldErr = (suffix: string) =>
              fieldErrors[`companies[${index}].${suffix}`] ??
              (index === 0
                ? fieldErrors[`company${suffix.charAt(0).toUpperCase()}${suffix.slice(1)}`]
                : undefined);

            return (
              <article
                key={index}
                id={cardId}
                className="flex flex-col rounded-xl border p-3"
                style={{ borderColor: RL_COLOR.border }}
                aria-label={`Empresa ${displayName || index + 1}`}
              >
                <div className="mb-3 flex items-start gap-2">
                  <Building2
                    className="mt-0.5 h-4 w-4 shrink-0"
                    style={{ color: RL_COLOR.brand }}
                    aria-hidden="true"
                  />
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-1.5">
                      <span className="truncate text-xs font-semibold">{displayName}</span>
                      {isPrimary && (
                        <Star
                          className="h-3 w-3 shrink-0 fill-current"
                          style={{ color: RL_COLOR.warning }}
                          aria-label="Compañía principal"
                          data-testid="icon-principal"
                        />
                      )}
                      {vigentesCount > 0 && (
                        <span
                          className="rounded-full px-1.5 py-0.5 text-[10px] font-semibold"
                          style={{ background: RL_COLOR.successBg, color: RL_COLOR.successText }}
                        >
                          {vigentesCount} escritura{vigentesCount !== 1 ? "s" : ""} vigente
                          {vigentesCount !== 1 ? "s" : ""}
                        </span>
                      )}
                    </div>
                    {mode === "view" && (
                      <span className="font-mono text-[11px] opacity-60">{displayNit}</span>
                    )}
                  </div>
                  {mode !== "view" && (
                    <button
                      type="button"
                      onClick={() => onRemoveCompany(index)}
                      aria-label={`Quitar empresa ${index + 1}`}
                      className={rlTextDangerActionClass}
                      style={rlTextDangerActionStyle}
                    >
                      Quitar
                    </button>
                  )}
                </div>

                {mode !== "view" ? (
                  <div className="mb-3 grid grid-cols-1 gap-2 sm:grid-cols-2">
                    <AccordionField
                      id={`lr-companyNit-${index}`}
                      label="NIT de la compañía"
                      error={fieldErr("nit")}
                    >
                      <input
                        id={`lr-companyNit-${index}`}
                        value={formRow.nit}
                        onChange={(e) =>
                          onContactChange(index, { nit: digitsOnly(e.target.value) })
                        }
                        className={RL_INPUT_CLS}
                        style={fieldErr("nit") ? { borderColor: RL_COLOR.danger } : undefined}
                        placeholder="NIT"
                        inputMode="numeric"
                        pattern="[0-9]*"
                        autoComplete="off"
                      />
                    </AccordionField>
                    <AccordionField
                      id={`lr-companyName-${index}`}
                      label="Razón social"
                      error={fieldErr("name")}
                    >
                      <input
                        id={`lr-companyName-${index}`}
                        value={formRow.name}
                        onChange={(e) => onContactChange(index, { name: e.target.value })}
                        className={RL_INPUT_CLS}
                        style={fieldErr("name") ? { borderColor: RL_COLOR.danger } : undefined}
                        placeholder="Razón social"
                      />
                    </AccordionField>
                    <AccordionField
                      id={`lr-companyEmail-${index}`}
                      label="Correo (opcional)"
                      error={fieldErr("email")}
                    >
                      <input
                        id={`lr-companyEmail-${index}`}
                        type="email"
                        value={formRow.email}
                        onChange={(e) => onContactChange(index, { email: e.target.value })}
                        className={RL_INPUT_CLS}
                      />
                    </AccordionField>
                    <AccordionField
                      id={`lr-companyPhone-${index}`}
                      label="Teléfono (opcional)"
                      error={fieldErr("phone")}
                    >
                      <input
                        id={`lr-companyPhone-${index}`}
                        type="tel"
                        inputMode="numeric"
                        pattern="[0-9]*"
                        autoComplete="tel"
                        value={formRow.phone}
                        onChange={(e) =>
                          onContactChange(index, { phone: digitsOnly(e.target.value) })
                        }
                        className={RL_INPUT_CLS}
                      />
                    </AccordionField>
                    <AccordionField
                      id={`lr-companyAddress-${index}`}
                      label="Dirección (opcional)"
                      error={fieldErr("address")}
                    >
                      <input
                        id={`lr-companyAddress-${index}`}
                        value={formRow.address}
                        onChange={(e) => onContactChange(index, { address: e.target.value })}
                        className={RL_INPUT_CLS}
                      />
                    </AccordionField>
                    <AccordionField
                      id={`lr-companyCity-${index}`}
                      label="Ciudad (opcional)"
                      error={fieldErr("city")}
                    >
                      <input
                        id={`lr-companyCity-${index}`}
                        value={formRow.city}
                        onChange={(e) => onContactChange(index, { city: e.target.value })}
                        className={RL_INPUT_CLS}
                      />
                    </AccordionField>
                  </div>
                ) : (
                  companySummary && (
                    <dl className="mb-3 grid grid-cols-1 gap-2 text-xs sm:grid-cols-2">
                      <ContactField label="NIT" value={companySummary.nit} />
                      <ContactField label="Razón social" value={companySummary.name} />
                      {companySummary.email && (
                        <ContactField label="Correo" value={companySummary.email} />
                      )}
                      {companySummary.phone && (
                        <ContactField label="Teléfono" value={companySummary.phone} />
                      )}
                      {companySummary.city && (
                        <ContactField label="Ciudad" value={companySummary.city} />
                      )}
                      {companySummary.address && (
                        <ContactField label="Dirección" value={companySummary.address} />
                      )}
                    </dl>
                  )
                )}

                <div className="mt-auto border-t pt-3" style={{ borderColor: RL_COLOR.border }}>
                  <DeedBlock
                    mode={mode}
                    deeds={deeds}
                    viewing={viewing}
                    onVer={handleVer}
                    onEditar={(deed) => {
                      const companyId = companySummary?.id;
                      if (!companyId) {
                        onError("Guarda la empresa antes de editar una escritura.");
                        return;
                      }
                      openDeedPanel(index, companyId, {
                        id: deed.id,
                        description: deed.description,
                        vigenciaDesde: deed.vigenciaDesde,
                        vigenciaHasta: deed.vigenciaHasta,
                      });
                    }}
                    onAsociar={() => void handleAsociar(index)}
                    asociando={persistingIndex === index}
                    // Solo bloqueado en alta del RL (aún no hay representante persistido).
                    disabled={mode === "create"}
                  />
                </div>
              </article>
            );
          })}
        </div>
      )}

      <DeedsFormPanel
        open={deedPanel !== null}
        editing={deedPanel?.editing ?? null}
        company={deedPanelCompany}
        onClose={closeDeedPanel}
        onSubmit={handleDeedSubmit}
        onSaved={handleDeedSaved}
        onError={onError}
        zClassName="z-[120]"
      />
    </>
  );
}

// ── Bloque de escrituras dentro de la tarjeta ─────────────────────────────────

interface DeedBlockProps {
  mode: PanelMode;
  deeds: LegalRepresentativeCompanySummary["deeds"];
  viewing: string | null;
  onVer: (deedId: string) => void;
  onEditar: (deed: NonNullable<LegalRepresentativeCompanySummary["deeds"][number]>) => void;
  onAsociar: () => void;
  asociando?: boolean;
  disabled: boolean;
}

function DeedBlock({
  mode,
  deeds,
  viewing,
  onVer,
  onEditar,
  onAsociar,
  asociando = false,
  disabled,
}: DeedBlockProps) {
  const canEdit = mode === "edit";

  return (
    <section aria-label="Escrituras de la compañía">
      <div className="mb-2 flex items-center justify-between gap-2">
        <h4 className="text-[11px] font-bold uppercase tracking-wide opacity-60">Escrituras</h4>
        {disabled ? (
          <span
            className="rounded-lg border px-2.5 py-1 text-[11px] opacity-50"
            style={{ borderColor: RL_COLOR.border }}
          >
            Disponible al guardar
          </span>
        ) : canEdit && deeds.length === 0 ? (
          <button
            type="button"
            onClick={onAsociar}
            disabled={asociando}
            className={rlOutlinedActionClass}
            style={rlOutlinedActionStyle}
            aria-label="Asociar escritura a esta compañía"
          >
            {asociando ? (
              <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
            ) : null}
            {asociando ? "Guardando empresa…" : "Asociar escritura"}
          </button>
        ) : null}
      </div>

      {disabled ? (
        <p className="text-[11px] opacity-50">
          Las escrituras estarán disponibles después de guardar el representante.
        </p>
      ) : deeds.length === 0 ? (
        <p className="text-[11px] opacity-60">
          Sin escritura. Al asociar, la empresa se guarda automáticamente si aún no lo está.
        </p>
      ) : (
        <ul className="space-y-2" aria-label="Historial de escrituras">
          {deeds.map((deed) => (
            <li
              key={deed.id}
              className="flex items-start justify-between gap-2 rounded-xl border px-3 py-2.5"
              style={{ borderColor: RL_COLOR.border }}
            >
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-1.5">
                  <FileText className="h-3.5 w-3.5 shrink-0 opacity-50" aria-hidden="true" />
                  <span className="truncate text-xs font-medium">{deed.description}</span>
                  <StatusBadge
                    tone={deedTone(deed.estado)}
                    label={deedEstadoLabel(deed.estado)}
                  />
                </div>
                <p className="mt-0.5 font-mono text-[10px] opacity-60">
                  {formatFecha(deed.vigenciaDesde)} – {formatFecha(deed.vigenciaHasta)}
                </p>
              </div>

              <div className="flex shrink-0 items-center gap-1">
                <button
                  type="button"
                  onClick={() => void onVer(deed.id)}
                  disabled={viewing === deed.id}
                  aria-label={`Ver PDF de ${deed.description}`}
                  className={rlOutlinedActionClass}
                  style={rlOutlinedActionStyle}
                >
                  {viewing === deed.id ? (
                    <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
                  ) : (
                    <Eye className="h-3.5 w-3.5" aria-hidden="true" />
                  )}
                  Ver PDF
                </button>

                {canEdit && (
                  <button
                    type="button"
                    onClick={() => onEditar(deed)}
                    aria-label={`Editar escritura ${deed.description}`}
                    className={rlOutlinedActionClass}
                    style={rlOutlinedActionStyle}
                  >
                    <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
                    Editar
                  </button>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function AccordionField({
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

function ContactField({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-[10px] font-semibold uppercase opacity-50">{label}</dt>
      <dd className="text-xs">{value}</dd>
    </div>
  );
}

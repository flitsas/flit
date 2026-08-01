"use client";

import { useEffect, useId, useRef, useState } from "react";
import {
  Building2,
  ChevronDown,
  Eye,
  FileText,
  Loader2,
  Pencil,
  Plus,
  Star,
  Trash2,
} from "lucide-react";
import { OT_INPUT_CLS } from "@/components/admin/transit-offices/ot-form-styles";
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
   * En `create` puede tener entradas sin id de compañía.
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
  onError: (message: string) => void;
}

// ── Main component ────────────────────────────────────────────────────────────

/**
 * HU #11179 — Acordeón de compañías del representante legal.
 *
 * • AC1: cada compañía es una sección plegable con aria-expanded / aria-controls.
 * • AC2: la compañía principal se distingue con un ícono Star + aria-label="Compañía principal".
 * • AC3: al desplegar, se lista el historial de escrituras con estado de vigencia + acciones.
 * • AC4: "Asociar escritura" abre DeedsFormPanel; al guardar actualiza el historial sin recarga.
 * • AC5: en modo `create` el bloque de escrituras aparece deshabilitado.
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
  onError,
}: RepresentativeCompaniesAccordionProps) {
  // Índices expandidos: en create el primer elemento abre por defecto (UX: hay que rellenarlo).
  const [openSet, setOpenSet] = useState<Set<number>>(
    () => new Set(mode === "create" ? [0] : []),
  );

  // Auto-abrir el acordeón de una compañía recién agregada.
  const prevCountRef = useRef(formCompanies.length);
  useEffect(() => {
    const newLen = formCompanies.length;
    if (newLen > prevCountRef.current) {
      setOpenSet((prev) => new Set([...prev, newLen - 1]));
    }
    prevCountRef.current = newLen;
  }, [formCompanies.length]);

  // Panel de escritura activo: { companyIndex, editing: DeedEditingRef | null }
  const [deedPanel, setDeedPanel] = useState<{
    companyIndex: number;
    editing: DeedEditingRef | null;
  } | null>(null);

  // Escritura siendo vista (cargando PDF).
  const [viewing, setViewing] = useState<string | null>(null);

  const baseId = useId();

  const toggle = (index: number) =>
    setOpenSet((prev) => {
      const next = new Set(prev);
      if (next.has(index)) next.delete(index);
      else next.add(index);
      return next;
    });

  const closeDeedPanel = () => setDeedPanel(null);

  const handleDeedSubmit = async (input: DeedFormInput): Promise<import("@/lib/api/admin-deeds").DeedSaved> => {
    if (deedPanel === null) throw new Error("no company context");
    const company = companies[deedPanel.companyIndex];
    const companyId = company?.id ?? "";
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

  // Compañía activa en el panel de escritura (para pasarla al DeedsFormPanel).
  const deedPanelCompany =
    deedPanel !== null && companies[deedPanel.companyIndex]
      ? {
          id: companies[deedPanel.companyIndex].id,
          name: companies[deedPanel.companyIndex].name,
          nit: companies[deedPanel.companyIndex].nit,
        }
      : null;

  const count = Math.max(formCompanies.length, companies.length);

  return (
    <>
      {/* Cabecera de sección + acción "Agregar empresa" */}
      {mode !== "view" && (
        <div className="flex items-center justify-between gap-2">
          <h3 className="text-[11px] font-bold uppercase tracking-wide opacity-60">
            Empresas representadas
          </h3>
          <button
            type="button"
            onClick={onAddCompany}
            className="flex items-center gap-1 rounded-lg border px-2.5 py-1.5 text-[11px] font-semibold"
            style={{ color: "#557EFF", borderColor: "#557EFF" }}
          >
            <Plus className="h-3.5 w-3.5" aria-hidden="true" /> Agregar empresa
          </button>
        </div>
      )}

      {mode === "view" && (
        <h3 className="mb-2 text-[11px] font-bold uppercase tracking-wide opacity-60">
          Empresas representadas
        </h3>
      )}

      {mode === "create" && (
        <p className="text-[11px] opacity-60">
          El representante se registra una sola vez; agrégale todas las empresas que representa. La
          primera es la compañía primaria.
        </p>
      )}

      {/* Acordeón */}
      <div className="space-y-2">
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

          const isOpen = openSet.has(index);
          const bodyId = `${baseId}-body-${index}`;
          const headerId = `${baseId}-header-${index}`;

          // Validación de campo por empresa.
          const fieldErr = (suffix: string) =>
            fieldErrors[`companies[${index}].${suffix}`] ??
            (index === 0
              ? fieldErrors[`company${suffix.charAt(0).toUpperCase()}${suffix.slice(1)}`]
              : undefined);

          return (
            <div
              key={index}
              className="rounded-xl border"
              style={{ borderColor: isOpen ? "#557EFF" : "#DFE5ED" }}
            >
              {/* ── Cabecera del acordeón ───────────────────────────────────── */}
              <div className="flex items-center gap-2 px-3 py-3">
                <button
                  id={headerId}
                  type="button"
                  aria-expanded={isOpen}
                  aria-controls={bodyId}
                  onClick={() => toggle(index)}
                  className="flex min-w-0 flex-1 items-center gap-2 text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-1"
                >
                  <Building2
                    className="h-4 w-4 shrink-0"
                    style={{ color: "#557EFF" }}
                    aria-hidden="true"
                  />

                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-1.5">
                      <span className="truncate text-xs font-semibold">{displayName}</span>
                      {isPrimary && (
                        <Star
                          className="h-3 w-3 shrink-0 fill-current"
                          style={{ color: "#F59E0B" }}
                          aria-label="Compañía principal"
                          data-testid="icon-principal"
                        />
                      )}
                      {vigentesCount > 0 && (
                        <span className="rounded-full px-1.5 py-0.5 text-[10px] font-semibold"
                          style={{ background: "rgba(112,207,58,0.14)", color: "#3f7a15" }}>
                          {vigentesCount} escritura{vigentesCount !== 1 ? "s" : ""} vigente
                          {vigentesCount !== 1 ? "s" : ""}
                        </span>
                      )}
                    </div>
                    <span className="font-mono text-[11px] opacity-60">{displayNit}</span>
                  </div>

                  <ChevronDown
                    className="h-4 w-4 shrink-0 transition-transform"
                    style={{
                      color: "#557EFF",
                      transform: isOpen ? "rotate(180deg)" : "rotate(0deg)",
                    }}
                    aria-hidden="true"
                  />
                </button>

                {/* Quitar empresa — solo en create/edit con más de 1 compañía */}
                {mode !== "view" && formCompanies.length > 1 && (
                  <button
                    type="button"
                    onClick={() => onRemoveCompany(index)}
                    aria-label={`Quitar empresa ${index + 1}`}
                    className="flex items-center gap-1 rounded-lg border px-2 py-1 text-[11px] font-semibold"
                    style={{ color: "#FF4E00", borderColor: "#f0c38e" }}
                  >
                    <Trash2 className="h-3.5 w-3.5" aria-hidden="true" /> Quitar
                  </button>
                )}
              </div>

              {/* ── Cuerpo del acordeón ─────────────────────────────────────── */}
              {isOpen && (
                <div
                  id={bodyId}
                  role="region"
                  aria-labelledby={headerId}
                  className="border-t border-[#DFE5ED] px-3 py-4 space-y-4"
                >
                  {/* Campos de contacto (editables en create/edit; ocultos en view) */}
                  {mode !== "view" ? (
                    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
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
                          className={OT_INPUT_CLS}
                          style={fieldErr("nit") ? { borderColor: "#FF4E00" } : undefined}
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
                          className={OT_INPUT_CLS}
                          style={fieldErr("name") ? { borderColor: "#FF4E00" } : undefined}
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
                          className={OT_INPUT_CLS}
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
                          className={OT_INPUT_CLS}
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
                          className={OT_INPUT_CLS}
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
                          className={OT_INPUT_CLS}
                        />
                      </AccordionField>
                    </div>
                  ) : (
                    /* Vista de solo lectura del contacto */
                    companySummary && (
                      <dl className="grid grid-cols-1 gap-2 text-xs sm:grid-cols-2">
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

                  {/* ── Bloque de escrituras ──────────────────────────────── */}
                  <DeedBlock
                    mode={mode}
                    deeds={deeds}
                    viewing={viewing}
                    onVer={handleVer}
                    onEditar={(deed) =>
                      setDeedPanel({
                        companyIndex: index,
                        editing: {
                          id: deed.id,
                          description: deed.description,
                          vigenciaDesde: deed.vigenciaDesde,
                          vigenciaHasta: deed.vigenciaHasta,
                        },
                      })
                    }
                    onAsociar={() =>
                      setDeedPanel({ companyIndex: index, editing: null })
                    }
                    disabled={mode === "create"}
                  />
                </div>
              )}
            </div>
          );
        })}
      </div>

      {/* DeedsFormPanel: se lanza desde el OtSidePanel, necesita z-index mayor */}
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

// ── Bloque de escrituras dentro del acordeón ─────────────────────────────────

interface DeedBlockProps {
  mode: PanelMode;
  deeds: LegalRepresentativeCompanySummary["deeds"];
  viewing: string | null;
  onVer: (deedId: string) => void;
  onEditar: (deed: NonNullable<LegalRepresentativeCompanySummary["deeds"][number]>) => void;
  onAsociar: () => void;
  disabled: boolean;
}

function DeedBlock({
  mode,
  deeds,
  viewing,
  onVer,
  onEditar,
  onAsociar,
  disabled,
}: DeedBlockProps) {
  const canEdit = mode === "edit";

  return (
    <section aria-label="Escrituras de la compañía">
      <div className="mb-2 flex items-center justify-between gap-2">
        <h4 className="text-[11px] font-bold uppercase tracking-wide opacity-60">
          Escrituras
        </h4>
        {/* AC5: en create el bloque está deshabilitado */}
        {disabled ? (
          <span className="rounded-lg border px-2.5 py-1 text-[11px] opacity-50"
            style={{ borderColor: "#DFE5ED" }}>
            Disponible al guardar
          </span>
        ) : canEdit ? (
          <button
            type="button"
            onClick={onAsociar}
            className="flex items-center gap-1 rounded-lg border px-2.5 py-1 text-[11px] font-semibold"
            style={{ color: "#557EFF", borderColor: "#557EFF" }}
            aria-label="Asociar escritura a esta compañía"
          >
            <Plus className="h-3.5 w-3.5" aria-hidden="true" /> Asociar escritura
          </button>
        ) : null}
      </div>

      {disabled ? (
        <p className="text-[11px] opacity-50">
          Las escrituras estarán disponibles después de guardar el representante.
        </p>
      ) : deeds.length === 0 ? (
        <p className="text-[11px] opacity-60">Sin escrituras registradas para esta compañía.</p>
      ) : (
        <ul className="space-y-2" aria-label="Historial de escrituras">
          {deeds.map((deed) => (
            <li
              key={deed.id}
              className="flex items-start justify-between gap-2 rounded-xl border px-3 py-2.5"
              style={{ borderColor: "#DFE5ED" }}
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
                  className="flex items-center gap-1 rounded-lg border px-2 py-1 text-[11px] font-medium disabled:opacity-50"
                  style={{ color: "#557EFF", borderColor: "#557EFF" }}
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
                    className="flex items-center gap-1 rounded-lg border px-2 py-1 text-[11px] font-medium"
                    style={{ color: "#162744", borderColor: "#DFE5ED" }}
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

// ── Utilidades de UI ─────────────────────────────────────────────────────────

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
        <p className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }} role="alert">
          {error}
        </p>
      )}
    </div>
  );
}

function ContactField({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="font-semibold opacity-60">{label}</dt>
      <dd className="mt-0.5">{value}</dd>
    </div>
  );
}

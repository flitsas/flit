"use client";

import { useCallback, useEffect, useState } from "react";
import { Building2, FileText, Loader2, Plus, UserSquare } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { useToast } from "@/components/admin/Toast";
import {
  fetchLegalRepresentative,
  type AssignableProcedureType,
  type LegalRepresentativeCompanySummary,
  type LegalRepresentativeItem,
} from "@/lib/api/admin-legal-representatives";
import {
  fetchDeedDetail,
  saveDeed,
  type DeedFormInput,
  type DeedSaved,
  type RepresentedCompany,
} from "@/lib/api/admin-deeds";
import { DeedsFormPanel } from "../deeds/DeedsFormPanel";
import {
  deedEstadoBadge,
  fullName,
  procedureTypeLabels,
  signatureStatus,
} from "./legalRepresentativesDisplay";

export interface LegalRepresentativeDetailModalProps {
  tenantId: string;
  /** Representante seleccionado en el listado (cabecera inmediata); `null` = cerrado. */
  item: LegalRepresentativeItem | null;
  procedureTypes: AssignableProcedureType[];
  onClose: () => void;
}

/**
 * Vista representante-céntrica de un representante legal (HU #10934). El representante es el hub: se
 * muestra una sola vez y, anidadas, sus EMPRESAS; por cada empresa se lista el HISTORIAL de sus
 * ESCRITURAS con su estado (vigente/vencida/programada/inactiva) y la acción "Ver PDF". Cada empresa
 * ofrece un punto de entrada para ASOCIAR una escritura nueva reutilizando `DeedsFormPanel` con la
 * compañía preseleccionada. El número de documento se muestra completo (gestión SuperAdmin autenticada).
 */
export function LegalRepresentativeDetailModal({
  tenantId,
  item,
  procedureTypes,
  onClose,
}: LegalRepresentativeDetailModalProps) {
  const { show } = useToast();
  const [detail, setDetail] = useState<LegalRepresentativeItem | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(false);
  const [viewingDeedId, setViewingDeedId] = useState<string | null>(null);
  // Compañía para la que se está asociando una escritura nueva (abre DeedsFormPanel preseleccionado).
  const [deedFormCompany, setDeedFormCompany] = useState<LegalRepresentativeCompanySummary | null>(null);

  const load = useCallback(
    async (id: string, signal?: AbortSignal) => {
      setLoading(true);
      setError(false);
      try {
        const full = await fetchLegalRepresentative(tenantId, id, signal);
        if (signal?.aborted) return;
        setDetail(full);
      } catch {
        if (!signal?.aborted) setError(true);
      } finally {
        if (!signal?.aborted) setLoading(false);
      }
    },
    [tenantId],
  );

  useEffect(() => {
    if (!item) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- limpia el detalle al cerrar
      setDetail(null);
      return;
    }
    const controller = new AbortController();
    // Muestra de inmediato lo que ya trae el listado (sin escrituras) y refresca con el detalle completo.
    setDetail(item);
    void load(item.id, controller.signal);
    return () => controller.abort();
  }, [item, load]);

  const handleViewDeed = async (deedId: string) => {
    setViewingDeedId(deedId);
    try {
      const deedDetail = await fetchDeedDetail(tenantId, deedId);
      if (deedDetail.viewUrl) {
        window.open(deedDetail.viewUrl, "_blank", "noopener,noreferrer");
      } else {
        show("El PDF de esta escritura aún no está disponible.", "error");
      }
    } catch {
      show("No se pudo abrir el PDF de la escritura.", "error");
    } finally {
      setViewingDeedId(null);
    }
  };

  const handleDeedSubmit = (input: DeedFormInput): Promise<DeedSaved> =>
    saveDeed(tenantId, null, input);

  const handleDeedSaved = () => {
    setDeedFormCompany(null);
    show("Escritura asociada a la empresa.", "success");
    if (item) void load(item.id);
  };

  if (!item) return null;
  const header = detail ?? item;
  const status = signatureStatus(header.hasSignatureOrIdentity);
  const tramites = procedureTypeLabels(header.procedureTypeIds, procedureTypes);
  const companies = detail?.companies ?? item.companies ?? [];

  // Compañía preseleccionada para el panel de escritura (mapeada al tipo del catálogo de escrituras).
  const presetCompany: RepresentedCompany | null = deedFormCompany
    ? { id: deedFormCompany.id, nit: deedFormCompany.nit, name: deedFormCompany.name }
    : null;

  return (
    <>
      <Modal
        open
        onClose={onClose}
        icon={UserSquare}
        title="Detalle del representante"
        description={fullName(header)}
        size="lg"
      >
        <dl className="grid grid-cols-1 gap-3 text-xs sm:grid-cols-2">
          <Field label="Representante" value={fullName(header)} />
          <div>
            <dt className="font-semibold opacity-60">Firma / Identidad</dt>
            <dd className="mt-0.5">
              <StatusBadge tone={status.tone} label={status.label} />
            </dd>
          </div>
          <Field label="Documento" value={`${header.documentType} ${header.documentNumber}`} />
          <Field label="Correo" value={header.email ?? "—"} />
          <Field label="Teléfono" value={header.phone ?? "—"} />
          <Field label="Ciudad" value={header.city ?? "—"} />
          <div className="sm:col-span-2">
            <dt className="font-semibold opacity-60">Tipos de trámite que puede firmar</dt>
            <dd className="mt-1 flex flex-wrap gap-1.5">
              {tramites.length === 0 ? (
                <span className="opacity-60">Ninguno</span>
              ) : (
                tramites.map((t, i) => <StatusBadge key={`${t}-${i}`} tone="info" label={t} />)
              )}
            </dd>
          </div>
        </dl>

        <section className="mt-5 space-y-3">
          <div className="flex items-center justify-between gap-2">
            <h3 className="text-[11px] font-bold uppercase tracking-wide opacity-60">
              Empresas y escrituras
            </h3>
            {loading && <Loader2 className="h-3.5 w-3.5 animate-spin opacity-60" />}
          </div>

          {error && (
            <p className="text-[11px] font-medium" style={{ color: "#FF4E00" }} role="alert">
              No se pudieron cargar las escrituras de las empresas.
            </p>
          )}

          {companies.length === 0 ? (
            <p className="text-[11px] opacity-60">Este representante aún no tiene empresas asociadas.</p>
          ) : (
            companies.map((company) => (
              <div
                key={company.id}
                className="space-y-3 rounded-xl border px-3 py-3"
                style={{ borderColor: "#DFE5ED" }}
              >
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="flex min-w-0 items-center gap-2">
                    <Building2 className="h-4 w-4 shrink-0" style={{ color: "#557EFF" }} />
                    <span className="min-w-0">
                      <span className="block truncate text-xs font-semibold">{company.name}</span>
                      <span className="block font-mono text-[11px] opacity-60">{company.nit}</span>
                    </span>
                  </div>
                  <button
                    type="button"
                    onClick={() => setDeedFormCompany(company)}
                    className="flex items-center gap-1 rounded-lg border px-2.5 py-1.5 text-[11px] font-semibold"
                    style={{ color: "#557EFF", borderColor: "#557EFF" }}
                  >
                    <Plus className="h-3.5 w-3.5" /> Asociar escritura
                  </button>
                </div>

                {company.deeds.length === 0 ? (
                  <p className="text-[11px] opacity-60">Sin escrituras registradas para esta empresa.</p>
                ) : (
                  <ul className="space-y-2">
                    {company.deeds.map((deed) => {
                      const badge = deedEstadoBadge(deed.estado);
                      return (
                        <li
                          key={deed.id}
                          className="flex flex-wrap items-center justify-between gap-2 rounded-lg border px-3 py-2"
                          style={{ borderColor: "#EEF1F6" }}
                        >
                          <div className="flex min-w-0 items-center gap-2">
                            <FileText className="h-3.5 w-3.5 shrink-0 opacity-60" />
                            <div className="min-w-0">
                              <span className="block truncate text-[11px] font-semibold">
                                {deed.description}
                              </span>
                              <span className="block text-[10px] opacity-60">
                                {deed.vigenciaDesde} – {deed.vigenciaHasta}
                              </span>
                            </div>
                          </div>
                          <div className="flex shrink-0 items-center gap-1.5">
                            <StatusBadge tone={badge.tone} label={badge.label} />
                            <button
                              type="button"
                              onClick={() => void handleViewDeed(deed.id)}
                              disabled={viewingDeedId === deed.id}
                              aria-label={`Ver PDF de ${deed.description}`}
                              className="flex items-center gap-1 rounded-lg border px-2.5 py-1 text-[11px] font-semibold disabled:opacity-60"
                            >
                              {viewingDeedId === deed.id && <Loader2 className="h-3 w-3 animate-spin" />}
                              Ver PDF
                            </button>
                          </div>
                        </li>
                      );
                    })}
                  </ul>
                )}
              </div>
            ))
          )}
        </section>

        <div className="mt-5 flex justify-end">
          <button
            type="button"
            onClick={onClose}
            className="rounded-xl px-5 py-2.5 text-sm font-semibold text-white"
            style={{ background: "#557EFF" }}
          >
            Cerrar
          </button>
        </div>
      </Modal>

      <DeedsFormPanel
        open={deedFormCompany !== null}
        editing={null}
        companies={presetCompany ? [presetCompany] : []}
        companiesLoading={false}
        presetCompanyIds={presetCompany ? [presetCompany.id] : undefined}
        onClose={() => setDeedFormCompany(null)}
        onSubmit={handleDeedSubmit}
        onSaved={handleDeedSaved}
        onError={(message) => show(message, "error")}
      />
    </>
  );
}

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="font-semibold opacity-60">{label}</dt>
      <dd className="mt-0.5">{value}</dd>
    </div>
  );
}

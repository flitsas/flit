"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import {
  createCompanyMandateSigner,
  fetchCompanyMandateSigners,
  fetchCompanyTransitOffices,
  fetchRepresentedCompanies,
  inactivateCompanyMandateSigner,
  reactivateCompanyMandateSigner,
  updateCompanyMandateSigner,
  type CompanyMandateSignerInput,
  type CompanyTransitOfficeOption,
  type RepresentedCompanyOption,
  type MandateSigner,
} from "@/lib/api/admin-mandate-signers";
import { formatDocumentWithType } from "@/lib/display/document-number";
import {
  motivoSinFirma,
  organismosSinMedioDeFirma,
} from "@/lib/plataforma/mandatario-firma";
import { CompanyMandatarioForm } from "./CompanyMandatarioForm";

/**
 * HU #11202 — mandatarios gestionados desde el configurador de la COMPAÑÍA.
 *
 * Antes los registraba cada organismo de tránsito y elegía a qué compañías aplicaban. Se invierte: la
 * empresa da de alta a su mandatario una sola vez y marca en cuáles de sus organismos aplica, que es
 * como funciona en la práctica —el mandatario es de la empresa, no del organismo—.
 */
export function CompanyMandatariosPanel({ tenantId }: { tenantId: string }) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [signers, setSigners] = useState<MandateSigner[]>([]);
  const [offices, setOffices] = useState<CompanyTransitOfficeOption[]>([]);
  const [companies, setCompanies] = useState<RepresentedCompanyOption[]>([]);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<MandateSigner | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const [signerList, officeList, companyList] = await Promise.all([
          fetchCompanyMandateSigners(tenantId, signal),
          fetchCompanyTransitOffices(tenantId, signal),
          // Best-effort: sin empresas el formulario sigue funcionando y el mandatario aplica a todas,
          // que es el comportamiento por defecto.
          fetchRepresentedCompanies(tenantId, signal).catch(() => []),
        ]);
        if (signal?.aborted) {
          return;
        }
        setSigners(signerList);
        setOffices(officeList);
        setCompanies(companyList);
        setStatus(signerList.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) {
          setStatus("error");
        }
      }
    },
    [tenantId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const officeNameById = useMemo(
    () => new Map(offices.map((o) => [o.transitOfficeId, o.name])),
    [offices],
  );

  const handleSubmit = async (input: CompanyMandateSignerInput) => {
    const saved = editing
      ? await updateCompanyMandateSigner(tenantId, editing.id, input)
      : await createCompanyMandateSigner(tenantId, input);
    setFormOpen(false);
    setEditing(null);
    show(editing ? "Mandatario actualizado." : "Mandatario registrado.", "success");
    await load();
    return saved;
  };

  const handleToggleActivo = async (signer: MandateSigner) => {
    setBusyId(signer.id);
    try {
      if (signer.isActive) {
        await inactivateCompanyMandateSigner(tenantId, signer.id);
        show(`${signer.fullName} quedó inactivo.`, "success");
      } else {
        await reactivateCompanyMandateSigner(tenantId, signer.id);
        show(`${signer.fullName} vuelve a estar activo.`, "success");
      }
      await load();
    } catch {
      show("No se pudo cambiar el estado del mandatario.", "error");
    } finally {
      setBusyId(null);
    }
  };

  const openCreate = () => {
    setEditing(null);
    setFormOpen(true);
  };

  /**
   * HU #11717 — organismos donde el mandatario está habilitado pero no podría firmar. Se calcula con
   * la misma regla que impone el backend al parametrizar, para que la consola no diga una cosa y el
   * guardado otra.
   */
  const sinFirmaPorSigner = (signer: MandateSigner) =>
    organismosSinMedioDeFirma(
      signer.transitOfficeIds ?? [],
      signer.physicalSignatureOfficeIds ?? [],
      signer,
    );

  const sinOrganismos = offices.length === 0;

  return (
    <div className="space-y-4">
      {sinOrganismos && (
        <p
          className="rounded-xl border px-3 py-2 text-xs"
          style={{ borderColor: "#F9AC00", background: "rgba(249,172,0,0.08)", color: "#8a6000" }}
          role="status"
        >
          Esta compañía todavía no tiene organismos de tránsito habilitados. Habilítalos en la matriz
          de organismos antes de registrar mandatarios: sin organismo no hay dónde aplicarlos.
        </p>
      )}

      <div className="flex justify-end">
        <button
          type="button"
          className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-50"
          style={{ background: "#557EFF" }}
          onClick={openCreate}
          disabled={sinOrganismos}
        >
          Nuevo mandatario
        </button>
      </div>

      <UiStateBoundary
        status={status}
        emptyMessage="Esta compañía no tiene mandatarios registrados."
        errorMessage="No se pudieron cargar los mandatarios."
        onRetry={() => void load()}
        skeletonRows={4}
      >
        <div className="overflow-x-auto">
          <table className="w-full border-separate border-spacing-y-2 text-xs">
            <thead>
              <tr className="text-left text-[10px] font-semibold uppercase text-foreground">
                <th className="rounded-l-xl bg-muted px-4 py-2.5">Mandatario</th>
                <th className="bg-muted px-4 py-2.5">Documento</th>
                <th className="bg-muted px-4 py-2.5">Organismos</th>
                <th className="rounded-r-xl bg-muted px-4 py-2.5 text-right">Acciones</th>
              </tr>
            </thead>
            <tbody>
              {signers.map((signer) => (
                <tr key={signer.id} className="bg-card">
                  <td className={`rounded-l-xl border-y border-l px-4 py-3 ${signer.isActive ? "" : "opacity-60"}`}>
                    <span className="font-semibold">{signer.fullName}</span>
                    {!signer.isActive && (
                      <span className="ml-2 inline-block rounded-full border bg-muted px-2 py-0.5 text-[10px] font-semibold text-muted-foreground">
                        Inactivo
                      </span>
                    )}
                  </td>
                  <td className={`border-y px-4 py-3 font-mono ${signer.isActive ? "" : "opacity-60"}`}>
                    {formatDocumentWithType(signer.documentType, signer.documentNumber)}
                  </td>
                  <td className={`border-y px-4 py-3 ${signer.isActive ? "" : "opacity-60"}`}>
                    {(signer.transitOfficeIds ?? []).length === 0
                      ? "—"
                      : (signer.transitOfficeIds ?? [])
                          .map((id) => officeNameById.get(id) ?? id)
                          .join(", ")}
                    {/* HU #11717 — se SEÑALA, no se inhabilita: los trámites en curso siguen
                        emitiendo su mandato como hoy. Los organismos de firma física quedan fuera,
                        porque ahí la línea en blanco es el resultado correcto. */}
                    {sinFirmaPorSigner(signer).length > 0 && (
                      <div
                        className="mt-1 text-[11px] leading-tight"
                        style={{ color: "#E5484D" }}
                        title={motivoSinFirma(signer)}
                      >
                        No puede firmar en{" "}
                        {sinFirmaPorSigner(signer)
                          .map((id) => officeNameById.get(id) ?? id)
                          .join(", ")}
                        : {motivoSinFirma(signer).toLowerCase()}
                      </div>
                    )}
                  </td>
                  <td className="rounded-r-xl border-y border-r px-4 py-3 text-right">
                    <button
                      type="button"
                      className="mr-3 text-[11px] font-semibold"
                      style={{ color: "#557EFF" }}
                      onClick={() => {
                        setEditing(signer);
                        setFormOpen(true);
                      }}
                    >
                      Editar
                    </button>
                    <button
                      type="button"
                      className="text-[11px] font-semibold disabled:opacity-50"
                      style={{ color: signer.isActive ? "#E5484D" : "#8CC63F" }}
                      onClick={() => void handleToggleActivo(signer)}
                      disabled={busyId === signer.id}
                    >
                      {signer.isActive ? "Inactivar" : "Reactivar"}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </UiStateBoundary>

      {formOpen && (
        <CompanyMandatarioForm
          tenantId={tenantId}
          offices={offices}
          companies={companies}
          editing={editing}
          onCancel={() => {
            setFormOpen(false);
            setEditing(null);
          }}
          onSubmit={handleSubmit}
        />
      )}
    </div>
  );
}

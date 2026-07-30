"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { ShieldAlert, ShieldCheck } from "lucide-react";
import {
  createMandateSigner,
  fetchMandateSignersWithFlags,
  fetchOtCompanies,
  linkMandateSignerIdentity,
  mockMandateSignerIdentity,
  inactivateMandateSigner,
  reactivateMandateSigner,
  resendMandateSignerIdentity,
  sendMandateSignerIdentity,
  updateMandateSigner,
  type MandateSigner,
  type MandateSignerInput,
  type MandateSignerSaved,
  type OtCompany,
} from "@/lib/api/admin-mandate-signers";
import { MandatarioFormPanel } from "./MandatarioFormPanel";
import {
  hasPriorIdentity,
  identityUi,
  puedeRenovarIdentidad,
  vigenciaLabel,
} from "./mandatario-identity";

/**
 * Pestaña "Mandatario" del hub Admin OT (ADR-0023): lista los mandatarios activos del OT,
 * permite registrar/editar (con multiselect de compañías y exclusividad) e inactivar (baja
 * lógica que libera compañías). 4 estados de UI + WCAG 2.1 AA.
 */
export function MandatariosSection({ transitOfficeId }: { transitOfficeId: string }) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [signers, setSigners] = useState<MandateSigner[]>([]);
  const [companies, setCompanies] = useState<OtCompany[]>([]);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<MandateSigner | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  // HU #11028 — la simulación de identidad solo existe donde el ambiente la habilita.
  const [mockEnabled, setMockEnabled] = useState(false);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const [signerResult, companyList] = await Promise.all([
          fetchMandateSignersWithFlags(transitOfficeId, signal),
          fetchOtCompanies(transitOfficeId, signal),
        ]);
        if (signal?.aborted) {
          return;
        }
        setSigners(signerResult.signers);
        setMockEnabled(signerResult.mockIdentityEnabled);
        setCompanies(companyList);
        setStatus(signerResult.signers.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) {
          setStatus("error");
        }
      }
    },
    [transitOfficeId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const companyNameById = useMemo(
    () => new Map(companies.map((c) => [c.companyTenantId, c.legalName])),
    [companies],
  );

  const openCreate = () => {
    setEditing(null);
    setFormOpen(true);
  };

  const openEdit = (signer: MandateSigner) => {
    setEditing(signer);
    setFormOpen(true);
  };

  const handleSubmit = (input: MandateSignerInput) =>
    editing
      ? updateMandateSigner(transitOfficeId, editing.id, input)
      : createMandateSigner(transitOfficeId, input);

  const handleInactivate = async (signer: MandateSigner) => {
    setBusyId(signer.id);
    try {
      await inactivateMandateSigner(transitOfficeId, signer.id);
      show(`Mandatario ${signer.fullName} inactivado. Sus compañías quedaron libres.`, "success");
      await load();
    } catch {
      show("No se pudo inactivar el mandatario.", "error");
    } finally {
      setBusyId(null);
    }
  };

  // HU #11000 — acción rápida de identidad desde la fila: envía la primera validación o
  // reenvía/renueva la existente (vencida, rechazada o en curso), sin abrir el panel de edición.
  const handleIdentity = async (signer: MandateSigner) => {
    setBusyId(signer.id);
    try {
      const result = hasPriorIdentity(signer.identityStatus)
        ? await resendMandateSignerIdentity(transitOfficeId, signer.id)
        : await sendMandateSignerIdentity(transitOfficeId, signer.id);
      show(
        result.reused
          ? `${signer.fullName} ya tiene una validación de identidad vigente.`
          : `Validación de identidad enviada al correo de ${signer.fullName}.`,
        "success",
      );
      await load();
    } catch {
      show("No se pudo enviar la validación de identidad.", "error");
    } finally {
      setBusyId(null);
    }
  };

  // HU #11028 — vincula una identidad que la PERSONA ya validó (como representante legal, en otro
  // organismo…). No envía correo: si no hay ninguna vigente, se dice y no se crea nada.
  const handleLinkIdentity = async (signer: MandateSigner) => {
    setBusyId(signer.id);
    try {
      await linkMandateSignerIdentity(transitOfficeId, signer.id);
      show(`Identidad vigente vinculada a ${signer.fullName}.`, "success");
      await load();
    } catch (err) {
      const conflicto = err instanceof Error && err.message.includes("409");
      show(
        conflicto
          ? `${signer.fullName} no tiene una validación de identidad vigente que vincular.`
          : "No se pudo vincular la validación de identidad.",
        "error",
      );
    } finally {
      setBusyId(null);
    }
  };

  // HU #11028 — simula una validación aprobada para poder probar la firma del mandato en ambientes
  // donde nadie puede completar una biométrica real. Queda marcada como simulada.
  const handleMockIdentity = async (signer: MandateSigner) => {
    setBusyId(signer.id);
    try {
      await mockMandateSignerIdentity(transitOfficeId, signer.id);
      show(`Validación de identidad SIMULADA para ${signer.fullName} (solo pruebas).`, "success");
      await load();
    } catch {
      show("No se pudo simular la validación de identidad.", "error");
    } finally {
      setBusyId(null);
    }
  };

  const handleReactivate = async (signer: MandateSigner) => {
    setBusyId(signer.id);
    try {
      await reactivateMandateSigner(transitOfficeId, signer.id);
      show(`Mandatario ${signer.fullName} reactivado. Asígnale compañías con «Editar».`, "success");
      await load();
    } catch {
      show("No se pudo reactivar el mandatario.", "error");
    } finally {
      setBusyId(null);
    }
  };

  const emptyCta = (
    <button
      type="button"
      className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
      style={{ background: "#557EFF" }}
      onClick={openCreate}
    >
      Registrar primer mandatario
    </button>
  );

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <button
          type="button"
          className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
          style={{ background: "#557EFF" }}
          onClick={openCreate}
        >
          Nuevo mandatario
        </button>
      </div>

      <UiStateBoundary
        status={status}
        emptyMessage="Este organismo de tránsito no tiene mandatarios registrados."
        emptyCta={emptyCta}
        errorMessage="No se pudieron cargar los mandatarios."
        onRetry={() => void load()}
        skeletonRows={4}
      >
        <div className="overflow-x-auto">
        <table className="w-full border-separate border-spacing-y-2 text-xs">
          <thead>
            <tr className="text-left text-[10px] font-semibold uppercase text-foreground">
              <th className="rounded-l-xl px-4 py-2.5 bg-muted">
                Mandatario
              </th>
              <th className="px-4 py-2.5 bg-muted">
                Documento
              </th>
              <th className="px-4 py-2.5 bg-muted">
                Compañías
              </th>
              <th className="px-4 py-2.5 bg-muted">
                Identidad
              </th>
              <th className="px-4 py-2.5 bg-muted">
                Huella
              </th>
              <th className="rounded-r-xl px-4 py-2.5 text-right bg-muted">
                Acciones
              </th>
            </tr>
          </thead>
          <tbody>
            {signers.map((signer) => (
              <tr key={signer.id} className="bg-card">
                <td className={`rounded-l-xl border-y border-l px-4 py-3 ${signer.isActive ? "" : "opacity-60"}`}>
                  <span className="font-semibold">{signer.fullName}</span>
                  {!signer.isActive && (
                    <span
                      className="ml-2 inline-block rounded-full border px-2 py-0.5 text-[10px] font-semibold bg-muted text-muted-foreground"
                    >
                      Inactivo
                    </span>
                  )}
                </td>
                <td className={`border-y px-4 py-3 font-mono ${signer.isActive ? "" : "opacity-60"}`}>
                  {maskDocument(signer.documentNumber)}
                </td>
                <td className={`border-y px-4 py-3 ${signer.isActive ? "" : "opacity-60"}`}>
                  {signer.companyTenantIds.length === 0
                    ? "—"
                    : signer.companyTenantIds
                        .map((id) => companyNameById.get(id) ?? id)
                        .join(", ")}
                </td>
                <td className={`border-y px-4 py-3 ${signer.isActive ? "" : "opacity-60"}`}>
                  {(() => {
                    const identity = identityUi(signer.identityStatus);
                    // HU #11060 — con la identidad vigente se informa HASTA CUÁNDO lo está, en vez de
                    // dejar solo el chip: es la diferencia entre "está bien" y "está bien hasta el X".
                    const vigencia = vigenciaLabel(
                      signer.identityStatus,
                      signer.identityValidUntil,
                    );
                    return (
                      <>
                        <span
                          data-testid={`ms-identity-${signer.id}`}
                          className="inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-[10px] font-semibold"
                          style={identity.style}
                        >
                          {identity.isValid ? (
                            <ShieldCheck className="h-3 w-3" />
                          ) : (
                            <ShieldAlert className="h-3 w-3" />
                          )}
                          {identity.label}
                        </span>
                        {vigencia && (
                          <span
                            className="mt-1 block text-[10px] opacity-60"
                            data-testid={`ms-identity-vigencia-${signer.id}`}
                          >
                            {vigencia}
                          </span>
                        )}
                      </>
                    );
                  })()}
                </td>
                <td
                  className={`border-y px-4 py-3 font-mono opacity-70 ${signer.isActive ? "" : "opacity-40"}`}
                  title={signer.integrityHash}
                >
                  {signer.integrityHash.slice(0, 10)}…
                </td>
                <td className="rounded-r-xl border-y border-r px-4 py-3 text-right">
                  <div className="flex justify-end gap-2">
                    {signer.isActive ? (
                      <>
                        <button
                          type="button"
                          className="rounded-lg border px-3 py-1.5 text-[11px] font-semibold disabled:opacity-50"
                          style={{ color: "#557EFF", borderColor: "#557EFF" }}
                          // HU #11060 — con la identidad vigente NO se ofrece renovar: el backend
                          // reutiliza la vigente y no reenvía nada, así que el botón prometía una
                          // acción que no ocurre. Se informa la vigencia en el chip de al lado.
                          disabled={
                            busyId === signer.id ||
                            !signer.email ||
                            !puedeRenovarIdentidad(signer.identityStatus)
                          }
                          title={
                            !puedeRenovarIdentidad(signer.identityStatus)
                              ? vigenciaLabel(signer.identityStatus, signer.identityValidUntil) ??
                                "La identidad ya está validada y vigente."
                              : signer.email
                                ? undefined
                                : "Agrega un correo al mandatario para poder enviarle la validación."
                          }
                          aria-label={`${identityUi(signer.identityStatus).action} de ${signer.fullName}`}
                          onClick={() => void handleIdentity(signer)}
                        >
                          {identityUi(signer.identityStatus).action}
                        </button>
                        <button
                          type="button"
                          className="rounded-lg border px-3 py-1.5 text-[11px] font-semibold disabled:opacity-50"
                          disabled={busyId === signer.id || signer.identityStatus === "valid"}
                          title={
                            signer.identityStatus === "valid"
                              ? "El mandatario ya tiene una identidad validada y vigente."
                              : "Vincula una validación de identidad que esta persona ya haya completado."
                          }
                          aria-label={`Vincular validación existente de ${signer.fullName}`}
                          onClick={() => void handleLinkIdentity(signer)}
                        >
                          Vincular validación
                        </button>
                        {mockEnabled && (
                          <button
                            type="button"
                            className="rounded-lg border px-3 py-1.5 text-[11px] font-semibold disabled:opacity-50"
                            style={{ color: "#b45309", borderColor: "#f0c38e" }}
                            disabled={busyId === signer.id || signer.identityStatus === "valid"}
                            title="Crea una validación de identidad SIMULADA. Solo para ambientes de prueba."
                            aria-label={`Simular validación de identidad de ${signer.fullName}`}
                            onClick={() => void handleMockIdentity(signer)}
                          >
                            Simular validación
                          </button>
                        )}
                        <button
                          type="button"
                          className="rounded-lg border px-3 py-1.5 text-[11px] font-semibold"
                          onClick={() => openEdit(signer)}
                        >
                          Editar
                        </button>
                        <button
                          type="button"
                          className="rounded-lg border px-3 py-1.5 text-[11px] font-semibold disabled:opacity-50"
                          style={{ color: "#FF4E00", borderColor: "#f0c38e" }}
                          disabled={busyId === signer.id}
                          aria-label={`Inactivar mandatario ${signer.fullName}`}
                          onClick={() => void handleInactivate(signer)}
                        >
                          Inactivar
                        </button>
                      </>
                    ) : (
                      <button
                        type="button"
                        className="rounded-lg border px-3 py-1.5 text-[11px] font-semibold text-white disabled:opacity-50"
                        style={{ background: "#557EFF", borderColor: "#557EFF" }}
                        disabled={busyId === signer.id}
                        aria-label={`Reactivar mandatario ${signer.fullName}`}
                        onClick={() => void handleReactivate(signer)}
                      >
                        Reactivar
                      </button>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        </div>
      </UiStateBoundary>

      <MandatarioFormPanel
        open={formOpen}
        transitOfficeId={transitOfficeId}
        editing={editing}
        companies={companies}
        onClose={() => setFormOpen(false)}
        onSubmit={handleSubmit}
        onSaved={(saved) => {
          setFormOpen(false);
          // HU #11000 — el aviso refleja qué pasó con la validación de identidad en el alta.
          show(editing ? "Mandatario actualizado." : mensajeAlta(saved.identity), "success");
          void load();
        }}
        onError={(message) => show(message, "error")}
      />
    </div>
  );
}

/** Aviso del alta según el desenlace de la validación de identidad (HU #11000). */
function mensajeAlta(identity: MandateSignerSaved["identity"]): string {
  switch (identity) {
    case "sent":
      return "Mandatario registrado. Se envió la validación de identidad a su correo.";
    case "reused":
      return "Mandatario registrado. Ya tenía una identidad validada vigente: se apalancó.";
    case "failed":
      return "Mandatario registrado, pero no se pudo enviar la validación de identidad. Reenvíala desde la fila.";
    default:
      return "Mandatario registrado. Agrégale un correo para enviarle la validación de identidad.";
  }
}

/** Enmascara el número de documento (PII, Ley 1581): solo se muestran los últimos 4 dígitos. */
function maskDocument(documentNumber: string): string {
  if (documentNumber.length <= 4) {
    return "••••";
  }
  return `••••${documentNumber.slice(-4)}`;
}

"use client";

import { useCallback, useEffect, useId, useState, type KeyboardEvent } from "react";
import { ToggleSwitch } from "@/components/admin/companies/ToggleSwitch";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import {
  approveOtClientProcedure,
  descargarOtConsolidado,
  fetchOtClientProcedures,
  fetchOtProfile,
  generarOtConsolidado,
  rejectOtClientProcedure,
  updateOtFeatureFlag,
  updateOtProfile,
} from "@/lib/api/admin-ot";
import type { OtClientProcedure, OtFeatureFlag, OtProfile } from "@/lib/api/types-ot";
import { TramitesProcedureList } from "./TramitesProcedureList";
import { QuipuxQueueList } from "./QuipuxQueueList";
import { OT_INPUT_CLS } from "./ot-form-styles";

export type TramitesPanel = "dashboard" | "quipux";

export interface TramitesSuperSectionProps {
  transitOfficeId: string;
}

const PAGE_SIZE = 20;

/** Súper-sección Trámites OT con switch Dashboard/QX (HU #10218). */
export function TramitesSuperSection({ transitOfficeId }: TramitesSuperSectionProps) {
  const { show } = useToast();
  const tabsId = useId();
  const [profile, setProfile] = useState<OtProfile | null>(null);
  const [profileStatus, setProfileStatus] = useState<UiStatus>("loading");
  const [listStatus, setListStatus] = useState<UiStatus>("loading");
  const [procedures, setProcedures] = useState<OtClientProcedure[]>([]);
  const [activePanel, setActivePanel] = useState<TramitesPanel>("dashboard");
  const [switchingMode, setSwitchingMode] = useState(false);
  const [togglingFlagId, setTogglingFlagId] = useState<string | null>(null);
  const [consolidadoActingId, setConsolidadoActingId] = useState<string | null>(null);
  const [approveTarget, setApproveTarget] = useState<OtClientProcedure | null>(null);
  const [rejectTarget, setRejectTarget] = useState<OtClientProcedure | null>(null);
  const [rejectReason, setRejectReason] = useState("");
  const [acting, setActing] = useState(false);

  const operationalFlags = (profile?.featureFlags ?? []).filter(
    (f) => !f.flagKey.startsWith("rule:"),
  );

  const isQuipuxMode = profile?.operationMode === "quipux";
  const isReadOnly = Boolean(profile?.quipuxReadOnly && isQuipuxMode);

  const loadProfile = useCallback(async (signal?: AbortSignal) => {
    setProfileStatus("loading");
    try {
      const data = await fetchOtProfile(signal, { transitOfficeId });
      if (signal?.aborted) {
        return;
      }
      setProfile(data);
      setActivePanel(data.operationMode === "quipux" ? "quipux" : "dashboard");
      setProfileStatus("ready");
    } catch {
      if (!signal?.aborted) {
        setProfileStatus("error");
      }
    }
  }, [transitOfficeId]);

  const loadProcedures = useCallback(async (signal?: AbortSignal) => {
    setListStatus("loading");
    try {
      const data = await fetchOtClientProcedures(
        { status: "entregado", page: 1, pageSize: PAGE_SIZE },
        signal,
        { transitOfficeId },
      );
      if (signal?.aborted) {
        return;
      }
      setProcedures(data.data);
      setListStatus(data.data.length === 0 ? "empty" : "ready");
    } catch {
      if (!signal?.aborted) {
        setListStatus("error");
      }
    }
  }, [transitOfficeId]);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    void loadProfile(controller.signal);
    return () => controller.abort();
  }, [loadProfile]);

  useEffect(() => {
    if (profileStatus !== "ready") {
      return;
    }
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga encadenada tras perfil OT
    void loadProcedures(controller.signal);
    return () => controller.abort();
  }, [profileStatus, loadProcedures]);

  const handleModeToggle = async (checked: boolean) => {
    if (!profile || switchingMode) {
      return;
    }

    const nextMode = checked ? "quipux" : "dashboard";
    setSwitchingMode(true);
    try {
      const updated = await updateOtProfile({ operationMode: nextMode });
      setProfile(updated);
      setActivePanel(nextMode === "quipux" ? "quipux" : "dashboard");
      show(
        nextMode === "quipux"
          ? "Consola en solo lectura: este OT opera en Quipux."
          : "Consola operativa en FLIT.",
        "success",
      );
    } catch {
      show("No se pudo cambiar el modo de operación.", "error");
    } finally {
      setSwitchingMode(false);
    }
  };

  const handleFlagToggle = async (flag: OtFeatureFlag, checked: boolean) => {
    if (!profile || togglingFlagId) {
      return;
    }

    setTogglingFlagId(flag.id);
    const previous = profile.featureFlags;
    setProfile({
      ...profile,
      featureFlags: profile.featureFlags.map((f) =>
        f.id === flag.id ? { ...f, isEnabled: checked } : f,
      ),
    });

    try {
      const updated = await updateOtFeatureFlag(flag.id, { isEnabled: checked });
      setProfile((current) =>
        current
          ? {
              ...current,
              featureFlags: current.featureFlags.map((f) =>
                f.id === updated.id ? updated : f,
              ),
            }
          : current,
      );
      show(`Flag "${flag.flagKey}" ${checked ? "activado" : "desactivado"}.`, "success");
    } catch {
      setProfile((current) => (current ? { ...current, featureFlags: previous } : current));
      show("No se pudo actualizar el feature flag.", "error");
    } finally {
      setTogglingFlagId(null);
    }
  };

  const handleGenerarConsolidado = async (procedure: OtClientProcedure) => {
    setConsolidadoActingId(procedure.id);
    try {
      await generarOtConsolidado(procedure.id, { transitOfficeId });
      show("Consolidado generado.", "success");
    } catch {
      show("No se pudo generar el consolidado (verifica FUR y documentos del trámite).", "error");
    } finally {
      setConsolidadoActingId(null);
    }
  };

  const handleVerConsolidado = async (procedure: OtClientProcedure) => {
    setConsolidadoActingId(procedure.id);
    try {
      await descargarOtConsolidado(procedure.id, procedure.referenceNumber, { transitOfficeId });
    } catch {
      show("El trámite aún no tiene consolidado generado.", "error");
    } finally {
      setConsolidadoActingId(null);
    }
  };

  const confirmApprove = async () => {
    if (!approveTarget) {
      return;
    }
    setActing(true);
    try {
      const updated = await approveOtClientProcedure(approveTarget.id);
      const next = procedures.filter((p) => p.id !== updated.id);
      setProcedures(next);
      setListStatus(next.length === 0 ? "empty" : "ready");
      setApproveTarget(null);
      show("Trámite aprobado.", "success");
    } catch {
      show("No se pudo aprobar el trámite.", "error");
    } finally {
      setActing(false);
    }
  };

  const confirmReject = async () => {
    if (!rejectTarget || !rejectReason.trim()) {
      return;
    }
    setActing(true);
    try {
      const updated = await rejectOtClientProcedure(rejectTarget.id, {
        reason: rejectReason.trim(),
      });
      const next = procedures.filter((p) => p.id !== updated.id);
      setProcedures(next);
      setListStatus(next.length === 0 ? "empty" : "ready");
      setRejectTarget(null);
      setRejectReason("");
      show("Trámite rechazado.", "success");
    } catch {
      show("No se pudo rechazar el trámite.", "error");
    } finally {
      setActing(false);
    }
  };

  const handleTabKeyDown = (event: KeyboardEvent<HTMLButtonElement>, panel: TramitesPanel) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      setActivePanel(panel);
    }
    if (event.key === "ArrowRight" || event.key === "ArrowLeft") {
      event.preventDefault();
      setActivePanel(panel === "dashboard" ? "quipux" : "dashboard");
    }
  };

  if (profileStatus === "loading") {
    return <UiStateBoundary status="loading" skeletonRows={5} />;
  }

  if (profileStatus === "error" || !profile) {
    return (
      <UiStateBoundary
        status="error"
        errorMessage="No se pudo cargar el perfil OT."
        onRetry={() => void loadProfile()}
      />
    );
  }

  return (
    <section aria-labelledby={`${tabsId}-heading`} className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <h2 id={`${tabsId}-heading`} className="text-sm font-bold">
            Trámites — OT {transitOfficeId.slice(0, 8)}…
          </h2>
          {isReadOnly && (
            <span
              className="rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide"
              style={{ background: "#FFF4EC", color: "#FF4E00" }}
            >
              Solo lectura
            </span>
          )}
        </div>
        <div className="w-full max-w-xs">
          {/* El nombre anterior («Modo QX — Integración con cola Quipux») hacía creer que aquí
              se decidía si a la secretaría se le radica por Quipux. No es eso: este toggle solo
              pone la consola de ESTE OT-cliente en solo lectura, porque aprueba dentro de
              Quipux. La radicación a la secretaría DESTINO se parametriza en el catálogo
              (código DIVIPO + banderas, HU #10710). */}
          <ToggleSwitch
            id={`${tabsId}-mode-qx`}
            label="Consola en solo lectura (opera en Quipux)"
            description="Este OT aprueba y rechaza dentro de Quipux, no en FLIT. No afecta a la radicación."
            checked={isQuipuxMode}
            onChange={(checked) => void handleModeToggle(checked)}
          />
        </div>
      </div>

      {operationalFlags.length > 0 && (
        <div
          className="rounded-2xl border bg-card p-4"
          aria-labelledby={`${tabsId}-flags-heading`}
        >
          <h3 id={`${tabsId}-flags-heading`} className="mb-3 text-xs font-bold">
            Feature flags operativos
          </h3>
          <ul className="space-y-2">
            {operationalFlags.map((flag) => (
              <li key={flag.id}>
                <ToggleSwitch
                  id={`${tabsId}-flag-${flag.id}`}
                  label={flag.flagKey}
                  description="Hot-swap sin reinicio de servicio"
                  checked={flag.isEnabled}
                  disabled={togglingFlagId === flag.id}
                  onChange={(checked) => void handleFlagToggle(flag, checked)}
                />
              </li>
            ))}
          </ul>
        </div>
      )}

      <div role="tablist" aria-label="Paneles de trámites" className="flex gap-2">
        <button
          type="button"
          role="tab"
          id={`${tabsId}-tab-dashboard`}
          aria-selected={activePanel === "dashboard"}
          aria-controls={`${tabsId}-panel-dashboard`}
          tabIndex={activePanel === "dashboard" ? 0 : -1}
          className={`rounded-xl px-4 py-2 text-xs font-semibold ${
            activePanel === "dashboard" ? "bg-[#557EFF] text-white" : "bg-muted text-foreground"
          }`}
          onClick={() => setActivePanel("dashboard")}
          onKeyDown={(e) => handleTabKeyDown(e, "dashboard")}
        >
          Dashboard FLIT
        </button>
        <button
          type="button"
          role="tab"
          id={`${tabsId}-tab-quipux`}
          aria-selected={activePanel === "quipux"}
          aria-controls={`${tabsId}-panel-quipux`}
          tabIndex={activePanel === "quipux" ? 0 : -1}
          className={`rounded-xl px-4 py-2 text-xs font-semibold ${
            activePanel === "quipux" ? "bg-[#557EFF] text-white" : "bg-muted text-foreground"
          }`}
          onClick={() => setActivePanel("quipux")}
          onKeyDown={(e) => handleTabKeyDown(e, "quipux")}
        >
          Cola QX (Quipux)
        </button>
      </div>

      <div
        role="tabpanel"
        id={`${tabsId}-panel-dashboard`}
        aria-labelledby={`${tabsId}-tab-dashboard`}
        hidden={activePanel !== "dashboard"}
        className="rounded-2xl border bg-card p-4"
      >
        <UiStateBoundary
          status={listStatus}
          emptyMessage="No hay trámites pendientes en el panel Dashboard."
          errorMessage="Error al cargar los trámites."
          onRetry={() => void loadProcedures()}
          skeletonRows={4}
        >
          <TramitesProcedureList
            procedures={procedures}
            showApprovalActions={!isReadOnly}
            onApprove={(id) => setApproveTarget(procedures.find((p) => p.id === id) ?? null)}
            onReject={(id) => setRejectTarget(procedures.find((p) => p.id === id) ?? null)}
            onGenerarConsolidado={isReadOnly ? undefined : handleGenerarConsolidado}
            onVerConsolidado={handleVerConsolidado}
            consolidadoActingId={consolidadoActingId}
          />
        </UiStateBoundary>
      </div>

      <div
        role="tabpanel"
        id={`${tabsId}-panel-quipux`}
        aria-labelledby={`${tabsId}-tab-quipux`}
        hidden={activePanel !== "quipux"}
        className="rounded-2xl border bg-card p-4"
      >
        {/* Cola Quipux REAL de la secretaría destino (tramites.quipux_submissions, HU #10774):
            las radicaciones con su estado en el ciclo de Quipux y las acciones de operación del
            SuperAdmin. Ya no es la fachada que repintaba la bandeja del dashboard. */}
        <QuipuxQueueList transitOfficeId={transitOfficeId} />
      </div>

      {approveTarget && (
        <div
          className="fixed inset-0 z-[90] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-label="Confirmar aprobación"
        >
          <div
            className="w-full max-w-md max-h-[90dvh] overflow-y-auto rounded-2xl border bg-card p-6 shadow-2xl"
          >
            <h2 className="text-lg font-semibold text-foreground">
              ¿Aprobar este trámite?
            </h2>
            <p className="mt-2 text-sm opacity-80">{approveTarget.referenceNumber}</p>
            <div className="mt-5 flex gap-3">
              <button
                type="button"
                className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
                onClick={() => setApproveTarget(null)}
                disabled={acting}
              >
                Cancelar
              </button>
              <button
                type="button"
                className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
                style={{ background: "#557EFF" }}
                disabled={acting}
                onClick={() => void confirmApprove()}
              >
                {acting ? "Procesando…" : "Confirmar"}
              </button>
            </div>
          </div>
        </div>
      )}

      {rejectTarget && (
        <div
          className="fixed inset-0 z-[90] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-label="Rechazar trámite"
        >
          <div
            className="w-full max-w-md max-h-[90dvh] overflow-y-auto rounded-2xl border bg-card p-6 shadow-2xl"
          >
            <h2 className="text-lg font-semibold text-foreground">
              Motivo del rechazo
            </h2>
            <textarea
              className={`mt-3 ${OT_INPUT_CLS}`}
              rows={3}
              value={rejectReason}
              onChange={(e) => setRejectReason(e.target.value)}
            />
            <div className="mt-5 flex gap-3">
              <button
                type="button"
                className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
                onClick={() => setRejectTarget(null)}
                disabled={acting}
              >
                Cancelar
              </button>
              <button
                type="button"
                className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
                style={{ background: "#FF4E00" }}
                disabled={acting || !rejectReason.trim()}
                onClick={() => void confirmReject()}
              >
                Confirmar rechazo
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}

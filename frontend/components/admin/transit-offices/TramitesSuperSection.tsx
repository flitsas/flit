"use client";

import { useCallback, useEffect, useId, useState, type KeyboardEvent } from "react";
import { ToggleSwitch } from "@/components/admin/companies/ToggleSwitch";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import {
  fetchOtClientProcedures,
  fetchOtProfile,
  updateOtFeatureFlag,
  updateOtProfile,
} from "@/lib/api/admin-ot";
import type { OtClientProcedure, OtFeatureFlag, OtProfile } from "@/lib/api/types-ot";
import { TramitesProcedureList } from "./TramitesProcedureList";

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

  const operationalFlags = (profile?.featureFlags ?? []).filter(
    (f) => !f.flagKey.startsWith("rule:"),
  );

  const isQuipuxMode = profile?.operationMode === "quipux";
  const isReadOnly = Boolean(profile?.quipuxReadOnly && isQuipuxMode);

  const loadProfile = useCallback(async (signal?: AbortSignal) => {
    setProfileStatus("loading");
    try {
      const data = await fetchOtProfile(signal);
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
  }, []);

  const loadProcedures = useCallback(async (signal?: AbortSignal) => {
    setListStatus("loading");
    try {
      const data = await fetchOtClientProcedures(
        { status: "pending_ot", page: 1, pageSize: PAGE_SIZE },
        signal,
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
  }, []);

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
        nextMode === "quipux" ? "Modo QX activado." : "Modo Dashboard activado.",
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
          <ToggleSwitch
            id={`${tabsId}-mode-qx`}
            label="Modo QX"
            description="Integración con cola Quipux"
            checked={isQuipuxMode}
            onChange={(checked) => void handleModeToggle(checked)}
          />
        </div>
      </div>

      {operationalFlags.length > 0 && (
        <div
          className="rounded-2xl border bg-white p-4"
          style={{ borderColor: "#DFE5ED" }}
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
          className="rounded-xl px-4 py-2 text-xs font-semibold"
          style={{
            background: activePanel === "dashboard" ? "#557EFF" : "#F4F7FC",
            color: activePanel === "dashboard" ? "#FFFFFF" : "#162744",
          }}
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
          className="rounded-xl px-4 py-2 text-xs font-semibold"
          style={{
            background: activePanel === "quipux" ? "#557EFF" : "#F4F7FC",
            color: activePanel === "quipux" ? "#FFFFFF" : "#162744",
          }}
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
        className="rounded-2xl border bg-white p-4"
        style={{ borderColor: "#DFE5ED" }}
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
          />
        </UiStateBoundary>
      </div>

      <div
        role="tabpanel"
        id={`${tabsId}-panel-quipux`}
        aria-labelledby={`${tabsId}-tab-quipux`}
        hidden={activePanel !== "quipux"}
        className="rounded-2xl border bg-white p-4"
        style={{ borderColor: "#DFE5ED" }}
      >
        <UiStateBoundary
          status={listStatus}
          emptyMessage="La cola Quipux no tiene trámites pendientes en este momento."
          errorMessage="Error al consultar la cola QX."
          onRetry={() => void loadProcedures()}
          skeletonRows={3}
        >
          <TramitesProcedureList
            procedures={procedures}
            showApprovalActions={false}
          />
          <p className="mt-3 text-[11px] opacity-60">
            Vista de cola Quipux en modo {isReadOnly ? "solo lectura" : "operativo"}.
          </p>
        </UiStateBoundary>
      </div>
    </section>
  );
}

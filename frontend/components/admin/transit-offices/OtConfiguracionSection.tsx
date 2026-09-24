"use client";

import { useCallback, useEffect, useId, useState } from "react";
import { ToggleSwitch } from "@/components/admin/companies/ToggleSwitch";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { fetchOtProfile, updateOtFeatureFlag, updateOtProfile } from "@/lib/api/admin-ot";
import type { OtFeatureFlag, OtProfile } from "@/lib/api/types-ot";
import { OT_FILTER_LABEL_CLS, OT_INPUT_CLS } from "./ot-form-styles";

export interface OtConfiguracionSectionProps {
  transitOfficeId: string;
}

type RevocationWindowParseResult =
  | { ok: true; value: number | null }
  | { ok: false; message: string };

/**
 * HU #12569 — valida el campo "Ventana de revocatoria (días hábiles)" en cliente antes de llamar
 * al backend. Vacío es válido y significa "sin límite" (`null`), nunca 0 ni "no tocado". Cualquier
 * otro valor debe ser un entero positivo (negativos y no numéricos bloquean el guardado).
 *
 * HU #12857 (Feature #12847) — movida aquí desde `TramitesSuperSection` (retirada): esta era su
 * única consumidora tras la extracción de HU #12569, y la ruta legacy `[id]/tramites` ya no existe.
 */
export function parseRevocationWindowInput(raw: string): RevocationWindowParseResult {
  const trimmed = raw.trim();
  if (trimmed === "") {
    return { ok: true, value: null };
  }
  if (!/^-?\d+$/.test(trimmed)) {
    return { ok: false, message: "Ingresa un número entero de días hábiles, o déjalo vacío." };
  }
  const value = Number(trimmed);
  if (value <= 0) {
    return { ok: false, message: "La ventana debe ser mayor a 0 días hábiles, o déjala vacía." };
  }
  return { ok: true, value };
}

/**
 * Pedido del usuario (2026-09-16) — "Configuración" del organismo, en su propia entrada del dock
 * ("Administración" → "Configuración"), separada de "Reglas" (motor de reglas documentales,
 * HU #10221 — otro modelo de datos, `OtRule[]`, no encaja con un formulario de ajustes sueltos).
 *
 * <p>Antes de esto, el modo Dashboard/QX, la "Ventana de revocatoria" (HU #12569) y los feature
 * flags operativos SOLO vivían dentro de `TramitesSuperSection` (`/admin/transit-offices/{id}/tramites`),
 * una ruta que dejó de estar enlazada desde cualquier menú cuando el hub se reorganizó (no está en
 * `OT_HUB_TABS` ni en el dock del Admin OT) — quedó huérfana: solo se llegaba escribiendo la URL a
 * mano. Esta sección extrae SOLO el bloque de ajustes (no la lista de trámites legacy de esa
 * pantalla, que el "Trámites" moderno del dock —`client-procedures`— ya reemplazó) para darle un
 * punto de entrada real, sin arrastrar la lista redundante.</p>
 *
 * <p>`parseRevocationWindowInput` vive en este archivo (movida desde `TramitesSuperSection`,
 * retirada en HU #12857): es la misma regla de negocio (HU #12569 AC1-AC3) y este componente es su
 * única consumidora, así que no tiene sentido duplicarla en otro lado.</p>
 */
export function OtConfiguracionSection({ transitOfficeId }: OtConfiguracionSectionProps) {
  const { show } = useToast();
  const tabsId = useId();
  const [profile, setProfile] = useState<OtProfile | null>(null);
  const [profileStatus, setProfileStatus] = useState<UiStatus>("loading");
  const [switchingMode, setSwitchingMode] = useState(false);
  const [revocationWindowInput, setRevocationWindowInput] = useState("");
  const [revocationWindowError, setRevocationWindowError] = useState<string | null>(null);
  const [savingRevocationWindow, setSavingRevocationWindow] = useState(false);
  const [togglingFlagId, setTogglingFlagId] = useState<string | null>(null);

  const operationalFlags = (profile?.featureFlags ?? []).filter(
    (f) => !f.flagKey.startsWith("rule:"),
  );
  const isQuipuxMode = profile?.operationMode === "quipux";

  const loadProfile = useCallback(async (signal?: AbortSignal) => {
    setProfileStatus("loading");
    try {
      const data = await fetchOtProfile(signal, { transitOfficeId });
      if (signal?.aborted) return;
      setProfile(data);
      setRevocationWindowInput(
        data.revocationWindowBusinessDays != null ? String(data.revocationWindowBusinessDays) : "",
      );
      setRevocationWindowError(null);
      setProfileStatus("ready");
    } catch {
      if (!signal?.aborted) setProfileStatus("error");
    }
  }, [transitOfficeId]);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    void loadProfile(controller.signal);
    return () => controller.abort();
  }, [loadProfile]);

  const handleModeToggle = async (checked: boolean) => {
    if (!profile || switchingMode) return;
    const nextMode = checked ? "quipux" : "dashboard";
    setSwitchingMode(true);
    try {
      const updated = await updateOtProfile({ operationMode: nextMode }, { transitOfficeId });
      setProfile(updated);
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

  const handleSaveRevocationWindow = async () => {
    if (!profile || savingRevocationWindow) return;
    const parsed = parseRevocationWindowInput(revocationWindowInput);
    if (!parsed.ok) {
      setRevocationWindowError(parsed.message);
      return;
    }
    setRevocationWindowError(null);
    setSavingRevocationWindow(true);
    try {
      const updated = await updateOtProfile(
        { revocationWindowBusinessDays: parsed.value },
        { transitOfficeId },
      );
      setProfile(updated);
      setRevocationWindowInput(
        updated.revocationWindowBusinessDays != null
          ? String(updated.revocationWindowBusinessDays)
          : "",
      );
      show(
        updated.revocationWindowBusinessDays == null
          ? "Ventana de revocatoria guardada: sin límite de ventana."
          : `Ventana de revocatoria guardada: ${updated.revocationWindowBusinessDays} día(s) hábil(es).`,
        "success",
      );
    } catch {
      show("No se pudo guardar la ventana de revocatoria.", "error");
    } finally {
      setSavingRevocationWindow(false);
    }
  };

  const handleFlagToggle = async (flag: OtFeatureFlag, checked: boolean) => {
    if (!profile || togglingFlagId) return;
    setTogglingFlagId(flag.id);
    const previous = profile.featureFlags;
    setProfile({
      ...profile,
      featureFlags: profile.featureFlags.map((f) => (f.id === flag.id ? { ...f, isEnabled: checked } : f)),
    });
    try {
      const updated = await updateOtFeatureFlag(flag.id, { isEnabled: checked }, { transitOfficeId });
      setProfile((current) =>
        current
          ? { ...current, featureFlags: current.featureFlags.map((f) => (f.id === updated.id ? updated : f)) }
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

  if (profileStatus === "loading") {
    return <UiStateBoundary status="loading" skeletonRows={4} />;
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
      <h2 id={`${tabsId}-heading`} className="text-sm font-bold">
        Configuración del organismo
      </h2>

      <div className="rounded-2xl border bg-card p-4">
        <ToggleSwitch
          id={`${tabsId}-mode-qx`}
          label="Consola en solo lectura (opera en Quipux)"
          description="Este OT aprueba y rechaza dentro de Quipux, no en FLIT. No afecta a la radicación."
          checked={isQuipuxMode}
          onChange={(checked) => void handleModeToggle(checked)}
        />
      </div>

      <div className="rounded-2xl border bg-card p-4" aria-labelledby={`${tabsId}-revocation-heading`}>
        <h3 id={`${tabsId}-revocation-heading`} className="mb-1 text-xs font-bold">
          Ventana de revocatoria
        </h3>
        <p className="mb-3 text-[11px] opacity-60">
          Días hábiles en los que el OT puede revocar un trámite aprobado. Déjala vacía para no
          limitar.
        </p>
        <div className="flex flex-wrap items-end gap-3">
          <div className="min-w-[200px] flex-1">
            <label htmlFor={`${tabsId}-revocation-window`} className={OT_FILTER_LABEL_CLS}>
              Ventana de revocatoria (días hábiles)
              <input
                id={`${tabsId}-revocation-window`}
                type="text"
                inputMode="numeric"
                autoComplete="off"
                className={OT_INPUT_CLS}
                placeholder="Sin límite"
                value={revocationWindowInput}
                disabled={savingRevocationWindow}
                aria-invalid={revocationWindowError ? true : undefined}
                aria-describedby={
                  revocationWindowError ? `${tabsId}-revocation-window-error` : undefined
                }
                onChange={(e) => {
                  setRevocationWindowInput(e.target.value);
                  if (revocationWindowError) setRevocationWindowError(null);
                }}
              />
            </label>
          </div>
          <button
            type="button"
            className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
            style={{ background: "#557EFF" }}
            disabled={savingRevocationWindow}
            onClick={() => void handleSaveRevocationWindow()}
          >
            {savingRevocationWindow ? "Guardando…" : "Guardar"}
          </button>
        </div>
        {revocationWindowError && (
          <p
            id={`${tabsId}-revocation-window-error`}
            role="alert"
            className="mt-2 text-[11px]"
            style={{ color: "#FF4E00" }}
          >
            {revocationWindowError}
          </p>
        )}
      </div>

      {operationalFlags.length > 0 && (
        <div className="rounded-2xl border bg-card p-4" aria-labelledby={`${tabsId}-flags-heading`}>
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
    </section>
  );
}

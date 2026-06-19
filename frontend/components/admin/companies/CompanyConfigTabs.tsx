"use client";

import { useState, type ReactNode } from "react";
import { Building2, FileClock, FileSignature, Save, Shuffle, Stamp } from "lucide-react";
import type { TenantSettings, TenantSettingsUpdate } from "@/lib/api/types";
import { formFromSettings, formToUpdate, type SettingsForm } from "./settingsForm";
import { MatriculaInicialTab } from "./tabs/MatriculaInicialTab";
import { TraspasosTab } from "./tabs/TraspasosTab";
import { ConfiguracionEmpresaTab } from "./tabs/ConfiguracionEmpresaTab";
import { ContingenciaFlitTab } from "./tabs/ContingenciaFlitTab";

// Contenedor multi-pestaña de configuración (HU #10194, AC2). Mantiene un único
// estado de formulario para las 4 pestañas de config y persiste todo con un solo
// PUT atómico ("Guardar todo"). Whitelist (AC3), matriz OT (AC4) e historial (AC5)
// se inyectan como slots — tienen endpoints propios y no entran en el PUT.

type TabId = "matricula" | "traspasos" | "config" | "contingencia" | "historial";

const TABS: { id: TabId; label: string; icon: typeof Stamp; isConfig: boolean }[] = [
  { id: "matricula", label: "Matrícula Inicial", icon: Stamp, isConfig: true },
  { id: "traspasos", label: "Traspasos", icon: Shuffle, isConfig: true },
  { id: "config", label: "Configuración Empresa", icon: Building2, isConfig: true },
  { id: "contingencia", label: "Contingencia FLIT", icon: FileSignature, isConfig: true },
  { id: "historial", label: "Historial de Cambios", icon: FileClock, isConfig: false },
];

export interface CompanyConfigTabsProps {
  settings: TenantSettings;
  /** Persiste la configuración. Debe lanzar ApiValidationError (con `errors[]`) en 422. */
  onSaveSettings: (update: TenantSettingsUpdate) => Promise<void>;
  whitelistSlot?: ReactNode;
  otSlot?: ReactNode;
  auditSlot?: ReactNode;
  onNotify?: (message: string, type: "success" | "error") => void;
}

export function CompanyConfigTabs({
  settings,
  onSaveSettings,
  whitelistSlot,
  otSlot,
  auditSlot,
  onNotify,
}: CompanyConfigTabsProps) {
  const [tab, setTab] = useState<TabId>("matricula");
  const [form, setForm] = useState<SettingsForm>(() => formFromSettings(settings));
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  const [banner, setBanner] = useState<{ type: "success" | "error"; message: string } | null>(null);

  const patch = (p: Partial<SettingsForm>) => setForm((f) => ({ ...f, ...p }));

  const handleSaveAll = async () => {
    setBanner(null);
    setFieldErrors({});
    setSaving(true);
    try {
      await onSaveSettings(formToUpdate(form));
      setBanner({ type: "success", message: "Configuración guardada correctamente." });
      onNotify?.("Configuración guardada correctamente.", "success");
    } catch (error) {
      const errors = (error as { errors?: { field: string; message: string }[] })?.errors;
      if (Array.isArray(errors) && errors.length > 0) {
        const mapped: Record<string, string> = {};
        for (const e of errors) {
          mapped[e.field] = e.message;
        }
        setFieldErrors(mapped);
        setBanner({
          type: "error",
          message: "Revisa los campos marcados: hay valores inválidos.",
        });
        onNotify?.("La configuración tiene campos inválidos.", "error");
      } else {
        setBanner({ type: "error", message: "No se pudo guardar la configuración. Intenta de nuevo." });
        onNotify?.("No se pudo guardar la configuración.", "error");
      }
    } finally {
      setSaving(false);
    }
  };

  const currentTab = TABS.find((t) => t.id === tab);

  return (
    <div className="flex flex-1 flex-col gap-4">
      <div className="flex items-center gap-1 overflow-x-auto border-b" style={{ borderColor: "#DFE5ED" }} role="tablist">
        {TABS.map((t) => {
          const Icon = t.icon;
          const active = tab === t.id;
          return (
            <button
              key={t.id}
              role="tab"
              aria-selected={active}
              onClick={() => setTab(t.id)}
              className="relative flex shrink-0 items-center gap-2 px-4 py-2.5 text-xs font-semibold transition"
              style={{ color: active ? "#557EFF" : undefined, opacity: active ? 1 : 0.65 }}
            >
              <Icon className="h-3.5 w-3.5" />
              {t.label}
              {active && (
                <span className="absolute right-2 left-2 -bottom-px h-0.5 rounded-full" style={{ background: "#557EFF" }} />
              )}
            </button>
          );
        })}
      </div>

      <div role="tabpanel" className="flex-1">
        {tab === "matricula" && (
          <MatriculaInicialTab form={form} onChange={patch} fieldErrors={fieldErrors} />
        )}
        {tab === "traspasos" && (
          <TraspasosTab form={form} onChange={patch} whitelistSlot={whitelistSlot} />
        )}
        {tab === "config" && (
          <ConfiguracionEmpresaTab form={form} onChange={patch} otSlot={otSlot} fieldErrors={fieldErrors} />
        )}
        {tab === "contingencia" && <ContingenciaFlitTab form={form} />}
        {tab === "historial" && auditSlot}
      </div>

      {banner && (
        <div
          role={banner.type === "error" ? "alert" : "status"}
          aria-live="polite"
          className="rounded-xl border px-4 py-3 text-xs font-medium"
          style={{
            borderColor: banner.type === "success" ? "#00DBD5" : "#FF4E00",
            color: banner.type === "success" ? "#0a8f8b" : "#FF4E00",
          }}
        >
          {banner.message}
        </div>
      )}

      {currentTab?.isConfig && (
        <div
          className="sticky bottom-0 flex items-center justify-end gap-3 border-t bg-white/90 py-3 backdrop-blur dark:bg-[#0B0F14]/90"
          style={{ borderColor: "#DFE5ED" }}
        >
          <button
            type="button"
            onClick={handleSaveAll}
            disabled={saving}
            className="flex items-center gap-2 rounded-xl px-5 py-2.5 text-sm font-semibold text-white disabled:opacity-60"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            <Save className="h-4 w-4" /> {saving ? "Guardando…" : "Guardar todo"}
          </button>
        </div>
      )}
    </div>
  );
}

"use client";

import { useMemo, useState, type ReactNode } from "react";
import { Building2, FileClock, FileText, Hash, Save, Shuffle, Stamp } from "lucide-react";
import type { TenantSettings, TenantSettingsUpdate } from "@/lib/api/types";
import { diffSettings, formFromSettings, formToUpdate, type SettingsForm } from "./settingsForm";
import { SaveConfigDialog, type SaveConfigPhase } from "./SaveConfigDialog";
import { MatriculaInicialTab } from "./tabs/MatriculaInicialTab";
import { TraspasosTab } from "./tabs/TraspasosTab";
import { ConfiguracionEmpresaTab } from "./tabs/ConfiguracionEmpresaTab";

// Contenedor multi-pestaña de configuración (HU #10194, AC2). Mantiene un único
// estado de formulario para las pestañas de config y persiste todo con un solo PUT
// atómico. "Guardar todo" NO guarda directo: detecta los cambios realizados y abre una
// ventana de confirmación (SaveConfigDialog) que los lista y pide confirmar; el resultado
// se muestra en esa misma ventana (sin banner de éxito que quede fijo en la vista).
// Whitelist (AC3), matriz OT (AC4) e historial (AC5) se inyectan como slots.

type TabId = "matricula" | "traspasos" | "config" | "documentos" | "placas" | "historial";

const TABS: { id: TabId; label: string; icon: typeof Stamp; isConfig: boolean }[] = [
  { id: "matricula", label: "Matrícula Inicial", icon: Stamp, isConfig: true },
  { id: "traspasos", label: "Traspasos", icon: Shuffle, isConfig: true },
  { id: "config", label: "Configuración Empresa", icon: Building2, isConfig: true },
  // HU #10523 (RF31) — parámetros documentales por gestora (no forma parte del PUT de settings).
  { id: "documentos", label: "Documentos", icon: FileText, isConfig: false },
  // HU #10653 (Feature #10587) — visualización de placas preasignadas por OT (solo si está activa).
  { id: "placas", label: "Placas preasignadas", icon: Hash, isConfig: false },
  { id: "historial", label: "Historial de Cambios", icon: FileClock, isConfig: false },
];

export interface CompanyConfigTabsProps {
  settings: TenantSettings;
  /** Persiste la configuración. Debe lanzar ApiValidationError (con `errors[]`) en 422. */
  onSaveSettings: (update: TenantSettingsUpdate) => Promise<void>;
  whitelistSlot?: ReactNode;
  otSlot?: ReactNode;
  auditSlot?: ReactNode;
  documentosSlot?: ReactNode;
  platesSlot?: ReactNode;
}

export function CompanyConfigTabs({
  settings,
  onSaveSettings,
  whitelistSlot,
  otSlot,
  auditSlot,
  documentosSlot,
  platesSlot,
}: CompanyConfigTabsProps) {
  const [tab, setTab] = useState<TabId>("matricula");
  const [form, setForm] = useState<SettingsForm>(() => formFromSettings(settings));
  // Línea base (última configuración guardada) para detectar cambios; se actualiza al guardar.
  const [initialForm, setInitialForm] = useState<SettingsForm>(() => formFromSettings(settings));
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [errorBanner, setErrorBanner] = useState<string | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [confirmPhase, setConfirmPhase] = useState<SaveConfigPhase>("confirm");
  const [confirmError, setConfirmError] = useState<string | null>(null);

  const patch = (p: Partial<SettingsForm>) => setForm((f) => ({ ...f, ...p }));

  const changes = useMemo(() => diffSettings(initialForm, form), [initialForm, form]);

  // "Guardar todo" no persiste directo: abre la confirmación con el resumen de cambios.
  const openConfirm = () => {
    setConfirmError(null);
    setConfirmPhase("confirm");
    setConfirmOpen(true);
  };

  const closeConfirm = () => setConfirmOpen(false);

  // Persiste tras la confirmación. El guardado es atómico: un único PUT con todos los campos.
  const doSave = async () => {
    setConfirmError(null);
    setConfirmPhase("saving");
    setErrorBanner(null);
    setFieldErrors({});
    try {
      await onSaveSettings(formToUpdate(form));
      setInitialForm(form); // nueva línea base: ya no quedan cambios pendientes
      setConfirmPhase("success");
    } catch (error) {
      const errors = (error as { errors?: { field: string; message: string }[] })?.errors;
      if (Array.isArray(errors) && errors.length > 0) {
        const mapped: Record<string, string> = {};
        for (const e of errors) {
          mapped[e.field] = e.message;
        }
        setFieldErrors(mapped);
        setErrorBanner("Revisa los campos marcados: hay valores inválidos.");
        // Cierra la confirmación para revelar los campos marcados bajo las pestañas.
        setConfirmOpen(false);
        setConfirmPhase("confirm");
      } else {
        setConfirmPhase("confirm");
        setConfirmError("No se pudo guardar la configuración. Intenta de nuevo.");
      }
    }
  };

  // La pestaña de placas solo aparece si la preasignación está activa para la compañía.
  const visibleTabs = useMemo(
    () => TABS.filter((t) => t.id !== "placas" || settings.preasignacionPlacaActiva),
    [settings.preasignacionPlacaActiva],
  );
  const currentTab = visibleTabs.find((t) => t.id === tab);

  return (
    <div className="flex flex-1 flex-col gap-4">
      <div className="flex items-center gap-1 overflow-x-auto border-b" role="tablist">
        {visibleTabs.map((t) => {
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
        {tab === "documentos" && documentosSlot}
        {tab === "placas" && platesSlot}
        {tab === "historial" && auditSlot}
      </div>

      {errorBanner && (
        <div
          role="alert"
          aria-live="polite"
          className="rounded-xl border px-4 py-3 text-xs font-medium"
          style={{ borderColor: "#FF4E00", color: "#FF4E00" }}
        >
          {errorBanner}
        </div>
      )}

      {currentTab?.isConfig && (
        <div
          className="sticky bottom-0 flex items-center justify-end gap-3 border-t bg-white/90 py-3 backdrop-blur dark:bg-[#0B0F14]/90"
        >
          <button
            type="button"
            onClick={openConfirm}
            disabled={confirmOpen}
            className="flex items-center gap-2 rounded-xl px-5 py-2.5 text-sm font-semibold text-white disabled:opacity-60"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            <Save className="h-4 w-4" /> Guardar todo
          </button>
        </div>
      )}

      {confirmOpen && (
        <SaveConfigDialog
          changes={changes}
          phase={confirmPhase}
          error={confirmError}
          onConfirm={doSave}
          onCancel={closeConfirm}
          onClose={closeConfirm}
        />
      )}
    </div>
  );
}

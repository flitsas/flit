"use client";

import { createContext, useContext, useMemo, useState, type ReactNode } from "react";
import { Building2, FileClock, FileText, Hash, Save, Shuffle, Stamp, UserCheck, Users } from "lucide-react";
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

type TabId =
  | "matricula"
  | "traspasos"
  | "config"
  | "documentos"
  | "placas"
  | "representantes"
  | "mandatarios"
  | "historial";

/**
 * Navegación auxiliar hacia el Baúl de Firmas (legado HU #10929).
 * La pestaña de representantes ya no muestra un baúl suelto; la firma se asocia
 * desde la ficha del representante. Se conserva el contexto por compatibilidad.
 */
export interface CompanyTabsNav {
  goToBaul: () => void;
  baulVisible: boolean;
}

export const CompanyTabsNavContext = createContext<CompanyTabsNav | null>(null);

/** Hook para que los slots naveguen entre pestañas. Fuera del proveedor devuelve no-ops seguros. */
export function useCompanyTabsNav(): CompanyTabsNav {
  return useContext(CompanyTabsNavContext) ?? { goToBaul: () => {}, baulVisible: false };
}

interface TabDef {
  id: TabId;
  label: string;
  icon: typeof Stamp;
  isConfig: boolean;
}

const TABS: TabDef[] = [
  { id: "matricula", label: "Matrícula Inicial", icon: Stamp, isConfig: true },
  { id: "traspasos", label: "Traspasos", icon: Shuffle, isConfig: true },
  { id: "config", label: "Configuración Empresa", icon: Building2, isConfig: true },
  // HU #10523 (RF31) — parámetros documentales por gestora (no forma parte del PUT de settings).
  { id: "documentos", label: "Documentos", icon: FileText, isConfig: false },
  // HU #10653 (Feature #10587) — visualización de placas preasignadas por OT (solo si está activa).
  { id: "placas", label: "Placas preasignadas", icon: Hash, isConfig: false },
  // HU #10904 (Feature #10852) — directorio de representantes legales de las compañías representadas.
  // Escrituras y firma/identidad se gestionan desde la ficha de cada representante (no hay baúl
  // suelto ni sección hermana de escrituras en esta pestaña).
  { id: "representantes", label: "Representantes legales", icon: Users, isConfig: false },
  // HU #11202 (Feature #11190) — los mandatarios los registra la COMPAÑÍA y elige en cuáles de sus
  // organismos aplican. Antes vivían en el perfil de cada organismo de tránsito, que era quien elegía
  // compañías: el mandatario es de la empresa, no del organismo.
  { id: "mandatarios", label: "Mandatarios", icon: UserCheck, isConfig: false },
  { id: "historial", label: "Historial de Cambios", icon: FileClock, isConfig: false },
];

export interface CompanyConfigTabsProps {
  settings: TenantSettings;
  /** Persiste la configuración. Debe lanzar ApiValidationError (con `errors[]`) en 422. */
  onSaveSettings: (update: TenantSettingsUpdate) => Promise<void>;
  whitelistSlot?: ReactNode;
  /** Tabla consolidada de Organismos de Tránsito: grant + bloqueos + restricciones de
   *  consulta scoped por OT (HU #10194 — consolidación; endpoints propios, fuera del PUT
   *  atómico de settings). */
  otSlot?: ReactNode;
  auditSlot?: ReactNode;
  documentosSlot?: ReactNode;
  /** HU #10653 — visor de placas preasignadas. Solo si la preasignación está activa. */
  platesSlot?: ReactNode;
  /**
   * HU #10904 — pestaña de representantes legales (directorio). Firma e identidad
   * viven en la ficha de cada persona; las escrituras bajo cada NIT del acordeón.
   */
  legalRepresentativesSlot?: ReactNode;
  /** HU #11202 — mandatarios de la compañía y los organismos donde aplican. */
  mandatariosSlot?: ReactNode;
  /**
   * HU #11062 — compañía que se está configurando. Se rotula por ENCIMA de la barra de pestañas para
   * que sobreviva al cambio de pestaña, y se repite en la confirmación de guardado. `null` mientras
   * se resuelve (o si la identidad no se pudo cargar): la pantalla sigue funcionando sin el rótulo.
   */
  company?: { razonSocial: string; nit: string } | null;
}

export function CompanyConfigTabs({
  settings,
  onSaveSettings,
  whitelistSlot,
  otSlot,
  auditSlot,
  documentosSlot,
  platesSlot,
  legalRepresentativesSlot,
  mandatariosSlot,
  company,
}: CompanyConfigTabsProps) {
  const [tab, setTab] = useState<TabId>("matricula");
  // La pestaña de placas solo aparece si la preasignación está activa.
  const visibleTabs = useMemo(
    () => TABS.filter((t) => t.id !== "placas" || settings.preasignacionPlacaActiva),
    [settings.preasignacionPlacaActiva],
  );
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

  // Si la pestaña activa deja de existir (p. ej. se desactivaron las placas), recae en la primera.
  const currentTab = visibleTabs.find((t) => t.id === tab) ?? visibleTabs[0];
  const activeTabId = currentTab.id;

  return (
    <div className="flex flex-1 flex-col gap-4">
      {/* HU #11062 — identifica la compañía en TODA la pantalla: va sobre la barra de pestañas, así
          que no desaparece al cambiar de pestaña. Antes el tenant solo estaba en la URL y nada en
          pantalla confirmaba sobre qué compañía persistía el "Guardar todo" (un PUT atómico). */}
      {company && (
        <header
          className="flex flex-wrap items-baseline gap-x-3 gap-y-1 rounded-xl border border-[#DFE5ED] px-4 py-3 dark:border-white/10"
          style={{ background: "rgba(85,126,255,0.04)" }}
          aria-label="Compañía en configuración"
        >
          <span className="text-[10px] font-semibold uppercase tracking-wider opacity-60">
            Configurando
          </span>
          <span className="text-sm font-bold text-[#162744] dark:text-white">
            {company.razonSocial}
          </span>
          <span className="text-xs opacity-70">NIT {company.nit}</span>
        </header>
      )}

      <div className="flex items-center gap-1 overflow-x-auto border-b" role="tablist">
        {visibleTabs.map((t) => {
          const Icon = t.icon;
          const active = activeTabId === t.id;
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
        {activeTabId === "matricula" && (
          <MatriculaInicialTab form={form} onChange={patch} fieldErrors={fieldErrors} />
        )}
        {activeTabId === "traspasos" && (
          <TraspasosTab form={form} onChange={patch} whitelistSlot={whitelistSlot} />
        )}
        {activeTabId === "config" && (
          <ConfiguracionEmpresaTab
            form={form}
            onChange={patch}
            otSlot={otSlot}
            fieldErrors={fieldErrors}
          />
        )}
        {activeTabId === "documentos" && documentosSlot}
        {activeTabId === "placas" && platesSlot}
        {activeTabId === "representantes" && legalRepresentativesSlot}
        {activeTabId === "mandatarios" && mandatariosSlot}
        {activeTabId === "historial" && auditSlot}
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
          company={company}
          onConfirm={doSave}
          onCancel={closeConfirm}
          onClose={closeConfirm}
        />
      )}
    </div>
  );
}

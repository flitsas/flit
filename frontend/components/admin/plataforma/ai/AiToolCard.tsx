"use client";

import { useEffect, useState } from "react";
import { AlertTriangle, ArrowDown, ArrowUp, CheckCircle2 } from "lucide-react";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { Modal } from "@/components/atom/Modal";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { SwitchToggle } from "@/components/ui/SwitchToggle";
import {
  activateAiProvider,
  interpretarErrorAdminAi,
  putAiToolSettings,
  type AiProviderCode,
  type AiProviderKeyStatus,
  type AiToolSettingsView,
} from "@/lib/api/admin-plataforma-ai";
import { formatFechaHora } from "@/lib/format/date";
import { AiTransitionsTable } from "./AiTransitionsTable";
import { descripcionHerramienta, etiquetaHerramienta, etiquetaProveedor } from "./labels";

type DraftProvider = {
  providerCode: AiProviderCode;
  enabled: boolean;
  priorityRank: number;
};

function toDraft(tool: AiToolSettingsView): { providers: DraftProvider[]; autoReturnToPrimary: boolean } {
  return {
    autoReturnToPrimary: tool.autoReturnToPrimary,
    providers: [...tool.providers]
      .sort((a, b) => a.priorityRank - b.priorityRank)
      .map((p, i) => ({
        providerCode: p.providerCode,
        enabled: p.enabled,
        priorityRank: i + 1,
      })),
  };
}

function draftEquals(
  a: { providers: DraftProvider[]; autoReturnToPrimary: boolean },
  b: { providers: DraftProvider[]; autoReturnToPrimary: boolean },
): boolean {
  if (a.autoReturnToPrimary !== b.autoReturnToPrimary) return false;
  if (a.providers.length !== b.providers.length) return false;
  return a.providers.every(
    (p, i) =>
      p.providerCode === b.providers[i].providerCode &&
      p.enabled === b.providers[i].enabled &&
      p.priorityRank === b.providers[i].priorityRank,
  );
}

function statusByCode(tool: AiToolSettingsView): Map<string, AiProviderKeyStatus> {
  return new Map(tool.providers.map((p) => [p.providerCode, p]));
}

/**
 * Sección por herramienta (OCR / DR. FLIT): badges Configurada/No configurada + máscara,
 * reorden/enabled/autoReturn (PUT), activar con confirmación (POST), historial.
 */
export function AiToolCard({
  tool,
  onUpdated,
}: {
  tool: AiToolSettingsView;
  onUpdated: (next: AiToolSettingsView) => void;
}) {
  const label = etiquetaHerramienta(tool.toolCode);
  const [draft, setDraft] = useState(() => toDraft(tool));
  const [baseline, setBaseline] = useState(() => toDraft(tool));
  const [saving, setSaving] = useState(false);
  const [activating, setActivating] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);
  const [confirmProvider, setConfirmProvider] = useState<AiProviderCode | null>(null);
  const [historyKey, setHistoryKey] = useState(0);

  // Sincroniza borrador cuando el padre refresca la tool (p. ej. tras activate externo).
  const toolStamp = `${tool.toolCode}:${tool.activeProvider}:${tool.updatedAt}:${tool.rowVersion ?? 0}`;
  useEffect(() => {
    const next = toDraft(tool);
    /* Sync intencional por stamp; remount por key borraría successMsg post-activate. */
    // eslint-disable-next-line react-hooks/set-state-in-effect -- sync borrador con tool del padre
    setDraft(next);
    setBaseline(next);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- solo toolStamp
  }, [toolStamp]);

  const dirty = !draftEquals(draft, baseline);
  const byCode = statusByCode(tool);

  const move = (index: number, delta: number) => {
    setDraft((prev) => {
      const next = [...prev.providers];
      const target = index + delta;
      if (target < 0 || target >= next.length) return prev;
      const tmp = next[index]!;
      next[index] = next[target]!;
      next[target] = tmp;
      return {
        ...prev,
        providers: next.map((p, i) => ({ ...p, priorityRank: i + 1 })),
      };
    });
    setSuccessMsg(null);
  };

  const toggleEnabled = (providerCode: AiProviderCode, enabled: boolean) => {
    setDraft((prev) => ({
      ...prev,
      providers: prev.providers.map((p) => (p.providerCode === providerCode ? { ...p, enabled } : p)),
    }));
    setSuccessMsg(null);
  };

  const guardar = async () => {
    setSaving(true);
    setErrorMsg(null);
    setSuccessMsg(null);
    try {
      const next = await putAiToolSettings(tool.toolCode, {
        autoReturnToPrimary: draft.autoReturnToPrimary,
        providers: draft.providers,
      });
      const synced = toDraft(next);
      setDraft(synced);
      setBaseline(synced);
      onUpdated(next);
      setHistoryKey((k) => k + 1);
      setSuccessMsg("Configuración guardada. Aplica en la siguiente solicitud de IA.");
    } catch (err) {
      setErrorMsg(interpretarErrorAdminAi(err).mensaje);
    } finally {
      setSaving(false);
    }
  };

  const confirmarActivacion = async () => {
    if (!confirmProvider) return;
    setActivating(true);
    setErrorMsg(null);
    setSuccessMsg(null);
    try {
      const next = await activateAiProvider(tool.toolCode, { providerCode: confirmProvider });
      const synced = toDraft(next);
      setDraft(synced);
      setBaseline(synced);
      onUpdated(next);
      setHistoryKey((k) => k + 1);
      setSuccessMsg(`${etiquetaProveedor(confirmProvider)} quedó como proveedor activo.`);
      setConfirmProvider(null);
    } catch (err) {
      setErrorMsg(interpretarErrorAdminAi(err).mensaje);
    } finally {
      setActivating(false);
    }
  };

  return (
    <section
      aria-labelledby={`ai-tool-${tool.toolCode}`}
      className="space-y-4 rounded-2xl border border-[#DFE5ED] bg-white/70 p-4 dark:border-white/10 dark:bg-[#0B0F14]/50"
      data-testid={`ai-tool-${tool.toolCode}`}
    >
      <header className="space-y-1">
        <div className="flex flex-wrap items-center gap-2">
          <h2 id={`ai-tool-${tool.toolCode}`} className="text-sm font-bold text-[#162744] dark:text-white">
            {label}
          </h2>
          <StatusBadge tone="info" label={`Activo: ${etiquetaProveedor(tool.activeProvider)}`} />
        </div>
        <p className="text-xs text-[#5A6A85]">{descripcionHerramienta(tool.toolCode)}</p>
        {tool.updatedAt ? (
          <p className="text-[11px] text-[#5A6A85]">Última actualización: {formatFechaHora(tool.updatedAt)}</p>
        ) : null}
      </header>

      {errorMsg ? (
        <InlineAlert tone="error" title="No se pudo aplicar el cambio">
          {errorMsg}
        </InlineAlert>
      ) : null}
      {successMsg ? (
        <InlineAlert tone="success" compact>
          {successMsg}
        </InlineAlert>
      ) : null}

      <ul className="space-y-2" aria-label={`Proveedores de ${label}`}>
        {draft.providers.map((slot, index) => {
          const status = byCode.get(slot.providerCode);
          const configured = Boolean(status?.hasApiKey);
          const mask = status?.apiKeyMask ?? null;
          const capable = status?.capable !== false;
          const isActive = tool.activeProvider === slot.providerCode;

          return (
            <li
              key={slot.providerCode}
              className="flex flex-wrap items-center gap-3 rounded-xl border border-[#DFE5ED] px-3 py-2.5 dark:border-white/10"
            >
              <span
                className="flex h-6 w-6 shrink-0 items-center justify-center rounded-lg text-[11px] font-bold text-white"
                style={{ background: "#557EFF" }}
                aria-label={`Prioridad ${slot.priorityRank}`}
              >
                {slot.priorityRank}
              </span>

              <div className="min-w-0 flex-1 space-y-1">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="text-xs font-semibold text-[#162744] dark:text-white">
                    {etiquetaProveedor(slot.providerCode)}
                  </span>
                  <StatusBadge
                    tone={configured ? "success" : "neutral"}
                    label={configured ? "Configurada" : "No configurada"}
                  />
                  {!capable ? <StatusBadge tone="warning" label="No aplica" /> : null}
                  {isActive ? <StatusBadge tone="info" label="Activo" /> : null}
                </div>
                <p className="font-mono text-[11px] text-[#5A6A85]">
                  {configured && mask ? mask : "Sin API key en el entorno"}
                </p>
              </div>

              <SwitchToggle
                label={`${slot.enabled ? "Deshabilitar" : "Habilitar"} ${etiquetaProveedor(slot.providerCode)} en ${label}`}
                checked={slot.enabled}
                onChange={(next) => toggleEnabled(slot.providerCode, next)}
                disabled={saving || activating}
              />

              <div className="flex items-center gap-1">
                <button
                  type="button"
                  onClick={() => move(index, -1)}
                  disabled={saving || activating || index === 0}
                  aria-label={`Subir prioridad de ${etiquetaProveedor(slot.providerCode)}`}
                  className="rounded-lg border border-[#DFE5ED] p-1 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:opacity-30 dark:border-white/10"
                >
                  <ArrowUp className="h-3.5 w-3.5" aria-hidden="true" />
                </button>
                <button
                  type="button"
                  onClick={() => move(index, 1)}
                  disabled={saving || activating || index === draft.providers.length - 1}
                  aria-label={`Bajar prioridad de ${etiquetaProveedor(slot.providerCode)}`}
                  className="rounded-lg border border-[#DFE5ED] p-1 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:opacity-30 dark:border-white/10"
                >
                  <ArrowDown className="h-3.5 w-3.5" aria-hidden="true" />
                </button>
              </div>

              <button
                type="button"
                onClick={() => {
                  setErrorMsg(null);
                  setConfirmProvider(slot.providerCode);
                }}
                disabled={
                  saving ||
                  activating ||
                  isActive ||
                  !slot.enabled ||
                  !configured ||
                  !capable
                }
                aria-label={`Activar ${etiquetaProveedor(slot.providerCode)} en ${label}`}
                className="inline-flex items-center gap-1.5 rounded-xl bg-[#557EFF] px-3 py-1.5 text-xs font-semibold text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:opacity-50"
              >
                <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
                Activar
              </button>
            </li>
          );
        })}
      </ul>

      <div className="flex flex-wrap items-center justify-between gap-3 border-t border-[#DFE5ED] pt-3 dark:border-white/10">
        <SwitchToggle
          id={`ai-auto-return-${tool.toolCode}`}
          label={`Retorno automático al primario en ${label}`}
          checked={draft.autoReturnToPrimary}
          onChange={(next) => {
            setDraft((prev) => ({ ...prev, autoReturnToPrimary: next }));
            setSuccessMsg(null);
          }}
          disabled={saving || activating}
        />
        <span className="text-[11px] text-[#5A6A85]">
          Auto-retorno al proveedor primario cuando esté disponible
        </span>
        <button
          type="button"
          onClick={() => void guardar()}
          disabled={!dirty || saving || activating}
          className="ml-auto rounded-xl bg-[#162744] px-4 py-2 text-xs font-semibold text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:opacity-50"
        >
          {saving ? "Guardando…" : "Guardar prioridad y opciones"}
        </button>
      </div>

      <AiTransitionsTable
        key={`${tool.toolCode}-${historyKey}`}
        toolCode={tool.toolCode}
        toolLabel={label}
      />

      {confirmProvider ? (
        <Modal
          open
          onClose={() => {
            if (!activating) setConfirmProvider(null);
          }}
          busy={activating}
          icon={AlertTriangle}
          iconBg="#F9AC00"
          title="Cambiar proveedor activo"
          titleClassName="text-base font-bold text-[#162744]"
          size="md"
        >
          <div className="space-y-3 text-xs" data-testid="ai-activate-confirm">
            <p>
              Vas a activar <strong>{etiquetaProveedor(confirmProvider)}</strong> como proveedor de{" "}
              <strong>{label}</strong>. El cambio aplica en la siguiente solicitud; las que ya están en
              curso no se interrumpen.
            </p>
            <div className="flex justify-end gap-2 pt-1">
              <button
                type="button"
                onClick={() => setConfirmProvider(null)}
                disabled={activating}
                className="rounded-xl border border-[#DFE5ED] px-4 py-2 text-xs font-semibold disabled:opacity-50 dark:border-white/10"
              >
                Cancelar
              </button>
              <button
                type="button"
                onClick={() => void confirmarActivacion()}
                disabled={activating}
                className="rounded-xl bg-[#557EFF] px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
              >
                {activating ? "Activando…" : "Confirmar activación"}
              </button>
            </div>
          </div>
        </Modal>
      ) : null}
    </section>
  );
}

"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { RefreshCw } from "lucide-react";
import { CarLoader } from "@/components/atom/CarLoader";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { useToast } from "@/components/admin/Toast";
import { SwitchToggle } from "@/components/ui/SwitchToggle";
import {
  getLatestRuntConfirmationRun,
  getRuntConfirmationSettings,
  putRuntConfirmationSettings,
  RUNT_CONFIRMATION_PROVIDERS,
  RuntConfirmationSettingsInvalidError,
  type RuntConfirmationProviderKey,
  type RuntConfirmationRun,
  type RuntConfirmationSettings,
  type RuntConfirmationSettingsInput,
} from "@/lib/api/admin-runt-confirmation";
import { UltimaCorridaResumen } from "./UltimaCorridaResumen";

type Estado = "loading" | "error" | "ready";

/** Lo que el usuario edita: texto en los numéricos para no pelear con el input a medio escribir. */
interface Borrador {
  enabled: boolean;
  runAtLocal: string;
  providerKey: RuntConfirmationProviderKey;
  graceDays: string;
  discrepancyAfterRuns: string;
  maxAttempts: string;
}

type Campo = keyof Borrador;

const HINTS: Record<Exclude<Campo, "enabled">, string> = {
  runAtLocal: "Hora local de Colombia a la que arranca la corrida diaria. Si el servicio está caído a esa hora, corre al volver.",
  providerKey: "Con quién se consulta el RUNT en la corrida. Cambiarlo no toca los intentos anteriores.",
  graceDays: "Días de espera tras la aprobación antes de la primera consulta. 0 = se consulta en la siguiente corrida.",
  discrepancyAfterRuns: "Cuántas corridas seguidas en NO antes de marcar el trámite en discrepancia.",
  maxAttempts: "Cuántos intentos como máximo; al llegar, el trámite deja de consultarse (queda en NO).",
};

function aBorrador(s: RuntConfirmationSettings): Borrador {
  return {
    enabled: s.enabled,
    runAtLocal: s.runAtLocal,
    providerKey: s.providerKey,
    graceDays: String(s.graceDays),
    discrepancyAfterRuns: String(s.discrepancyAfterRuns),
    maxAttempts: String(s.maxAttempts),
  };
}

function aEntrada(b: Borrador): RuntConfirmationSettingsInput {
  return {
    enabled: b.enabled,
    runAtLocal: b.runAtLocal.trim(),
    providerKey: b.providerKey,
    graceDays: Number(b.graceDays),
    discrepancyAfterRuns: Number(b.discrepancyAfterRuns),
    maxAttempts: Number(b.maxAttempts),
  };
}

function iguales(a: Borrador, b: Borrador): boolean {
  return (Object.keys(a) as Campo[]).every((k) => a[k] === b[k]);
}

const inputCls =
  "w-full rounded-xl border bg-white px-3 py-2 text-sm text-[#162744] outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:opacity-50 dark:bg-[#0B0F14] dark:text-white";

/**
 * Plataforma → Confirmación RUNT → Configuración (HU #12279). Interruptor + cinco campos que se
 * guardan COMPLETOS con un PUT; el 400 del backend marca el campo señalado y solo ese. Debajo, el
 * resumen de la última corrida (programada o manual).
 */
export function ConfirmacionRuntConfigPanel() {
  const toast = useToast();
  const [estado, setEstado] = useState<Estado>("loading");
  const [persistido, setPersistido] = useState<RuntConfirmationSettings | null>(null);
  const [borrador, setBorrador] = useState<Borrador | null>(null);
  const [errores, setErrores] = useState<Partial<Record<Campo, string>>>({});
  const [guardando, setGuardando] = useState(false);
  const [ultima, setUltima] = useState<RuntConfirmationRun | null | undefined>(undefined);

  const cargar = useCallback(async (signal?: AbortSignal) => {
    setEstado("loading");
    try {
      const [settings, latest] = await Promise.all([
        getRuntConfirmationSettings(signal),
        getLatestRuntConfirmationRun(signal).catch(() => null),
      ]);
      if (signal?.aborted) return;
      setPersistido(settings);
      setBorrador(aBorrador(settings));
      setUltima(latest);
      setErrores({});
      setEstado("ready");
    } catch (err) {
      if (signal?.aborted || (err instanceof DOMException && err.name === "AbortError")) return;
      setEstado("error");
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API (mismo patrón que NotificacionesBankPanel)
    void cargar(controller.signal);
    return () => controller.abort();
  }, [cargar]);

  const sucio = useMemo(
    () => persistido !== null && borrador !== null && !iguales(aBorrador(persistido), borrador),
    [persistido, borrador],
  );

  const set = <K extends Campo>(campo: K, valor: Borrador[K]) => {
    setBorrador((b) => (b ? { ...b, [campo]: valor } : b));
    setErrores((e) => (e[campo] ? { ...e, [campo]: undefined } : e));
  };

  const descartar = () => {
    if (persistido) setBorrador(aBorrador(persistido));
    setErrores({});
  };

  const guardar = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!borrador) return;
    setGuardando(true);
    setErrores({});
    try {
      const saved = await putRuntConfirmationSettings(aEntrada(borrador));
      setPersistido(saved);
      setBorrador(aBorrador(saved));
      toast.show("Configuración guardada", "success");
    } catch (err) {
      if (err instanceof RuntConfirmationSettingsInvalidError) {
        const porCampo: Partial<Record<Campo, string>> = {};
        for (const fe of err.errors) porCampo[fe.field as Campo] = fe.message;
        setErrores(porCampo);
      } else {
        toast.show(err instanceof Error ? err.message : "No se pudo guardar la configuración.", "error");
      }
    } finally {
      setGuardando(false);
    }
  };

  if (estado === "loading") {
    return (
      <div className="py-16" data-testid="confirmacion-runt-config-loading">
        <CarLoader mode="runt" label="Cargando la configuración…" />
      </div>
    );
  }

  if (estado === "error" || !borrador) {
    return (
      <InlineAlert
        tone="error"
        title="No se pudo cargar la configuración"
        action={
          <button type="button" onClick={() => void cargar()} className="text-xs font-semibold underline">
            Reintentar
          </button>
        }
      >
        Vuelve a intentarlo. Si persiste, revisa que el servicio de trámites esté disponible.
      </InlineAlert>
    );
  }

  const campoNumero = (campo: "graceDays" | "discrepancyAfterRuns" | "maxAttempts", label: string, min: number, unidad: string) => {
    const id = `confirmacion-runt-${campo}`;
    const error = errores[campo];
    return (
      <div className="flex flex-col gap-1">
        <label htmlFor={id} className="text-xs font-semibold text-[#162744] dark:text-white">
          {label}
        </label>
        <div className={`flex items-center rounded-xl border bg-white dark:bg-[#0B0F14] ${error ? "border-[#FF4E00]" : "border-[#DFE5ED] dark:border-white/10"}`}>
          <input
            id={id}
            type="number"
            inputMode="numeric"
            min={min}
            step={1}
            value={borrador[campo]}
            onChange={(e) => set(campo, e.target.value)}
            disabled={guardando}
            aria-invalid={error ? true : undefined}
            aria-describedby={`${id}-hint${error ? ` ${id}-error` : ""}`}
            className="w-full min-w-0 rounded-xl bg-transparent px-3 py-2 font-mono text-sm text-[#162744] outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:opacity-50 dark:text-white"
          />
          <span className="shrink-0 pr-3 text-[11px] text-[#59677D] dark:text-white/55">{unidad}</span>
        </div>
        <p id={`${id}-hint`} className="text-[11px] leading-snug text-[#59677D] dark:text-white/60">
          {HINTS[campo]}
        </p>
        {error ? (
          <p id={`${id}-error`} role="alert" className="text-[11px] font-semibold text-[#B33600] dark:text-[#FF8A5B]">
            {error}
          </p>
        ) : null}
      </div>
    );
  };

  return (
    <div className="flex flex-col gap-5">
      <form onSubmit={(e) => void guardar(e)} className="flex flex-col gap-5" noValidate data-testid="confirmacion-runt-config-form">
        {/* Interruptor */}
        <div className="flex items-start justify-between gap-4 rounded-2xl border border-[#DFE5ED] bg-white p-4 dark:border-white/10 dark:bg-[#0B0F14]">
          <div className="flex flex-col gap-0.5">
            <label htmlFor="confirmacion-runt-enabled" className="text-sm font-semibold text-[#162744] dark:text-white">
              Consulta programada activa
            </label>
            <p id="confirmacion-runt-enabled-hint" className="text-[11px] leading-snug text-[#59677D] dark:text-white/60">
              Apagada, la corrida diaria no consulta al RUNT y lo deja anotado en el historial. «Consultar ahora» sigue disponible.
            </p>
          </div>
          <SwitchToggle
            id="confirmacion-runt-enabled"
            checked={borrador.enabled}
            onChange={(v) => set("enabled", v)}
            label="Consulta programada activa"
            disabled={guardando}
            describedById="confirmacion-runt-enabled-hint"
          />
        </div>

        <div className="grid gap-x-5 gap-y-4 [grid-template-columns:repeat(auto-fit,minmax(220px,1fr))]">
          <div className="flex flex-col gap-1">
            <label htmlFor="confirmacion-runt-runAtLocal" className="text-xs font-semibold text-[#162744] dark:text-white">
              Hora de ejecución diaria
            </label>
            <input
              id="confirmacion-runt-runAtLocal"
              type="time"
              value={borrador.runAtLocal}
              onChange={(e) => set("runAtLocal", e.target.value)}
              disabled={guardando}
              aria-invalid={errores.runAtLocal ? true : undefined}
              aria-describedby={`confirmacion-runt-runAtLocal-hint${errores.runAtLocal ? " confirmacion-runt-runAtLocal-error" : ""}`}
              className={`${inputCls} font-mono ${errores.runAtLocal ? "border-[#FF4E00]" : "border-[#DFE5ED] dark:border-white/10"}`}
            />
            <p id="confirmacion-runt-runAtLocal-hint" className="text-[11px] leading-snug text-[#59677D] dark:text-white/60">
              {HINTS.runAtLocal}
            </p>
            {errores.runAtLocal ? (
              <p id="confirmacion-runt-runAtLocal-error" role="alert" className="text-[11px] font-semibold text-[#B33600] dark:text-[#FF8A5B]">
                {errores.runAtLocal}
              </p>
            ) : null}
          </div>

          <div className="flex flex-col gap-1">
            <label htmlFor="confirmacion-runt-providerKey" className="text-xs font-semibold text-[#162744] dark:text-white">
              Proveedor activo
            </label>
            <select
              id="confirmacion-runt-providerKey"
              value={borrador.providerKey}
              onChange={(e) => set("providerKey", e.target.value as RuntConfirmationProviderKey)}
              disabled={guardando}
              aria-invalid={errores.providerKey ? true : undefined}
              aria-describedby={`confirmacion-runt-providerKey-hint${errores.providerKey ? " confirmacion-runt-providerKey-error" : ""}`}
              className={`${inputCls} ${errores.providerKey ? "border-[#FF4E00]" : "border-[#DFE5ED] dark:border-white/10"}`}
            >
              {RUNT_CONFIRMATION_PROVIDERS.map((p) => (
                <option key={p.key} value={p.key}>
                  {p.label}
                </option>
              ))}
            </select>
            <p id="confirmacion-runt-providerKey-hint" className="text-[11px] leading-snug text-[#59677D] dark:text-white/60">
              {HINTS.providerKey}
            </p>
            {errores.providerKey ? (
              <p id="confirmacion-runt-providerKey-error" role="alert" className="text-[11px] font-semibold text-[#B33600] dark:text-[#FF8A5B]">
                {errores.providerKey}
              </p>
            ) : null}
          </div>

          {campoNumero("graceDays", "Días de gracia tras la aprobación", 0, "días")}
          {campoNumero("discrepancyAfterRuns", "Corridas antes de discrepancia", 1, "corridas")}
          {campoNumero("maxAttempts", "Tope de reintentos", 1, "intentos")}
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <button
            type="submit"
            disabled={guardando || !sucio}
            className="rounded-full bg-gradient-to-r from-[#22D3C5] to-[#557EFF] px-4 py-2 text-xs font-semibold text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:opacity-50"
          >
            {guardando ? "Guardando…" : "Guardar configuración"}
          </button>
          <button
            type="button"
            onClick={descartar}
            disabled={guardando || !sucio}
            className="rounded-full border border-[#DFE5ED] px-4 py-2 text-xs font-semibold text-[#162744] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:opacity-50 dark:border-white/10 dark:text-white"
          >
            Descartar cambios
          </button>
          {persistido?.updatedAt ? (
            <span className="text-[11px] text-[#59677D] dark:text-white/60">
              Última modificación: {new Date(persistido.updatedAt).toLocaleString("es-CO")}
            </span>
          ) : null}
        </div>
      </form>

      <section className="flex flex-col gap-2" aria-labelledby="confirmacion-runt-ultima-corrida">
        <div className="flex items-center justify-between">
          <h2 id="confirmacion-runt-ultima-corrida" className="text-sm font-semibold text-[#162744] dark:text-white">
            Última corrida
          </h2>
          <button
            type="button"
            onClick={() => void getLatestRuntConfirmationRun().then(setUltima).catch(() => undefined)}
            className="flex items-center gap-1 text-xs font-semibold text-[#557EFF] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          >
            <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
            Actualizar
          </button>
        </div>
        <UltimaCorridaResumen run={ultima ?? null} />
      </section>
    </div>
  );
}

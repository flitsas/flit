"use client";

import { useCallback, useEffect, useState } from "react";
import { Icono } from "@flit/ui/Icono";
import { CarLoader } from "@/components/atom/CarLoader";
import { InlineAlert } from "@/components/atom/InlineAlert";
import {
  getDrFlitPlatformSettings,
  interpretarErrorDrFlitSettings,
  putDrFlitPlatformSettings,
  type DrFlitPlatformSettings,
} from "@/lib/api/admin-plataforma-dr-flit";

type Estado = "loading" | "error" | "ready" | "saving";

/**
 * HU #14772 — SuperAdmin: interruptor de visibilidad de FAB/panel DR. FLIT.
 * Cuatro estados: loading / error / ready / saving.
 */
export function DrFlitVisibilidadPanel() {
  const [estado, setEstado] = useState<Estado>("loading");
  const [settings, setSettings] = useState<DrFlitPlatformSettings | null>(null);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [recarga, setRecarga] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    void getDrFlitPlatformSettings(controller.signal)
      .then((data) => {
        if (controller.signal.aborted) return;
        setSettings(data);
        setEstado("ready");
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted || (err instanceof DOMException && err.name === "AbortError")) return;
        setErrorMsg(interpretarErrorDrFlitSettings(err));
        setEstado("error");
      });
    return () => controller.abort();
  }, [recarga]);

  const reintentar = useCallback(() => {
    setErrorMsg(null);
    setEstado("loading");
    setRecarga((n) => n + 1);
  }, []);

  const guardar = useCallback(
    async (uiEnabled: boolean) => {
      if (!settings) return;
      setEstado("saving");
      setErrorMsg(null);
      try {
        const next = await putDrFlitPlatformSettings({
          uiEnabled,
          rowVersion: settings.rowVersion,
        });
        setSettings(next);
        setEstado("ready");
      } catch (err) {
        setErrorMsg(interpretarErrorDrFlitSettings(err));
        setEstado("error");
      }
    },
    [settings],
  );

  if (estado === "loading") {
    return (
      <div className="flex min-h-[200px] items-center justify-center" data-testid="dr-flit-settings-loading">
        <CarLoader label="Cargando configuración de DR. FLIT…" />
      </div>
    );
  }

  if (estado === "error" && !settings) {
    return (
      <InlineAlert
        tone="error"
        title="No se pudo cargar la configuración"
        action={
          <button type="button" onClick={reintentar} className="text-xs font-semibold underline">
            Reintentar
          </button>
        }
      >
        {errorMsg ?? "Inténtalo de nuevo."}
      </InlineAlert>
    );
  }

  if (!settings) {
    return (
      <InlineAlert tone="info" title="Sin configuración">
        Aún no hay fila de settings de DR. FLIT en este ambiente. Tras migrar el backend aparecerá aquí.
      </InlineAlert>
    );
  }

  const checked = settings.uiEnabled;
  const busy = estado === "saving";

  return (
    <div className="space-y-4" data-testid="dr-flit-settings-ready">
      {errorMsg ? (
        <InlineAlert tone="error" title="No se pudo guardar">
          {errorMsg}
        </InlineAlert>
      ) : null}

      <div className="flex flex-wrap items-start justify-between gap-4 rounded-xl border border-border bg-background/60 p-4">
        <div className="min-w-0 space-y-1">
          <p className="text-sm font-semibold text-foreground">Mostrar asistente DR. FLIT</p>
          <p className="text-xs text-muted-foreground">
            Cuando está activo, los usuarios autenticados ven el botón flotante y el panel del asistente.
            Cuando está apagado, no se monta la UI (el chat LLM y el resto de APIs no cambian).
          </p>
        </div>
        <button
          type="button"
          role="switch"
          aria-checked={checked}
          aria-label="Mostrar asistente DR. FLIT"
          disabled={busy}
          onClick={() => void guardar(!checked)}
          className={[
            "relative h-8 w-14 shrink-0 rounded-full transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand",
            checked ? "bg-flit-brand" : "bg-muted",
            busy ? "opacity-60" : "",
          ].join(" ")}
        >
          <span
            className={[
              "absolute top-1 h-6 w-6 rounded-full bg-white shadow transition",
              checked ? "left-7" : "left-1",
            ].join(" ")}
          />
        </button>
      </div>

      <p className="text-[11px] text-muted-foreground">
        Versión de fila: {settings.rowVersion}
        {settings.updatedAt ? ` · Último cambio: ${new Date(settings.updatedAt).toLocaleString("es-CO")}` : ""}
      </p>

      <button
        type="button"
        onClick={reintentar}
        disabled={busy}
        className="inline-flex items-center gap-1.5 rounded-md text-xs font-semibold text-flit-brand focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand"
      >
        <Icono nombre="reintento" className="h-3.5 w-3.5" />
        Actualizar
      </button>
    </div>
  );
}

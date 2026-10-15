"use client";

import { useCallback, useEffect, useState } from "react";
import { RefreshCw } from "lucide-react";
import { CarLoader } from "@/components/atom/CarLoader";
import { InlineAlert } from "@/components/atom/InlineAlert";
import {
  interpretarErrorAdminAi,
  listAiTools,
  type AiToolSettingsView,
} from "@/lib/api/admin-plataforma-ai";
import { AiAlertsSection } from "./AiAlertsSection";
import { AiToolCard } from "./AiToolCard";

type Estado = "loading" | "error" | "empty" | "ready";

const ORDEN_HERRAMIENTAS = ["ocr", "dr_flit"] as const;

function ordenarTools(tools: AiToolSettingsView[]): AiToolSettingsView[] {
  const rank = (code: string) => {
    const i = ORDEN_HERRAMIENTAS.indexOf(code as (typeof ORDEN_HERRAMIENTAS)[number]);
    return i === -1 ? 99 : i;
  };
  return [...tools].sort((a, b) => rank(a.toolCode) - rank(b.toolCode));
}

/**
 * HU #13511 / #13512 — Super Admin → Plataforma → Configuración de IA.
 * Cuatro estados UI: loading / error / empty / filled. Secciones OCR y DR. FLIT,
 * historial, alertas y activación con confirmación.
 */
export function ConfiguracionIaPanel() {
  const [estado, setEstado] = useState<Estado>("loading");
  const [tools, setTools] = useState<AiToolSettingsView[]>([]);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [recarga, setRecarga] = useState(0);

  const cargar = useCallback(async (signal?: AbortSignal) => {
    setEstado("loading");
    setErrorMsg(null);
    try {
      const data = await listAiTools(signal);
      if (signal?.aborted) return;
      const lista = ordenarTools(data.tools ?? []);
      setTools(lista);
      setEstado(lista.length === 0 ? "empty" : "ready");
    } catch (err) {
      if (signal?.aborted || (err instanceof DOMException && err.name === "AbortError")) return;
      setErrorMsg(interpretarErrorAdminAi(err).mensaje);
      setEstado("error");
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void cargar(controller.signal);
    return () => controller.abort();
  }, [cargar, recarga]);

  const onToolUpdated = useCallback((next: AiToolSettingsView) => {
    setTools((prev) => ordenarTools(prev.map((t) => (t.toolCode === next.toolCode ? next : t))));
  }, []);

  if (estado === "loading") {
    return (
      <div className="flex min-h-[240px] items-center justify-center" data-testid="ai-config-loading">
        <CarLoader label="Cargando configuración de IA…" />
      </div>
    );
  }

  if (estado === "error") {
    return (
      <InlineAlert
        tone="error"
        title="No se pudo cargar la configuración de IA"
        action={
          <button
            type="button"
            onClick={() => setRecarga((n) => n + 1)}
            className="text-xs font-semibold underline"
          >
            Reintentar
          </button>
        }
      >
        {errorMsg ?? "Inténtalo de nuevo."}
      </InlineAlert>
    );
  }

  if (estado === "empty") {
    return (
      <div className="space-y-4" data-testid="ai-config-empty">
        <InlineAlert tone="info" title="Sin herramientas configuradas">
          Aún no hay filas de configuración de IA en este ambiente. Cuando el backend publique el seed
          (OCR y DR. FLIT), aparecerán aquí.
        </InlineAlert>
        <button
          type="button"
          onClick={() => setRecarga((n) => n + 1)}
          className="inline-flex items-center gap-1.5 rounded-md text-xs font-semibold text-[#557EFF] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
        >
          <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
          Actualizar
        </button>
      </div>
    );
  }

  return (
    <div className="space-y-6" data-testid="ai-config-ready">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-xs text-[#5A6A85]">
          Prioridad y proveedor activo por herramienta. Las API keys viven solo en variables de entorno;
          aquí ves presencia y máscara.
        </p>
        <button
          type="button"
          onClick={() => setRecarga((n) => n + 1)}
          className="inline-flex items-center gap-1.5 rounded-md text-xs font-semibold text-[#557EFF] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
        >
          <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
          Actualizar
        </button>
      </div>

      <AiAlertsSection />

      {tools.map((tool) => (
        <AiToolCard key={tool.toolCode} tool={tool} onUpdated={onToolUpdated} />
      ))}
    </div>
  );
}

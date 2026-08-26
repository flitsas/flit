"use client";

import { useCallback, useEffect, useState } from "react";
import { MandatoOtConfigForm } from "@/components/admin/plataforma/MandatoOtConfigForm";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import {
  fetchMandateOtConfig,
  type MandateOtConfigView,
} from "@/lib/api/admin-plataforma-mandatos";
import { ApiError } from "@/lib/api/types";
import { tipoNegocioLabel, resolveTipoNegocio } from "@/lib/plataforma/mandato-templates";

export function OtMandatosSection({ transitOfficeId }: { transitOfficeId: string }) {
  const [status, setStatus] = useState<"loading" | "ready" | "error">("loading");
  const [error, setError] = useState<string | null>(null);
  const [office, setOffice] = useState<MandateOtConfigView | null>(null);
  const [panel, setPanel] = useState<"mandato" | "mandatario" | null>(null);

  const load = useCallback(async () => {
    setStatus("loading");
    setError(null);
    try {
      const view = await fetchMandateOtConfig(transitOfficeId);
      setOffice(view);
      setStatus("ready");
    } catch (err) {
      setOffice(null);
      setStatus("error");
      setError(err instanceof ApiError ? err.message : "No se pudo cargar la configuración de mandatos.");
    }
  }, [transitOfficeId]);

  useEffect(() => {
    // Carga inicial del OT: el status loading/ready vive en este módulo.
    // eslint-disable-next-line react-hooks/set-state-in-effect -- fetch al montar / cambiar id
    void load();
  }, [load]);

  if (status === "loading") {
    return <UiStateBoundary status="loading" />;
  }

  if (status === "error" || !office) {
    return (
      <UiStateBoundary
        status="error"
        errorMessage={error ?? "No se pudo cargar la configuración de mandatos."}
        onRetry={() => void load()}
      />
    );
  }

  const tipo = tipoNegocioLabel(resolveTipoNegocio(office.assignmentMode));

  return (
    <div className="flex flex-col gap-4" data-testid="ot-mandatos-section">
      <p className="text-xs text-[#59677D] dark:text-white/65">
        Esta pantalla comparte los mismos datos que Plataforma → Mandatos. Un solo modelo por
        empresa que radica, para todas las familias de trámite.
      </p>
      <div className="rounded-2xl border border-[#DFE5ED] bg-white p-4 dark:border-white/10 dark:bg-[#0B0F14]">
        <h2 className="text-sm font-semibold text-[#162244] dark:text-white">{office.name}</h2>
        <dl className="mt-3 grid gap-2 text-xs text-[#59677D] dark:text-white/65 sm:grid-cols-2">
          <div>
            <dt className="font-semibold text-[#162244] dark:text-white">Plantilla</dt>
            <dd>{office.templateCode}</dd>
          </div>
          <div>
            <dt className="font-semibold text-[#162244] dark:text-white">Modelo del OT</dt>
            <dd>{tipo}</dd>
          </div>
        </dl>
        <div className="mt-4 flex flex-wrap gap-2">
          <button
            type="button"
            onClick={() => setPanel("mandato")}
            className="rounded-full px-4 py-2 text-xs font-semibold text-white"
            style={{ background: "linear-gradient(90deg,#557EFF 0%,#00DBD5 100%)" }}
          >
            Configurar mandato
          </button>
          <button
            type="button"
            onClick={() => setPanel("mandatario")}
            className="rounded-full border border-[#DFE5ED] bg-white px-4 py-2 text-xs font-semibold text-[#162244] dark:border-white/15 dark:bg-transparent dark:text-white"
          >
            Tipo por empresa que radica
          </button>
        </div>
      </div>

      {panel ? (
        <MandatoOtConfigForm
          office={office}
          mode={panel}
          onClose={() => setPanel(null)}
          onSaved={(view) => {
            setOffice(view);
            setPanel(null);
          }}
        />
      ) : null}
    </div>
  );
}

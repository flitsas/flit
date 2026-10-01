"use client";

import { useCallback, useEffect, useState } from "react";
import { listMandatoFormats, type MandatoFormatView } from "@/lib/api/admin-plataforma-mandatos";

export type MandatoFormatosStatus = "loading" | "ready" | "error";

/** Estado del catálogo de formatos que reciben los componentes que lo muestran (HU #13174). */
export interface MandatoFormatosState {
  formatos: readonly MandatoFormatView[];
  status: MandatoFormatosStatus;
  reload: () => void;
}

/** Catálogo de formatos de contrato de mandato desde el backend (GET /mandatos/formatos). */
export function useMandatoFormatos(): MandatoFormatosState {
  const [formatos, setFormatos] = useState<readonly MandatoFormatView[]>([]);
  const [status, setStatus] = useState<MandatoFormatosStatus>("loading");
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    const ctrl = new AbortController();
    // Una recarga con datos a la vista (tras editar un formato) es silenciosa: no vuelve a "loading".
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API
    setStatus((s) => (s === "ready" ? s : "loading"));
    listMandatoFormats(ctrl.signal)
      .then((items) => {
        if (ctrl.signal.aborted) return;
        setFormatos(items);
        setStatus("ready");
      })
      .catch(() => {
        if (ctrl.signal.aborted) return;
        setStatus("error");
      });
    return () => ctrl.abort();
  }, [revision]);

  const reload = useCallback(() => setRevision((n) => n + 1), []);

  return { formatos, status, reload };
}

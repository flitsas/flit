"use client";

import { useState } from "react";
import { InlineAlert } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { useToast } from "@/components/Toast";
import { BOTON_PRIMARIO_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { P } from "@/lib/nav";
import { tienePermiso } from "@/lib/sesion";

interface ResultadoPlacas {
  agregadas: { placa: string }[];
  yaExistian: string[];
  invalidas: string[];
}

/**
 * Tras registrar desde OCR: «¿Registrar la placa ABC123 como placa monitoreada?» (RN-062). Las que ya estaban
 * monitoreadas no se ofrecen; si el usuario no puede gestionar el monitoreo, solo se le informa.
 */
export function OfertaPlacas({ placas }: { placas: string[] }) {
  const toast = useToast();
  const { sesion } = useSesion();
  const puede = tienePermiso(sesion, P.monitoreoGestionar) && !sesion?.usuario.esSuperAdmin;
  const [hecho, setHecho] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  if (placas.length === 0) return null;
  if (hecho) return <InlineAlert tone="success">{hecho}</InlineAlert>;

  const registrar = async () => {
    setEnviando(true);
    try {
      const r = await apiFetch<ResultadoPlacas>("/monitoreo/placas", { method: "POST", body: { placas, origen: "OCR" } });
      const partes = [`${r.agregadas.length} placa(s) quedaron monitoreadas`];
      if (r.yaExistian.length) partes.push(`${r.yaExistian.join(", ")} ya estaba(n) monitoreada(s)`);
      if (r.invalidas.length) partes.push(`${r.invalidas.join(", ")} no es una placa válida`);
      setHecho(`${partes.join("; ")}.`);
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudieron registrar las placas.", "error");
    } finally {
      setEnviando(false);
    }
  };

  const lista = placas.join(", ");
  return (
    <InlineAlert tone="info" title={placas.length === 1 ? `¿Registrar la placa ${lista} como placa monitoreada?` : `¿Registrar ${placas.length} placas como monitoreadas?`}>
      <div className="flex flex-col gap-2">
        {placas.length > 1 && <span>{lista}</span>}
        <span>Así sus comparendos nuevos llegarán solos por SIMIT y la fuente municipal.</span>
        {puede ? (
          <div>
            <button type="button" className={BOTON_PRIMARIO_CLS} disabled={enviando} onClick={registrar}>
              {enviando ? "Registrando…" : placas.length === 1 ? "Monitorear la placa" : "Monitorear las placas"}
            </button>
          </div>
        ) : (
          <span className="opacity-70">Pídeselo a quien gestiona NITs y placas en tu empresa.</span>
        )}
      </div>
    </InlineAlert>
  );
}

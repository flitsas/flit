"use client";

import { useEffect, useMemo } from "react";
import { InlineAlert } from "@flit/ui";
import { apiFetch, apiPostBlob } from "@/lib/api";
import { esPlantillaDeCorreo } from "@/lib/etiquetas";
import { useCarga } from "@/lib/useCarga";

interface Contenido {
  tipo: string;
  asunto: string | null;
  cuerpoHtml: string;
  comparendoId?: string | null;
  /** Renting arma los correos sin marca de FLIT (D9). */
  conjunto?: string | null;
}

type Resultado = { correo: { asunto: string | null; html: string; desconocidas: string[] } } | { pdf: string };

/**
 * Vista previa de una plantilla con datos de ejemplo (o de un comparendo real): los correos como HTML en un iframe sin
 * scripts; los documentos como el PDF que genera el backend, con marca de agua «VISTA PREVIA».
 */
export function VistaPrevia({ contenido, alto = 560 }: { contenido: Contenido | null; alto?: number }) {
  const clave = contenido ? JSON.stringify(contenido) : null;
  const correo = contenido ? esPlantillaDeCorreo(contenido.tipo) : false;
  const vista = useCarga<Resultado>(clave, async (signal) => {
    const body = { tipo: contenido!.tipo, asunto: contenido!.asunto, cuerpoHtml: contenido!.cuerpoHtml, comparendoId: contenido!.comparendoId ?? null, conjunto: contenido!.conjunto ?? null, formato: correo ? "html" : "pdf" };
    if (correo) return { correo: await apiFetch<{ asunto: string | null; html: string; desconocidas: string[] }>("/plantillas/vista-previa", { method: "POST", body, signal }) };
    return { pdf: URL.createObjectURL(await apiPostBlob("/plantillas/vista-previa", body, signal)) };
  });
  const pdf = vista.datos && "pdf" in vista.datos ? vista.datos.pdf : null;
  useEffect(() => () => { if (pdf) URL.revokeObjectURL(pdf); }, [pdf]);
  const resultadoCorreo = useMemo(() => (vista.datos && "correo" in vista.datos ? vista.datos.correo : null), [vista.datos]);

  if (!contenido) return <p className="text-sm opacity-60">Elige una plantilla para ver cómo queda.</p>;
  if (vista.estado === "loading") return <p className="text-sm opacity-60">Armando la vista previa…</p>;
  if (vista.estado === "error") return <InlineAlert tone="error">{vista.error}</InlineAlert>;

  return (
    <div className="flex flex-col gap-2">
      {resultadoCorreo && (
        <>
          {resultadoCorreo.desconocidas.length > 0 && (
            <InlineAlert tone="warning" compact>
              Variables que no existen: {resultadoCorreo.desconocidas.map((v) => `{{${v}}}`).join(", ")}
            </InlineAlert>
          )}
          {resultadoCorreo.asunto && (
            <p className="text-xs">
              <span className="font-semibold">Asunto:</span> {resultadoCorreo.asunto}
            </p>
          )}
          <iframe title="Vista previa del correo" sandbox="" srcDoc={resultadoCorreo.html} className="w-full rounded-xl border border-[#DFE5ED] bg-white" style={{ height: alto }} />
        </>
      )}
      {pdf && <iframe title="Vista previa del documento" src={pdf} className="w-full rounded-xl border border-[#DFE5ED]" style={{ height: alto }} />}
      <p className="text-[11px] opacity-60">Con datos de ejemplo. El documento real usa los datos del comparendo, la empresa y su representante.</p>
    </div>
  );
}

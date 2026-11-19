"use client";

import { useEffect } from "react";
import { InlineAlert, Modal } from "@flit/ui";
import { Datos } from "@/components/ui/Acordeon";
import { BOTON_PRIMARIO_CLS } from "@/components/ui/estilos";
import { apiGetBlob } from "@/lib/api";
import type { VistaPreviaDp } from "@/lib/dp";
import { useCarga } from "@/lib/useCarga";

/**
 * «Ver cómo llegaría» (HU #14619): el derecho de petición y el correo que lo acompaña, tal como saldrían con este
 * comparendo, con los bloqueos que hoy impiden el envío. Es solo una vista previa: no envía nada ni reserva radicado.
 * El correo va en un iframe con `sandbox` sin permisos: nunca como HTML dentro de la página.
 */
export function VistaPreviaDpModal({ comparendoId, tipologiaId, vista, sinPdf, onCerrar, zClassName }: {
  comparendoId: string;
  tipologiaId: string;
  vista: VistaPreviaDp;
  /** Hay un bloqueo que impide armar el documento (p. ej. no hay plantilla): se explica en vez de pedirlo. */
  sinPdf: boolean;
  onCerrar: () => void;
  zClassName?: string;
}) {
  const pdf = useCarga(sinPdf ? null : `dp-pdf:${comparendoId}:${tipologiaId}`, async (signal) =>
    URL.createObjectURL(await apiGetBlob(`/comparendos/${comparendoId}/dp/vista-previa.pdf`, { tipologiaId }, signal)),
  );
  const url = pdf.datos;
  useEffect(() => () => { if (url) URL.revokeObjectURL(url); }, [url]);

  return (
    <Modal
      icono="documento"
      open
      onClose={onCerrar}
      size="2xl"
      zClassName={zClassName}
      title="Así llegaría el derecho de petición"
      description="VISTA PREVIA · no se envía nada ni se reserva radicado"
      footer={
        <div className="flex justify-end">
          <button type="button" className={BOTON_PRIMARIO_CLS} onClick={onCerrar}>
            Cerrar
          </button>
        </div>
      }
    >
      <div className="flex min-w-0 flex-col gap-4">
        {vista.bloqueos.length > 0 && (
          <InlineAlert tone="error" title="Hoy no se podría enviar" compact>
            <ul className="list-disc pl-4">
              {vista.bloqueos.map((b) => (
                <li key={b.codigo}>{b.mensaje}</li>
              ))}
            </ul>
          </InlineAlert>
        )}
        {vista.advertencias.length > 0 && (
          <InlineAlert tone="warning" compact>
            {vista.advertencias.join(" ")}
          </InlineAlert>
        )}
        <Datos
          filas={[
            ["Organismo de tránsito", vista.organismo],
            ["Se enviaría a", vista.destinatarios.join(", ") || null],
            ["Con copia (CC)", vista.cc.length > 0 ? vista.cc.join(", ") : null],
            ["Copia oculta (CCO)", vista.bcc.length > 0 ? vista.bcc.join(", ") : null],
            ["Firma", vista.representante ? `${vista.representante.nombres}${vista.representante.cargo ? ` (${vista.representante.cargo})` : ""}` : null],
            ["Anexos", vista.anexos.join(", ")],
          ]}
        />
        <div className="grid min-w-0 grid-cols-1 gap-4 xl:grid-cols-2">
          <div className="flex min-w-0 flex-col gap-2">
            <h4 className="text-xs font-semibold">Documento (PDF)</h4>
            {sinPdf ? (
              <InlineAlert tone="warning" compact>No se puede armar el documento mientras exista el bloqueo de arriba.</InlineAlert>
            ) : pdf.estado === "loading" ? (
              <p className="text-xs opacity-60">Armando el documento…</p>
            ) : pdf.estado === "error" ? (
              <InlineAlert tone="error" compact>{pdf.error}</InlineAlert>
            ) : (
              url && <iframe title="Vista previa del documento" src={url} className="h-[440px] w-full rounded-xl border border-border" />
            )}
          </div>
          <div className="flex min-w-0 flex-col gap-2">
            <h4 className="text-xs font-semibold">Correo que lo acompaña</h4>
            {vista.correo ? (
              <>
                {vista.correo.asunto && (
                  <p className="break-words text-xs">
                    <span className="font-semibold">Asunto:</span> {vista.correo.asunto}
                  </p>
                )}
                <iframe title="Vista previa del correo" sandbox="" srcDoc={vista.correo.html} className="h-[440px] w-full rounded-xl border border-border bg-card" />
              </>
            ) : (
              <InlineAlert tone="warning" compact>Todavía no hay un correo publicado para los derechos de petición. Pídelo al administrador de FLIT.</InlineAlert>
            )}
          </div>
        </div>
      </div>
    </Modal>
  );
}

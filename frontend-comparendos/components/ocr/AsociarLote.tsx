"use client";

import { useState } from "react";
import { Modal } from "@flit/ui";
import { BOTON_PRIMARIO_CLS, controlCls } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { type LoteEncolado, textosVistaPreviaLote, type VistaPreviaLote } from "@/lib/ocr";

const PIE_CLS = "flex flex-col-reverse gap-2 sm:flex-row sm:flex-wrap sm:justify-end [&>button]:w-full sm:[&>button]:w-auto";
const NOTA_CLS = "min-w-0 break-words rounded-xl border border-border bg-secondary px-3 py-2 text-xs";
const ALERTA_CLS = "min-w-0 break-words rounded-xl border border-[var(--badge-warning-fg)] px-3 py-2 text-xs text-[var(--badge-warning-fg)]";

/**
 * «Asociar los N» (GDC-EVID §9 y §10): confirma con la vista previa del servidor (cuántos documentos y cuántos correos) y
 * encola el lote; un trabajador del servidor lo asocia en segundo plano y el avance se ve en la franja del paquete. Los
 * reemplazos nunca entran: se revisan uno por uno.
 */
export function AsociarLote({
  paqueteId,
  vista,
  onCerrar,
  onEncolado,
  onError,
}: {
  paqueteId: string;
  vista: VistaPreviaLote;
  onCerrar: () => void;
  onEncolado: (r: LoteEncolado) => void;
  onError: (mensaje: string) => void;
}) {
  const [enviando, setEnviando] = useState(false);
  const textos = textosVistaPreviaLote(vista);

  const confirmar = async () => {
    setEnviando(true);
    try {
      onEncolado(await apiFetch<LoteEncolado>(`/ocr/paquetes/${paqueteId}/asociar-lote`, { method: "POST", body: {} }));
    } catch (e) {
      setEnviando(false);
      onError(e instanceof Error && e.message ? e.message : "No se pudo iniciar la asociación.");
    }
  };

  return (
    <Modal
      icono="documento"
      open
      onClose={onCerrar}
      busy={enviando}
      title="Asociar documentos a sus comparendos"
      description={textos.documentos}
      footer={
        <div className={PIE_CLS}>
          <button type="button" className={controlCls(false)} disabled={enviando} onClick={onCerrar}>
            Cancelar
          </button>
          <button type="button" data-autofocus className={BOTON_PRIMARIO_CLS} disabled={enviando || vista.asociables === 0} onClick={confirmar}>
            {enviando ? "Enviando…" : "Asociar y enviar"}
          </button>
        </div>
      }
    >
      <div className="flex flex-col gap-3 text-sm">
        <p role="note" className={NOTA_CLS}>
          {textos.correos}
        </p>
        {vista.correos.omitidos.length > 0 && (
          <div className="flex min-w-0 flex-col gap-1">
            <p className="text-xs font-semibold">No saldrá correo</p>
            <ul className="max-h-40 list-disc overflow-y-auto pl-5 text-xs">
              {vista.correos.omitidos.map((o) => (
                <li key={o.motivo} className="break-words">
                  {o.motivo} ({o.cantidad})
                </li>
              ))}
            </ul>
          </div>
        )}
        {textos.porRevisar.map((t) => (
          <p key={t} className={ALERTA_CLS}>
            {t}
          </p>
        ))}
        <p className="text-xs text-muted-foreground">Se asocian en segundo plano: puedes seguir trabajando y ver el avance en esta pantalla.</p>
      </div>
    </Modal>
  );
}

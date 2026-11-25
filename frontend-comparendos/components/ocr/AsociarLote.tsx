"use client";

import { useEffect, useRef, useState } from "react";
import { Modal } from "@flit/ui";
import { BOTON_PRIMARIO_CLS, controlCls } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { asociarEnTandas, correosDelLote, type ResultadoLote, type TandaLote, textosVistaPreviaLote, type VistaPreviaLote } from "@/lib/ocr";

const PIE_CLS = "flex flex-col-reverse gap-2 sm:flex-row sm:flex-wrap sm:justify-end [&>button]:w-full sm:[&>button]:w-auto";
const NOTA_CLS = "min-w-0 break-words rounded-xl border border-border bg-secondary px-3 py-2 text-xs";
const ALERTA_CLS = "min-w-0 break-words rounded-xl border border-[var(--badge-warning-fg)] px-3 py-2 text-xs text-[var(--badge-warning-fg)]";

function Lista({ titulo, items }: { titulo: string; items: { clave: string; texto: string }[] }) {
  if (items.length === 0) return null;
  return (
    <div className="flex min-w-0 flex-col gap-1">
      <p className="text-xs font-semibold">{titulo}</p>
      <ul className="max-h-40 list-disc overflow-y-auto pl-5 text-xs">
        {items.map((i) => (
          <li key={i.clave} className="break-words">
            {i.texto}
          </li>
        ))}
      </ul>
    </div>
  );
}

/**
 * «Asociar los N» (GDC-EVID §9): confirma con la vista previa del servidor (cuántos documentos y cuántos correos), asocia
 * en tandas mostrando el avance y termina con el resultado. Los reemplazos nunca entran: se revisan uno por uno.
 */
export function AsociarLote({ paqueteId, vista, onCerrar }: { paqueteId: string; vista: VistaPreviaLote; onCerrar: (huboCambios: boolean) => void }) {
  const [fase, setFase] = useState<"confirmar" | "procesando" | "resultado">("confirmar");
  const [avance, setAvance] = useState({ hechos: 0, total: vista.asociables });
  const [resultado, setResultado] = useState<ResultadoLote | null>(null);
  const textos = textosVistaPreviaLote(vista);
  // Al confirmar, el botón con el foco se deshabilita: el foco pasa al avance para no perderse (lectores de pantalla).
  const avanceRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (fase === "procesando") avanceRef.current?.focus();
  }, [fase]);

  const confirmar = async () => {
    setFase("procesando");
    const r = await asociarEnTandas(
      () => apiFetch<TandaLote>(`/ocr/paquetes/${paqueteId}/asociar-lote`, { method: "POST", body: {} }),
      (hechos, restantes) => setAvance({ hechos, total: hechos + restantes }),
    );
    setResultado(r);
    setFase("resultado");
  };

  if (fase === "resultado" && resultado) {
    const correos = correosDelLote(resultado.asociados);
    const n = resultado.asociados.length;
    return (
      <Modal
        icono="doble-check"
        open
        onClose={() => onCerrar(true)}
        title="Resultado de la asociación"
        description={n === 1 ? "Se asoció 1 documento a su comparendo." : `Se asociaron ${n} documentos a sus comparendos.`}
        footer={
          <div className={PIE_CLS}>
            <button type="button" data-autofocus className={BOTON_PRIMARIO_CLS} onClick={() => onCerrar(true)}>
              Listo
            </button>
          </div>
        }
      >
        <div className="flex flex-col gap-3 text-sm" data-resultado-lote>
          {resultado.interrumpido && (
            <p role="alert" className={ALERTA_CLS}>
              {resultado.interrumpido}
            </p>
          )}
          <p className={NOTA_CLS}>
            {correos.enCamino === 0
              ? "No quedó ningún correo por salir."
              : correos.enCamino === 1
                ? "1 correo con el documento va en camino al conductor; sale en unos minutos."
                : `${correos.enCamino} correos con el documento van en camino a los conductores; salen en unos minutos.`}
          </p>
          <Lista titulo="Sin correo al conductor" items={correos.noSalen.map((m) => ({ clave: m.motivo, texto: `${m.motivo} (${m.cantidad})` }))} />
          <Lista titulo="No se asociaron" items={resultado.omitidos.map((o) => ({ clave: o.documentoId, texto: `${o.nombreArchivo}: ${o.motivo}` }))} />
        </div>
      </Modal>
    );
  }

  const procesando = fase === "procesando";
  const pct = avance.total > 0 ? Math.min(100, Math.round((avance.hechos / avance.total) * 100)) : 0;
  return (
    <Modal
      icono="documento"
      open
      onClose={() => onCerrar(false)}
      busy={procesando}
      title={procesando ? "Asociando documentos…" : "Asociar documentos a sus comparendos"}
      description={textos.documentos}
      footer={
        <div className={PIE_CLS}>
          <button type="button" className={controlCls(false)} disabled={procesando} onClick={() => onCerrar(false)}>
            Cancelar
          </button>
          <button type="button" data-autofocus className={BOTON_PRIMARIO_CLS} disabled={procesando || vista.asociables === 0} onClick={confirmar}>
            {procesando ? "Asociando…" : "Asociar y enviar"}
          </button>
        </div>
      }
    >
      <div className="flex flex-col gap-3 text-sm">
        <p role="note" className={NOTA_CLS}>
          {textos.correos}
        </p>
        <Lista titulo="No saldrá correo" items={vista.correos.omitidos.map((o) => ({ clave: o.motivo, texto: `${o.motivo} (${o.cantidad})` }))} />
        {textos.porRevisar.map((t) => (
          <p key={t} className={ALERTA_CLS}>
            {t}
          </p>
        ))}
        {procesando && (
          <div ref={avanceRef} tabIndex={-1} data-avance-lote className="flex flex-col gap-1 rounded-lg outline-none focus-visible:ring-2 focus-visible:ring-flit-brand" aria-live="polite">
            <div
              role="progressbar"
              aria-label="Avance de la asociación"
              aria-valuemin={0}
              aria-valuemax={avance.total}
              aria-valuenow={avance.hechos}
              className="h-2 overflow-hidden rounded-full bg-secondary"
            >
              <div className="h-full rounded-full bg-flit-brand transition-[width]" style={{ width: `${pct}%` }} />
            </div>
            <p className="text-xs tabular-nums text-muted-foreground">
              {avance.hechos} de {avance.total}
            </p>
          </div>
        )}
      </div>
    </Modal>
  );
}

"use client";

import { InlineAlert } from "@flit/ui";
import { formatoFechaHora } from "@/lib/format/date";
import { avisoSinAvanceLote, type LoteEnCurso, textoAvanceLote, textosResumenLote } from "@/lib/ocr";

const ENLACE_CLS = "font-semibold text-flit-brand-ink hover:underline";

function Lista({ titulo, items }: { titulo: string; items: { clave: string; texto: string }[] }) {
  if (items.length === 0) return null;
  return (
    <div className="mt-2 flex min-w-0 flex-col gap-1">
      <p className="font-semibold">{titulo}</p>
      <ul className="max-h-40 list-disc overflow-y-auto pl-5">
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
 * GDC-EVID §10: el lote de «Asociar los N» corre en el servidor. Mientras quedan documentos por asociar, la franja muestra el
 * avance (la pantalla se actualiza sola); al terminar, el resumen se queda hasta que la gestora lo cierre.
 */
export function FranjaLote({ lote, onCerrar, onVer }: { lote: LoteEnCurso; onCerrar: () => void; onVer: (estado: "Asociado" | "Duplicado") => void }) {
  // Sin punto final: la fecha ya termina en «a. m.» / «p. m.».
  const pedido = `Pedido${lote.solicitadoPor ? ` por ${lote.solicitadoPor}` : ""} el ${formatoFechaHora(lote.solicitadoEn)}`;

  if (lote.porAsociar > 0) {
    const hechos = Math.max(0, lote.total - lote.porAsociar);
    const pct = lote.total > 0 ? Math.min(100, Math.round((hechos / lote.total) * 100)) : 0;
    return (
      <InlineAlert tone="info" title={textoAvanceLote(lote)}>
        <div className="flex min-w-0 flex-col gap-1" data-franja-lote="en-curso">
          <div
            role="progressbar"
            aria-label="Avance de la asociación"
            aria-valuemin={0}
            aria-valuemax={lote.total}
            aria-valuenow={hechos}
            className="h-1.5 w-full max-w-md overflow-hidden rounded-full bg-secondary"
          >
            <div className="h-full rounded-full bg-flit-brand transition-[width]" style={{ width: `${pct}%` }} />
          </div>
          {(() => {
            // La franja se vuelve a pintar con cada consulta (cada pocos segundos), así que los minutos se mantienen al día.
            const estancado = avisoSinAvanceLote(lote);
            return (
              estancado && (
                <p role="status" data-sin-avance className="font-semibold text-[var(--badge-warning-fg)]">
                  {estancado}
                </p>
              )
            );
          })()}
          <p>Se asocian en segundo plano: puedes seguir trabajando o cerrar esta pantalla. El avance se actualiza solo.</p>
          <p className="text-muted-foreground">{pedido}</p>
        </div>
      </InlineAlert>
    );
  }

  const textos = textosResumenLote(lote);
  return (
    <InlineAlert tone={lote.noAsociados.length > 0 ? "warning" : "success"} title="Asociación terminada">
      <div className="flex min-w-0 flex-col" data-franja-lote="terminado">
        <p>{textos.titulo}</p>
        {textos.correos && <p>{textos.correos}</p>}
        <Lista titulo="Sin correo al conductor" items={lote.sinCorreo.map((m) => ({ clave: m.motivo, texto: `${m.motivo} (${m.cantidad})` }))} />
        <Lista titulo="No se asociaron" items={lote.noAsociados.map((n) => ({ clave: n.documentoId, texto: `${n.nombreArchivo}: ${n.motivo}` }))} />
        <p className="mt-2 text-muted-foreground">
          {pedido}
          {lote.terminadoEn ? ` · terminó el ${formatoFechaHora(lote.terminadoEn)}` : ""}
        </p>
        <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1">
          {lote.asociados > 0 && (
            <button type="button" className={ENLACE_CLS} onClick={() => onVer("Asociado")}>
              Ver los asociados
            </button>
          )}
          {lote.noAsociados.length > 0 && (
            <button type="button" className={ENLACE_CLS} onClick={() => onVer("Duplicado")}>
              Ver los no asociados
            </button>
          )}
          <button type="button" className={ENLACE_CLS} onClick={onCerrar}>
            Cerrar resumen
          </button>
        </div>
      </div>
    </InlineAlert>
  );
}

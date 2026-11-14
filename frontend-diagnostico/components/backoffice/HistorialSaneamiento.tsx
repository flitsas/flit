import { AYUDA_CLS } from "@/components/configuracion/estilos";
import type { EventoHistorialBackoffice } from "@/lib/backoffice/saneamientos";
import type { EstadoSaneamiento, EventoSaneamiento } from "@/lib/saneamiento/api";
import { ROTULO_FUENTE, etiquetaSaneamiento } from "@/lib/saneamiento/estados";
import { fechaCorta } from "@/lib/tablero/detalle";

// Historial completo del backoffice (spec-ui §6.7; plan §3.5; HU #14577 AC36): todos los eventos, el más reciente primero,
// con fecha, texto, estado anterior y nuevo, concepto, actor («Compañía» o «FLIT · usuario {8}», D-13) y motivo.
// D-63: los eventos no traen estados; se deducen recorriendo el historial en orden (la máquina de estados del plan §3.2).

const TEXTO: Record<EventoSaneamiento, string> = {
  SOLICITADO: "Se solicitó el saneamiento",
  DESMARCADO: "Se quitó la solicitud",
  TOMADO: "FLIT empezó el saneamiento",
  RENGLON_AGREGADO: "FLIT agregó un concepto",
  RENGLON_QUITADO: "FLIT quitó un concepto",
  RENGLON_ACTUALIZADO: "Se actualizó el concepto",
  EVIDENCIA_CARGADA: "FLIT cargó una evidencia",
  EVIDENCIA_QUITADA: "FLIT quitó una evidencia",
  OBSERVACION_EDITADA: "Se editó la observación",
  CERRADO_SANEADO: "FLIT cerró el saneamiento como saneado",
  CERRADO_NO_PROCEDE: "FLIT cerró el saneamiento: no procede",
  REABIERTO: "FLIT reabrió el saneamiento",
  ANULADO_FLIT: "FLIT anuló el saneamiento",
};

/** Estado al que lleva cada evento de transición; los demás no cambian el estado. */
const DESTINO: Partial<Record<EventoSaneamiento, EstadoSaneamiento>> = {
  SOLICITADO: "SOLICITADO",
  DESMARCADO: "ANULADO",
  TOMADO: "EN_GESTION",
  CERRADO_SANEADO: "SANEADO",
  CERRADO_NO_PROCEDE: "NO_PROCEDE",
  REABIERTO: "EN_GESTION",
  ANULADO_FLIT: "ANULADO",
};

interface Linea {
  evento: EventoHistorialBackoffice;
  anterior: EstadoSaneamiento | null;
  nuevo: EstadoSaneamiento | null;
}

/** Recorre el historial de más antiguo a más reciente y devuelve las líneas del más reciente al más antiguo. */
export function lineasHistorial(historial: readonly EventoHistorialBackoffice[]): Linea[] {
  const orden = historial.map((e, i) => ({ e, i })).sort((a, b) => a.e.ocurrido_en.localeCompare(b.e.ocurrido_en) || a.i - b.i);
  let estado: EstadoSaneamiento | null = null;
  const lineas = orden.map(({ e }) => {
    const destino = DESTINO[e.evento] ?? null;
    const linea: Linea = { evento: e, anterior: destino ? estado : null, nuevo: destino };
    if (destino) estado = destino;
    return linea;
  });
  return lineas.reverse();
}

const rotuloEstado = (e: EstadoSaneamiento) => etiquetaSaneamiento(e, { backoffice: true })?.texto ?? "";

export function HistorialSaneamiento({ historial }: { historial: readonly EventoHistorialBackoffice[] }) {
  const lineas = lineasHistorial(historial);
  return (
    <section aria-labelledby="historial-saneamiento" className="flex flex-col gap-3">
      <h2 id="historial-saneamiento" className="text-base font-semibold text-flit-primary">
        Historial
      </h2>
      {lineas.length === 0 ? (
        <p className={AYUDA_CLS}>Sin eventos.</p>
      ) : (
        <ol aria-label="Historial" className="flex flex-col gap-2">
          {lineas.map(({ evento: e, anterior, nuevo }, i) => {
            const fuente = e.fuente ? (ROTULO_FUENTE[e.fuente] ?? null) : null;
            const texto = e.evento === "RENGLON_ACTUALIZADO" && fuente ? `${TEXTO[e.evento]} ${fuente}` : (TEXTO[e.evento] ?? "Evento del saneamiento");
            return (
              <li key={`${i}-${e.evento}-${e.ocurrido_en}`} className="flex flex-col gap-0.5 border-l-2 border-[color:var(--di-divider)] pl-3 text-sm text-flit-primary">
                <span>
                  <span className={`${AYUDA_CLS} tabular-nums`}>{fechaCorta(e.ocurrido_en)}</span>
                  {" · "}
                  {texto}
                </span>
                {nuevo && (
                  <span className={AYUDA_CLS}>
                    {anterior ? `${rotuloEstado(anterior)} → ${rotuloEstado(nuevo)}` : rotuloEstado(nuevo)}
                  </span>
                )}
                {fuente && e.evento !== "RENGLON_ACTUALIZADO" && <span className={AYUDA_CLS}>{`Concepto: ${fuente}`}</span>}
                <span className={AYUDA_CLS}>{e.actor !== "FLIT" ? "Compañía" : e.usuario_id ? `FLIT · usuario ${e.usuario_id.slice(0, 8)}` : "FLIT"}</span>
                {e.motivo?.trim() && <span className="whitespace-pre-line break-words">{`Motivo: ${e.motivo}`}</span>}
              </li>
            );
          })}
        </ol>
      )}
    </section>
  );
}

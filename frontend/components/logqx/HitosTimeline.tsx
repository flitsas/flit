"use client";

import { useState } from "react";
import { Check, Clock, Cpu, Hourglass, RefreshCw, X } from "lucide-react";
import type { LogQxHito, LogQxRadicacion } from "@/lib/api/admin-log-qx";
import {
  codigoQx,
  etapa,
  formatDuracion,
  formatFecha,
  resultado,
  secretaria,
} from "@/lib/logqx/labels";

/**
 * Línea de hitos de una radicación (HU #11789). El servidor ya devuelve las rachas de sondeo
 * colapsadas; aquí solo se dibujan.
 *
 * La decisión visual que sostiene la pantalla: los HITOS son tarjetas sólidas y el SONDEO es una
 * banda hundida y punteada. El ruido tiene que parecer ruido de fondo antes de que nadie lea una
 * palabra — con 1.065 consultas, si se dibujan igual que los hitos vuelve a ser la v1.
 */
export function HitosTimeline({
  hitos,
  radicacion,
}: {
  hitos: LogQxHito[];
  radicacion: LogQxRadicacion;
}) {
  if (hitos.length === 0) {
    return (
      <div className="px-6 py-12 text-center">
        <p className="text-sm opacity-70">Esta radicación todavía no tiene eventos registrados.</p>
      </div>
    );
  }

  const esperando = radicacion.status === "registrado";

  return (
    <ol className="flex flex-col px-6 py-6" aria-label="Línea de hitos de la radicación">
      {hitos.map((h, i) => (
        <Entrada key={i} hito={h} indice={i} ultima={i === hitos.length - 1 && !esperando} />
      ))}
      {esperando && <EsperandoDecision nombreSecretaria={radicacion.transitOfficeName} />}
    </ol>
  );
}

function Entrada({
  hito,
  indice,
  ultima,
}: {
  hito: LogQxHito;
  indice: number;
  ultima: boolean;
}) {
  return (
    <li className="grid grid-cols-[26px_1fr] gap-3.5 pb-3.5 last:pb-0">
      <Rail tipo={hito.tipo} outcome={hito.outcome} ultima={ultima} />
      {hito.tipo === "sondeo" ? <BloqueSondeo hito={hito} indice={indice} /> : <Hito hito={hito} />}
    </li>
  );
}

function Rail({
  tipo,
  outcome,
  ultima,
}: {
  tipo: string;
  outcome: string;
  ultima: boolean;
}) {
  const esError = outcome !== "ok";
  // Tonos de la paleta de estados del sistema (`lib/tramites/estados`), no colores propios de
  // esta pantalla: el mismo verde/naranja/azul que usan los chips del resto de la consola.
  const color =
    tipo === "sondeo"
      ? "text-[#557EFF] border-[#557EFF]"
      : esError
        ? "text-[#C2410C] border-[#C2410C]"
        : "text-[#15803D] border-[#15803D]";
  const Icon = tipo === "sondeo" ? RefreshCw : esError ? X : Check;

  return (
    <div className="flex flex-col items-center">
      <span
        aria-hidden="true"
        className={`grid h-6 w-6 shrink-0 place-items-center rounded-full border-[1.5px] bg-white dark:bg-[#0B0F14] ${color}`}
      >
        <Icon className="h-3.5 w-3.5" strokeWidth={2.5} aria-hidden="true" />
      </span>
      {!ultima && <span className="-mb-3.5 mt-0.5 w-0.5 flex-1 bg-[#DFE5ED] dark:bg-white/10" />}
    </div>
  );
}

/** Un hito real: tarjeta sólida. */
function Hito({ hito }: { hito: LogQxHito }) {
  const res = resultado(hito.outcome);
  const duracion = formatDuracion(hito.durationMs);

  return (
    <div className="min-w-0 rounded-[9px] border border-[#DFE5ED] bg-white px-3.5 py-2.5 dark:border-white/10 dark:bg-[#0B0F14]">
      <div className="flex flex-wrap items-baseline gap-2.5">
        <h3 className="text-[13.5px] font-semibold">{etapa(hito.stage)}</h3>
        {hito.outcome !== "ok" && (
          <span
            className={`text-[10px] font-bold uppercase ${
              hito.outcome === "error_definitivo" ? "text-[#991B1B]" : "text-[#C2410C]"
            }`}
          >
            {res.label}
          </span>
        )}
        <time className="ml-auto font-mono text-[11.5px] tabular-nums opacity-55">
          {formatFecha(hito.occurredAt)}
        </time>
      </div>

      <div className="mt-1.5 flex flex-wrap gap-x-3.5 gap-y-1 text-[12.5px] opacity-75">
        {hito.codigo != null && (
          <span>
            código <span className="font-mono">{codigoQx(hito.codigo)}</span>
          </span>
        )}
        {duracion && (
          <span className="inline-flex items-center gap-1">
            <Clock className="h-3 w-3" aria-hidden="true" />
            <span className="font-mono">{duracion}</span>
          </span>
        )}
      </div>

      {hito.mensaje && (
        <p className="mt-2 rounded-r-md border-l-2 border-[#C9D6EA] bg-[#F4F7FC] px-3 py-1.5 text-[12.5px] italic opacity-80 dark:border-white/15 dark:bg-white/5">
          “{hito.mensaje}”
        </p>
      )}
    </div>
  );
}

/**
 * El sondeo colapsado: banda punteada y hundida, deliberadamente distinta de un hito. Al desplegar
 * se explica que las consultas individuales viven en el log completo, en vez de listarlas aquí —
 * traerlas todas volvería a cargar lo que este bloque existe para evitar.
 */
function BloqueSondeo({ hito, indice }: { hito: LogQxHito; indice: number }) {
  const [abierto, setAbierto] = useState(false);
  const media = formatDuracion(hito.duracionMediaMs);
  const detalleId = `logqx-sondeo-${indice}`;

  return (
    <div
      className="min-w-0 rounded-[9px] border border-dashed border-[#C9D6EA] px-3.5 py-2.5 dark:border-white/15"
      style={{
        backgroundImage:
          "repeating-linear-gradient(135deg, rgba(85,126,255,0.05) 0 9px, transparent 9px 18px)",
      }}
    >
      <div className="flex flex-wrap items-baseline gap-2.5">
        <h3 className="text-[13px] font-semibold opacity-75">Consultando estado del trámite</h3>
        <time className="ml-auto font-mono text-[11.5px] tabular-nums opacity-55">
          desde {formatFecha(hito.occurredAt)}
        </time>
      </div>

      <p className="mt-1 text-[12.5px] opacity-80">
        <span className="font-mono text-[13px] font-bold tabular-nums opacity-100">
          {hito.consultas?.toLocaleString("es-CO")}
        </span>{" "}
        consultas · todas <b>sin novedad</b> · última{" "}
        <span className="font-mono">{formatFecha(hito.hasta)}</span>
        {media && (
          <>
            {" "}
            · duración media <span className="font-mono">{media}</span>
          </>
        )}
      </p>

      <button
        type="button"
        aria-expanded={abierto}
        aria-controls={detalleId}
        onClick={() => setAbierto((v) => !v)}
        className="mt-2 inline-flex items-center gap-1.5 text-[12px] font-semibold text-[#557EFF] hover:underline"
      >
        {abierto ? "▾" : "▸"} Qué hay dentro de este bloque
      </button>

      {abierto && (
        <div
          id={detalleId}
          className="mt-2 border-t border-dashed border-[#C9D6EA] pt-2 text-[12px] opacity-75 dark:border-white/15"
        >
          <p>
            Son {hito.consultas?.toLocaleString("es-CO")} consultas de estado consecutivas, todas
            con el mismo resultado y sin que el trámite cambiara. Se agrupan porque, una a una, no
            aportan información.
          </p>
          <p className="mt-1.5">
            Para verlas de forma individual —o filtrarlas— usa la pestaña{" "}
            <b>Log completo</b> y desactiva «ocultar consultas sin novedad».
          </p>
          <p className="mt-1.5 flex flex-wrap gap-x-3 gap-y-1 font-mono text-[11.5px]">
            <span className="inline-flex items-center gap-1">
              <Cpu className="h-3 w-3" aria-hidden="true" /> Consulta de estado
            </span>
            {hito.codigo != null && <span>código {codigoQx(hito.codigo)}</span>}
          </p>
        </div>
      )}
    </div>
  );
}

/** Cierre de la línea: qué se está esperando ahora mismo. */
function EsperandoDecision({ nombreSecretaria }: { nombreSecretaria: string }) {
  return (
    <li className="grid grid-cols-[26px_1fr] gap-3.5">
      <div className="flex flex-col items-center">
        <span
          aria-hidden="true"
          className="grid h-6 w-6 shrink-0 place-items-center rounded-full border-[1.5px] border-[#C2410C] bg-white text-[#C2410C] dark:bg-[#0B0F14]"
        >
          <Hourglass className="h-3.5 w-3.5" strokeWidth={2.5} aria-hidden="true" />
        </span>
      </div>
      <div className="min-w-0 rounded-[9px] border border-dashed border-[#DFE5ED] px-3.5 py-2.5 dark:border-white/10">
        <h3 className="text-[13.5px] font-semibold">
          Esperando la decisión de {secretaria(nombreSecretaria)}
        </h3>
        <p className="mt-1 text-[12.5px] opacity-70">Se sigue consultando periódicamente.</p>
      </div>
    </li>
  );
}

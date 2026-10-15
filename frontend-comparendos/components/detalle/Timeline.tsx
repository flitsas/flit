"use client";

import { useState } from "react";
import Link from "next/link";
import { MessageSquarePlus } from "lucide-react";
import { InlineAlert } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { useToast } from "@/components/Toast";
import { BOTON_PRIMARIO_CLS, INPUT_CLS, TEXTAREA_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { EVENTO_TIMELINE } from "@/lib/etiquetas";
import { formatoFechaHora } from "@/lib/format/date";
import { P } from "@/lib/nav";
import { tienePermiso } from "@/lib/sesion";
import type { EventoTimeline } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

const COLOR_ACTOR: Record<string, string> = {
  Usuario: "#557EFF",
  Sistema: "#5B6B82",
  Scheduler: "#8B5CF6",
  ApiExterna: "#00A3A3",
  Conductor: "#D9A400",
};

/**
 * Timeline del comparendo estilo guía de envío (§7.2): el evento más reciente arriba, filtro por tipo y «Agregar
 * observación» como un hito más (no se edita ni se borra).
 */
/** Secciones del detalle a las que lleva un evento sin pantalla propia (DP, pago, documentos del conductor). */
export type SeccionDetalle = "dp" | "pago" | "contraventor";

const ENLACE_SECCION: Record<string, { seccion: SeccionDetalle; texto: string }> = {
  derecho_peticion: { seccion: "dp", texto: "Ver el derecho de petición" },
  pago: { seccion: "pago", texto: "Ver el pago" },
  documento_soporte: { seccion: "contraventor", texto: "Ver los documentos del conductor" },
};

export function Timeline({
  comparendoId,
  puedeObservar,
  onCambio,
  onIr,
}: {
  comparendoId: string;
  puedeObservar: boolean;
  onCambio?: () => void;
  /** Abre la sección del detalle que originó el evento. */
  onIr?: (seccion: SeccionDetalle) => void;
}) {
  const toast = useToast();
  const { sesion } = useSesion();
  const verMensajes = tienePermiso(sesion, P.mensajesVer);
  const [tipo, setTipo] = useState("");
  const [nota, setNota] = useState("");
  const [escribiendo, setEscribiendo] = useState(false);
  const [guardando, setGuardando] = useState(false);
  const eventos = useCarga(`timeline:${comparendoId}:${tipo}`, (signal) =>
    apiFetch<EventoTimeline[]>(`/comparendos/${comparendoId}/timeline`, { signal, query: { tipo: tipo || undefined } }),
  );

  const guardar = async () => {
    setGuardando(true);
    try {
      await apiFetch(`/comparendos/${comparendoId}/observaciones`, { method: "POST", body: { texto: nota } });
      setNota("");
      setEscribiendo(false);
      eventos.recargar();
      onCambio?.();
      toast.show("Observación agregada.", "success");
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo guardar la observación.", "error");
    } finally {
      setGuardando(false);
    }
  };

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <select className={`${INPUT_CLS} w-56`} value={tipo} onChange={(e) => setTipo(e.target.value)} aria-label="Filtrar por tipo de evento">
          <option value="">Todos los eventos</option>
          {Object.entries(EVENTO_TIMELINE).map(([valor, texto]) => (
            <option key={valor} value={valor}>
              {texto}
            </option>
          ))}
        </select>
      </div>

      {escribiendo && (
        <div className="flex flex-col gap-2">
          <textarea className={TEXTAREA_CLS} rows={3} maxLength={2000} value={nota} onChange={(e) => setNota(e.target.value)} placeholder="Escribe la observación…" aria-label="Observación" />
          <div className="flex items-center gap-2">
            <button type="button" className={BOTON_PRIMARIO_CLS} disabled={guardando || nota.trim().length === 0} onClick={guardar}>
              {guardando ? "Guardando…" : "Guardar observación"}
            </button>
            <button type="button" className="text-xs font-semibold opacity-70 hover:underline" onClick={() => setEscribiendo(false)}>
              Cancelar
            </button>
            <span className="ml-auto text-[11px] opacity-60">{nota.length}/2000 · No se puede editar después</span>
          </div>
        </div>
      )}

      {eventos.estado === "loading" && <p className="text-xs opacity-60">Cargando eventos…</p>}
      {eventos.estado === "error" && <InlineAlert tone="error" action={<button type="button" onClick={eventos.recargar}>Reintentar</button>}>{eventos.error}</InlineAlert>}
      {eventos.estado === "ready" && (eventos.datos ?? []).length === 0 && <p className="text-xs opacity-60">Sin eventos para mostrar.</p>}

      <ol className="relative ml-2 border-l-2 border-[#DFE5ED] dark:border-white/10">
        {(eventos.datos ?? []).map((e) => (
          <li key={e.id} className="relative mb-4 ml-5">
            <span className="absolute -left-[27px] top-1 h-3 w-3 rounded-full ring-4 ring-white dark:ring-[#0B0F14]" style={{ background: COLOR_ACTOR[e.actorTipo] ?? "#5B6B82" }} aria-hidden />
            <div className="flex flex-wrap items-baseline gap-x-2">
              <span className="text-xs font-semibold">{EVENTO_TIMELINE[e.tipo] ?? e.tipo}</span>
              <span className="text-[11px] opacity-60">
                {formatoFechaHora(e.ocurridoEn)} · {e.actorNombre}
              </span>
            </div>
            <p className="whitespace-pre-wrap text-xs text-[#334155] dark:text-slate-300">{e.descripcion}</p>
            {/* §7.2: el evento lleva a lo que lo originó: el correo al Centro de mensajes; el DP, el pago y los documentos a su sección del detalle. */}
            {e.relacionadoTipo === "comunicacion_saliente" && e.relacionadoId && verMensajes && (
              <Link href={`/mensajes?mensaje=${e.relacionadoId}`} className="text-[11px] font-semibold text-[#557EFF] hover:underline">
                Ver el correo
              </Link>
            )}
            {e.relacionadoTipo && ENLACE_SECCION[e.relacionadoTipo] && onIr && (
              <button type="button" className="text-[11px] font-semibold text-[#557EFF] hover:underline" onClick={() => onIr(ENLACE_SECCION[e.relacionadoTipo!]!.seccion)}>
                {ENLACE_SECCION[e.relacionadoTipo]!.texto}
              </button>
            )}
          </li>
        ))}
      </ol>

      {/* Epic GDC-8 / MOD06 §5.1: «Agregar observación» flota sobre el timeline y sigue a la vista al bajar por los eventos. */}
      {puedeObservar && !escribiendo && (
        <div className="pointer-events-none sticky bottom-3 flex justify-end">
          <button
            type="button"
            className={`${BOTON_PRIMARIO_CLS} pointer-events-auto rounded-full shadow-lg`}
            onClick={() => setEscribiendo(true)}
          >
            <MessageSquarePlus className="h-3.5 w-3.5" aria-hidden />
            Agregar observación
          </button>
        </div>
      )}
    </div>
  );
}

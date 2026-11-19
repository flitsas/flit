"use client";

import { useState } from "react";
import Link from "next/link";
import { InlineAlert } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { useToast } from "@/components/Toast";
import { BOTON_PRIMARIO_CLS, INPUT_CLS, TEXTAREA_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { EVENTO_TIMELINE, nombreActor, textoParaGestora } from "@/lib/etiquetas";
import { formatoFechaHora } from "@/lib/format/date";
import { P } from "@/lib/nav";
import { tienePermiso } from "@/lib/sesion";
import type { EventoTimeline } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";
import { iconoComo } from "@flit/ui/iconoComo";
import { narracionDesdeInfraccion } from "@/components/detalle/narracion";

const ICONO_MAS = iconoComo("mas");

/** Eventos que se ven sin pedir más (R2). */
export const EVENTOS_VISIBLES = 8;

const COLOR_ACTOR: Record<string, string> = {
  Usuario: "var(--color-flit-brand)",
  Sistema: "var(--muted-foreground)",
  Scheduler: "var(--badge-purple-fg)",
  ApiExterna: "var(--color-flit-tech)",
  Conductor: "var(--badge-warning-fg)",
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
  version = 0,
  puedeObservar,
  onCambio,
  onIr,
  fechaInfraccion = null,
}: {
  comparendoId: string;
  /** Fecha de imposición: cada evento dice cuántos días después de la infracción ocurrió (junto a su fecha). */
  fechaInfraccion?: string | null;
  /** Sube cuando el detalle cambia por una acción: el timeline vuelve a cargar. */
  version?: number;
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
  // R2: las 8 más recientes; «Ver los N» muestra el resto en su sitio (el filtro por tipo sigue igual).
  const [todos, setTodos] = useState(false);
  const eventos = useCarga(`timeline:${comparendoId}:${tipo}:${version}`, (signal) =>
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
        <select className={`${INPUT_CLS} w-full sm:w-auto sm:max-w-full`} value={tipo} onChange={(e) => setTipo(e.target.value)} aria-label="Filtrar por tipo de evento">
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
            <button type="button" className="text-xs font-semibold text-muted-foreground hover:underline" onClick={() => setEscribiendo(false)}>
              Cancelar
            </button>
            <span className="ml-auto text-xs text-muted-foreground">{nota.length}/2000 · No se puede editar después</span>
          </div>
        </div>
      )}

      {/* Reserva el alto de unos eventos mientras carga: lo de abajo no salta cuando llegan (R2, HANNAH). */}
      {eventos.estado === "loading" && <p className="min-h-48 text-xs text-muted-foreground">Cargando eventos…</p>}
      {eventos.estado === "error" && <InlineAlert tone="error" action={<button type="button" onClick={eventos.recargar}>Reintentar</button>}>{eventos.error}</InlineAlert>}
      {eventos.estado === "ready" && (eventos.datos ?? []).length === 0 && <p className="text-xs text-muted-foreground">Sin eventos para mostrar.</p>}

      <ol className="relative ml-2 border-l-2 border-border">
        {(todos ? (eventos.datos ?? []) : (eventos.datos ?? []).slice(0, EVENTOS_VISIBLES)).map((e) => {
          const narracion = narracionDesdeInfraccion(fechaInfraccion, e.ocurridoEn);
          return (
          <li key={e.id} className="relative mb-4 ml-5">
            <span className="absolute -left-[27px] top-1 h-3 w-3 rounded-full ring-4 ring-card" style={{ background: COLOR_ACTOR[e.actorTipo] ?? "var(--muted-foreground)" }} aria-hidden />
            <div className="flex flex-wrap items-baseline gap-x-2">
              <span className="text-sm font-semibold">{EVENTO_TIMELINE[e.tipo] ?? e.tipo}</span>
              <span className="text-xs text-muted-foreground">
                {formatoFechaHora(e.ocurridoEn)}
                {narracion && ` · ${narracion}`} · {nombreActor(e.actorNombre)}
              </span>
            </div>
            <p className="max-w-[80ch] whitespace-pre-wrap text-sm text-foreground">{textoParaGestora(e.descripcion)}</p>
            {/* §7.2: el evento lleva a lo que lo originó: el correo al Centro de mensajes; el DP, el pago y los documentos a su sección del detalle. */}
            {e.relacionadoTipo === "comunicacion_saliente" && e.relacionadoId && verMensajes && (
              <Link href={`/mensajes?mensaje=${e.relacionadoId}`} className="text-xs font-semibold text-flit-brand-ink hover:underline">
                Ver el correo
              </Link>
            )}
            {e.relacionadoTipo && ENLACE_SECCION[e.relacionadoTipo] && onIr && (
              <button type="button" className="text-xs font-semibold text-flit-brand-ink hover:underline" onClick={() => onIr(ENLACE_SECCION[e.relacionadoTipo!]!.seccion)}>
                {ENLACE_SECCION[e.relacionadoTipo]!.texto}
              </button>
            )}
          </li>
          );
        })}
      </ol>
      {!todos && (eventos.datos ?? []).length > EVENTOS_VISIBLES && (
        <button type="button" className="self-start text-sm font-semibold text-flit-brand-ink hover:underline" onClick={() => setTodos(true)}>
          Ver los {(eventos.datos ?? []).length} eventos
        </button>
      )}

      {/* Epic GDC-8 / MOD06 §5.1: «Agregar observación» flota sobre el timeline y sigue a la vista al bajar por los eventos. */}
      {puedeObservar && !escribiendo && (
        <div className="pointer-events-none sticky bottom-3 flex justify-end">
          <button
            type="button"
            className={`${BOTON_PRIMARIO_CLS} pointer-events-auto rounded-full`}
            onClick={() => setEscribiendo(true)}
          >
            <ICONO_MAS className="h-3.5 w-3.5" aria-hidden />
            Agregar observación
          </button>
        </div>
      )}
    </div>
  );
}

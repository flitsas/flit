"use client";

import { Send, X } from "lucide-react";
import { useEffect, useId, useRef, useState, type FormEvent, type KeyboardEvent } from "react";
import { boton } from "@/components/landing/estilos";
import { ASSETS_DR_FLIT } from "@/lib/assets";
import { AVISO_DEMOSTRACION, PREGUNTAS_SUGERIDAS, responder, SALUDO, type ContextoChat } from "@/lib/chat/respuestas";
import { DrFlitAvatar } from "./DrFlitAvatar";

interface Mensaje {
  id: number;
  de: "usuario" | "asistente";
  texto: string;
}

/** Demora que finge «está escribiendo» (la misma del diseño). */
const DEMORA_MS = 650;
const MAX_MENSAJE = 300;

/**
 * Asistente Dr. FLIT SIMULADO: sin servicio externo, sin IA y sin datos personales. Lo que el ciudadano escribe vive solo en el
 * estado de este componente (no hay almacenamiento, consola, telemetría ni URL) y se pierde al recargar. Las listas de trámites y
 * organismos llegan por `contexto` desde los catálogos reales del BFF. Mensajes en texto plano, sin markdown.
 */
export function DrFlit({ contexto = {} }: { contexto?: ContextoChat }) {
  const [abierto, setAbierto] = useState(false);
  const [mensajes, setMensajes] = useState<Mensaje[]>([{ id: 0, de: "asistente", texto: SALUDO }]);
  const [borrador, setBorrador] = useState("");
  const [escribiendo, setEscribiendo] = useState(false);
  const [sugerencias, setSugerencias] = useState<readonly string[]>([]);
  const lanzador = useRef<HTMLButtonElement>(null);
  const cerrar = useRef<HTMLButtonElement>(null);
  const conversacion = useRef<HTMLDivElement>(null);
  const temporizador = useRef<ReturnType<typeof setTimeout> | null>(null);
  const siguiente = useRef(1);
  const aviso = useId();

  useEffect(() => { if (abierto) cerrar.current?.focus(); }, [abierto]);
  useEffect(() => () => { if (temporizador.current) clearTimeout(temporizador.current); }, []);
  useEffect(() => {
    const el = conversacion.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [mensajes, escribiendo, abierto]);

  function minimizar() {
    setAbierto(false);
    lanzador.current?.focus();
  }

  function enviar(texto: string) {
    const limpio = texto.trim();
    if (!limpio || escribiendo) return;
    setMensajes((m) => [...m, { id: siguiente.current++, de: "usuario", texto: limpio }]);
    setBorrador("");
    setSugerencias([]);
    setEscribiendo(true);
    temporizador.current = setTimeout(() => {
      const respuesta = responder(limpio, contexto);
      setMensajes((m) => [...m, { id: siguiente.current++, de: "asistente", texto: respuesta.texto }]);
      setSugerencias(respuesta.sugerencias);
      setEscribiendo(false);
    }, DEMORA_MS);
  }

  function alEnviar(e: FormEvent) {
    e.preventDefault();
    enviar(borrador);
  }

  function alTeclear(e: KeyboardEvent<HTMLTextAreaElement>) {
    // Enter envía; Mayús+Enter agrega un salto de línea.
    if (e.key === "Enter" && !e.shiftKey && !e.nativeEvent.isComposing) {
      e.preventDefault();
      enviar(borrador);
    }
  }

  return (
    <>
      <button
        ref={lanzador}
        type="button"
        aria-label={abierto ? "Minimizar Dr. FLIT" : "Abrir Dr. FLIT, tu asistente inteligente"}
        aria-expanded={abierto}
        aria-controls="dr-flit-widget"
        onClick={() => (abierto ? minimizar() : setAbierto(true))}
        className={boton("ghost", "dr-flit-tab flex h-auto items-center gap-3 whitespace-normal rounded-[9px] border border-border bg-flit-gradient p-3 text-left text-primary-foreground", "none")}
      >
        <DrFlitAvatar origen={ASSETS_DR_FLIT.lanzador} tamano="h-14 w-14" />
        <span className="min-w-0 flex-1 text-left">
          <span className="block font-bold">Dr. FLIT</span>
          <span className="block text-xs">Tu asistente inteligente</span>
          <span className="mt-1 flex items-center gap-1.5 text-xs"><span aria-hidden className="online-dot h-2.5 w-2.5 rounded-full bg-success" />En línea</span>
        </span>
      </button>
      {abierto && <div aria-hidden className="chat-backdrop" onClick={minimizar} />}
      {abierto && (
        <aside
          id="dr-flit-widget"
          role="dialog"
          aria-modal="false"
          aria-label="Asistente virtual DR. FLIT"
          aria-describedby={aviso}
          onKeyDown={(e) => { if (e.key === "Escape") minimizar(); }}
          className="dashboard-chat chat-widget overflow-hidden rounded-[9px] border border-border bg-card shadow-float"
        >
          <div className="flex shrink-0 items-center gap-3 bg-flit-gradient p-3 text-primary-foreground">
            <DrFlitAvatar origen={ASSETS_DR_FLIT.avatar} tamano="h-10 w-10" nombre="Robot asistente DR. FLIT" />
            <div className="flex-1 text-center">
              <h2 className="font-bold">DR. FLIT</h2>
              <p className="text-xs">Asistente virtual de demostración</p>
              <span className="flex items-center justify-center gap-1.5 text-xs"><span aria-hidden className="h-2.5 w-2.5 rounded-full bg-success" />En línea</span>
            </div>
            <button ref={cerrar} type="button" aria-label="Minimizar chat" onClick={minimizar} className={boton("ghost", "h-11 w-11 text-primary-foreground", "none")}><X /></button>
          </div>
          <p id={aviso} className="shrink-0 border-b border-border bg-muted px-3 py-2 text-xs">{AVISO_DEMOSTRACION}</p>
          <div ref={conversacion} role="log" aria-live="polite" aria-label="Conversación con DR. FLIT" tabIndex={0} className="flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto p-3">
            {mensajes.map((m) => (
              <div key={m.id} className={`group flex w-full max-w-[95%] flex-col gap-2 ${m.de === "usuario" ? "is-user ml-auto justify-end" : "is-assistant"}`}>
                <div className="flex w-fit min-w-0 max-w-full flex-col gap-2 overflow-hidden whitespace-pre-wrap text-sm leading-relaxed group-[.is-user]:ml-auto group-[.is-user]:rounded-lg group-[.is-user]:bg-primary group-[.is-user]:px-4 group-[.is-user]:py-3 group-[.is-user]:text-secondary-foreground group-[.is-assistant]:text-foreground">
                  <span className="sr-only">{m.de === "usuario" ? "Tú: " : "DR. FLIT: "}</span>
                  {m.texto}
                </div>
              </div>
            ))}
            {escribiendo && <p role="status" className="text-xs text-foreground/70">DR. FLIT está escribiendo…</p>}
            {sugerencias.length > 0 && (
              <div role="group" className="flex flex-wrap gap-2" aria-label="Sugerencias relacionadas">
                {sugerencias.map((q) => (
                  <button key={q} type="button" disabled={escribiendo} onClick={() => enviar(q)} className={boton("ghost", "h-auto whitespace-normal rounded-full border border-input bg-background px-3 py-2 text-left text-[10px] text-primary", "none")}>{q}</button>
                ))}
              </div>
            )}
          </div>
          <div role="group" aria-label="Preguntas frecuentes" className="chat-questions shrink-0 space-y-2 border-t border-border p-3">
            {PREGUNTAS_SUGERIDAS.map((q) => (
              <button key={q} type="button" disabled={escribiendo} onClick={() => enviar(q)} className={boton("ghost", "h-auto min-h-9 w-full whitespace-normal rounded-full border border-border bg-card px-3 py-2 text-left text-[11px] leading-snug text-primary", "none")}>{q}</button>
            ))}
          </div>
          <form onSubmit={alEnviar} className="shrink-0 border-t border-border p-3 pt-2">
            <label htmlFor="dr-flit-mensaje" className="sr-only">Mensaje para DR. FLIT</label>
            <div className="flex items-end gap-2">
              <textarea
                id="dr-flit-mensaje"
                rows={2}
                maxLength={MAX_MENSAJE}
                autoComplete="off"
                placeholder="Escribe tu consulta…"
                value={borrador}
                onChange={(e) => setBorrador(e.target.value)}
                onKeyDown={alTeclear}
                className="field min-h-10 flex-1 resize-none text-xs"
              />
              <button type="submit" aria-label="Enviar mensaje" title="Enviar mensaje" disabled={escribiendo || !borrador.trim()} className={boton("ghost", "h-11 w-11 text-primary", "none")}><Send /></button>
            </div>
          </form>
        </aside>
      )}
    </>
  );
}

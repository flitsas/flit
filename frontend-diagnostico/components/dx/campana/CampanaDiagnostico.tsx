"use client";

import { useEffect, useId, useRef, useState } from "react";
import { createPortal } from "react-dom";
import Link from "next/link";
import { NotificacionPersistente } from "@flit/ui/NotificacionPersistente";
import { IconoDxCampana } from "@/components/dx/iconos/IconoDxCampana";
import { fechaDelDato } from "@/components/tablero/CeldaSemaforo";
import type { NotificacionDiagnostico } from "@/lib/notificaciones/api";
import { textoNotificacion } from "@/lib/notificaciones/mensajes";
import { useNotificaciones } from "@/lib/notificaciones/useNotificaciones";
import { descargarArchivo, mensajeDescarga, pedirZip, urlArchivoZip } from "@/lib/reportes/api";

const FOCO = "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)]";
const BOTON_TEXTO = `min-h-9 rounded-full px-3 text-sm font-semibold text-[color:var(--di-action)] hover:bg-[color:var(--di-accent-soft)] ${FOCO}`;

const esZipDescargable = (i: NotificacionDiagnostico) => i.tipo === "ZIP_EVIDENCIAS_LISTO";
const avisosSinLeer = (n: number) => (n === 1 ? "1 aviso sin leer" : `${n} avisos sin leer`);

/**
 * Campana de Diagnóstico (HU #14562 · AC50–AC54, ADR-0086). Se monta en la barra de la suite por `headerActions` de
 * `DiagnosticoShell`, sin tocar `@flit/shell`. Botón con contador de no leídas (oculto en 0), panel con la lista,
 * vacío, error con Reintentar y «Marcar todas como leídas». Cada ZIP_EVIDENCIAS_LISTO no leído que llega en un sondeo
 * posterior a la primera carga abre una NotificacionPersistente arriba a la derecha (sin autocierre) con «Descargar».
 * Los textos se arman en el cliente (`textoNotificacion`): nada de lo que el servidor mande como texto se pinta.
 */
export function CampanaDiagnostico() {
  const n = useNotificaciones();
  const id = useId();
  const [abierto, setAbierto] = useState(false);
  const botonRef = useRef<HTMLButtonElement>(null);
  const contenedorRef = useRef<HTMLDivElement>(null);
  // ZIP no leídos de la primera carga: no abren el aviso persistente (siguen en la campana).
  const [base, setBase] = useState<ReadonlySet<string> | null>(null);
  const [cerrados, setCerrados] = useState<ReadonlySet<string>>(new Set());
  const [errorDescarga, setErrorDescarga] = useState<Record<string, string>>({});
  const [montado, setMontado] = useState(false);

  if (base === null && n.estado === "listo") setBase(new Set(n.items.filter((i) => i.tipo === "ZIP_EVIDENCIAS_LISTO").map((i) => i.id)));

  useEffect(() => setMontado(true), []);

  useEffect(() => {
    if (!abierto) return;
    const fuera = (e: MouseEvent) => {
      if (contenedorRef.current && !contenedorRef.current.contains(e.target as Node)) setAbierto(false);
    };
    document.addEventListener("mousedown", fuera);
    return () => document.removeEventListener("mousedown", fuera);
  }, [abierto]);

  const cerrarPanel = () => {
    setAbierto(false);
    botonRef.current?.focus();
  };

  const descargar = async (i: NotificacionDiagnostico) => {
    try {
      // Sin la evidencia en el aviso, el POST del ZIP devuelve el que ya está LISTA (200) del mismo lote.
      let evidencia = i.referencia_id;
      if (!evidencia) {
        const estado = await pedirZip(i.diagnostico_id);
        if (estado.estado !== "LISTA") throw new Error("El ZIP todavía se está preparando. Espera a que quede listo.");
        evidencia = estado.id;
      }
      await descargarArchivo(urlArchivoZip(i.diagnostico_id, evidencia), "evidencias.zip");
      await n.marcarLeida(i.id);
    } catch (e) {
      setErrorDescarga((m) => ({ ...m, [i.id]: mensajeDescarga(e) }));
    }
  };

  const visibles = n.items.map((i) => ({ item: i, texto: textoNotificacion(i) })).filter((v) => v.texto !== null);
  const persistentes =
    base === null ? [] : visibles.filter((v) => esZipDescargable(v.item) && !v.item.leida_en && !base.has(v.item.id) && !cerrados.has(v.item.id));

  let cuerpo: React.ReactNode;
  if (n.estado === "error") {
    cuerpo = (
      <div className="flex flex-col items-start gap-2 p-4">
        <p className="text-sm font-semibold text-[color:var(--di-danger)]">No se pudieron cargar los avisos</p>
        <p className="text-sm text-[color:var(--di-text-secondary)]">El resto de Diagnóstico sigue funcionando.</p>
        <button type="button" onClick={n.reintentar} className={BOTON_TEXTO}>
          Reintentar
        </button>
      </div>
    );
  } else if (n.estado === "cargando") {
    cuerpo = (
      <p aria-busy="true" className="p-4 text-sm text-[color:var(--di-text-secondary)]">
        Cargando avisos…
      </p>
    );
  } else if (visibles.length === 0) {
    cuerpo = <p className="p-4 text-sm text-[color:var(--di-text-secondary)]">No tienes avisos</p>;
  } else {
    cuerpo = (
      <ul className="flex max-h-96 flex-col overflow-y-auto">
        {visibles.map(({ item, texto }) => (
          <li key={item.id} className="flex gap-3 border-t border-[color:var(--di-divider)] px-4 py-3 first:border-t-0">
            <span
              aria-hidden="true"
              className={`mt-1.5 size-2 shrink-0 rounded-full ${item.leida_en ? "bg-transparent" : "bg-[color:var(--di-action)]"}`}
            />
            <div className="flex min-w-0 flex-1 flex-col gap-1">
              <p className="text-sm font-semibold text-[color:var(--di-text-primary)]">
                {texto!.titulo}
                {!item.leida_en && <span className="sr-only"> (sin leer)</span>}
              </p>
              <p className="text-sm text-[color:var(--di-text-secondary)]">{texto!.mensaje}</p>
              <time dateTime={item.creada_en} className="text-xs tabular-nums text-[color:var(--di-text-tertiary)]">
                {fechaDelDato(item.creada_en)}
              </time>
              <div>
                {/* HU #14562 · m-7: nombre único por aviso (texto visible + mensaje y fecha para el lector de pantalla). */}
                {esZipDescargable(item) ? (
                  <button type="button" onClick={() => void descargar(item)} className={BOTON_TEXTO}>
                    Descargar
                    <span className="sr-only">{`: ${texto!.mensaje} (${fechaDelDato(item.creada_en)})`}</span>
                  </button>
                ) : (
                  <Link href={`/diagnosticos/${encodeURIComponent(item.diagnostico_id)}`} onClick={() => {
                      setAbierto(false);
                      void n.marcarLeida(item.id);
                    }}
                    className={BOTON_TEXTO}
                  >
                    Ver lote
                    <span className="sr-only">{`: ${texto!.mensaje} (${fechaDelDato(item.creada_en)})`}</span>
                  </Link>
                )}
                {errorDescarga[item.id] && (
                  <p role="alert" className="text-xs text-[color:var(--di-danger)]">
                    {errorDescarga[item.id]}
                  </p>
                )}
              </div>
            </div>
          </li>
        ))}
      </ul>
    );
  }

  return (
    // HU #14562 · D1: `suite-ancla` + `suite-panel-barra` de @flit/shell anclan el panel al botón y, a ≤ 640 px, a la barra
    // con 8 px a cada lado (si no, a 360 px el panel se corta por la izquierda).
    <div ref={contenedorRef} className="suite-ancla">
      <button
        ref={botonRef}
        type="button"
        aria-label={n.noLeidas > 0 ? `Avisos, ${avisosSinLeer(n.noLeidas)}` : "Avisos"}
        aria-expanded={abierto}
        aria-controls={abierto ? `${id}-panel` : undefined}
        data-cargado={n.estado !== "cargando"}
        onClick={() => setAbierto((a) => !a)}
        // HU #14562 · m-7: Escape cierra el panel también con el foco en el botón.
        onKeyDown={(e) => {
          if (e.key === "Escape" && abierto) {
            e.preventDefault();
            cerrarPanel();
          }
        }}
        className={`relative inline-flex size-10 items-center justify-center rounded-full text-[color:var(--di-text-primary)] hover:bg-[color:var(--di-accent-soft)] ${FOCO}`}
      >
        <IconoDxCampana />
        {/* HU #14562 (auditoría M5, 1.4.3): --kit-rojo vale lo mismo en claro y oscuro; blanco encima da 6,4:1. Con --di-danger
            el oscuro (#ff6b6b) quedaba en 2,8:1. */}
        {n.noLeidas > 0 && (
          <span
            aria-hidden="true"
            className="absolute -right-0.5 -top-0.5 inline-flex min-w-5 items-center justify-center rounded-full bg-[color:var(--kit-rojo)] px-1 text-[11px] font-semibold tabular-nums text-white"
          >
            {n.noLeidas > 99 ? "99+" : n.noLeidas}
          </span>
        )}
      </button>
      {abierto && (
        <div
          id={`${id}-panel`}
          role="region"
          aria-label="Avisos"
          onKeyDown={(e) => {
            if (e.key === "Escape") cerrarPanel();
          }}
          // HU #14562 · m-1: menú propio de Diagnóstico, con su radio, su sombra de dock y su motion (por opacidad).
          className="suite-panel-barra rounded-[var(--dx-radio)] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] shadow-[var(--dx-sombra-dock)] opacity-100 transition-opacity duration-[var(--dx-motion-base)] starting:opacity-0 motion-reduce:transition-none"
        >
          <div className="flex items-center justify-between gap-2 border-b border-[color:var(--di-divider)] px-4 py-3">
            <h2 className="text-sm font-semibold text-[color:var(--di-heading)]">Avisos</h2>
            {n.noLeidas > 0 && n.estado !== "error" && (
              <button type="button" onClick={() => void n.leerTodas()} className={BOTON_TEXTO}>
                Marcar todas como leídas
              </button>
            )}
          </div>
          {cuerpo}
        </div>
      )}
      {montado &&
        persistentes.length > 0 &&
        createPortal(
          <div className="fixed right-4 top-4 z-[60] flex w-[min(24rem,calc(100vw-2rem))] flex-col gap-2">
            {persistentes.map(({ item, texto }) => (
              <NotificacionPersistente
                key={item.id}
                titulo={texto!.titulo}
                mensaje={errorDescarga[item.id] ?? texto!.mensaje}
                accion={{ label: "Descargar", onClick: () => void descargar(item) }}
                onCerrar={() => setCerrados((s) => new Set(s).add(item.id))}
              />
            ))}
          </div>,
          document.body,
        )}
    </div>
  );
}

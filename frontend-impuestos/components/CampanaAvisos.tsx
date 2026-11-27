"use client";

import { useCallback, useEffect, useId, useRef, useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { Icono } from "@flit/ui/Icono";
import { RUTAS, apiFetch } from "@/lib/api";
import { rotular } from "@/lib/rotulos";

/** Aviso del producto (plan §5.1, GET /notificaciones). */
export interface AvisoImpuestos {
  id: string;
  tipo: string;
  liquidacionId: string;
  creadaEn: string;
  leida: boolean;
}

/** Títulos de los avisos por tipo (MA-11). Un tipo desconocido se pinta «Sin clasificar», nunca el código. */
const TIPOS_AVISO: Readonly<Record<string, string>> = {
  LOTE_TERMINADO: "Lote terminado",
};

/** Mensaje bajo el título (spec-visual §4.4); el tipo desconocido lleva el genérico. */
const MENSAJES_AVISO: Readonly<Record<string, string>> = {
  LOTE_TERMINADO: "Ya está el resultado de cada placa del lote.",
};
const MENSAJE_SIN_CLASIFICAR = "Abre el lote para ver el detalle.";

type Estado = { tipo: "cargando" } | { tipo: "error" } | { tipo: "listo"; items: AvisoImpuestos[] };

const FOCO = "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)]";
const BOTON_TEXTO = `min-h-9 rounded-full px-3 text-sm font-semibold text-[color:var(--di-action)] hover:bg-[color:var(--di-accent-soft)] ${FOCO}`;
const FECHA = new Intl.DateTimeFormat("es-CO", { dateStyle: "medium", timeStyle: "short", timeZone: "America/Bogota" });

const avisosSinLeer = (n: number) => (n === 1 ? "1 aviso sin leer" : `${n} avisos sin leer`);
const fecha = (iso: string) => {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "" : FECHA.format(d);
};

/**
 * Campana de avisos de Impuestos (plan §6.1, AC-FE-29): se monta en la barra por `headerActions`. Lee los avisos al abrir
 * el producto y otra vez al cambiar de ruta, sin sondeo. Botón con el número de no leídas (oculto en 0), panel con
 * cargando, error con Reintentar, vacío y la lista con «Ver lote», que lleva al detalle y marca el aviso como leído.
 * Escape o un clic fuera cierran el panel; Escape devuelve el foco al botón.
 */
export function CampanaAvisos() {
  const pathname = usePathname() ?? "/";
  const id = useId();
  const [estado, setEstado] = useState<Estado>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);
  const [abierto, setAbierto] = useState(false);
  const botonRef = useRef<HTMLButtonElement>(null);
  const contenedorRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const controller = new AbortController();
    apiFetch<{ items?: AvisoImpuestos[] }>(RUTAS.notificaciones, { signal: controller.signal }).then(
      (r) => {
        if (!controller.signal.aborted) setEstado({ tipo: "listo", items: r?.items ?? [] });
      },
      () => {
        if (!controller.signal.aborted) setEstado({ tipo: "error" });
      },
    );
    return () => controller.abort();
  }, [pathname, intento]);

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

  const marcarLeida = useCallback((aviso: AvisoImpuestos) => {
    setAbierto(false);
    if (aviso.leida) return;
    setEstado((e) => (e.tipo === "listo" ? { ...e, items: e.items.map((i) => (i.id === aviso.id ? { ...i, leida: true } : i)) } : e));
    // Si falla, el aviso vuelve como no leído en la siguiente lectura; no interrumpe la navegación.
    apiFetch(RUTAS.notificacionLeida(aviso.id), { method: "POST" }).catch(() => {});
  }, []);

  const items = estado.tipo === "listo" ? estado.items : [];
  const noLeidas = items.filter((i) => !i.leida).length;

  let cuerpo: React.ReactNode;
  if (estado.tipo === "error") {
    cuerpo = (
      <div className="flex flex-col items-start gap-2 p-4">
        <p className="text-sm font-semibold text-[color:var(--di-danger)]">No se pudieron cargar los avisos</p>
        <p className="text-sm text-[color:var(--di-text-secondary)]">El resto de Impuestos sigue funcionando.</p>
        <button
          type="button"
          onClick={() => {
            setEstado({ tipo: "cargando" });
            setIntento((n) => n + 1);
          }}
          className={BOTON_TEXTO}
        >
          Reintentar
        </button>
      </div>
    );
  } else if (estado.tipo === "cargando") {
    cuerpo = (
      <p aria-busy="true" className="p-4 text-sm text-[color:var(--di-text-secondary)]">
        Cargando avisos…
      </p>
    );
  } else if (items.length === 0) {
    cuerpo = <p className="p-4 text-sm text-[color:var(--di-text-secondary)]">No tienes avisos</p>;
  } else {
    cuerpo = (
      <ul className="flex max-h-96 flex-col overflow-y-auto">
        {items.map((aviso) => {
          const titulo = rotular({ contexto: "tipo de aviso", codigo: aviso.tipo, respaldo: TIPOS_AVISO });
          const mensaje = Object.prototype.hasOwnProperty.call(MENSAJES_AVISO, aviso.tipo) ? MENSAJES_AVISO[aviso.tipo] : MENSAJE_SIN_CLASIFICAR;
          const cuando = fecha(aviso.creadaEn);
          return (
            <li key={aviso.id} className="flex gap-3 border-t border-[color:var(--di-divider)] px-4 py-3 first:border-t-0">
              <span aria-hidden="true" className={`mt-1.5 size-2 shrink-0 rounded-full ${aviso.leida ? "bg-transparent" : "bg-[color:var(--di-action)]"}`} />
              <div className="flex min-w-0 flex-1 flex-col gap-1">
                <p className="text-sm font-semibold text-[color:var(--di-text-primary)]">
                  {titulo}
                  {!aviso.leida && <span className="sr-only"> (sin leer)</span>}
                </p>
                <p className="text-sm text-[color:var(--di-text-secondary)]">{mensaje}</p>
                <time dateTime={aviso.creadaEn} className="text-xs tabular-nums text-[color:var(--di-text-tertiary)]">
                  {cuando}
                </time>
                <div>
                  <Link href={`/liquidaciones/${encodeURIComponent(aviso.liquidacionId)}`} onClick={() => marcarLeida(aviso)} className={BOTON_TEXTO}>
                    Ver lote
                    <span className="sr-only">{`: ${titulo}${cuando ? ` (${cuando})` : ""}`}</span>
                  </Link>
                </div>
              </div>
            </li>
          );
        })}
      </ul>
    );
  }

  return (
    // `suite-ancla` + `suite-panel-barra` de @flit/shell anclan el panel al botón y, en pantallas angostas, a la barra.
    <div ref={contenedorRef} className="suite-ancla">
      <button
        ref={botonRef}
        type="button"
        aria-label={noLeidas > 0 ? `Avisos, ${avisosSinLeer(noLeidas)}` : "Avisos"}
        aria-expanded={abierto}
        aria-controls={abierto ? `${id}-panel` : undefined}
        onClick={() => setAbierto((a) => !a)}
        onKeyDown={(e) => {
          if (e.key === "Escape" && abierto) {
            e.preventDefault();
            cerrarPanel();
          }
        }}
        className={`relative inline-flex size-10 items-center justify-center rounded-full text-[color:var(--di-text-primary)] hover:bg-[color:var(--di-accent-soft)] ${FOCO}`}
      >
        <Icono nombre="campana" size={20} />
        {noLeidas > 0 && (
          <span
            aria-hidden="true"
            className="absolute -right-0.5 -top-0.5 inline-flex min-w-5 items-center justify-center rounded-full bg-[color:var(--kit-rojo)] px-1 text-[11px] font-semibold tabular-nums text-white"
          >
            {noLeidas > 99 ? "99+" : noLeidas}
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
          className="suite-panel-barra rounded-[var(--di-radius-modal)] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] shadow-[var(--kit-sombra)] transition-opacity duration-[var(--di-motion-base)] motion-reduce:transition-none"
        >
          <div className="border-b border-[color:var(--di-divider)] px-4 py-3">
            <h2 className="text-sm font-semibold text-[color:var(--di-heading)]">Avisos</h2>
          </div>
          {cuerpo}
        </div>
      )}
    </div>
  );
}

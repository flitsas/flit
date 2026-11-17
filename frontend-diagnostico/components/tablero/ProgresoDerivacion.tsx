"use client";

import { useEffect, useId, useRef, useState } from "react";
import { RotateCcw, X } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { AYUDA_CLS, BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS, FOCO_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import {
  mensajeMasivo,
  obtenerProgreso,
  reintentarFallidas,
  rotuloMotivo,
  type ProgresoOperacion,
} from "@/lib/tablero/derivacion";

/** Sondeo del progreso (diseño §6): cada 3 s mientras está en curso; si no cambia, la espera se duplica hasta 10 s. */
export const SONDEO_PROGRESO_MS = 3000;
export const SONDEO_PROGRESO_MAX_MS = 10000;

/** Placa que no entró en la operación al encolar (no elegible en ese momento), con su motivo. */
export interface PlacaNoEncolada {
  placa: string;
  motivoCodigo: string | null;
}

export interface ProgresoDerivacionProps {
  diagnosticoId: string;
  /** Progreso conocido al abrir el panel: el del encolado o el de la operación activa recuperada al volver a la pantalla. */
  inicial: ProgresoOperacion;
  /** Leer el progreso de inmediato (tras encolar) o esperar el primer intervalo (operación recuperada, que ya viene leída). */
  leerAlInicio: boolean;
  noEncoladas?: PlacaNoEncolada[];
  /** La operación terminó: quien pinta la grilla la vuelve a leer para mostrar «Ver trámite #N». */
  onTerminada: (progreso: ProgresoOperacion) => void;
  onCerrar: () => void;
}

const firma = (p: ProgresoOperacion) => `${p.estado}|${p.creadas}|${p.omitidas}|${p.fallidas}|${p.enCurso}|${p.total}`;
const procesadas = (p: ProgresoOperacion) => p.creadas + p.omitidas + p.fallidas;

type Ciclo = { n: number; inmediato: boolean };

/**
 * Progreso de «Crear trámites (N)» (Punto 4, decisión 4.5; diseño §6). Barra y contadores (procesadas de total, creadas,
 * omitidas, fallidas y en curso) en una región `status`; el sondeo solo lee Diagnóstico (contadores propios) y alarga la espera
 * si nada cambia. Al terminar, el resumen: omitidas con su motivo de negocio (incluye las que no se encolaron) y fallidas con
 * «Reintentar fallidas», que las reencola y vuelve a sondear. El usuario puede salir de la pantalla: al volver, el tablero
 * recupera la operación activa del lote. Un fallo del sondeo lo detiene con Reintentar, sin perder lo ya leído.
 */
export function ProgresoDerivacion({ diagnosticoId, inicial, leerAlInicio, noEncoladas = [], onTerminada, onCerrar }: ProgresoDerivacionProps) {
  const tituloId = useId();
  const [progreso, setProgreso] = useState(inicial);
  const [errorSondeo, setErrorSondeo] = useState<string | null>(null);
  const [errorReintento, setErrorReintento] = useState<string | null>(null);
  const [reintentando, setReintentando] = useState(false);
  const [ciclo, setCiclo] = useState<Ciclo>({ n: 0, inmediato: leerAlInicio });
  const progresoRef = useRef(inicial);
  const onTerminadaRef = useRef(onTerminada);
  useEffect(() => {
    onTerminadaRef.current = onTerminada;
  });
  const operacionId = inicial.id;

  useEffect(() => {
    if (progresoRef.current.terminada && !ciclo.inmediato) return;
    let vivo = true;
    let temporizador: ReturnType<typeof setTimeout> | undefined;
    const controller = new AbortController();
    let espera = SONDEO_PROGRESO_MS;
    let anterior: string | null = null;

    async function leer() {
      try {
        const p = await obtenerProgreso(diagnosticoId, operacionId, controller.signal);
        if (!vivo) return;
        progresoRef.current = p;
        setProgreso(p);
        setErrorSondeo(null);
        if (p.terminada) {
          onTerminadaRef.current(p);
          return;
        }
        const actual = firma(p);
        espera = actual === anterior ? Math.min(espera * 2, SONDEO_PROGRESO_MAX_MS) : SONDEO_PROGRESO_MS;
        anterior = actual;
        temporizador = setTimeout(() => void leer(), espera);
      } catch (e) {
        if (!vivo || controller.signal.aborted) return;
        setErrorSondeo(e instanceof Error && e.message ? e.message : "No se pudo leer el avance.");
      }
    }

    if (ciclo.inmediato) void leer();
    else temporizador = setTimeout(() => void leer(), SONDEO_PROGRESO_MS);
    return () => {
      vivo = false;
      controller.abort();
      clearTimeout(temporizador);
    };
  }, [diagnosticoId, operacionId, ciclo]);

  async function reintentar() {
    if (reintentando) return;
    setReintentando(true);
    setErrorReintento(null);
    try {
      const reencolados = await reintentarFallidas(diagnosticoId, operacionId);
      const p = progresoRef.current;
      const siguiente: ProgresoOperacion = {
        ...p,
        estado: "EN_CURSO",
        terminada: false,
        fallidas: Math.max(0, p.fallidas - reencolados),
        enCurso: p.enCurso + reencolados,
        items: p.items.filter((i) => i.estado !== "FALLIDO"),
      };
      progresoRef.current = siguiente;
      setProgreso(siguiente);
      setCiclo((c) => ({ n: c.n + 1, inmediato: true }));
    } catch (e) {
      setErrorReintento(mensajeMasivo(e));
    } finally {
      setReintentando(false);
    }
  }

  const hechas = procesadas(progreso);
  const porcentaje = progreso.total > 0 ? Math.round((hechas / progreso.total) * 100) : 100;
  const omitidas = [
    ...progreso.items.filter((i) => i.estado === "OMITIDO").map((i) => ({ placa: i.placa ?? "", texto: rotuloMotivo(i.motivoCodigo, i.motivo) })),
    ...noEncoladas.map((n) => ({ placa: n.placa, texto: rotuloMotivo(n.motivoCodigo) })),
  ];
  const fallidas = progreso.items.filter((i) => i.estado === "FALLIDO").map((i) => ({ placa: i.placa ?? "", texto: rotuloMotivo(i.motivoCodigo, i.motivo) }));

  return (
    <section aria-labelledby={tituloId} className={`${TARJETA_CLS} flex flex-col gap-3`}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id={tituloId} className="text-base font-semibold text-flit-primary">
          Creación de trámites
        </h2>
        {progreso.terminada && (
          <button type="button" onClick={onCerrar} className={BOTON_SECUNDARIO_CLS}>
            <X className="h-4 w-4" aria-hidden="true" />
            Cerrar resumen
          </button>
        )}
      </div>

      <div role="status" aria-live="polite" className="flex flex-col gap-2">
        {/* HU #14562 AC32: el avance «Creando trámites» se ve hasta que la operación termina. */}
        {!progreso.terminada && <p className="text-sm font-semibold text-flit-primary">Creando trámites…</p>}
        <p className="text-sm font-medium text-flit-primary">
          {`${hechas} de ${progreso.total} procesadas · ${progreso.creadas} creadas · ${progreso.omitidas + noEncoladas.length} omitidas · ${progreso.fallidas} fallidas · ${progreso.enCurso} en curso`}
        </p>
        {!progreso.terminada && <p className={AYUDA_CLS}>Puedes salir de esta pantalla: la creación sigue y al volver verás el avance.</p>}
      </div>
      <div
        role="progressbar"
        aria-label="Avance de la creación de trámites"
        aria-valuemin={0}
        aria-valuemax={progreso.total}
        aria-valuenow={hechas}
        className="h-2 w-full overflow-hidden rounded-full bg-secondary"
      >
        <div className="h-full rounded-full transition-[width]" style={{ width: `${porcentaje}%`, background: "var(--di-gradient-action)" }} />
      </div>

      {errorSondeo && (
        <InlineAlert
          tone="warning"
          title="Se detuvo la actualización del avance"
          action={
            <button type="button" onClick={() => setCiclo((c) => ({ n: c.n + 1, inmediato: true }))} className={BOTON_REINTENTAR_CLS}>
              Reintentar
            </button>
          }
        >
          {errorSondeo}
        </InlineAlert>
      )}

      {progreso.terminada && (
        <div className="flex flex-col gap-3">
          <h3 className="text-sm font-semibold text-flit-primary">Resumen de la creación de trámites</h3>
          {omitidas.length > 0 && (
            <div className="flex flex-col gap-1">
              <h4 className="text-xs font-semibold text-flit-primary">{`Omitidas (${omitidas.length})`}</h4>
              <ul aria-label="Placas omitidas" className="flex flex-col gap-0.5 text-xs text-muted-foreground">
                {omitidas.map((o, i) => (
                  <li key={`${o.placa}-${i}`}>{`${o.placa} — ${o.texto}`}</li>
                ))}
              </ul>
            </div>
          )}
          {fallidas.length > 0 && (
            <div className="flex flex-col gap-1">
              <h4 className="text-xs font-semibold text-flit-primary">{`Fallidas (${fallidas.length})`}</h4>
              <ul aria-label="Placas fallidas" className="flex flex-col gap-0.5 text-xs text-muted-foreground">
                {fallidas.map((o, i) => (
                  <li key={`${o.placa}-${i}`}>{`${o.placa} — ${o.texto}`}</li>
                ))}
              </ul>
            </div>
          )}
          {progreso.fallidas > 0 && (
            <div>
              <button
                type="button"
                onClick={() => void reintentar()}
                disabled={reintentando}
                aria-busy={reintentando}
                className={`inline-flex items-center gap-1.5 ${BOTON_REINTENTAR_CLS} ${FOCO_CLS}`}
              >
                <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
                {`Reintentar fallidas (${progreso.fallidas})`}
              </button>
            </div>
          )}
          {errorReintento && (
            <p role="alert" className="text-xs font-semibold" style={{ color: "var(--badge-danger-fg)" }}>
              {errorReintento}
            </p>
          )}
        </div>
      )}
    </section>
  );
}

"use client";

// Vista inicial de una sesión de organismo de tránsito (Feature #11939 / HU #11940, #11941).
//
// Existe porque `Dashboard` —el inicio del gestor— le pregunta a `/analytics/*` y a
// `/tramites/biometric-validations`, y las tres llamadas filtran por el tenant de quien llama. Un
// trámite vive en el tenant de la EMPRESA CLIENTE; el usuario del OT vive en el tenant del
// ORGANISMO. Las respuestas volvían 200 y vacías, así que el organismo veía cuatro ceros: no era un
// conteo mal hecho, era la fuente de datos equivocada. Ese aislamiento es el mismo que ya obligó a
// darle al OT su propia puerta en el detalle del trámite (Feature #11928), y es deseable.
//
// El encuadre también cambia, y es la mitad del arreglo: «Matrículas / Traspasos / Completados»
// miden la producción de una empresa, y «Validaciones Biométricas» es un paso que ejecuta el gestor
// al radicar. Aquí se responde otra pregunta, la de la primera mirada del día: qué hay en mi cola y
// qué se está envejeciendo. La consola de Reportes sigue respondiendo «¿cómo vamos?»; esta pantalla
// responde «¿qué tengo que hacer ahora?».
//
// De ahí sale la ausencia de filtro de fechas: la cola describe el AHORA, y un rango no movería un
// solo número. Es la misma conclusión —y por el mismo motivo— que ya documenta `OtNowTab`.

import { useCallback, useEffect, useState } from "react";
import { AlertTriangle, CheckCircle2, Clock, Inbox, Timer } from "lucide-react";
import { fetchOtProfile } from "@/lib/api/admin-ot";
import {
  fetchOtDrilldown,
  fetchOtOperationalPanel,
  OT_DRILLDOWN_BUCKETS,
  type OtDrilldownBucket,
  type OtMetricsParams,
  type OtOperationalPanel,
} from "@/lib/api/ot-metrics";
import { resolveOtTransitOfficeId } from "@/components/admin/transit-offices/ot-nav";
import {
  DrilldownPanel,
  type DrilldownState,
} from "@/components/admin/transit-offices/_reportes/DrilldownPanel";
import { defaultRange } from "@/components/admin/transit-offices/_reportes/filters";
import { formatHours } from "@/components/admin/transit-offices/_reportes/report-columns";

/**
 * Ventana de la mediana de decisión. Fija y declarada en la propia tarjeta: el usuario no la eligió,
 * así que no puede deducirla. Coincide con el rango que se manda al endpoint (`defaultRange`).
 */
const VENTANA_MEDIANA_DIAS = 30;

type Estado = "cargando" | "listo" | "error";

/** Abre el detalle de un bloque del panel. `null` en los indicadores que no son navegables. */
type AbrirBloque = (bucket: OtDrilldownBucket, label: string) => void;

export function OtDashboard() {
  const [panel, setPanel] = useState<OtOperationalPanel | null>(null);
  const [params, setParams] = useState<OtMetricsParams | null>(null);
  const [estado, setEstado] = useState<Estado>("cargando");
  const [mensajeError, setMensajeError] = useState<string | null>(null);
  const [intento, setIntento] = useState(0);
  const [drilldown, setDrilldown] = useState<DrilldownState | null>(null);

  useEffect(() => {
    const controller = new AbortController();

    async function cargar() {
      setEstado("cargando");
      try {
        // El id del organismo sale del perfil, con la caché de sesión que ya usa el dock.
        const transitOfficeId = await resolveOtTransitOfficeId(async () => {
          const perfil = await fetchOtProfile(controller.signal);
          return perfil.transitOfficeId;
        });
        // El rango solo gobierna la mediana; la cola y la antigüedad describen este momento.
        const consulta: OtMetricsParams = { ...defaultRange(), transitOfficeId };
        const datos = await fetchOtOperationalPanel(consulta);
        if (controller.signal.aborted) return;
        setParams(consulta);
        setPanel(datos);
        setEstado("listo");
      } catch (err) {
        if (controller.signal.aborted || (err as Error)?.name === "AbortError") return;
        setMensajeError(
          err instanceof Error && err.message
            ? err.message
            : "No se pudo cargar el estado de la cola.",
        );
        setEstado("error");
      }
    }

    void cargar();
    return () => controller.abort();
  }, [intento]);

  const reintentar = useCallback(() => setIntento((n) => n + 1), []);

  // El detalle se pide con los MISMOS parámetros del panel, para que la lista nunca contradiga a la
  // tarjeta que la abrió: es el backend quien recalcula el bloque con idénticos predicados.
  const abrirBloque = useCallback<AbrirBloque>(
    (bucket, label) => {
      if (!params) return;
      setDrilldown({ bucket, label, loading: true, error: null, data: null });
      fetchOtDrilldown(params, bucket)
        .then((data) => setDrilldown({ bucket, label, loading: false, error: null, data }))
        .catch((e: unknown) =>
          setDrilldown({
            bucket,
            label,
            loading: false,
            error: e instanceof Error ? e.message : "No se pudo cargar el detalle.",
            data: null,
          }),
        );
    },
    [params],
  );

  return (
    <div
      className="app-bg flex min-h-screen flex-col gap-4 px-6 pb-10 pt-6 text-[#162744] dark:text-white"
      data-testid="ot-inicio"
    >
      <header>
        <h1 className="text-2xl font-bold leading-tight md:text-3xl">Tu cola de trabajo</h1>
        <p className="mt-1 text-sm text-[#6B7280] dark:text-white/50">
          Estado de este momento y movimiento del día calendario de Bogotá.
        </p>
      </header>

      {estado === "error" ? (
        <ErrorPanel message={mensajeError} onRetry={reintentar} />
      ) : (
        <PanelOperativo panel={estado === "listo" ? panel : null} onAbrir={abrirBloque} />
      )}

      <DrilldownPanel
        state={drilldown}
        transitOfficeId={params?.transitOfficeId ?? ""}
        onClose={() => setDrilldown(null)}
      />
    </div>
  );
}

/**
 * Un fallo NO se pinta como ceros. Ese es exactamente el defecto que tenía la pantalla anterior:
 * una cola con problemas se leía como una cola sana.
 */
function ErrorPanel({ message, onRetry }: { message: string | null; onRetry: () => void }) {
  return (
    <div
      role="alert"
      className="flex flex-col items-start gap-3 rounded-2xl border border-[#FF4E00]/40 bg-[#FF4E00]/[0.07] px-5 py-4"
    >
      <p className="flex items-center gap-2 text-sm font-semibold text-[#162744] dark:text-white">
        <AlertTriangle className="h-4 w-4 shrink-0 text-[#FF4E00]" aria-hidden="true" />
        No se pudo cargar el estado de la cola
      </p>
      <p className="text-xs text-[#6B7280] dark:text-white/60">
        {message ?? "Intenta de nuevo en un momento."}
      </p>
      <button
        type="button"
        onClick={onRetry}
        className="rounded-lg border border-[#DFE5ED] px-3 py-1.5 text-xs font-semibold transition hover:border-[#557EFF] dark:border-white/15"
      >
        Reintentar
      </button>
    </div>
  );
}

function PanelOperativo({
  panel,
  onAbrir,
}: {
  panel: OtOperationalPanel | null;
  onAbrir: AbrirBloque;
}) {
  const cargando = panel === null;
  const movimiento = panel?.movimiento;
  const cola = panel?.cola;
  const antiguedad = panel?.antiguedad;
  const pendientes = movimiento?.pendientesTotal ?? 0;
  const sinPendientes = !cargando && pendientes === 0;

  return (
    <div className="flex flex-col gap-4">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Kpi
          label="Esperan mi decisión"
          value={cola?.porRevisar}
          cargando={cargando}
          color="#557EFF"
          icon={Inbox}
          onAbrir={() => onAbrir(OT_DRILLDOWN_BUCKETS.porRevisar, "Esperan mi decisión")}
        />
        <Kpi
          label="Pendientes en total"
          value={pendientes}
          cargando={cargando}
          color="#F9AC00"
          icon={Clock}
          hint="Incluye lo que espera a un tercero"
          onAbrir={() => onAbrir(OT_DRILLDOWN_BUCKETS.pendientes, "Pendientes en total")}
        />
        <Kpi
          label="Entregados hoy"
          value={movimiento?.entregadosHoy}
          cargando={cargando}
          color="#8CC63F"
          icon={CheckCircle2}
          onAbrir={() => onAbrir(OT_DRILLDOWN_BUCKETS.entregadosHoy, "Entregados hoy")}
        />
        <Kpi
          label="Tiempo mediano de decisión"
          // Se formatea en vez de interpolar el número crudo: una mediana de dos minutos salía como
          // «0.03 h», con punto decimal inglés y en una unidad donde el dato no significa nada.
          value={cargando ? undefined : formatHours(movimiento?.tiempoMedianoDecisionHoras)}
          cargando={cargando}
          color="#00DBD5"
          icon={Timer}
          hint={`Últimos ${VENTANA_MEDIANA_DIAS} días`}
          // Una mediana no es un conjunto de trámites: no hay lista que abrir detrás.
        />
      </div>

      {sinPendientes ? (
        <Tarjeta>
          <p className="py-6 text-center text-sm text-[#6B7280] dark:text-white/50">
            No hay trámites pendientes en este momento.
          </p>
        </Tarjeta>
      ) : (
        <div className="grid grid-cols-1 gap-3 lg:grid-cols-2">
          <Tarjeta titulo="En qué está esperando cada trámite">
            {cargando ? (
              <Esqueleto filas={3} />
            ) : (
              <div className="flex flex-col gap-3">
                <Espera
                  label="Por revisar"
                  value={cola?.porRevisar ?? 0}
                  total={pendientes}
                  miTurno
                  onAbrir={() => onAbrir(OT_DRILLDOWN_BUCKETS.porRevisar, "Por revisar")}
                />
                <Espera
                  label="Esperando asignar placa"
                  value={cola?.esperandoAsignarPlaca ?? 0}
                  total={pendientes}
                  onAbrir={() =>
                    onAbrir(OT_DRILLDOWN_BUCKETS.esperandoPlaca, "Esperando asignar placa")
                  }
                />
                <Espera
                  label="En espera del cliente"
                  value={cola?.enEsperaDelCliente ?? 0}
                  total={pendientes}
                  onAbrir={() =>
                    onAbrir(OT_DRILLDOWN_BUCKETS.enEsperaDelCliente, "En espera del cliente")
                  }
                />
                <p className="text-[11px] text-[#9AA5B4] dark:text-white/40">
                  Solo «Por revisar» espera una acción del organismo.
                </p>
              </div>
            )}
          </Tarjeta>

          <Tarjeta titulo="Antigüedad de lo pendiente">
            {cargando ? (
              <Esqueleto filas={1} />
            ) : (
              <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                <Tramo
                  label="0–1 día"
                  value={antiguedad?.hasta1Dia ?? 0}
                  onAbrir={() => onAbrir(OT_DRILLDOWN_BUCKETS.hasta1Dia, "Pendientes de 0–1 día")}
                />
                <Tramo
                  label="2–3 días"
                  value={antiguedad?.entre2y3Dias ?? 0}
                  onAbrir={() => onAbrir(OT_DRILLDOWN_BUCKETS.entre2y3Dias, "Pendientes de 2–3 días")}
                />
                <Tramo
                  label="4–7 días"
                  value={antiguedad?.entre4y7Dias ?? 0}
                  onAbrir={() => onAbrir(OT_DRILLDOWN_BUCKETS.entre4y7Dias, "Pendientes de 4–7 días")}
                />
                <Tramo
                  label="Más de 7 días"
                  value={antiguedad?.masDe7Dias ?? 0}
                  alarma
                  onAbrir={() =>
                    onAbrir(OT_DRILLDOWN_BUCKETS.masDe7Dias, "Pendientes de más de 7 días")
                  }
                />
              </div>
            )}
          </Tarjeta>
        </div>
      )}
    </div>
  );
}

// ── Piezas ────────────────────────────────────────────────────────────────────

function Tarjeta({ titulo, children }: { titulo?: string; children: React.ReactNode }) {
  return (
    <section className="rounded-2xl border border-[#DFE5ED] bg-white p-5 dark:border-white/10 dark:bg-[#0B0F14]">
      {titulo && (
        <h2 className="mb-4 text-[11px] font-semibold uppercase tracking-wide text-[#6B7280] dark:text-white/50">
          {titulo}
        </h2>
      )}
      {children}
    </section>
  );
}

/**
 * Envoltorio de un indicador navegable. Un bloque en cero NO se ofrece como enlace: llevaría a una
 * lista vacía, que es una promesa incumplida, y enseña a desconfiar del resto de los números.
 */
function Navegable({
  navegable,
  etiqueta,
  className,
  onAbrir,
  children,
}: {
  navegable: boolean;
  etiqueta: string;
  className: string;
  onAbrir?: () => void;
  children: React.ReactNode;
}) {
  if (!navegable || !onAbrir) {
    return <div className={className}>{children}</div>;
  }
  return (
    <button
      type="button"
      onClick={onAbrir}
      // El nombre accesible dice a dónde lleva: el texto visible es una etiqueta y un número, que
      // por sí solos no anuncian que haya una lista detrás.
      aria-label={etiqueta}
      className={`${className} text-left transition hover:border-[#557EFF] hover:shadow-sm focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]`}
    >
      {children}
    </button>
  );
}

function Kpi({
  label,
  value,
  cargando,
  color,
  icon: Icon,
  hint,
  onAbrir,
}: {
  label: string;
  value: number | string | undefined;
  cargando: boolean;
  color: string;
  icon: typeof Inbox;
  hint?: string;
  onAbrir?: () => void;
}) {
  const numero = typeof value === "number" ? value : null;
  return (
    <Navegable
      navegable={!cargando && numero !== null && numero > 0}
      etiqueta={`Ver los ${numero} trámites: ${label.toLowerCase()}`}
      onAbrir={onAbrir}
      className="flex w-full items-center justify-between rounded-2xl border border-[#DFE5ED] bg-white p-4 dark:border-white/10 dark:bg-[#0B0F14]"
    >
      <div className="min-w-0">
        <p className="text-[11px] font-medium opacity-70">{label}</p>
        <p className="mt-1 text-3xl font-bold tabular-nums" style={{ color }}>
          {cargando ? "—" : (value ?? 0)}
        </p>
        {hint && <p className="mt-0.5 text-[10px] text-[#9AA5B4] dark:text-white/35">{hint}</p>}
      </div>
      <div
        className="grid h-11 w-11 shrink-0 place-items-center rounded-xl"
        style={{ background: `${color}1A` }}
      >
        <Icon className="h-5 w-5" style={{ color }} aria-hidden="true" />
      </div>
    </Navegable>
  );
}

/**
 * Una fila del desglose de la cola. `miTurno` distingue lo que espera al organismo de lo que espera
 * a un tercero: sin esa separación el OT lee como deuda propia un atraso que no lo es.
 */
function Espera({
  label,
  value,
  total,
  miTurno,
  onAbrir,
}: {
  label: string;
  value: number;
  total: number;
  miTurno?: boolean;
  onAbrir?: () => void;
}) {
  const pct = total === 0 ? 0 : Math.min(100, Math.round((value / total) * 100));
  return (
    <Navegable
      navegable={value > 0}
      etiqueta={`Ver los ${value} trámites: ${label.toLowerCase()}`}
      onAbrir={onAbrir}
      className="grid grid-cols-[minmax(7rem,11rem)_1fr_auto] items-center gap-3 rounded-lg text-xs"
    >
      <span className="truncate" title={label}>
        {label}
        {miTurno && <span className="sr-only"> (espera una acción del organismo)</span>}
      </span>
      <span className="h-2 overflow-hidden rounded bg-[#EEF1F5] dark:bg-white/10">
        <span
          className="block h-full rounded"
          style={{
            width: `${pct}%`,
            background: miTurno ? "linear-gradient(135deg,#557EFF,#00DBD5)" : "#C9D2E0",
          }}
        />
      </span>
      <span className="font-semibold tabular-nums">{value}</span>
    </Navegable>
  );
}

/**
 * Tramo de antigüedad. La alarma se enciende solo con contenido: un bloque resaltado siempre en cero
 * enseña a ignorar el resaltado justo cuando sí importa.
 */
function Tramo({
  label,
  value,
  alarma,
  onAbrir,
}: {
  label: string;
  value: number;
  alarma?: boolean;
  onAbrir?: () => void;
}) {
  const encendida = Boolean(alarma) && value > 0;
  return (
    <Navegable
      navegable={value > 0}
      etiqueta={`Ver los ${value} trámites pendientes de ${label.toLowerCase()}`}
      onAbrir={onAbrir}
      className={`w-full rounded-xl border px-3 py-3 text-center ${
        encendida
          ? "border-[#FF4E00]/45 bg-[#FF4E00]/[0.06]"
          : "border-[#DFE5ED] dark:border-white/10"
      }`}
    >
      <p className={`text-2xl font-bold tabular-nums ${encendida ? "text-[#FF4E00]" : ""}`}>
        {value}
      </p>
      <p className="mt-0.5 text-[10px] text-[#6B7280] dark:text-white/50">{label}</p>
    </Navegable>
  );
}

function Esqueleto({ filas }: { filas: number }) {
  return (
    <div className="flex flex-col gap-3" aria-hidden="true">
      {Array.from({ length: filas }, (_, i) => (
        <div key={i} className="h-8 animate-pulse rounded-lg bg-[#EEF1F5] dark:bg-white/5" />
      ))}
    </div>
  );
}

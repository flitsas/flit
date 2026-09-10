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

import { useCallback, useEffect, useMemo, useState } from "react";
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import {
  AlertTriangle,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  Clock,
  ExternalLink,
  Inbox,
  Timer,
} from "lucide-react";
import { fetchOtProfile } from "@/lib/api/admin-ot";
import { fetchTransitOffices } from "@/lib/api/admin-companies";
import {
  fetchOtDrilldown,
  fetchOtOperationalPanel,
  fetchOtReport,
  OT_DRILLDOWN_BUCKETS,
  type OtDrilldownBucket,
  type OtMetricsParams,
  type OtOperationalPanel,
  type OtReportSeriesPoint,
  type OtReportSummary,
} from "@/lib/api/ot-metrics";
import { bannerImageUrl, type ActiveBanner } from "@/lib/api/public-banners";
import { useActiveBanners } from "@/hooks/useActiveBanners";
import { bannerAmbientGradient, useDominantColor } from "@/hooks/useDominantColor";
import { resolveOtTransitOfficeId } from "@/components/admin/transit-offices/ot-nav";
import {
  DrilldownPanel,
  type DrilldownState,
} from "@/components/admin/transit-offices/_reportes/DrilldownPanel";
import { defaultRange, lastDaysRange } from "@/components/admin/transit-offices/_reportes/filters";
import { formatHours } from "@/components/admin/transit-offices/_reportes/report-columns";

/**
 * Ventana de la mediana de decisión. Fija y declarada en la propia tarjeta: el usuario no la eligió,
 * así que no puede deducirla. Coincide con el rango que se manda al endpoint (`defaultRange`).
 */
const VENTANA_MEDIANA_DIAS = 30;

/** Ventana de la franja de actividad. Corta a propósito: aquí se mira el ritmo, no la historia. */
const ACTIVIDAD_DIAS = 14;

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
  const [organismo, setOrganismo] = useState<string | null>(null);

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

  // Nombre del organismo para la bienvenida. Va por su cuenta y falla en silencio: si el catálogo no
  // responde, la cabecera se queda sin nombre pero la cola —que es a lo que se viene— se sigue
  // viendo. Mismo origen que usa la cabecera del hub OT.
  useEffect(() => {
    const transitOfficeId = params?.transitOfficeId;
    if (!transitOfficeId) return;
    const controller = new AbortController();
    void fetchTransitOffices(undefined, controller.signal)
      .then((catalogo) => {
        if (controller.signal.aborted) return;
        const oficina = catalogo.find((o) => o.id === transitOfficeId);
        if (oficina) setOrganismo(`${oficina.name} (${oficina.code})`);
      })
      .catch(() => undefined);
    return () => controller.abort();
  }, [params?.transitOfficeId]);

  const reintentar = useCallback(() => setIntento((n) => n + 1), []);

  // Banners Activos globales (HU #12242, AC2): mismo set y mismo hook que ve el gestor.
  const activeBanners = useActiveBanners();

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
      {/* Banner + KPIs: misma grilla que el dashboard del gestor (`Dashboard.tsx`) — banner en
          2/3, KPIs en 2×2 en el 1/3 restante — para que ambos contenedores de banner midan lo
          mismo. Durante error no hay KPIs que mostrar (AC del panel operativo); el banner ocupa
          entonces el ancho completo en vez de dejar la columna vacía. */}
      <div className="grid grid-cols-1 gap-3 shrink-0 md:grid-cols-3">
        <Bienvenida
          organismo={organismo}
          panel={estado === "listo" ? panel : null}
          banners={activeBanners}
          className={estado === "error" ? "md:col-span-3" : "md:col-span-2"}
        />
        {estado !== "error" && (
          <PanelOperativoKpis panel={estado === "listo" ? panel : null} onAbrir={abrirBloque} />
        )}
      </div>

      {estado === "error" ? (
        <ErrorPanel message={mensajeError} onRetry={reintentar} />
      ) : (
        <PanelOperativoResto panel={estado === "listo" ? panel : null} onAbrir={abrirBloque} />
      )}

      <PeriodoReciente transitOfficeId={params?.transitOfficeId} />

      <DrilldownPanel
        state={drilldown}
        transitOfficeId={params?.transitOfficeId ?? ""}
        onClose={() => setDrilldown(null)}
      />
    </div>
  );
}

/** Cada cuánto rota el banner. Mismo ritmo que el del gestor, para que la plataforma se sienta una. */
const ROTACION_MS = 6000;

/** Fondo del carrusel, IGUAL en todos los slides (mensaje fijo y banner) — ver `Bienvenida`. */
const BRAND_GRADIENT = "linear-gradient(120deg,#00dbd5 0%,#557eff 100%)";

type MensajeSlide = {
  kind: "mensaje";
  id: string;
  title: string;
  body: string;
};

type BannerSlide = {
  kind: "banner";
  id: string;
  name: string;
  imageUrl: string;
  linkUrl: string | null;
};

type OrganismoSlide = MensajeSlide | BannerSlide;

/**
 * Slides del banner del organismo.
 *
 * El carrusel del gestor no servía tal cual: anunciaba «validación de identidad con IA ya integrada
 * en TUS trámites» a quien no radica trámites ni valida biometrías. Lo que se conserva es el
 * mecanismo —el sitio donde se pasan mensajes—; lo que cambia es que aquí el único mensaje propio
 * habla de la cola. Detrás van los banners Activos del Administrador (HU #12242, AC2): el mismo
 * set global que ve el gestor. Sin banners activos, el slide fijo queda solo (AC3).
 */
function mensajesDelOrganismo(
  organismo: string | null,
  panel: OtOperationalPanel | null,
  banners: ActiveBanner[],
): OrganismoSlide[] {
  const porRevisar = panel?.cola.porRevisar ?? null;
  // Opción A (dato ya cargado en el mismo panel operativo, sin llamada nueva): la cola no solo
  // dice cuántos esperan, sino cuántos ya llevan estancados y cuál es la mediana de decisión hoy.
  const estancados = panel?.antiguedad.masDe7Dias ?? 0;
  const mediana = panel?.movimiento.tiempoMedianoDecisionHoras ?? null;

  const cola =
    porRevisar === null
      ? "Aquí ves el estado de tu cola en este momento y el movimiento del día."
      : porRevisar === 0
        ? "No tienes trámites esperando decisión en este momento."
        : `Tienes ${porRevisar} ${porRevisar === 1 ? "trámite" : "trámites"} esperando tu decisión` +
          (estancados > 0
            ? `, ${estancados} ${estancados === 1 ? "lleva" : "llevan"} más de 7 días esperando`
            : "") +
          ".";

  const medianaTexto =
    porRevisar !== null && mediana !== null
      ? ` Tu mediana de decisión hoy es de ${formatHours(mediana)}.`
      : "";

  const fijo: MensajeSlide = {
    kind: "mensaje",
    id: "bienvenida",
    title: "Tu cola de trabajo",
    body: `${cola}${medianaTexto} Los datos son del día calendario de Bogotá.`,
  };

  const bannerSlides: BannerSlide[] = banners.map((banner) => ({
    kind: "banner",
    id: banner.id,
    name: banner.name,
    imageUrl: bannerImageUrl(banner.id),
    linkUrl: banner.linkUrl,
  }));

  return [fijo, ...bannerSlides];
}

/**
 * Banner del organismo: el nombre siempre visible y los slides rotando encima.
 *
 * El nombre NO entra en la rotación a propósito: identifica el organismo y desaparecería dos de cada
 * tres veces.
 */
function Bienvenida({
  organismo,
  panel,
  banners,
  className = "",
}: {
  organismo: string | null;
  panel: OtOperationalPanel | null;
  banners: ActiveBanner[];
  className?: string;
}) {
  // Si una imagen de banner falla al cargar (`onError`), ese slide puntual se retira sin romper
  // el resto del carrusel (AC3).
  const [failedBannerIds, setFailedBannerIds] = useState<Set<string>>(new Set());
  const bannersVisibles = useMemo(
    () => banners.filter((banner) => !failedBannerIds.has(banner.id)),
    [banners, failedBannerIds],
  );
  const mensajes = useMemo(
    () => mensajesDelOrganismo(organismo, panel, bannersVisibles),
    [organismo, panel, bannersVisibles],
  );
  const [actual, setActual] = useState(0);

  useEffect(() => {
    const id = setInterval(() => setActual((i) => (i + 1) % mensajes.length), ROTACION_MS);
    return () => clearInterval(id);
  }, [mensajes.length]);

  // Defensivo: si el slide actual desaparece (banner con imagen rota), el índice se reacomoda
  // en el siguiente render; mientras tanto no debe intentar leer un slide inexistente.
  const mensaje = mensajes[actual] ?? mensajes[0];
  const bannerColor = useDominantColor(mensaje.kind === "banner" ? mensaje.imageUrl : undefined);

  return (
    <header
      className={`relative flex flex-col justify-between overflow-hidden rounded-2xl text-white ${className}`}
      style={{ minHeight: "220px" }}
    >
      {/* Capa de fondo: gradiente de marca fijo en el slide de la cola; en un banner, el color
          PROMEDIO de esa misma imagen (así combina con cualquier banner, no solo con el
          azul/turquesa de marca) — es el respaldo que se ve cuando `object-contain` deja margen
          (abajo de `md`, ver siguiente bloque); en `md+` es invisible, cubierto por el banner a
          pantalla completa. */}
      <div
        className="absolute inset-0"
        style={{ background: mensaje.kind === "mensaje" ? BRAND_GRADIENT : bannerAmbientGradient(bannerColor) }}
      />
      {mensaje.kind === "banner" && (
        // Ajuste adaptable, mismo criterio que Spotify/YouTube/Amazon: con espacio de sobra
        // (`md:` en adelante, header a 2/3 de ancho junto a los KPIs) se ajusta completo al
        // contenedor (object-cover, sesgado a la derecha para no cortar el texto); en pantallas
        // angostas (abajo de `md`, el header pasa a ancho completo y el recorte horizontal sería
        // mucho más agresivo) se ve la imagen COMPLETA sin recortar (object-contain).
        <img
          src={mensaje.imageUrl}
          alt={mensaje.name}
          onError={() => setFailedBannerIds((prev) => (prev.has(mensaje.id) ? prev : new Set(prev).add(mensaje.id)))}
          className="absolute inset-0 h-full w-full object-contain object-center md:object-cover md:object-[80%_center]"
        />
      )}
      {/* Velo para que el nombre del organismo y los controles mantengan contraste sobre
          cualquier imagen de banner; sobre el gradiente es imperceptible. */}
      <div
        className="absolute inset-0"
        style={{ background: "linear-gradient(180deg, rgba(0,0,0,0.25) 0%, rgba(0,0,0,0) 45%, rgba(0,0,0,0.35) 100%)" }}
      />
      {mensaje.kind === "mensaje" && (
        <div className="absolute -right-10 -top-10 h-36 w-36 rounded-full bg-white opacity-15" />
      )}

      {/* Sin título visible del banner (solo el banner): el enlace cubre toda la imagen — al
          acercarse, se opaca un poco y aparece el ícono de enlace; clic en cualquier punto abre
          el enlace. El nombre del organismo (fuera de la rotación) sigue viéndose igual. */}
      {mensaje.kind === "banner" && mensaje.linkUrl && (
        <a
          href={mensaje.linkUrl}
          target="_blank"
          rel="noopener noreferrer"
          aria-label={mensaje.name}
          className="group absolute inset-0"
        >
          <span className="absolute inset-0 bg-black/0 transition-colors duration-200 group-hover:bg-black/25" />
          <span className="absolute inset-0 flex items-center justify-center opacity-0 transition-opacity duration-200 group-hover:opacity-100">
            <span className="flex h-10 w-10 items-center justify-center rounded-full bg-white/90 text-[#162744] shadow-lg">
              <ExternalLink className="h-5 w-5" aria-hidden="true" />
            </span>
          </span>
        </a>
      )}

      {/* pointer-events-none: esta caja ocupa TODO el alto del header (flex-1) aunque su contenido
          solo ocupe una franja arriba y otra abajo — sin esto, el hueco vacío del medio tapaba el
          enlace del banner de abajo y ni el hover ni el clic le llegaban nunca. Se restaura
          pointer-events-auto solo en la franja de controles (puntos/flechas), que sí son clicables. */}
      <div className="relative flex flex-1 flex-col justify-between px-6 py-5 pointer-events-none">
        <div className="max-w-[85%]">
          <p className="text-[11px] font-semibold uppercase tracking-wide opacity-80">
            {organismo ?? "Organismo de tránsito"}
          </p>
          {mensaje.kind === "mensaje" ? (
            <>
              <h1 className="mt-1 text-2xl font-bold leading-tight md:text-3xl">{mensaje.title}</h1>
              <p className="mt-1.5 text-sm leading-snug opacity-90">{mensaje.body}</p>
            </>
          ) : (
            <span className="sr-only">{mensaje.name}</span>
          )}
        </div>

        <div className="mt-4 flex items-center justify-between pointer-events-auto">
          <div className="flex gap-1">
            {mensajes.map((m, i) => (
              <button
                key={m.id}
                type="button"
                onClick={() => setActual(i)}
                aria-label={`Mensaje ${i + 1} de ${mensajes.length}`}
                aria-current={i === actual}
                className="h-1.5 rounded-full transition-all"
                style={{
                  width: i === actual ? 16 : 5,
                  background: i === actual ? "#ffffff" : "rgba(255,255,255,0.5)",
                }}
              />
            ))}
          </div>
          <div className="flex items-center gap-1">
            <button
              type="button"
              onClick={() => setActual((i) => (i - 1 + mensajes.length) % mensajes.length)}
              aria-label="Mensaje anterior"
              className="grid h-6 w-6 place-items-center rounded-full bg-white/15 hover:bg-white/25"
            >
              <ChevronLeft className="h-3 w-3" aria-hidden="true" />
            </button>
            <button
              type="button"
              onClick={() => setActual((i) => (i + 1) % mensajes.length)}
              aria-label="Mensaje siguiente"
              className="grid h-6 w-6 place-items-center rounded-full bg-white/15 hover:bg-white/25"
            >
              <ChevronRight className="h-3 w-3" aria-hidden="true" />
            </button>
          </div>
        </div>
      </div>
    </header>
  );
}

/**
 * Carga de un recurso del organismo, con los tres estados que esta pantalla distingue.
 *
 * `cargar` tiene que ser una función estable (de módulo): así entra en las dependencias del efecto
 * sin necesidad de refs, y cada tarjeta conserva su propio ciclo de vida.
 */
function useRecursoOt<T>(
  transitOfficeId: string | undefined,
  cargar: (transitOfficeId: string, signal: AbortSignal) => Promise<T>,
) {
  const [dato, setDato] = useState<T | null>(null);
  const [estado, setEstado] = useState<Estado>("cargando");

  useEffect(() => {
    if (!transitOfficeId) return;
    const controller = new AbortController();
    // No se marca «cargando» aquí: el estado ya nace así, y hacerlo dentro del efecto es
    // exactamente lo que prohíbe `react-hooks/set-state-in-effect`. El id del organismo se resuelve
    // una vez por sesión, así que no hay una segunda carga que anunciar.
    cargar(transitOfficeId, controller.signal)
      .then((resultado) => {
        if (controller.signal.aborted) return;
        setDato(resultado);
        setEstado("listo");
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted || (err as Error)?.name === "AbortError") return;
        setEstado("error");
      });
    return () => controller.abort();
  }, [transitOfficeId, cargar]);

  return { dato, estado };
}

/** El informe pagina filas que aquí no se usan: solo interesa `resumen`, así que se pide la mínima. */
function cargarResumenDelPeriodo(transitOfficeId: string, signal: AbortSignal) {
  return fetchOtReport(
    { ...lastDaysRange(ACTIVIDAD_DIAS), transitOfficeId, page: 1, pageSize: 1 },
    signal,
  ).then((informe) => informe.resumen);
}

/**
 * Lo reciente: el ritmo del periodo y en qué quedó lo que entró.
 *
 * Las dos tarjetas salen de UNA sola llamada —el informe devuelve la serie y el desglose juntos—,
 * y van aparte de la cola: si el informe se cae, la cola, que es a lo que se entra a esta pantalla,
 * se sigue viendo.
 */
function PeriodoReciente({ transitOfficeId }: { transitOfficeId: string | undefined }) {
  const { dato: resumen, estado } = useRecursoOt<OtReportSummary>(
    transitOfficeId,
    cargarResumenDelPeriodo,
  );

  return (
    <div className="grid grid-cols-1 gap-3 lg:grid-cols-2">
      <Actividad estado={estado} serie={resumen?.serie ?? null} />
      <Composicion estado={estado} resumen={resumen} />
    </div>
  );
}

/** Ritmo del periodo: qué entró y qué se decidió cada día. */
function Actividad({ estado, serie }: { estado: Estado; serie: OtReportSeriesPoint[] | null }) {
  const hayMovimiento = (serie ?? []).some(
    (p) => p.radicados > 0 || p.aprobados > 0 || p.rechazados > 0,
  );

  return (
    <Tarjeta titulo={`Actividad de los últimos ${ACTIVIDAD_DIAS} días`}>
      {estado === "cargando" && <Esqueleto filas={1} />}
      {estado === "error" && (
        <p role="alert" className="py-6 text-center text-sm text-[#6B7280] dark:text-white/50">
          No se pudo cargar la actividad reciente.
        </p>
      )}
      {estado === "listo" && !hayMovimiento && (
        // Un gráfico en blanco no distingue «no pasó nada» de «no cargó». Se dice con palabras.
        <p className="py-6 text-center text-sm text-[#6B7280] dark:text-white/50">
          No hubo movimiento en los últimos {ACTIVIDAD_DIAS} días.
        </p>
      )}
      {estado === "listo" && hayMovimiento && (
        <div className="h-56 w-full" data-testid="ot-inicio-actividad">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={serie ?? []} margin={{ top: 4, right: 8, left: -20, bottom: 0 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="#EEF1F5" vertical={false} />
              <XAxis dataKey="label" tick={{ fontSize: 10 }} tickLine={false} axisLine={false} />
              <YAxis tick={{ fontSize: 10 }} tickLine={false} axisLine={false} allowDecimals={false} />
              <Tooltip cursor={{ fill: "rgba(85,126,255,0.06)" }} />
              <Bar dataKey="radicados" name="Radicados" fill="#557EFF" radius={[3, 3, 0, 0]} />
              <Bar dataKey="aprobados" name="Aprobados" fill="#8CC63F" radius={[3, 3, 0, 0]} />
              <Bar dataKey="rechazados" name="Rechazados" fill="#FF4E00" radius={[3, 3, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      )}
    </Tarjeta>
  );
}

/**
 * Agrupación de lo recibido en el periodo, en las tres palabras con las que el organismo habla de un
 * trámite: lo aprobó, lo rechazó, o todavía no lo decidió.
 *
 * El backend expone ocho estados propios del OT y aquí se doblan a cuatro grupos. El detalle de qué
 * entra en cada uno va en el tooltip de la fila, porque la agrupación tiene juicio dentro y quien la
 * lea tiene derecho a verlo:
 *
 * - «Esperando placa» es un expediente YA aprobado al que le falta la placa, así que cuenta como
 *   aprobado. Ponerlo en «sin decisión» diría que el organismo no se ha pronunciado, y sí lo hizo.
 * - «En subsanación» es un rechazo con la subsanación abierta: el verdicto fue en contra aunque el
 *   trámite vaya a volver.
 * - «Esperando al cliente» es el grupo con menos filo: cae ahí tanto un trámite pausado a la espera
 *   de la empresa como uno ya con placa asignada. Se cuenta como sin decisión y el tooltip lo dice.
 *
 * El desglose fino de los ocho estados vive en la consola de Reportes; aquí se busca la lectura de
 * un vistazo.
 */
function composicionDelPeriodo(resumen: OtReportSummary) {
  return [
    {
      id: "aprobados",
      name: "Aprobados",
      color: "#8CC63F",
      value: resumen.aprobados + resumen.esperandoPlaca,
      hint: "Aprobados y los que ya tienen el expediente aprobado a la espera de asignar placa.",
    },
    {
      id: "rechazados",
      name: "Rechazados",
      color: "#FF4E00",
      value: resumen.rechazados + resumen.enSubsanacion,
      hint: "Rechazados, con o sin subsanación abierta.",
    },
    {
      id: "sin-decision",
      name: "Sin decisión",
      color: "#557EFF",
      value: resumen.enRevision + resumen.esperandoCliente + resumen.otros,
      hint: "En revisión y los que esperan algo de la empresa (SOAT, impuestos o trámite pausado).",
    },
    {
      id: "anulados",
      name: "Anulados",
      color: "#94A3B8",
      value: resumen.anulados,
      hint: "La empresa los dio de baja después de radicarlos.",
    },
  ].filter((grupo) => grupo.value > 0);
}

/**
 * En qué quedó lo que entró en el periodo.
 *
 * El universo son los trámites RECIBIDOS en la ventana, no los decididos: es lo que hace que los
 * cuatro grupos cierren contra el total y que el porcentaje del centro signifique algo.
 */
function Composicion({ estado, resumen }: { estado: Estado; resumen: OtReportSummary | null }) {
  const grupos = resumen ? composicionDelPeriodo(resumen) : [];
  const total = grupos.reduce((suma, g) => suma + g.value, 0);
  const aprobados = grupos.find((g) => g.id === "aprobados")?.value ?? 0;
  const pctAprobados = total === 0 ? 0 : (aprobados / total) * 100;

  return (
    <Tarjeta titulo={`En qué quedó lo recibido en ${ACTIVIDAD_DIAS} días`}>
      {estado === "cargando" && <Esqueleto filas={1} />}
      {estado === "error" && (
        <p role="alert" className="py-6 text-center text-sm text-[#6B7280] dark:text-white/50">
          No se pudo cargar la composición del periodo.
        </p>
      )}
      {estado === "listo" && total === 0 && (
        <p className="py-6 text-center text-sm text-[#6B7280] dark:text-white/50">
          No se recibió ningún trámite en los últimos {ACTIVIDAD_DIAS} días.
        </p>
      )}
      {estado === "listo" && total > 0 && (
        <div
          className="flex flex-col items-center gap-4 sm:flex-row"
          data-testid="ot-inicio-composicion"
        >
          <div className="relative h-40 w-40 shrink-0">
            <ResponsiveContainer width="100%" height="100%">
              <PieChart>
                <Pie
                  data={grupos}
                  dataKey="value"
                  nameKey="name"
                  innerRadius="62%"
                  outerRadius="92%"
                  paddingAngle={1}
                  stroke="none"
                  isAnimationActive={false}
                >
                  {grupos.map((g) => (
                    <Cell key={g.id} fill={g.color} />
                  ))}
                </Pie>
                <Tooltip />
              </PieChart>
            </ResponsiveContainer>
            {/* La cifra del centro es la que resume la tarjeta; va fuera del SVG para poder
                componerla con dos tamaños sin pelear con el posicionamiento de recharts. */}
            <div className="pointer-events-none absolute inset-0 grid place-items-center text-center">
              <div>
                <p className="text-xl font-bold tabular-nums">{formatPct(pctAprobados)}</p>
                <p className="text-[9px] font-semibold uppercase tracking-wide text-[#9AA5B4]">
                  Aprobados
                </p>
              </div>
            </div>
          </div>

          <ul className="flex w-full min-w-0 flex-1 flex-col gap-1.5">
            {grupos.map((g) => (
              <li
                key={g.id}
                title={g.hint}
                className="flex items-center justify-between gap-3 text-xs"
              >
                <span className="flex min-w-0 items-center gap-2">
                  <span
                    className="h-2.5 w-2.5 shrink-0 rounded-full"
                    style={{ background: g.color }}
                    aria-hidden="true"
                  />
                  <span className="truncate">{g.name}</span>
                </span>
                <span className="shrink-0 tabular-nums">
                  <b className="font-semibold">{g.value}</b>{" "}
                  <span className="text-[#9AA5B4] dark:text-white/40">
                    ({formatPct((g.value / total) * 100)})
                  </span>
                </span>
              </li>
            ))}
            <li className="mt-1 border-t border-[#EEF1F5] pt-1.5 text-[11px] text-[#9AA5B4] dark:border-white/10 dark:text-white/40">
              {total} recibidos en total
            </li>
          </ul>
        </div>
      )}
    </Tarjeta>
  );
}

/** Un decimal, como en el resto de porcentajes del producto. */
function formatPct(valor: number): string {
  return `${valor.toFixed(1)} %`;
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

/**
 * Cifras derivadas del panel operativo, compartidas por {@link PanelOperativoKpis} (al lado del
 * banner) y {@link PanelOperativoResto} (desglose debajo) — un solo lugar que decide qué significa
 * "cargando" o "sin pendientes" para que las dos mitades del panel jamás se contradigan.
 */
function useDerivadosPanelOperativo(panel: OtOperationalPanel | null) {
  const cargando = panel === null;
  const movimiento = panel?.movimiento;
  const cola = panel?.cola;
  const antiguedad = panel?.antiguedad;
  const pendientes = movimiento?.pendientesTotal ?? 0;
  const sinPendientes = !cargando && pendientes === 0;
  const sinMediana = !cargando && (movimiento?.tiempoMedianoDecisionHoras ?? null) === null;
  return { cargando, movimiento, cola, antiguedad, pendientes, sinPendientes, sinMediana };
}

/**
 * Los 4 indicadores del panel operativo, en 2×2 — mismo patrón que las 4 tarjetas KPI del
 * dashboard del gestor al lado de su banner (`Dashboard.tsx`), para que ambos contenedores de
 * banner midan exactamente lo mismo (misma grilla `md:grid-cols-3`, mismo `col-span-1` de al lado).
 */
function PanelOperativoKpis({
  panel,
  onAbrir,
}: {
  panel: OtOperationalPanel | null;
  onAbrir: AbrirBloque;
}) {
  const { cargando, movimiento, cola, sinMediana } = useDerivadosPanelOperativo(panel);
  const pendientes = movimiento?.pendientesTotal ?? 0;

  return (
    <div className="grid grid-cols-2 gap-3">
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
        // Sin decisiones en la ventana no hay mediana que calcular. Antes esto pintaba un «—» del
        // tamaño de un titular y en color de acento: se leía como una barra de algún color, no
        // como una cifra ausente. Ahora se dice con palabras por qué está vacío.
        value={
          cargando
            ? undefined
            : sinMediana
              ? "Sin decisiones aún"
              : // Se formatea en vez de interpolar el número crudo: una mediana de dos minutos
                // salía como «0.03 h», con punto decimal inglés y en una unidad donde el dato no
                // significa nada.
                formatHours(movimiento?.tiempoMedianoDecisionHoras)
        }
        cargando={cargando}
        color="#00DBD5"
        sinDato={sinMediana}
        icon={Timer}
        hint={
          sinMediana
            ? `Ninguna decisión en los últimos ${VENTANA_MEDIANA_DIAS} días`
            : `Últimos ${VENTANA_MEDIANA_DIAS} días`
        }
        // Una mediana no es un conjunto de trámites: no hay lista que abrir detrás.
      />
    </div>
  );
}

/** Desglose de la cola y antigüedad de lo pendiente — el resto del panel operativo, debajo del banner. */
function PanelOperativoResto({
  panel,
  onAbrir,
}: {
  panel: OtOperationalPanel | null;
  onAbrir: AbrirBloque;
}) {
  const { cargando, cola, antiguedad, pendientes, sinPendientes } = useDerivadosPanelOperativo(panel);

  return (
    <div className="flex flex-col gap-4">
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
  sinDato,
  onAbrir,
}: {
  label: string;
  value: number | string | undefined;
  cargando: boolean;
  color: string;
  icon: typeof Inbox;
  hint?: string;
  /** No hay cifra que mostrar. Se apaga el color de acento: una cifra ausente no es un resultado. */
  sinDato?: boolean;
  onAbrir?: () => void;
}) {
  const numero = typeof value === "number" ? value : null;
  // Una cifra corta manda con el tamaño de titular; una frase a 30 px se sale de la tarjeta.
  const tamano = numero === null && typeof value === "string" && value.length > 6
    ? "text-lg"
    : "text-3xl";
  return (
    <Navegable
      navegable={!cargando && numero !== null && numero > 0}
      etiqueta={`Ver los ${numero} trámites: ${label.toLowerCase()}`}
      onAbrir={onAbrir}
      className="flex w-full items-center justify-between rounded-2xl border border-[#DFE5ED] bg-white p-4 dark:border-white/10 dark:bg-[#0B0F14]"
    >
      <div className="min-w-0">
        <p className="text-[11px] font-medium opacity-70">{label}</p>
        <p
          className={`mt-1 font-bold tabular-nums ${tamano} ${sinDato ? "text-[#9AA5B4] dark:text-white/40" : ""}`}
          style={sinDato ? undefined : { color }}
        >
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

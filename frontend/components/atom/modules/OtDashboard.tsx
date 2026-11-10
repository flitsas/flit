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
import { AlertTriangle, ChevronLeft, ChevronRight, ExternalLink } from "lucide-react";
import { TarjetaMetrica } from "@flit/ui/TarjetaMetrica";
import { rutaIlustracion, type Ilustracion } from "@flit/ui/ilustraciones";
import { fetchOtProfile } from "@/lib/api/admin-ot";
import { fetchTransitOffices } from "@/lib/api/admin-companies";
import { getToken } from "@/lib/api/client";
import { ApiError } from "@/lib/api/types";
import { decodeJwtPayload, hasPermission, isOtAdmin, isOtUser, isSuperAdmin } from "@/lib/auth/jwt";
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

/** Hero del dashboard OT (A18 es N/A: no se unifica con «Hola, {nombre}»). */
export const OT_DASHBOARD_HERO_TITLE = "Tu cola de trabajo";
/** KPIs de métrica OT (A19 es N/A: no se clonan ni se unifican con COPY.E03). */
export const OT_KPI_ESPERAN_MI_DECISION = "Esperan mi decisión";
export const OT_KPI_ENTREGADOS_HOY = "Entregados hoy";

/**
 * Ventana de la mediana de decisión. Fija y declarada en la propia tarjeta: el usuario no la eligió,
 * así que no puede deducirla. Coincide con el rango que se manda al endpoint (`defaultRange`).
 */
const VENTANA_MEDIANA_DIAS = 30;

/** Ventana de la franja de actividad. Corta a propósito: aquí se mira el ritmo, no la historia. */
const ACTIVIDAD_DIAS = 14;

type Estado = "cargando" | "listo" | "error" | "sin-permiso";

const MENSAJE_SIN_PERMISO_COLA = "No tienes permiso para ver esta información.";

function sinPermisoDeCola(): boolean {
  const claims = decodeJwtPayload(getToken());
  if (!claims || !isOtUser(claims) || isSuperAdmin(claims) || isOtAdmin(claims)) return false;
  return !hasPermission(claims, "ot.reportes.manage");
}

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
      if (sinPermisoDeCola()) {
        setPanel(null);
        setMensajeError(null);
        setEstado("sin-permiso");
        return;
      }
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
        if (err instanceof ApiError && err.status === 403) {
          setEstado("sin-permiso");
          return;
        }
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
      className="app-bg flex min-h-screen flex-col gap-4 px-6 pb-10 pt-6 text-foreground"
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
          className={estado === "error" || estado === "sin-permiso" ? "md:col-span-3" : "md:col-span-2"}
        />
        {estado !== "error" && estado !== "sin-permiso" && (
          <PanelOperativoKpis panel={estado === "listo" ? panel : null} onAbrir={abrirBloque} />
        )}
      </div>

      {estado === "sin-permiso" ? (
        <p role="status" className="rounded-2xl border border-border bg-card px-5 py-4 text-sm text-muted-foreground">
          {MENSAJE_SIN_PERMISO_COLA}
        </p>
      ) : estado === "error" ? (
        <ErrorPanel message={mensajeError} onRetry={reintentar} />
      ) : (
        <PanelOperativoResto panel={estado === "listo" ? panel : null} onAbrir={abrirBloque} />
      )}

      {estado !== "sin-permiso" && <PeriodoReciente transitOfficeId={params?.transitOfficeId} />}

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

/**
 * Fondo del slide fijo (la cola). HU #14481 (propuesta), misma receta que el Inicio del gestor: el `.page-header` del
 * prototipo, una tarjeta clara de superficie a cielo con texto en tinta, en vez del degradado azul y turquesa con
 * texto blanco. Los banners con imagen conservan su fondo y su velo.
 */
const BRAND_GRADIENT = "linear-gradient(120deg,var(--di-surface-elevated) 35%,var(--di-sky) 100%)";

/** Ilustración del slide fijo: la de organismos de tránsito del prototipo, a la izquierda como `.page-header__ilus`. */
const ILUSTRACION_COLA = rutaIlustracion("organismos");

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
    title: OT_DASHBOARD_HERO_TITLE,
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
  const fijo = mensaje.kind === "mensaje";

  return (
    <header
      className={`relative flex flex-col justify-between overflow-hidden ${
        fijo
          ? "rounded-[28px] border border-[color:var(--di-border)] text-[color:var(--di-heading)] shadow-[var(--shadow-card)]"
          : "rounded-2xl text-white"
      } ${className}`}
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
        style={{
          background: fijo
            ? "transparent"
            : "linear-gradient(180deg, rgba(0,0,0,0.25) 0%, rgba(0,0,0,0) 45%, rgba(0,0,0,0.35) 100%)",
        }}
      />
      {mensaje.kind === "mensaje" && (
        // Círculos del prototipo: el turquesa lleno arriba a la derecha y, con `outline`, el aro grande de cielo
        // alrededor (`.page-header::before/::after`), sin marcado nuevo.
        <div
          className="absolute -right-[60px] -top-[110px] h-[230px] w-[230px] rounded-full opacity-90 max-sm:-right-12 max-sm:-top-[60px] max-sm:h-[120px] max-sm:w-[120px]"
          style={{
            background: "var(--di-highlight-decor)",
            outline: "1.5px solid color-mix(in srgb, var(--di-sky-line) 50%, transparent)",
            outlineOffset: "95px",
          }}
        />
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
            <span className="flex h-10 w-10 items-center justify-center rounded-full bg-white/90 text-flit-primary shadow-lg">
              <ExternalLink className="h-5 w-5" aria-hidden="true" />
            </span>
          </span>
        </a>
      )}

      {/* pointer-events-none: esta caja ocupa TODO el alto del header (flex-1) aunque su contenido
          solo ocupe una franja arriba y otra abajo — sin esto, el hueco vacío del medio tapaba el
          enlace del banner de abajo y ni el hover ni el clic le llegaban nunca. Se restaura
          pointer-events-auto solo en la franja de controles (puntos/flechas), que sí son clicables. */}
      {/* En el slide fijo, la ilustración va de fondo a la izquierda de esta misma caja (92 px; 58 px en celular),
          como `.page-header__ilus`, sin marcado nuevo. Va arriba, a la altura del antetítulo y el título: la columna de
          cifras estira el banner, y centrada en vertical quedaba flotando lejos del texto. */}
      <div
        className={`relative flex flex-1 flex-col justify-between px-6 py-5 pointer-events-none ${
          fijo
            ? "bg-no-repeat bg-[length:92px_92px] bg-[position:24px_20px] pl-[132px] max-sm:bg-[length:58px_58px] max-sm:bg-[position:14px_20px] max-sm:pl-[84px]"
            : ""
        }`}
        style={fijo ? { backgroundImage: `url("${ILUSTRACION_COLA}")` } : undefined}
      >
        <div className="max-w-[85%]">
          {/* Antetítulo del prototipo (`.antetitulo`): el nombre del organismo en versalitas, en el azul de acento. */}
          <p
            className={`text-[11px] uppercase ${
              fijo ? "font-bold tracking-[0.06em] text-[color:var(--di-highlight)]" : "font-semibold tracking-wide opacity-80"
            }`}
          >
            {organismo ?? "Organismo de tránsito"}
          </p>
          {mensaje.kind === "mensaje" ? (
            <>
              <h1 className="mt-1 text-2xl font-bold leading-tight text-[color:var(--di-heading)] md:text-3xl after:mt-2.5 after:block after:h-[5px] after:w-16 after:rounded-full after:bg-[linear-gradient(90deg,var(--di-sky-line),var(--di-sky))] after:content-['']">
                {mensaje.title}
              </h1>
              <p className="mt-2.5 text-sm leading-snug text-[color:var(--di-text-secondary)]">{mensaje.body}</p>
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
                  background: fijo
                    ? i === actual
                      ? "var(--di-action)"
                      : "var(--di-border-control)"
                    : i === actual
                      ? "#ffffff"
                      : "rgba(255,255,255,0.5)",
                }}
              />
            ))}
          </div>
          <div className="flex items-center gap-1">
            <button
              type="button"
              onClick={() => setActual((i) => (i - 1 + mensajes.length) % mensajes.length)}
              disabled={mensajes.length <= 1}
              aria-label="Mensaje anterior"
              className={`grid h-6 w-6 place-items-center rounded-full disabled:cursor-not-allowed disabled:opacity-40 ${
                fijo
                  ? "bg-[var(--di-surface-elevated)] text-[color:var(--di-action)] shadow-[var(--shadow-card)] hover:bg-[var(--di-accent-soft)]"
                  : "bg-white/15 hover:bg-white/25 disabled:hover:bg-white/15"
              }`}
            >
              <ChevronLeft className="h-3 w-3" aria-hidden="true" />
            </button>
            <button
              type="button"
              onClick={() => setActual((i) => (i + 1) % mensajes.length)}
              disabled={mensajes.length <= 1}
              aria-label="Mensaje siguiente"
              className={`grid h-6 w-6 place-items-center rounded-full disabled:cursor-not-allowed disabled:opacity-40 ${
                fijo
                  ? "bg-[var(--di-surface-elevated)] text-[color:var(--di-action)] shadow-[var(--shadow-card)] hover:bg-[var(--di-accent-soft)]"
                  : "bg-white/15 hover:bg-white/25 disabled:hover:bg-white/15"
              }`}
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
        <p role="alert" className="py-6 text-center text-sm text-muted-foreground">
          No se pudo cargar la actividad reciente.
        </p>
      )}
      {estado === "listo" && !hayMovimiento && (
        // Un gráfico en blanco no distingue «no pasó nada» de «no cargó». Se dice con palabras.
        <p className="py-6 text-center text-sm text-muted-foreground">
          No hubo movimiento en los últimos {ACTIVIDAD_DIAS} días.
        </p>
      )}
      {estado === "listo" && hayMovimiento && (
        <div className="h-56 w-full" data-testid="ot-inicio-actividad">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={serie ?? []} margin={{ top: 4, right: 8, left: -20, bottom: 0 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" vertical={false} />
              <XAxis dataKey="label" tick={{ fontSize: 10 }} tickLine={false} axisLine={false} />
              <YAxis tick={{ fontSize: 10 }} tickLine={false} axisLine={false} allowDecimals={false} />
              <Tooltip cursor={{ fill: "color-mix(in srgb, var(--color-flit-brand) 6%, transparent)" }} />
              <Bar dataKey="radicados" name="Radicados" fill="var(--color-flit-brand)" radius={[3, 3, 0, 0]} />
              <Bar dataKey="aprobados" name="Aprobados" fill="var(--flit-success-ink)" radius={[3, 3, 0, 0]} />
              <Bar dataKey="rechazados" name="Rechazados" fill="var(--color-flit-alert)" radius={[3, 3, 0, 0]} />
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
 * El backend expone nueve estados propios del OT y aquí se doblan a cinco grupos. El detalle de qué
 * entra en cada uno va en el tooltip de la fila, porque la agrupación tiene juicio dentro y quien la
 * lea tiene derecho a verlo:
 *
 * - «Esperando placa» (ADR-0059) es un trámite radicado sin placa que el organismo aún no revisó: la
 *   placa se asigna ANTES de la revisión, así que cuenta como sin decisión.
 * - «En subsanación» es un rechazo con la subsanación abierta: el verdicto fue en contra aunque el
 *   trámite vaya a volver.
 * - «Esperando al cliente» es el grupo con menos filo: cae ahí tanto un trámite pausado a la espera
 *   de la empresa como uno ya con placa asignada. Se cuenta como sin decisión y el tooltip lo dice.
 * - «Revocados» va aparte: se aprobó y el organismo lo deshizo. Sumarlo a aprobados mentiría y
 *   sumarlo a rechazados escondería que hubo una aprobación de por medio.
 *
 * El desglose fino de los nueve estados vive en la consola de Reportes; aquí se busca la lectura de
 * un vistazo.
 */
function composicionDelPeriodo(resumen: OtReportSummary) {
  return [
    {
      id: "aprobados",
      name: "Aprobados",
      color: "var(--flit-success-ink)",
      value: resumen.aprobados,
      hint: "Aprobados por el organismo.",
    },
    {
      id: "rechazados",
      name: "Rechazados",
      color: "var(--color-flit-alert)",
      value: resumen.rechazados + resumen.enSubsanacion,
      hint: "Rechazados, con o sin subsanación abierta.",
    },
    {
      id: "sin-decision",
      name: "Sin decisión",
      color: "var(--color-flit-brand)",
      value:
        resumen.enRevision + resumen.esperandoPlaca + resumen.esperandoCliente + resumen.otros,
      hint: "En revisión, esperando placa y los que esperan algo de la empresa (SOAT, impuestos o trámite pausado).",
    },
    {
      id: "revocados",
      name: "Revocados",
      color: "var(--badge-purple-fg)",
      value: resumen.revocados,
      hint: "Aprobados que el organismo revocó después.",
    },
    {
      id: "anulados",
      name: "Anulados",
      color: "var(--badge-neutral-fg)",
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
        <p role="alert" className="py-6 text-center text-sm text-muted-foreground">
          No se pudo cargar la composición del periodo.
        </p>
      )}
      {estado === "listo" && total === 0 && (
        <p className="py-6 text-center text-sm text-muted-foreground">
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
                <p className="text-[9px] font-semibold uppercase tracking-wide text-muted-foreground">
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
                  <span className="text-muted-foreground">
                    ({formatPct((g.value / total) * 100)})
                  </span>
                </span>
              </li>
            ))}
            <li className="mt-1 border-t border-border pt-1.5 text-[11px] text-muted-foreground">
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
      className="flex flex-col items-start gap-3 rounded-2xl border border-[var(--badge-warning-border)] bg-[var(--badge-warning-bg)] px-5 py-4"
    >
      <p className="flex items-center gap-2 text-sm font-semibold text-foreground">
        <AlertTriangle className="h-4 w-4 shrink-0 text-[var(--badge-warning-fg)]" aria-hidden="true" />
        No se pudo cargar el estado de la cola
      </p>
      <p className="text-xs text-muted-foreground">
        {message ?? "Intenta de nuevo en un momento."}
      </p>
      <button
        type="button"
        onClick={onRetry}
        className="rounded-lg border border-border px-3 py-1.5 text-xs font-semibold transition hover:border-flit-brand"
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
        label={OT_KPI_ESPERAN_MI_DECISION}
        value={cola?.porRevisar}
        cargando={cargando}
        ilustracion="pendientes"
        pendiente
        onAbrir={() => onAbrir(OT_DRILLDOWN_BUCKETS.porRevisar, OT_KPI_ESPERAN_MI_DECISION)}
      />
      <Kpi
        label="Pendientes en total"
        value={pendientes}
        cargando={cargando}
        ilustracion="calendario"
        hint="Incluye lo que espera a un tercero"
        onAbrir={() => onAbrir(OT_DRILLDOWN_BUCKETS.pendientes, "Pendientes en total")}
      />
      <Kpi
        label={OT_KPI_ENTREGADOS_HOY}
        value={movimiento?.entregadosHoy}
        cargando={cargando}
        ilustracion="tramites"
        onAbrir={() => onAbrir(OT_DRILLDOWN_BUCKETS.entregadosHoy, OT_KPI_ENTREGADOS_HOY)}
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
        sinDato={sinMediana}
        ilustracion="procesos"
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
          <p className="py-6 text-center text-sm text-muted-foreground">
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
                <p className="text-[11px] text-muted-foreground">
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
    // `.card` de la propuesta: borde --di-border y radio de 20 px; el título como `.card__cabeza h2`, sin versalitas.
    <section className="rounded-[20px] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] p-5 shadow-[var(--shadow-card)]">
      {titulo && (
        <h2 className="mb-4 text-[0.9375rem] font-semibold leading-tight text-[color:var(--di-heading)]">
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
      className={`${className} text-left transition hover:border-flit-brand hover:shadow-sm focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-flit-brand`}
    >
      {children}
    </button>
  );
}

function Kpi({
  label,
  value,
  cargando,
  ilustracion,
  pendiente,
  hint,
  sinDato,
  onAbrir,
}: {
  label: string;
  value: number | string | undefined;
  cargando: boolean;
  /** Ilustración del prototipo a la derecha de la cifra (HU #14481). */
  ilustracion: Ilustracion;
  /** Lo que espera al organismo: con trámites, la tarjeta lleva el borde turquesa (`.metrica--pendiente`). */
  pendiente?: boolean;
  hint?: string;
  /** No hay cifra que mostrar. Se apaga la tinta: una cifra ausente no es un resultado. */
  sinDato?: boolean;
  onAbrir?: () => void;
}) {
  const numero = typeof value === "number" ? value : null;
  // Una cifra corta manda con el tamaño de titular; una frase a 30 px se sale de la tarjeta.
  const tamano = numero === null && typeof value === "string" && value.length > 6
    ? "text-lg"
    : "text-3xl";
  // Un bloque en cero NO se ofrece como enlace (ver `Navegable`): sin `onClick`, la tarjeta queda quieta.
  const navegable = !cargando && numero !== null && numero > 0 && Boolean(onAbrir);
  return (
    // HU #14481 (propuesta), receta del Inicio del gestor: `TarjetaMetrica` —etiqueta, cifra en tinta e ilustración—
    // en vez del número de color y el ícono en un círculo. En la columna angosta junto al banner la tarjeta va a una
    // columna y la ilustración se fija abajo a la derecha (con `max-w-none`, o la rejilla la aplasta). `content-start`:
    // si la fila la estira (la mediana es más alta), la cifra sigue pegada bajo el rótulo en vez de caer al fondo. El
    // pie es la aclaración de la cifra, no una acción: va en el gris terciario.
    <TarjetaMetrica
      className="relative w-full grid-cols-1 content-start p-4 [&>img]:absolute [&>img]:bottom-3.5 [&>img]:right-3.5 [&>img]:h-9 [&>img]:w-9 [&>img]:max-w-none [&>img]:shrink-0 [&>span:nth-child(2)]:pr-11 [&>span:nth-child(4)]:pr-11 [&>span:nth-child(4)]:font-normal [&>span:nth-child(4)]:text-[color:var(--di-text-tertiary)]"
      etiqueta={label}
      cifra={<span className={tamano}>{cargando ? "—" : (value ?? 0)}</span>}
      colorCifra={sinDato ? "var(--di-text-tertiary)" : undefined}
      ilustracion={rutaIlustracion(ilustracion)}
      pie={hint}
      pendiente={Boolean(pendiente) && navegable}
      onClick={navegable ? onAbrir : undefined}
      // El nombre accesible dice a dónde lleva: el texto visible es una etiqueta y un número, que por sí solos no
      // anuncian que haya una lista detrás.
      aria-label={navegable ? `Ver los ${numero} trámites: ${label.toLowerCase()}` : undefined}
    />
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
      {/* `.avance__barra` de la propuesta: pista hundida y relleno en sus tonos, sin verde. Lo que espera al
          organismo va en el degradado azul → turquesa; lo que espera a un tercero, en cielo. */}
      <span className="h-2 overflow-hidden rounded-full bg-[var(--di-surface-sunken)]">
        <span
          className="block h-full rounded-full"
          style={{
            width: `${pct}%`,
            background: miTurno ? "var(--di-gradient-brand)" : "color-mix(in srgb, var(--di-sky-line) 40%, transparent)",
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
          ? "border-[var(--badge-warning-border)] bg-[var(--badge-warning-bg)]"
          : "border-[color:var(--di-border)] bg-[var(--di-surface-elevated)]"
      }`}
    >
      <p className={`text-2xl font-bold tabular-nums ${encendida ? "text-[var(--badge-warning-fg)]" : "text-[color:var(--di-text-primary)]"}`}>
        {value}
      </p>
      <p className="mt-0.5 text-[10px] text-muted-foreground">{label}</p>
    </Navegable>
  );
}

function Esqueleto({ filas }: { filas: number }) {
  return (
    <div className="flex flex-col gap-3" aria-hidden="true">
      {Array.from({ length: filas }, (_, i) => (
        <div key={i} className="h-8 animate-pulse rounded-lg bg-secondary" />
      ))}
    </div>
  );
}

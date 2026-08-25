"use client";

// Detalle de un trámite de la integración (HU #11819). Cuatro capas, de la más útil a la más
// técnica: el recorrido responde «dónde se atascó», las consultas responden «por qué», los datos
// responden «con qué llegó» y el log queda al fondo para cuando nada de lo anterior baste.
import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { Ban, Check, CircleDashed, Eye, EyeOff, Hourglass, X, type LucideIcon } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import {
  fetchConsultasFuenteIct,
  fetchDatosTramiteIct,
  fetchLogTramiteIct,
  fetchRecorridoIct,
  revelarDatosPersonalesIct,
  type ConsultaFuenteIct,
  type DatosTramiteIct,
  type EventoLogTramiteIct,
  type HitoTrazabilidad,
  type RecorridoTramiteIct,
  type SeccionDatos,
  type TramiteIct,
} from "@/lib/api/ict-trazabilidad";
import { formatearDuracion } from "@/lib/ict/trazabilidad";

type Pestana = "recorrido" | "consultas" | "datos" | "log";

const PESTANAS: { id: Pestana; label: string }[] = [
  { id: "recorrido", label: "Recorrido" },
  { id: "consultas", label: "Consultas al RUNT" },
  { id: "datos", label: "Datos recibidos" },
  { id: "log", label: "Log técnico" },
];

/**
 * Tono e icono de cada desenlace de etapa, con los valores de la paleta de estados de la consola.
 * Iconos de `lucide` y nunca glifos sueltos (✓ ✕ ○): el navegador los pinta como emoji y
 * desentonan con el resto de la aplicación.
 */
const TONO_RESULTADO: Record<string, { fg: string; bg: string; Icon: LucideIcon }> = {
  ok: { fg: "#15803D", bg: "rgba(34,197,94,0.14)", Icon: Check },
  error: { fg: "#991B1B", bg: "rgba(153,27,27,0.14)", Icon: X },
  espera: { fg: "#C2410C", bg: "rgba(255,78,0,0.14)", Icon: Hourglass },
  anulado: { fg: "#6B21A8", bg: "rgba(107,33,168,0.14)", Icon: Ban },
  pendiente: { fg: "#64748B", bg: "rgba(148,163,184,0.16)", Icon: CircleDashed },
};

export function DetalleTramiteIct({ tramite, esAdmin }: { tramite: TramiteIct; esAdmin: boolean }) {
  const [pestana, setPestana] = useState<Pestana>("recorrido");

  return (
    <div className="mx-1 rounded-2xl border border-[#557EFF]/30 bg-white dark:border-white/10 dark:bg-[#0B0F14]">
      <div
        role="tablist"
        aria-label={`Detalle del trámite ${tramite.numero}`}
        className="flex gap-1 overflow-x-auto border-b border-[#DFE5ED] px-3 dark:border-white/10"
      >
        {PESTANAS.map((p) => {
          const activa = pestana === p.id;
          return (
            <button
              key={p.id}
              type="button"
              role="tab"
              aria-selected={activa}
              onClick={() => setPestana(p.id)}
              // Mismo patrón que TramitesListToolbar: color más subrayado absoluto, no pastilla.
              className={`relative whitespace-nowrap px-3 py-2.5 text-xs font-semibold transition ${
                activa ? "text-[#557EFF]" : "opacity-60 hover:opacity-100"
              }`}
            >
              {p.label}
              {activa && (
                <span
                  className="absolute inset-x-2 -bottom-px h-0.5 rounded-t bg-[#557EFF]"
                  aria-hidden="true"
                />
              )}
            </button>
          );
        })}
      </div>

      <div className="p-4">
        {pestana === "recorrido" && <PanelRecorrido numero={tramite.numero} tramite={tramite} esAdmin={esAdmin} />}
        {pestana === "consultas" && <PanelConsultas numero={tramite.numero} estado={tramite.estado} />}
        {pestana === "datos" && <PanelDatos numero={tramite.numero} />}
        {pestana === "log" && <PanelLog numero={tramite.numero} />}
      </div>
    </div>
  );
}

/**
 * Carga perezosa compartida: cada pestaña pide lo suyo la primera vez que se abre, no antes.
 *
 * Recibe la FUNCIÓN del cliente de API, no un cierre. Las cuatro (`fetchRecorridoIct`,
 * `fetchConsultasFuenteIct`, …) comparten firma y son de módulo, así que su identidad es estable y
 * la lista de dependencias puede escribirse como literal —que es lo que exigen las reglas de
 * hooks—. Con un cierre en línea la identidad cambiaría en cada render y el efecto se repetiría sin
 * fin.
 */
function useCargaPerezosa<T>(
  pedir: (numero: number, signal?: AbortSignal) => Promise<T>,
  numero: number,
) {
  const [dato, setDato] = useState<T | null>(null);
  const [status, setStatus] = useState<UiStatus>("loading");

  const ejecutar = useCallback(() => {
    const controller = new AbortController();
    setStatus("loading");
    pedir(numero, controller.signal)
      .then((d) => {
        if (controller.signal.aborted) return;
        setDato(d);
        // Una lista vacía es un estado legítimo con su propio mensaje, no un fallo.
        setStatus(Array.isArray(d) && d.length === 0 ? "empty" : "ready");
      })
      .catch(() => {
        if (!controller.signal.aborted) setStatus("error");
      });
    return () => controller.abort();
  }, [pedir, numero]);

  // `ejecutar` pone el estado en «cargando» ANTES de lanzar la petición: sin eso la pestaña se
  // vería vacía mientras llega la respuesta, que es justo cuando el usuario necesita ver que algo
  // está pasando. Devuelve la función de aborto, así que el efecto también limpia al desmontar.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => ejecutar(), [ejecutar]);

  return { dato, status, reintentar: ejecutar };
}

// ── Recorrido ────────────────────────────────────────────────────────────────

function PanelRecorrido({
  numero,
  tramite,
  esAdmin,
}: {
  numero: number;
  tramite: TramiteIct;
  esAdmin: boolean;
}) {
  const { dato, status, reintentar } = useCargaPerezosa<RecorridoTramiteIct>(fetchRecorridoIct, numero);

  return (
    <UiStateBoundary
      status={status}
      skeletonRows={4}
      errorMessage="No se pudo cargar el recorrido de este trámite."
      onRetry={reintentar}
      emptyMessage="Este trámite todavía no tiene recorrido registrado."
    >
      {dato && (
        <div className="flex flex-col gap-4">
          <ol className="flex flex-col">
            {dato.hitos.map((h, i) => (
              <Hito key={`${h.etapa}-${i}`} hito={h} ultimo={i === dato.hitos.length - 1} />
            ))}
          </ol>

          <div className="flex flex-wrap gap-x-8 gap-y-2 rounded-xl border border-[#DFE5ED] bg-[#F4F7FC] px-4 py-3 dark:border-white/10 dark:bg-white/[0.03]">
            <Tiempo label="Tiempo total" segundos={dato.tiempos.segundosTotal} />
            <Tiempo label="Hasta activar" segundos={dato.tiempos.segundosHastaActivar} />
            <Tiempo label="Hasta crear el borrador" segundos={dato.tiempos.segundosHastaCrearBorrador} />
          </div>

          {dato.procedureInstanceId && (
            <div className="flex flex-wrap items-center gap-3">
              <Link
                href={hrefTramite(dato.procedureInstanceId, tramite.clientTenantId, esAdmin)}
                className="inline-flex items-center rounded-lg bg-[#557EFF]/10 px-3 py-2 text-xs font-semibold text-[#557EFF] hover:bg-[#557EFF]/20"
              >
                Ver trámite
              </Link>
              {dato.organismoTransito && (
                <span className="text-xs opacity-70">
                  {dato.organismoTransito}
                  {dato.codigoOrganismoTransito && (
                    <span className="font-mono opacity-70"> · {dato.codigoOrganismoTransito}</span>
                  )}
                </span>
              )}
            </div>
          )}
        </div>
      )}
    </UiStateBoundary>
  );
}

function Hito({ hito, ultimo }: { hito: HitoTrazabilidad; ultimo: boolean }) {
  const tono = TONO_RESULTADO[hito.resultado] ?? TONO_RESULTADO.pendiente;
  const Icon = tono.Icon;
  const hora = hito.ocurrido
    ? new Date(hito.ocurrido).toLocaleString("es-CO", { dateStyle: "short", timeStyle: "medium" })
    : null;

  return (
    <li className="grid grid-cols-[28px_1fr] gap-3">
      <div className="flex flex-col items-center">
        <span
          className="grid h-6 w-6 shrink-0 place-items-center rounded-full"
          style={{ background: tono.bg }}
          aria-hidden="true"
        >
          <Icon className="h-3.5 w-3.5" style={{ color: tono.fg }} />
        </span>
        {!ultimo && <span className="w-px flex-1 bg-[#C9D6EA] dark:bg-white/15" aria-hidden="true" />}
      </div>

      <div className={ultimo ? "pb-0" : "pb-5"}>
        <div className="flex flex-wrap items-baseline gap-2">
          <span className="text-xs font-semibold">{hito.titulo}</span>
          <span className="font-mono text-[10px] tabular-nums opacity-55">
            {hora ?? "— sin registrar —"}
          </span>
          {hito.segundosDesdeAnterior !== null && (
            <span
              className="rounded-full px-2 py-0.5 font-mono text-[10px] font-semibold tabular-nums"
              style={
                hito.esTramoMasLento
                  ? { background: "rgba(255,78,0,0.14)", color: "#C2410C" }
                  : { background: "rgba(100,116,139,0.12)", color: "#475569" }
              }
              // El tramo lento no se distingue solo por color: se nombra.
              title={hito.esTramoMasLento ? "Tramo más lento del recorrido" : undefined}
            >
              +{formatearDuracion(hito.segundosDesdeAnterior)}
              {hito.esTramoMasLento && " · el más lento"}
            </span>
          )}
        </div>
        {hito.mensaje && (
          <p
            className="mt-1.5 rounded-lg px-3 py-2 text-[11px] font-medium"
            style={{ background: "rgba(153,27,27,0.10)", color: "#991B1B" }}
          >
            {hito.mensaje}
          </p>
        )}
      </div>
    </li>
  );
}

function Tiempo({ label, segundos }: { label: string; segundos: number | null }) {
  return (
    <div className="flex flex-col">
      <span className="text-[10px] font-semibold uppercase tracking-wider opacity-55">{label}</span>
      <span className="font-mono text-sm font-semibold tabular-nums">
        {formatearDuracion(segundos)}
      </span>
    </div>
  );
}

/**
 * Enlace al trámite de FLIT. Solo el SuperAdmin necesita el tenant en la URL: para el resto lo
 * deriva su propia sesión. Sin él, la pantalla de trámite responde «Falta header X-Tenant-Id»
 * (misma lección del LOG QX, Feature #11784).
 */
function hrefTramite(instanceId: string, tenantId: string, esAdmin: boolean): string {
  return esAdmin && tenantId
    ? `/tramites/${instanceId}?t=${encodeURIComponent(tenantId)}`
    : `/tramites/${instanceId}`;
}

// ── Consultas al RUNT ────────────────────────────────────────────────────────

/**
 * El mensaje de «no hay nada» tiene que decir la verdad. Un trámite que ya pasó por la etapa y no
 * dejó consultas registradas NO es lo mismo que uno que todavía no ha llegado: contarlo igual
 * escondería justo el hueco de trazabilidad que este módulo existe para destapar.
 */
function mensajeSinConsultas(estado: string): string {
  return estado === "recibido" || estado === "en_validacion_negocio"
    ? "Este trámite todavía no ha llegado a la etapa de consulta a fuentes externas."
    : "El trámite pasó por esta etapa, pero no quedó registrada ninguna consulta a fuentes externas.";
}

function PanelConsultas({ numero, estado }: { numero: number; estado: string }) {
  const { dato, status, reintentar } = useCargaPerezosa<ConsultaFuenteIct[]>(fetchConsultasFuenteIct, numero);
  const [abierta, setAbierta] = useState<string | null>(null);

  const bloqueante = dato?.find((c) => c.bloquea);

  return (
    <UiStateBoundary
      status={status}
      skeletonRows={3}
      errorMessage="No se pudieron cargar las consultas a fuentes de este trámite."
      onRetry={reintentar}
      emptyMessage={mensajeSinConsultas(estado)}
    >
      {dato && dato.length > 0 && (
        <div className="flex flex-col gap-3">
          {bloqueante && (
            <p
              className="rounded-lg px-3 py-2 text-[11px] font-medium"
              style={{ background: "rgba(153,27,27,0.10)", color: "#991B1B" }}
            >
              La consulta de {bloqueante.tipoConsultaEtiqueta.toLowerCase()} del{" "}
              {bloqueante.nivelActorEtiqueta.toLowerCase()} lleva {bloqueante.intentos}{" "}
              {bloqueante.intentos === 1 ? "intento" : "intentos"} sin resolverse. Sin ella el trámite
              no puede avanzar.
            </p>
          )}
          <div className="overflow-x-auto">
            <table className="w-full min-w-[640px] text-xs">
              <thead>
                <tr className="text-left text-[10px] font-semibold uppercase opacity-55">
                  <th className="px-3 py-2">Nivel</th>
                  <th className="px-3 py-2">Consulta</th>
                  <th className="px-3 py-2">Documento o placa</th>
                  <th className="px-3 py-2">Consultada</th>
                  <th className="px-3 py-2">Válida</th>
                  <th className="px-3 py-2">Intentos</th>
                  <th className="px-3 py-2" />
                </tr>
              </thead>
              <tbody>
                {dato.map((c) => (
                  <tr
                    key={c.id}
                    className="border-t border-[#DFE5ED] dark:border-white/10"
                    style={c.bloquea ? { background: "rgba(153,27,27,0.06)" } : undefined}
                  >
                    <td className="px-3 py-2">{c.nivelActorEtiqueta}</td>
                    <td className="px-3 py-2">{c.tipoConsultaEtiqueta}</td>
                    <td className="px-3 py-2 font-mono text-[11px]">{c.identificador ?? "—"}</td>
                    <td className="px-3 py-2">
                      <SiNo valor={c.consultada} />
                    </td>
                    <td className="px-3 py-2">
                      <SiNo valor={c.valida} />
                    </td>
                    <td className="px-3 py-2 font-mono tabular-nums">{c.intentos}</td>
                    <td className="px-3 py-2">
                      {c.respuesta ? (
                        <button
                          type="button"
                          onClick={() => setAbierta(abierta === c.id ? null : c.id)}
                          className="rounded-md bg-[#557EFF]/10 px-2 py-1 text-[10px] font-semibold text-[#557EFF] hover:bg-[#557EFF]/20"
                        >
                          {abierta === c.id ? "Ocultar respuesta" : "Ver respuesta"}
                        </button>
                      ) : (
                        <span className="opacity-45">—</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {abierta && (
            <pre className="max-h-64 overflow-auto rounded-lg border border-[#DFE5ED] bg-[#F4F7FC] p-3 font-mono text-[11px] leading-relaxed dark:border-white/10 dark:bg-white/[0.03]">
              {formatearJson(dato.find((c) => c.id === abierta)?.respuesta)}
            </pre>
          )}
          <p className="text-[10px] opacity-55">
            Las respuestas llegan con los datos personales enmascarados.
          </p>
        </div>
      )}
    </UiStateBoundary>
  );
}

function SiNo({ valor }: { valor: boolean }) {
  return (
    <span className="font-semibold" style={{ color: valor ? "#15803D" : "#991B1B" }}>
      {valor ? "Sí" : "No"}
    </span>
  );
}

function formatearJson(texto: string | null | undefined): string {
  if (!texto) return "";
  try {
    return JSON.stringify(JSON.parse(texto), null, 2);
  } catch {
    // No es JSON: se muestra tal cual en vez de esconderlo.
    return texto;
  }
}

// ── Datos recibidos ──────────────────────────────────────────────────────────

function PanelDatos({ numero }: { numero: number }) {
  const { dato, status, reintentar } = useCargaPerezosa<DatosTramiteIct>(fetchDatosTramiteIct, numero);

  // HU #11820. El revelado es una acción explícita y su resultado NO sustituye a la carga normal:
  // se guarda aparte para poder volver a tapar sin repetir la petición ni el registro de auditoría.
  const [revelado, setRevelado] = useState<SeccionDatos[] | null>(null);
  const [revelando, setRevelando] = useState(false);
  const [errorRevelado, setErrorRevelado] = useState<string | null>(null);

  const alternarRevelado = async () => {
    if (revelado) {
      setRevelado(null);
      return;
    }
    setRevelando(true);
    setErrorRevelado(null);
    try {
      const r = await revelarDatosPersonalesIct(numero);
      setRevelado(r.secciones);
    } catch {
      // El caso normal es no tener el permiso; el mensaje lo dice sin hablar de códigos HTTP.
      setErrorRevelado(
        "No tienes permiso para ver los datos personales sin enmascarar. Pídeselo a quien administre los roles.",
      );
    } finally {
      setRevelando(false);
    }
  };

  // Las secciones reveladas sustituyen a las enmascaradas SOLO en los actores; el resto del detalle
  // (transacción, adjuntos) no lleva datos personales y no cambia.
  const secciones = dato
    ? dato.secciones.map((s) => revelado?.find((r) => r.titulo === s.titulo) ?? s)
    : [];

  return (
    <UiStateBoundary
      status={status}
      skeletonRows={4}
      errorMessage="No se pudieron cargar los datos de este trámite."
      onRetry={reintentar}
      emptyMessage="Sin datos registrados para este trámite."
    >
      {dato && (
        <div className="flex flex-col gap-3">
          {/* Secciones de negocio, no un volcado JSON: la petición del cliente tiene decenas de
              campos y agruparlos por significado es lo que la hace legible. */}
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {secciones.map((s) => (
              <section
                key={s.titulo}
                className="flex flex-col gap-2 rounded-xl border border-[#DFE5ED] p-3 dark:border-white/10"
              >
                <h4 className="text-[10px] font-bold uppercase tracking-wider text-[#557EFF]">
                  {s.titulo}
                </h4>
                {s.datos.length === 0 ? (
                  <p className="text-[11px] italic opacity-55">
                    Sin registros en esta sección.
                  </p>
                ) : (
                  s.datos.map((d) => (
                    <div
                      key={d.etiqueta}
                      className="flex justify-between gap-3 border-b border-[#DFE5ED] pb-1.5 text-[11px] last:border-b-0 last:pb-0 dark:border-white/10"
                    >
                      <span className="shrink-0 opacity-55">{d.etiqueta}</span>
                      <span
                        className={`min-w-0 break-words text-right font-medium ${
                          d.esSensible ? "font-mono tracking-wider" : ""
                        }`}
                      >
                        {d.valor ?? "—"}
                      </span>
                    </div>
                  ))
                )}
              </section>
            ))}
          </div>
          <div className="flex flex-wrap items-center gap-3">
            <button
              type="button"
              onClick={() => void alternarRevelado()}
              disabled={revelando}
              className="inline-flex items-center gap-1.5 rounded-lg bg-[#557EFF]/10 px-3 py-1.5 text-[11px] font-semibold text-[#557EFF] hover:bg-[#557EFF]/20 disabled:opacity-50"
            >
              {revelado ? (
                <EyeOff className="h-3.5 w-3.5" aria-hidden="true" />
              ) : (
                <Eye className="h-3.5 w-3.5" aria-hidden="true" />
              )}
              {revelando
                ? "Revelando…"
                : revelado
                  ? "Volver a ocultar"
                  : "Revelar datos personales"}
            </button>
            <p className="text-[10px] opacity-55">
              {revelado
                ? "Este acceso quedó registrado con tu usuario y la fecha."
                : "Los datos personales se muestran enmascarados. Revelarlos deja constancia de quién lo hizo."}
            </p>
          </div>
          {errorRevelado && (
            <p
              className="rounded-lg px-3 py-2 text-[11px] font-medium"
              style={{ background: "rgba(153,27,27,0.10)", color: "#991B1B" }}
            >
              {errorRevelado}
            </p>
          )}
        </div>
      )}
    </UiStateBoundary>
  );
}

// ── Log técnico ──────────────────────────────────────────────────────────────

function PanelLog({ numero }: { numero: number }) {
  const { dato, status, reintentar } = useCargaPerezosa<EventoLogTramiteIct[]>(fetchLogTramiteIct, numero);

  return (
    <UiStateBoundary
      status={status}
      skeletonRows={3}
      errorMessage="No se pudo cargar el log de este trámite."
      onRetry={reintentar}
      emptyMessage="No hay peticiones registradas para este trámite en la ventana consultada."
    >
      {dato && dato.length > 0 && (
        <div className="flex flex-col gap-3">
          <p className="rounded-lg bg-[#557EFF]/[0.08] px-3 py-2 text-[11px] text-[#1D4ED8] dark:text-[#9DB8FF]">
            Solo las peticiones que tocan a este trámite. El log completo de la plataforma sigue en
            el módulo Log ICT.
          </p>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[680px] text-xs">
              <thead>
                <tr className="text-left text-[10px] font-semibold uppercase opacity-55">
                  <th className="px-3 py-2">Hora</th>
                  <th className="px-3 py-2">Tipo</th>
                  <th className="px-3 py-2">Método</th>
                  <th className="px-3 py-2">Destino</th>
                  <th className="px-3 py-2">Código</th>
                  <th className="px-3 py-2">Duración</th>
                </tr>
              </thead>
              <tbody>
                {dato.map((e) => (
                  <tr key={e.id} className="border-t border-[#DFE5ED] align-top dark:border-white/10">
                    <td className="px-3 py-2 font-mono tabular-nums">
                      {new Date(e.ocurrido).toLocaleString("es-CO", {
                        dateStyle: "short",
                        timeStyle: "medium",
                      })}
                    </td>
                    <td className="px-3 py-2">
                      {e.tipo}
                      <span className="block text-[10px] opacity-55">{e.direccion}</span>
                    </td>
                    <td className="px-3 py-2 font-mono text-[11px] font-semibold">{e.metodo}</td>
                    <td className="px-3 py-2">
                      <span className="break-all font-mono text-[11px]">{e.ruta}</span>
                      {/* La cifra que explica por qué el log crudo resulta ilegible. */}
                      {e.tramitesEnLaPeticion > 1 && (
                        <span className="block text-[10px] opacity-55">
                          Esta petición traía {e.tramitesEnLaPeticion} trámites; se muestra por ser
                          la que originó este.
                        </span>
                      )}
                    </td>
                    <td className="px-3 py-2">
                      <span
                        className="rounded-md px-2 py-0.5 font-mono text-[11px] font-semibold"
                        style={
                          e.codigo >= 400
                            ? { background: "rgba(153,27,27,0.14)", color: "#991B1B" }
                            : { background: "rgba(34,197,94,0.14)", color: "#15803D" }
                        }
                      >
                        {e.codigo}
                      </span>
                    </td>
                    <td className="px-3 py-2 font-mono tabular-nums">
                      {e.duracionMs.toLocaleString("es-CO")} ms
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </UiStateBoundary>
  );
}

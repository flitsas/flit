"use client";

import { useEffect, useMemo, useRef, useState , type ComponentType } from "react";
import Link from "next/link";
import { notFound, useRouter } from "next/navigation";
import { iconoComo } from "@flit/ui/iconoComo";
import { INLINE_ALERT_TONES, InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { useSesion } from "@/components/DiagnosticoShell";
import { BOTON_PRIMARIO_CLS, BOTON_REINTENTAR_CLS, FOCO_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { ApiError } from "@/lib/api";
import { accesoNegado } from "@/lib/carga/api";
import { urlVolverAlListado } from "@/lib/diagnosticos/listado";
import {
  cerrarPanel,
  mensajeCierre,
  mensajeReenviadas,
  reintentarVehiculo,
  type CeldaGrilla,
  type FilaVehiculo,
  type Grilla,
  type ResultadoReintento,
} from "@/lib/tablero/api";
import { hayPendientes, marcarReintentada, marcarReprocesada } from "@/lib/tablero/fusionarCeldas";
import { useGrillaIncremental } from "@/lib/tablero/useGrillaIncremental";
import { PanelDictamen } from "./PanelDictamen";
import { BotonPdfVehiculo } from "./BotonPdfVehiculo";
import { BotonReintentar } from "./BotonReintentar";
import { AccionTramiteFila, ModalTipoTramite } from "./BotonCrearTramite";
import { CasillaDerivacion } from "./BarraSeleccionDerivacion";
import { ProgresoDerivacion, type PlacaNoEncolada } from "./ProgresoDerivacion";
import {
  derivacionDeFila,
  encolarTramites,
  marcarConTramite,
  marcarEncoladas,
  mensajeMasivo,
  operacionActiva,
  pideTipo,
  type ProgresoOperacion,
} from "@/lib/tablero/derivacion";
import { DescargasLote } from "./DescargasLote";
import { EstadoGlobal, EstimadoVsConsumido } from "./EstadoGlobal";
import { AccionesBloqueLote } from "./AccionesBloqueLote";
import { CabeceraLote } from "./CabeceraLote";
import { GrillaLote } from "./GrillaLote";
import { MetricasLote } from "./MetricasLote";
import type { FiltroTablero } from "@/lib/tablero/filtrosTablero";
import { pedirSondeoNotificaciones } from "@/lib/notificaciones/useNotificaciones";

/** «hh:mm» en hora de Colombia (HU #14562 AC26). */
const HORA = new Intl.DateTimeFormat("es-CO", { hour: "2-digit", minute: "2-digit", hourCycle: "h23", timeZone: "America/Bogota" });

/**
 * Aviso no intrusivo del reintento manual (Punto 3): la paleta `success` de InlineAlert con su propio nombre accesible
 * (InlineAlert no lo admite y anidaría otro `status`). Recibe el foco: el botón pulsado desaparece al dejar de estar pegada.
 */
function AvisoReenviadas({ ref, children }: { ref: React.Ref<HTMLDivElement>; children: React.ReactNode }) {
  const { color, background, border, Icon } = INLINE_ALERT_TONES.success;
  return (
    <div
      ref={ref}
      tabIndex={-1}
      role="status"
      aria-live="polite"
      aria-label="Resultado del reintento"
      className={`flex items-center gap-3 rounded-xl p-3 text-xs font-semibold ${FOCO_CLS}`}
      style={{ background, border: `1px solid ${border}`, color }}
    >
      <Icon className="h-4 w-4 shrink-0" aria-hidden="true" />
      {children}
    </div>
  );
}

/** «Cerrar panel» procede con el lote EN_PROCESO, sin celdas abiertas y con alguna en error (si no, la API responde 409). */
/** Operación masiva a la vista: la recién encolada (se lee de inmediato) o la recuperada al volver (ya leída). */
type OperacionVisible = { inicial: ProgresoOperacion; leerAlInicio: boolean; noEncoladas: PlacaNoEncolada[] };

const placaDe = (v: FilaVehiculo) => v.placa || v.vin || v.identificador;

const puedeCerrarPanel = (g: Grilla) =>
  g.estado === "EN_PROCESO" && !hayPendientes(g) && g.vehiculos.some((v) => v.celdas.some((c) => c.estado === "ERROR"));

/** Ícono del estado vacío: del kit (`iconoComo`) o de lucide cuando el kit no trae el glifo (iconoComo("buscar")). */
type IconoVacio = ComponentType<{ className?: string; strokeWidth?: number; "aria-hidden"?: boolean | "true" | "false" }>;
const ICONO_VACIO = iconoComo("portapapeles");

function Vacio({ titulo, children, Icono = ICONO_VACIO }: { titulo: string; children?: React.ReactNode; Icono?: IconoVacio }) {
  return (
    <section aria-labelledby="tablero-vacio" className={`${TARJETA_CLS} flex flex-col items-center gap-3 py-12 text-center`}>
      <Icono className="h-10 w-10 text-flit-brand" aria-hidden="true" strokeWidth={1.5} />
      <h2 id="tablero-vacio" className="text-lg font-semibold">
        {titulo}
      </h2>
      {children}
    </section>
  );
}

/**
 * Tablero del lote (HU #13748, F04-H2) en /diagnosticos/{id}, a donde navegan la confirmación de la carga y el Express:
 * tarjeta título con el nombre del lote, estado global y avance, la grilla de semáforos con sondeo incremental cada 3 s,
 * «Reprocesar» para el admin en las rojas y «Cerrar panel» cuando el lote terminó con errores. Cuatro estados: cargando,
 * error (con Reintentar; el 404 va a la página 404 de la app), vacío (sin vehículos o sin permiso) y lleno. Arriba, migas «Diagnósticos ›
 * {lote}» y «Volver a diagnósticos» al listado con los filtros que traía (?desde=, solo como search params de /, HU #14087).
 * Cada semáforo abre su detalle en una pestaña nueva.
 */
export function TableroLote({ diagnosticoId, desde = null }: { diagnosticoId: string; desde?: string | null }) {
  const router = useRouter();
  const { sesion, tramitesUrl = "" } = useSesion();
  const puedeVer = sesion?.capacidades.verDiagnosticos ?? false;
  const puedeReprocesar = sesion?.capacidades.reprocesarConsultas ?? false;
  const puedeCargar = sesion?.capacidades.crearDiagnosticos ?? false;
  // HU #13851: PDF por vehículo, Excel y ZIP del lote; sin la capacidad no hay ningún control (la API vuelve a exigirla).
  const puedeDescargar = sesion?.capacidades.descargarReportes ?? false;
  // Punto 4: «Crear trámite» / «Ver trámite #N» solo con la capacidad `derivar` (la API vuelve a exigir el permiso).
  const puedeDerivar = sesion?.capacidades.derivar ?? false;
  const { grilla, cargando, error, errorSondeo, sondeando, actualizadoEn, reintentar, reanudar, refrescar, actualizar } = useGrillaIncremental(diagnosticoId, {
    habilitado: puedeVer,
  });
  const [aviso, setAviso] = useState<string | null>(null);
  // Punto 3: «N consultas reenviadas» tras un reintento manual; recibe el foco porque el botón pulsado desaparece.
  const [reenviadas, setReenviadas] = useState<string | null>(null);
  const reenviadasRef = useRef<HTMLDivElement>(null);
  const [cerrando, setCerrando] = useState(false);
  // HU #14562 AC24 (D4): «Cerrar lote» pide confirmación antes de llamar a cerrar-panel.
  const [confirmandoCierre, setConfirmandoCierre] = useState(false);
  const [dictamen, setDictamen] = useState<{ vehiculo: FilaVehiculo; disparador: HTMLElement | null } | null>(null);
  // HU #14562 F-05: «Crear trámite» desde el pie del panel del dictamen; el número cambia en cada pedido.
  const [solicitudTramite, setSolicitudTramite] = useState<{ vehiculoId: string; n: number } | null>(null);
  const volver = urlVolverAlListado(desde);
  // HU #14562 AC27: tarjeta-filtro activa (exclusiva); null = sin filtro.
  const [filtro, setFiltro] = useState<FiltroTablero | null>(null);

  // Punto 4, masivo (incremento 3): selección por id de vehículo (sobrevive al sondeo incremental), sin preselección.
  const [seleccion, setSeleccion] = useState<ReadonlySet<string>>(() => new Set());
  const [encolando, setEncolando] = useState(false);
  const [errorMasivo, setErrorMasivo] = useState<string | null>(null);
  const [pidiendoTipoMasivo, setPidiendoTipoMasivo] = useState(false);
  const [operacion, setOperacion] = useState<OperacionVisible | null>(null);
  const recuperada = useRef(false);
  const derivaciones = useMemo(() => new Map((grilla?.vehiculos ?? []).map((v) => [v.id, derivacionDeFila(v)])), [grilla]);
  // Sin la capacidad o con la derivación apagada (`derivacion: null` o backend previo) no hay casillas, barra ni progreso.
  const hayDerivacion = puedeDerivar && [...derivaciones.values()].some((d) => d.conocida);
  const elegibles = (grilla?.vehiculos ?? []).filter((v) => derivaciones.get(v.id)?.elegible).map((v) => v.id);
  // Una placa que dejó de ser elegible (p. ej. otro usuario creó su trámite) sale de la cuenta sola.
  const seleccionadas = elegibles.filter((id) => seleccion.has(id));

  useEffect(() => {
    // Al volver a la pantalla se retoma la operación activa del lote (si el backend la expone; un error se ignora).
    if (!hayDerivacion || recuperada.current) return;
    recuperada.current = true;
    operacionActiva(diagnosticoId)
      .then((p) => {
        if (p) setOperacion((actual) => actual ?? { inicial: p, leerAlInicio: false, noEncoladas: [] });
      })
      .catch(() => {});
  }, [hayDerivacion, diagnosticoId]);

  function cambiarSeleccion(vehiculoId: string, marcada: boolean) {
    setSeleccion((s) => {
      const nueva = new Set(s);
      if (marcada) nueva.add(vehiculoId);
      else nueva.delete(vehiculoId);
      return nueva;
    });
  }

  async function encolar(tipo?: string) {
    if (!grilla || encolando || seleccionadas.length === 0) return;
    const ids = seleccionadas;
    setEncolando(true);
    setErrorMasivo(null);
    try {
      const r = await encolarTramites(grilla.id, ids, tipo);
      const placas = new Map(grilla.vehiculos.map((v) => [v.id, placaDe(v)]));
      const noEncolados = new Set(r.noElegibles.map((n) => n.vehiculoId));
      actualizar((g) => marcarEncoladas(g, ids.filter((id) => !noEncolados.has(id))));
      setSeleccion(new Set());
      setOperacion({
        inicial: {
          id: r.operacionId,
          estado: r.encolados > 0 ? "EN_CURSO" : "TERMINADA",
          total: r.encolados,
          creadas: 0,
          omitidas: 0,
          fallidas: 0,
          enCurso: r.encolados,
          terminada: r.encolados === 0,
          items: [],
        },
        leerAlInicio: r.encolados > 0,
        noEncoladas: r.noElegibles.map((n) => ({ placa: placas.get(n.vehiculoId) ?? "", motivoCodigo: n.motivoCodigo })),
      });
    } catch (e) {
      if (!tipo && pideTipo(e)) {
        setPidiendoTipoMasivo(true);
        return;
      }
      setErrorMasivo(mensajeMasivo(e));
    } finally {
      setEncolando(false);
    }
  }

  function alCrearMasivo() {
    // Decisión 4.3: en lote Simple el tipo se pide una sola vez para todas las seleccionadas.
    if (grilla?.modo === "SIMPLE") setPidiendoTipoMasivo(true);
    else void encolar();
  }

  const codigo403 = accesoNegado(error);
  useEffect(() => {
    if (codigo403) router.replace(`/403?code=${encodeURIComponent(codigo403)}`);
  }, [codigo403, router]);

  function alReprocesar(celda: CeldaGrilla) {
    setAviso(null);
    actualizar((g) => marcarReprocesada(g, celda.id));
    reanudar();
  }

  function alFallarReproceso(mensaje: string, volverALeer: boolean) {
    setReenviadas(null);
    setAviso(mensaje);
    if (volverALeer) refrescar();
  }

  /** Reintento manual (Punto 3): sin `vehiculoId`, el del lote. Oculta la acción al instante y vuelve a leer la grilla. */
  function alReintentar(resultado: ResultadoReintento, vehiculoId?: string) {
    setAviso(null);
    setReenviadas(mensajeReenviadas(resultado?.reencoladas ?? 0));
    actualizar((g) => marcarReintentada(g, vehiculoId));
    refrescar();
  }

  useEffect(() => {
    if (reenviadas) reenviadasRef.current?.focus();
  }, [reenviadas]);

  const accionVehiculo =
    puedeDescargar || puedeReprocesar || puedeDerivar
      ? (v: FilaVehiculo) => (
          <div className="flex flex-col items-start gap-2">
            {puedeDerivar && grilla && (
              <AccionTramiteFila
                diagnosticoId={grilla.id}
                vehiculo={v}
                modo={grilla.modo}
                tramiteAnalizado={grilla.lote?.tipo_tramite?.nombre ?? null}
                tramitesUrl={tramitesUrl}
                empresaId={sesion?.empresa.id}
                onCreado={(vid, tramite) => actualizar((g) => marcarConTramite(g, vid, tramite))}
                onRefrescar={refrescar}
                solicitudCrear={solicitudTramite?.vehiculoId === v.id ? solicitudTramite.n : undefined}
              />
            )}
            {puedeDescargar && grilla && <BotonPdfVehiculo diagnosticoId={grilla.id} estadoLote={grilla.estado} vehiculo={v} />}
            {puedeReprocesar && v.pegada && (
              <BotonReintentar
                etiqueta={`Reintentar consultas de la placa ${v.placa || v.vin || v.identificador}`}
                texto="Reintentar placa"
                reintentar={() => reintentarVehiculo(diagnosticoId, v.id)}
                onReintentado={(r) => alReintentar(r, v.id)}
                onError={alFallarReproceso}
              />
            )}
          </div>
        )
      : undefined;

  async function cerrar() {
    setConfirmandoCierre(false);
    setCerrando(true);
    setAviso(null);
    try {
      const cerrado = await cerrarPanel(diagnosticoId);
      actualizar((g) => ({ ...g, estado: cerrado.estado }));
      // El estimado frente a consumido llega con el lote cerrado: se vuelve a leer una vez.
      refrescar();
    } catch (e) {
      const code = accesoNegado(e);
      if (code) return router.replace(`/403?code=${encodeURIComponent(code)}`);
      setAviso(mensajeCierre(e));
      if (e instanceof ApiError && e.status === 409) refrescar();
    } finally {
      setCerrando(false);
    }
  }

  // HU #14562 AC49/AC57: el 404 (no existe o es de otro usuario con visibilidad PROPIOS) es la página 404 de la app, que no
  // revela si el lote existe.
  if (puedeVer && error instanceof ApiError && error.status === 404) notFound();

  let contenido;
  if (!puedeVer) {
    contenido = (
      <Vacio titulo="No tienes permiso para ver este diagnóstico">
        <p className="max-w-md text-sm text-muted-foreground">Pídele acceso al administrador de tu compañía.</p>
      </Vacio>
    );
  } else if (cargando || codigo403) {
    contenido = <VehicleLoader label="Cargando el tablero…" />;
  } else if (error || !grilla) {
    contenido = (
      <InlineAlert
        tone="error"
        title="No se pudo cargar el tablero"
        action={
          <button type="button" onClick={reintentar} className={BOTON_REINTENTAR_CLS}>
            Reintentar
          </button>
        }
      >
        {error instanceof Error ? error.message : "Intenta de nuevo en un momento."}
      </InlineAlert>
    );
  } else if (grilla.avance.vehiculos === 0) {
    contenido = (
      <Vacio titulo="Este diagnóstico todavía no tiene vehículos">
        <p className="max-w-md text-sm text-muted-foreground">El tablero se llena cuando se confirman los vehículos del lote.</p>
        {grilla.estado === "CREADO" && puedeCargar && (
          <Link href={`/diagnosticos/${encodeURIComponent(diagnosticoId)}/carga`} className={BOTON_PRIMARIO_CLS}>
            Cargar vehículos
          </Link>
        )}
      </Vacio>
    );
  } else {
    contenido = (
      <>
        <section aria-label="Avance del lote" className={TARJETA_CLS}>
          <EstadoGlobal grilla={grilla} sondeando={sondeando} />
        </section>
        <DescargasLote diagnosticoId={diagnosticoId} estadoLote={grilla.estado} puedeDescargar={puedeDescargar} />
        <MetricasLote grilla={grilla} filtro={filtro} onFiltro={setFiltro} />
        {reenviadas && <AvisoReenviadas ref={reenviadasRef}>{reenviadas}</AvisoReenviadas>}
        {aviso && (
          <InlineAlert tone="error" title="No se completó la acción">
            {aviso}
          </InlineAlert>
        )}
        {hayDerivacion && operacion && (
          <ProgresoDerivacion
            key={operacion.inicial.id}
            diagnosticoId={grilla.id}
            inicial={operacion.inicial}
            leerAlInicio={operacion.leerAlInicio}
            noEncoladas={operacion.noEncoladas}
            // HU #14562 AC30: la fila derivada llega por su versión de fila en el sondeo incremental, sin ?desde=0.
            onTerminada={() => {
              refrescar();
              // HU #14562 AC51: terminó la creación masiva; la campana sondea de inmediato.
              pedirSondeoNotificaciones();
            }}
            onCerrar={() => setOperacion(null)}
          />
        )}
        {errorSondeo != null && (
          <InlineAlert
            tone="warning"
            title={actualizadoEn ? `Datos desactualizados desde ${HORA.format(actualizadoEn)}` : "Datos desactualizados"}
            action={
              <button type="button" onClick={refrescar} className={BOTON_REINTENTAR_CLS}>
                Actualizar ahora
              </button>
            }
          >
            Se detuvo la actualización automática: {errorSondeo instanceof Error ? errorSondeo.message : "no se pudo leer el avance del lote."}
          </InlineAlert>
        )}
        <section aria-label="Grilla del lote" className={`${TARJETA_CLS} flex flex-col gap-4`}>
          <AccionesBloqueLote
            diagnosticoId={diagnosticoId}
            hayDerivacion={hayDerivacion}
            elegibles={elegibles.length}
            seleccionadas={seleccionadas.length}
            encolando={encolando}
            errorMasivo={errorMasivo}
            onSeleccionarTodas={() => setSeleccion(new Set(elegibles))}
            onQuitar={() => setSeleccion(new Set())}
            onCrear={alCrearMasivo}
            totalPegadas={grilla.total_pegadas ?? 0}
            puedeReprocesar={puedeReprocesar}
            onReintentado={(r) => alReintentar(r)}
            onErrorReintento={alFallarReproceso}
          />
          <GrillaLote
            grilla={grilla}
            filtro={filtro}
            puedeReprocesar={puedeReprocesar}
            onReprocesada={alReprocesar}
            onErrorReproceso={alFallarReproceso}
            accionVehiculo={accionVehiculo}
            tituloAccionVehiculo={puedeReprocesar || puedeDerivar ? "Acciones" : "Reporte"}
            casillaVehiculo={
              hayDerivacion
                ? (v) => (
                    <CasillaDerivacion
                      vehiculo={v}
                      derivacion={derivaciones.get(v.id) ?? derivacionDeFila(v)}
                      marcada={seleccion.has(v.id)}
                      onCambiar={cambiarSeleccion}
                    />
                  )
                : undefined
            }
            onAbrirDictamen={(vehiculo, disparador) => setDictamen({ vehiculo, disparador })}
          />
          <EstimadoVsConsumido estimado={grilla.estimado_consultas} plegable />
        </section>
      </>
    );
  }

  return (
    <>
      <CabeceraLote
        grilla={grilla}
        empresa={sesion?.empresa.nombre ?? undefined}
        volver={volver}
        puedeCerrar={!!grilla && puedeCerrarPanel(grilla)}
        cerrando={cerrando}
        confirmando={confirmandoCierre}
        onPedirCierre={() => setConfirmandoCierre(true)}
        onCancelarCierre={() => setConfirmandoCierre(false)}
        onConfirmarCierre={() => void cerrar()}
      />
      {contenido}
      {pidiendoTipoMasivo && (
        <ModalTipoTramite
          identificador=""
          descripcion={`Elige el tipo del borrador para las ${seleccionadas.length} placas seleccionadas.`}
          textoConfirmar={`Crear trámites (${seleccionadas.length})`}
          onClose={() => setPidiendoTipoMasivo(false)}
          onConfirmar={(codigo) => {
            setPidiendoTipoMasivo(false);
            void encolar(codigo);
          }}
        />
      )}
      {dictamen && (
        <PanelDictamen
          diagnosticoId={diagnosticoId}
          vehiculoId={dictamen.vehiculo.id}
          identificador={dictamen.vehiculo.placa || dictamen.vehiculo.vin || dictamen.vehiculo.identificador}
          modo="compania"
          puedeCorregir={false}
          onClose={() => setDictamen(null)}
          devolverFocoA={dictamen.disparador}
          tramiteAnalizado={grilla?.lote?.tipo_tramite?.nombre ?? null}
          consultas={(grilla?.vehiculos.find((v) => v.id === dictamen.vehiculo.id) ?? dictamen.vehiculo).celdas.map((c) => ({ fuente: c.fuente, estado: c.estado }))}
          onCrearTramite={
            puedeDerivar && derivaciones.get(dictamen.vehiculo.id)?.elegible
              ? () => {
                  const vehiculoId = dictamen.vehiculo.id;
                  setDictamen(null);
                  setSolicitudTramite((s) => ({ vehiculoId, n: (s?.n ?? 0) + 1 }));
                }
              : undefined
          }
        />
      )}
    </>
  );
}

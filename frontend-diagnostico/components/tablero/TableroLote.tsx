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
import { AccionesVehiculo } from "./AccionesVehiculo";
import { AvisoSaneamientoEnCurso, ModalTipoTramite, saneamientoEnCurso } from "./BotonCrearTramite";
import { CasillaTablero, mensajeSolicitudSaneamiento, seleccionDeFila, type ResultadoSanearVisible } from "./BarraSeleccionTablero";
import { SANEAMIENTO_SOLICITADO } from "@/components/saneamiento/BotonSanear";
import { banderaPanelDeuda, filaDeEstado } from "@/components/saneamiento/BanderaPanelDeuda";
import { PanelDeudaSaneamiento } from "@/components/saneamiento/PanelDeudaSaneamiento";
import { useRegionVivaPantalla } from "@/components/saneamiento/RegionVivaPantalla";
import { mensajeErrorSaneamiento, solicitarSaneamientos } from "@/lib/saneamiento/api";
import { puedeSanear as tieneSanear } from "@/lib/sesion";
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
import { hayDatosSaneamiento, hayVeredictos, leerFiltroSaneamiento, type FiltroSaneamiento, type FiltroTablero } from "@/lib/tablero/filtrosTablero";
import { pedirSondeoNotificaciones } from "@/lib/notificaciones/useNotificaciones";

/** «hh:mm» en hora de Colombia (HU #14562 AC26). */
const HORA = new Intl.DateTimeFormat("es-CO", { hour: "2-digit", minute: "2-digit", hourCycle: "h23", timeZone: "America/Bogota" });

/**
 * Aviso no intrusivo del reintento manual (Punto 3): la paleta `success` de InlineAlert (que anidaría otro `status`).
 * Recibe el foco: el botón pulsado desaparece al dejar de estar pegada. HU #14562 · m-15: sin aria-label, que reemplazaba
 * al texto anunciado; el lector lee «N consultas reenviadas».
 */
function AvisoReenviadas({ ref, children }: { ref: React.Ref<HTMLDivElement>; children: React.ReactNode }) {
  const { color, background, border, Icon } = INLINE_ALERT_TONES.success;
  return (
    <div
      ref={ref}
      tabIndex={-1}
      role="status"
      aria-live="polite"
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
export function TableroLote({
  diagnosticoId,
  desde = null,
  saneamiento = null,
}: {
  diagnosticoId: string;
  desde?: string | null;
  /** HU #14577 AC9: valor crudo de `?saneamiento=` (con|sin enciende el chip; p. ej. la campana navega con «con»). */
  saneamiento?: string | string[] | null;
}) {
  const filtroSaneamientoInicial = leerFiltroSaneamiento(saneamiento);
  const router = useRouter();
  const { sesion, tramitesUrl = "" } = useSesion();
  const puedeVer = sesion?.capacidades.verDiagnosticos ?? false;
  const puedeReprocesar = sesion?.capacidades.reprocesarConsultas ?? false;
  const puedeCargar = sesion?.capacidades.crearDiagnosticos ?? false;
  // HU #13851: PDF por vehículo, Excel y ZIP del lote; sin la capacidad no hay ningún control (la API vuelve a exigirla).
  const puedeDescargar = sesion?.capacidades.descargarReportes ?? false;
  // Punto 4: «Crear trámite» / «Ver trámite #N» solo con la capacidad `derivar` (la API vuelve a exigir el permiso).
  const puedeDerivar = sesion?.capacidades.derivar ?? false;
  // HU #14577 (E3): bandera y «Sanear (N)» solo con la capacidad `sanear` (la API vuelve a exigir el permiso).
  const puedeSanear = tieneSanear(sesion?.capacidades);
  const { anunciar, region: regionViva } = useRegionVivaPantalla();
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
  // HU #14577 AC13 (PS-2): panel «Deuda y saneamiento» del vehículo; `disparador` recibe el foco al cerrarlo.
  const [deuda, setDeuda] = useState<{ vehiculo: FilaVehiculo; disparador: HTMLElement | null } | null>(null);
  // HU #14562 F-05: «Crear trámite» desde el pie del panel del dictamen; el número cambia en cada pedido.
  const [solicitudTramite, setSolicitudTramite] = useState<{ vehiculoId: string; n: number } | null>(null);
  const volver = urlVolverAlListado(desde);
  // HU #14562 AC27: tarjeta-filtro activa (exclusiva); null = sin filtro.
  const [filtro, setFiltro] = useState<FiltroTablero | null>(null);
  // HU #14577 AC9: chip de saneamiento (en la URL, D-09). Una navegación nueva con otro ?saneamiento= manda.
  const [filtroSaneamiento, setFiltroSaneamiento] = useState<FiltroSaneamiento | null>(filtroSaneamientoInicial);
  const [filtroSaneamientoPrevio, setFiltroSaneamientoPrevio] = useState(filtroSaneamientoInicial);
  if (filtroSaneamientoPrevio !== filtroSaneamientoInicial) {
    setFiltroSaneamientoPrevio(filtroSaneamientoInicial);
    setFiltroSaneamiento(filtroSaneamientoInicial);
  }
  function cambiarFiltroSaneamiento(valor: FiltroSaneamiento | null) {
    setFiltroSaneamiento(valor);
    // Sin navegar (no vuelve a pedir la página): el App Router sincroniza replaceState con useSearchParams.
    const url = new URL(window.location.href);
    if (valor) url.searchParams.set("saneamiento", valor);
    else url.searchParams.delete("saneamiento");
    window.history.replaceState(null, "", `${url.pathname}${url.search}${url.hash}`);
  }

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
  // HU #14577 §2.6: sin la clave `saneamiento` en las filas (backend previo) no hay chips ni «Sanear (N)».
  const conDatosSaneamiento = hayDatosSaneamiento(grilla?.vehiculos ?? []);
  const haySaneamiento = puedeSanear && conDatosSaneamiento;
  const hayAlgunSaneamiento = (grilla?.vehiculos ?? []).some((v) => v.saneamiento?.estado != null);
  // HU #14577 AC13: «Ver deuda» de la fila (solo terminados) con un backend que ya expone el saneamiento (D-102).
  const hayVerDeuda = conDatosSaneamiento && (grilla?.vehiculos ?? []).some((v) => v.terminado);
  // HU #14577 AC10: la casilla se habilita si la fila es elegible para alguna acción que el usuario tiene.
  const haySeleccion = hayDerivacion || haySaneamiento;
  const selecciones = useMemo(
    () => new Map((grilla?.vehiculos ?? []).map((v) => [v.id, seleccionDeFila(v, { derivar: hayDerivacion, sanear: haySaneamiento })])),
    [grilla, hayDerivacion, haySaneamiento],
  );
  const elegibles = (grilla?.vehiculos ?? []).filter((v) => selecciones.get(v.id)?.habilitada).map((v) => v.id);
  // Una placa que dejó de ser elegible (p. ej. otro usuario creó su trámite) sale de la cuenta sola.
  const seleccionadas = elegibles.filter((id) => seleccion.has(id));
  // Cada acción cuenta solo los seleccionados elegibles para ella.
  const seleccionadasDerivar = seleccionadas.filter((id) => selecciones.get(id)?.derivar);
  const seleccionadasSanear = seleccionadas.filter((id) => selecciones.get(id)?.sanear);
  const [sanearEnviando, setSanearEnviando] = useState(false);
  const [errorSanear, setErrorSanear] = useState<string | null>(null);
  const [resultadoSanear, setResultadoSanear] = useState<ResultadoSanearVisible | null>(null);

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
    if (!grilla || encolando || seleccionadasDerivar.length === 0) return;
    const ids = seleccionadasDerivar;
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

  /** HU #14577 AC11: «Sanear (N)» con POST /saneamientos/solicitudes; resultado parcial con los omitidos. */
  async function sanearMasivo() {
    if (!grilla || sanearEnviando || seleccionadasSanear.length === 0) return;
    const ids = seleccionadasSanear;
    setSanearEnviando(true);
    setErrorSanear(null);
    try {
      const r = await solicitarSaneamientos(ids);
      const omitidos = new Set(r.omitidos.map((o) => o.vehiculo_id));
      const solicitadas = new Set(ids.filter((id) => !omitidos.has(id)));
      actualizar((g) => ({ ...g, vehiculos: g.vehiculos.map((v) => (solicitadas.has(v.id) ? { ...v, saneamiento: SANEAMIENTO_SOLICITADO } : v)) }));
      // Las solicitadas salen de la selección; las omitidas quedan marcadas.
      setSeleccion((s) => new Set([...s].filter((id) => !solicitadas.has(id))));
      const mensaje = mensajeSolicitudSaneamiento(r);
      setResultadoSanear((previo) => ({ n: (previo?.n ?? 0) + 1, ...mensaje }));
      // UA-4: un solo canal. Si queda selección, el foco se queda en «Sanear» y anuncia la región viva; si no, el foco va al
      // aviso (BarraSeleccionTablero) y ese foco ya lo lee.
      if (seleccionadas.some((id) => !solicitadas.has(id))) anunciar(mensaje.texto);
      refrescar();
    } catch (e) {
      if (e instanceof ApiError && e.status === 403) {
        router.replace(`/403?code=${encodeURIComponent(e.code ?? "SIN_PERMISO")}`);
        return;
      }
      setErrorSanear(mensajeErrorSaneamiento(e, "No pudimos solicitar el saneamiento. Intenta de nuevo."));
    } finally {
      setSanearEnviando(false);
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

  const actualizarSaneamientoFila = (vehiculoId: string, saneamientoFila: FilaVehiculo["saneamiento"]) =>
    actualizar((g) => ({ ...g, vehiculos: g.vehiculos.map((f) => (f.id === vehiculoId ? { ...f, saneamiento: saneamientoFila } : f)) }));

  // FE-2 (spec-ui §2.3): las acciones de la fila van en el menú «Acciones» (desplegable + mensajes; UA-1 retiró el chip del saneamiento).
  const accionVehiculo =
    // HU #14584 §6.4: «Ver dictamen» también vive en el menú, así que basta con que alguna fila traiga veredicto.
    puedeDescargar || puedeReprocesar || puedeDerivar || puedeSanear || hayAlgunSaneamiento || hayVerDeuda || hayVeredictos(grilla?.vehiculos ?? [])
      ? (v: FilaVehiculo) =>
          grilla && (
            <AccionesVehiculo
              diagnosticoId={grilla.id}
              vehiculo={v}
              estadoLote={grilla.estado}
              modo={grilla.modo}
              tramiteAnalizado={grilla.lote?.tipo_tramite?.nombre ?? null}
              tramitesUrl={tramitesUrl}
              empresaId={sesion?.empresa.id}
              capacidades={{ sanear: puedeSanear, derivar: puedeDerivar, descargar: puedeDescargar, reprocesar: puedeReprocesar }}
              conDatosSaneamiento={conDatosSaneamiento}
              // D-29: «Ver saneamiento» y «Ver deuda» abren PS-2; el foco vuelve al disparador «Acciones» de la fila.
              onAbrirDeuda={(disparador) => setDeuda({ vehiculo: v, disparador })}
              onCambioSaneamiento={(saneamiento) => actualizarSaneamientoFila(v.id, saneamiento)}
              onCreadoTramite={(vid, tramite) => actualizar((g) => marcarConTramite(g, vid, tramite))}
              onRefrescar={refrescar}
              onAnunciar={anunciar}
              solicitudCrear={solicitudTramite?.vehiculoId === v.id ? solicitudTramite.n : undefined}
              reintentar={() => reintentarVehiculo(diagnosticoId, v.id)}
              onReintentado={(r) => alReintentar(r, v.id)}
              onErrorReintento={alFallarReproceso}
              onVerDictamen={(disparador) => setDictamen({ vehiculo: v, disparador })}
            />
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

  // HU #14577 AC12: cuántos de los que se van a crear tienen un saneamiento en curso (aviso de la confirmación masiva).
  const enCursoMasivo = (grilla?.vehiculos ?? []).filter((v) => seleccionadasDerivar.includes(v.id) && saneamientoEnCurso(v)).length;
  const avisoMasivo = enCursoMasivo > 0 ? <AvisoSaneamientoEnCurso cantidad={enCursoMasivo} /> : undefined;

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
            haySeleccion={haySeleccion}
            elegibles={elegibles.length}
            seleccionadas={seleccionadas.length}
            onSeleccionarTodas={() => setSeleccion(new Set(elegibles))}
            onQuitar={() => setSeleccion(new Set())}
            crear={hayDerivacion ? { cantidad: seleccionadasDerivar.length, enviando: encolando, onCrear: alCrearMasivo, error: errorMasivo } : undefined}
            sanear={
              haySaneamiento
                ? { cantidad: seleccionadasSanear.length, enviando: sanearEnviando, onSanear: () => void sanearMasivo(), error: errorSanear, resultado: resultadoSanear }
                : undefined
            }
            totalPegadas={grilla.total_pegadas ?? 0}
            puedeReprocesar={puedeReprocesar}
            onReintentado={(r) => alReintentar(r)}
            onErrorReintento={alFallarReproceso}
          />
          <GrillaLote
            grilla={grilla}
            filtro={filtro}
            filtroSaneamiento={conDatosSaneamiento ? filtroSaneamiento : null}
            onFiltroSaneamiento={conDatosSaneamiento ? cambiarFiltroSaneamiento : undefined}
            puedeReprocesar={puedeReprocesar}
            onReprocesada={alReprocesar}
            onErrorReproceso={alFallarReproceso}
            accionVehiculo={accionVehiculo}
            tituloAccionVehiculo="Acciones"
            casillaVehiculo={
              haySeleccion
                ? (v) => (
                    <CasillaTablero
                      vehiculo={v}
                      seleccion={selecciones.get(v.id) ?? seleccionDeFila(v, { derivar: hayDerivacion, sanear: haySaneamiento })}
                      marcada={seleccion.has(v.id)}
                      onCambiar={cambiarSeleccion}
                    />
                  )
                : undefined
            }
            onQuitarFiltro={() => {
              setFiltro(null);
              if (filtroSaneamiento) cambiarFiltroSaneamiento(null);
            }}
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
      {regionViva}
      {pidiendoTipoMasivo && (
        <ModalTipoTramite
          identificador=""
          descripcion={`Elige el tipo del borrador para las ${seleccionadasDerivar.length} placas seleccionadas.`}
          textoConfirmar={`Crear trámites (${seleccionadasDerivar.length})`}
          aviso={avisoMasivo}
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
          // HU #14577 AC13 / D-15: «Ver deuda y sanear» cierra el dictamen y abre PS-2; al cerrar, el foco vuelve a la fila.
          onVerDeuda={
            conDatosSaneamiento
              ? () => {
                  const { vehiculo, disparador } = dictamen;
                  setDictamen(null);
                  setDeuda({ vehiculo, disparador });
                }
              : undefined
          }
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
      {deuda && grilla && (
        <PanelDeudaSaneamiento
          key={deuda.vehiculo.id}
          diagnosticoId={grilla.id}
          vehiculoId={deuda.vehiculo.id}
          identificador={placaDe(deuda.vehiculo)}
          puedeSanear={puedeSanear}
          puedeDescargar={puedeDescargar}
          devolverFocoA={deuda.disparador}
          onClose={() => setDeuda(null)}
          // La fila del tablero sigue al panel (optimista y confirmado); el sondeo trae luego el valor del servidor.
          onCambioEstado={(estado) => actualizarSaneamientoFila(deuda.vehiculo.id, filaDeEstado(estado))}
          accionBandera={banderaPanelDeuda(grilla.id, deuda.vehiculo)}
        />
      )}
    </>
  );
}

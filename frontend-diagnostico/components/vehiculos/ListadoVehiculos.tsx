"use client";

import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { BarraSeleccion } from "@flit/ui/BarraSeleccion";
import { Button } from "@flit/ui/Button";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { Icono } from "@flit/ui/Icono";
import { iconoComo } from "@flit/ui/iconoComo";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { Pagination } from "@flit/ui/Pagination";
import { Placa } from "@flit/ui/Placa";
import {
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
} from "@flit/ui/table-styles";
import {
  ACCION_FILA_CLS,
  BOTON_PRIMARIO_CLS,
  BOTON_REINTENTAR_CLS,
  BOTON_SECUNDARIO_CLS,
  FOCO_CLS,
  MARCA_CLS,
  TARJETA_CLS,
} from "@/components/configuracion/estilos";
import { IconoDxBandera } from "@/components/dx/iconos/IconoDxBandera";
import { IconoDxDeuda } from "@/components/dx/iconos/IconoDxDeuda";
import { banderaPanelDeuda, filaDeEstado } from "@/components/saneamiento/BanderaPanelDeuda";
import { BotonSanear, SANEAMIENTO_SOLICITADO } from "@/components/saneamiento/BotonSanear";
import { EtiquetaSaneamiento } from "@/components/saneamiento/EtiquetaSaneamiento";
import { PanelDeudaSaneamiento } from "@/components/saneamiento/PanelDeudaSaneamiento";
import { useRegionVivaPantalla } from "@/components/saneamiento/RegionVivaPantalla";
import { fechaDelDato } from "@/components/tablero/CeldaSemaforo";
import { mensajeSolicitudSaneamiento, textoSeleccionadas, type ResultadoSanearVisible } from "@/components/tablero/BarraSeleccionTablero";
import { CABECERA_TABLA_CLS, CABECERA_TABLA_ESTILO, CELDA_TABLA_CLS, Etiqueta, FILA_TABLA_CLS, type TonoEtiqueta } from "@/components/tablero/vistasDetalle/comun";
import { ApiError } from "@/lib/api";
import { nombreModo } from "@/lib/diagnosticos/listado";
import {
  MAXIMO_DEUDA_VARIAS,
  MAXIMO_SOLICITUD_MASIVA,
  descargarExcelDeudaVarias,
  mensajeErrorSaneamiento,
  solicitarSaneamientos,
  type SaneamientoFila,
} from "@/lib/saneamiento/api";
import { ROTULO_MOTIVO_NO_ELEGIBLE, deLaPlaca, laPlaca } from "@/lib/saneamiento/estados";
import {
  filtrosVehiculosDesdeUrl,
  identificadorFila,
  hayFiltrosVehiculos,
  listarVehiculos,
  traducirErrorVehiculos,
  urlVehiculos,
  type ErrorVehiculos,
  type FilaVehiculoListado,
  type FiltrosVehiculos as Filtros,
  type ListaVehiculos,
} from "@/lib/vehiculos/listado";
import { FiltrosVehiculos } from "./FiltrosVehiculos";

// PS-3 «Vehículos» del inicio (spec-ui §4; plan §7.4 y §6.6; HU #14577 AC21–AC23). Todo valor visual sale de tokens
// (--di-*, --badge-*, recetas del kit); sin hex. Cuatro estados del patrón de Diagnóstico (D-01).

export interface ListadoVehiculosProps {
  /** Capacidad `crearDiagnosticos`: acción «Diagnosticar un vehículo» del vacío. */
  puedeCrear: boolean;
  /** Capacidad `sanear`: bandera por fila y «Sanear (N)». */
  puedeSanear: boolean;
  /** Capacidad `descargarReportes`: «Descargar deuda (N)» y las descargas del panel PS-2. */
  puedeDescargar: boolean;
}

type Carga = { tipo: "cargando" } | { tipo: "error"; error: ErrorVehiculos } | { tipo: "listo"; lista: ListaVehiculos };

const COLUMNAS = ["Placa", "Diagnóstico", "Fecha", "Modo", "Estado", "Dictamen", "Saneamiento", "Trámite", "Acciones"] as const;
const CELDA_CLS = `${CELDA_TABLA_CLS} ${TABLA_MOVIL_CELL_CLS}`;
const ERROR_CLS = "text-xs font-semibold text-[color:var(--di-danger)]";
const ENLACE_CLS = `inline-flex items-center gap-1 rounded-full text-xs font-semibold text-[var(--flit-brand-ink)] underline underline-offset-2 ${FOCO_CLS}`;
const SELECT_TAMANO_CLS = `h-9 rounded-[9px] border border-input bg-card px-2 text-sm text-flit-primary ${FOCO_CLS}`;

const ERROR_DESCARGA = "No pudimos generar el archivo. Intenta de nuevo.";
const ERROR_SANEAR = "No pudimos solicitar el saneamiento. Intenta de nuevo.";
/** D-108: la casilla de una placa en proceso, para quien solo puede descargar, dice lo mismo que la bandera. */
const MOTIVO_EN_PROCESO = ROTULO_MOTIVO_NO_ELEGIBLE.NO_TERMINADO;

const IconoBuscar = iconoComo("buscar");
const IconoPortapapeles = iconoComo("portapapeles");

const terminado = (f: FilaVehiculoListado) => f.estado === "TERMINADO";
const contarVehiculos = (n: number) => (n === 1 ? "1 vehículo" : `${n} vehículos`);
/** D-104: `Placa` del kit solo para placas (hasta 7 letras y números); un VIN u otro identificador va en texto. */
const esPlaca = (texto: string) => /^[A-Z0-9]{1,7}$/.test(texto);

const ESTADO_VEHICULO: Record<FilaVehiculoListado["estado"], { texto: string; tono: TonoEtiqueta }> = {
  EN_PROCESO: { texto: "En proceso", tono: "info" },
  TERMINADO: { texto: "Terminado", tono: "success" },
};

function dictamenDe(f: FilaVehiculoListado): { texto: string; tono: TonoEtiqueta } {
  if (f.veredicto === "VIABLE") return { texto: "Viable", tono: "success" };
  if (f.veredicto === "BLOQUEADO") return { texto: "Bloqueado", tono: "danger" };
  return { texto: "Sin dictamen", tono: "neutral" };
}

/** Selección de una fila: para qué acción cuenta y, deshabilitada, por qué (unión con « · » sin repetir). */
interface SeleccionFila {
  habilitada: boolean;
  sanear: boolean;
  descargar: boolean;
  motivo?: string;
}

function seleccionDe(f: FilaVehiculoListado, puedeSanear: boolean, puedeDescargar: boolean): SeleccionFila {
  const sanear = puedeSanear && f.saneamiento?.elegible === true;
  const descargar = puedeDescargar && terminado(f);
  const habilitada = sanear || descargar;
  if (habilitada) return { habilitada, sanear, descargar };
  const motivos: string[] = [];
  const m = f.saneamiento?.motivo_no_elegible;
  if (puedeSanear && m && Object.prototype.hasOwnProperty.call(ROTULO_MOTIVO_NO_ELEGIBLE, m)) motivos.push(ROTULO_MOTIVO_NO_ELEGIBLE[m]);
  if (puedeDescargar && !terminado(f)) motivos.push(MOTIVO_EN_PROCESO);
  const unicos = [...new Set(motivos)];
  return { habilitada, sanear, descargar, motivo: unicos.length ? unicos.join(" · ") : undefined };
}

/** Casilla «Seleccionar la placa X» (sin identificador, «Seleccionar el vehículo sin placa»): deshabilitada, el motivo va en un texto `hidden` por `aria-describedby` y en `title`. */
function Casilla({ placa, seleccion, marcada, onCambiar }: { placa: string; seleccion: SeleccionFila; marcada: boolean; onCambiar: (marcada: boolean) => void }) {
  const motivoId = `${useId()}-motivo`;
  const motivo = seleccion.habilitada ? undefined : seleccion.motivo;
  return (
    <>
      <input
        type="checkbox"
        aria-label={`Seleccionar ${laPlaca(placa)}`}
        aria-describedby={motivo ? motivoId : undefined}
        checked={seleccion.habilitada && marcada}
        disabled={!seleccion.habilitada}
        title={motivo}
        onChange={(e) => onCambiar(e.target.checked)}
        className={`${MARCA_CLS} disabled:cursor-not-allowed disabled:opacity-50`}
      />
      {motivo && (
        <span id={motivoId} hidden>
          {motivo}
        </span>
      )}
    </>
  );
}

/** Botón de la barra: con N = 0 o sobre el tope queda `aria-disabled` (sigue en el Tab) con el motivo como descripción. */
function AccionBarra(props: {
  variante: "primary" | "secondary";
  cantidad: number;
  enviando: boolean;
  textoEnvio: string;
  texto: string;
  icono: ReactNode;
  bloqueo: string | null;
  onClick: () => void;
}) {
  const { variante, cantidad, enviando, textoEnvio, texto, icono, bloqueo, onClick } = props;
  const bloqueoId = `${useId()}-bloqueo`;
  return (
    <>
      <Button
        variant={variante}
        cargando={enviando}
        aria-disabled={bloqueo ? "true" : undefined}
        aria-describedby={bloqueo ? bloqueoId : undefined}
        className={bloqueo ? "cursor-not-allowed opacity-60" : undefined}
        onClick={() => {
          if (!bloqueo && !enviando) onClick();
        }}
      >
        {enviando ? (
          textoEnvio
        ) : (
          <>
            {icono}
            {`${texto} (${cantidad})`}
          </>
        )}
      </Button>
      {bloqueo && (
        <span id={bloqueoId} hidden>
          {bloqueo}
        </span>
      )}
    </>
  );
}

/**
 * Grilla de placas de la compañía (pestaña «Vehículos» del inicio). Filtros en la URL (`?vista=vehiculos&…`, lib/vehiculos);
 * cada cambio la reescribe, vuelve a la página 1 y limpia la selección. Tabla con placa, diagnóstico, fecha, modo, estado,
 * dictamen, saneamiento y trámite; por fila, la bandera (BotonSanear), «Abrir diagnóstico» (tablero del lote) y «Ver deuda»
 * (PS-2, solo terminados). La fila no es clicable entera: tiene varias acciones. Selección por página con «Sanear (N)»
 * (POST /saneamientos/solicitudes, como el tablero) y «Descargar deuda (N)» (Excel de 1 a 200 placas). Al cambiar de página
 * el foco va a la tabla y se anuncia «Página p de n». Por debajo de 900 px cada fila es una tarjeta (TABLA_MOVIL_*).
 */
export function ListadoVehiculos({ puedeCrear, puedeSanear, puedeDescargar }: ListadoVehiculosProps) {
  const router = useRouter();
  const params = useSearchParams();
  const clave = params.toString();
  const [claveVista, setClaveVista] = useState(clave);
  const [filtros, setFiltros] = useState<Filtros>(() => filtrosVehiculosDesdeUrl(params));
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);
  const [seleccion, setSeleccion] = useState<ReadonlySet<string>>(() => new Set());
  const [panel, setPanel] = useState<{ fila: FilaVehiculoListado; disparador: HTMLElement | null } | null>(null);
  const [sanearEnviando, setSanearEnviando] = useState(false);
  const [errorSanear, setErrorSanear] = useState<string | null>(null);
  const [resultadoSanear, setResultadoSanear] = useState<ResultadoSanearVisible | null>(null);
  const [descargando, setDescargando] = useState(false);
  const [errorDescarga, setErrorDescarga] = useState<string | null>(null);
  const { anunciar, region } = useRegionVivaPantalla();
  // Tras cambiar de página: el foco va a la tabla nueva y se anuncia la página (spec-ui §4.5).
  const focoTablaPendiente = useRef(false);
  const resultadoRef = useRef<HTMLDivElement>(null);

  // La URL cambió desde fuera (atrás/adelante, enlace compartido): los filtros la siguen.
  if (clave !== claveVista) {
    setClaveVista(clave);
    const deUrl = filtrosVehiculosDesdeUrl(new URLSearchParams(clave));
    if (urlVehiculos(deUrl) !== urlVehiculos(filtros)) {
      setFiltros(deUrl);
      setCarga({ tipo: "cargando" });
      setSeleccion(new Set());
    }
  }

  useEffect(() => {
    const control = new AbortController();
    listarVehiculos(filtros, control.signal).then(
      (lista) => {
        if (control.signal.aborted) return;
        setCarga({ tipo: "listo", lista });
        if (focoTablaPendiente.current) anunciar(`Página ${lista.pagina} de ${Math.max(1, Math.ceil(lista.total / lista.tamano))}`);
      },
      (error: unknown) => {
        if (!control.signal.aborted) setCarga({ tipo: "error", error: traducirErrorVehiculos(error) });
      },
    );
    return () => control.abort();
  }, [filtros, intento, anunciar]);

  const acceso = carga.tipo === "error" && carga.error.tipo === "ACCESO" ? carga.error.code : null;
  useEffect(() => {
    if (acceso) router.replace(`/403?code=${encodeURIComponent(acceso)}`);
  }, [acceso, router]);

  const lista = carga.tipo === "listo" ? carga.lista : null;
  const filas = lista?.vehiculos ?? [];
  const hayCasillas = puedeSanear || puedeDescargar;
  const selecciones = new Map(filas.map((f) => [f.vehiculo_id, seleccionDe(f, puedeSanear, puedeDescargar)]));
  const elegibles = filas.filter((f) => selecciones.get(f.vehiculo_id)?.habilitada).map((f) => f.vehiculo_id);
  const seleccionadas = elegibles.filter((id) => seleccion.has(id));
  const paraSanear = seleccionadas.filter((id) => selecciones.get(id)?.sanear);
  const paraDescargar = seleccionadas.filter((id) => selecciones.get(id)?.descargar);
  const seleccionVacia = seleccionadas.length === 0;

  useEffect(() => {
    // El foco se queda en «Sanear»; si la barra desapareció (selección vacía), pasa al resultado (patrón del tablero).
    if (resultadoSanear && seleccionVacia) resultadoRef.current?.focus();
  }, [resultadoSanear, seleccionVacia]);

  function limpiarAvisos() {
    setSeleccion(new Set());
    setErrorSanear(null);
    setResultadoSanear(null);
    setErrorDescarga(null);
  }

  /** Cambia filtros: sin página explícita vuelve a la 1; reescribe la URL sin agregar entradas al historial. */
  function aplicar(cambios: Partial<Filtros>) {
    const siguiente: Filtros = { ...filtros, ...cambios, pagina: cambios.pagina ?? 1 };
    if (urlVehiculos(siguiente) === urlVehiculos(filtros)) return;
    setFiltros(siguiente);
    setCarga({ tipo: "cargando" });
    limpiarAvisos();
    router.replace(urlVehiculos(siguiente), { scroll: false });
  }

  function recargar() {
    if (carga.tipo === "error" && carga.error.tipo === "AVISO" && carga.error.reiniciarPagina) {
      aplicar({ pagina: 1 });
      return;
    }
    setCarga({ tipo: "cargando" });
    limpiarAvisos();
    setIntento((n) => n + 1);
  }

  /** Relectura sin loader (409 de la bandera: el estado cambió en el servidor). */
  const refrescar = () => setIntento((n) => n + 1);

  const limpiar = () => aplicar({ placa: "", estado: null, veredicto: null, saneamiento: null, saneamientoEstado: null, modo: null, conTramite: null, desde: "", hasta: "" });

  function actualizarFila(vehiculoId: string, saneamiento: SaneamientoFila) {
    setCarga((c) =>
      c.tipo === "listo" ? { ...c, lista: { ...c.lista, vehiculos: c.lista.vehiculos.map((f) => (f.vehiculo_id === vehiculoId ? { ...f, saneamiento } : f)) } } : c,
    );
  }

  function cambiarSeleccion(vehiculoId: string, marcada: boolean) {
    setSeleccion((s) => {
      const nueva = new Set(s);
      if (marcada) nueva.add(vehiculoId);
      else nueva.delete(vehiculoId);
      return nueva;
    });
  }

  const negar = (e: unknown) => {
    if (!(e instanceof ApiError) || e.status !== 403) return false;
    router.replace(`/403?code=${encodeURIComponent(e.code ?? "SIN_PERMISO")}`);
    return true;
  };

  /** «Sanear (N)», como en el tablero (spec-ui §2.4): las solicitadas cambian en local; las omitidas quedan marcadas (D-112). */
  async function sanear() {
    if (sanearEnviando || paraSanear.length === 0) return;
    const ids = paraSanear;
    setSanearEnviando(true);
    setErrorSanear(null);
    try {
      const r = await solicitarSaneamientos(ids);
      const omitidos = new Set(r.omitidos.map((o) => o.vehiculo_id));
      const solicitadas = new Set(ids.filter((id) => !omitidos.has(id)));
      for (const id of solicitadas) actualizarFila(id, SANEAMIENTO_SOLICITADO);
      setSeleccion((s) => new Set([...s].filter((id) => !solicitadas.has(id))));
      const mensaje = mensajeSolicitudSaneamiento(r);
      setResultadoSanear((previo) => ({ n: (previo?.n ?? 0) + 1, ...mensaje }));
      // UA-4: un solo canal. Con selección restante el foco se queda en «Sanear» y anuncia la región viva; si la selección
      // queda vacía, el foco va al aviso y ese foco ya lo lee.
      if (seleccionadas.some((id) => !solicitadas.has(id))) anunciar(mensaje.texto);
    } catch (e) {
      if (!negar(e)) setErrorSanear(mensajeErrorSaneamiento(e, ERROR_SANEAR));
    } finally {
      setSanearEnviando(false);
    }
  }

  /** «Descargar deuda (N)»: un solo Excel por POST /vehiculos/deuda/excel (1 a 200 placas terminadas). */
  async function descargar() {
    if (descargando || paraDescargar.length === 0 || paraDescargar.length > MAXIMO_DEUDA_VARIAS) return;
    setDescargando(true);
    setErrorDescarga(null);
    try {
      await descargarExcelDeudaVarias(paraDescargar);
      anunciar("Descarga lista.");
    } catch (e) {
      // D-43: texto fijo, nunca el mensaje ni el código del servidor (AC43).
      if (!negar(e)) setErrorDescarga(ERROR_DESCARGA);
    } finally {
      setDescargando(false);
    }
  }

  const errorCampo = carga.tipo === "error" && carga.error.tipo === "CAMPO" ? carga.error : null;

  let contenido: ReactNode;
  if (carga.tipo === "cargando" || acceso) {
    contenido = <VehicleLoader label="Cargando vehículos…" />;
  } else if (carga.tipo === "error" && carga.error.tipo === "CAMPO") {
    contenido = (
      <InlineAlert tone="warning" title="Revisa los filtros">
        Corrige el filtro marcado para ver los vehículos.
      </InlineAlert>
    );
  } else if (carga.tipo === "error") {
    const reiniciar = carga.error.tipo === "AVISO" && carga.error.reiniciarPagina;
    contenido = (
      <InlineAlert
        tone="error"
        title="No pudimos cargar los vehículos"
        action={
          <button type="button" onClick={recargar} className={BOTON_REINTENTAR_CLS}>
            Reintentar
          </button>
        }
      >
        {/* AC43 (D-107): texto fijo en español; nunca el mensaje, el código ni el estado HTTP del servidor. */}
        {reiniciar ? "La página pedida no es válida. Vuelve a la primera página." : "Revisa tu conexión e intenta de nuevo."}
      </InlineAlert>
    );
  } else if (carga.lista.vehiculos.length === 0 && hayFiltrosVehiculos(filtros)) {
    contenido = (
      <EstadoVacio
        anunciar
        nivelTitulo={3}
        icono={<IconoBuscar className="h-7 w-7" aria-hidden="true" strokeWidth={1.5} />}
        titulo="Ningún vehículo coincide"
        mensaje="Prueba con otros filtros."
        acciones={[
          <button key="limpiar" type="button" onClick={limpiar} className={BOTON_SECUNDARIO_CLS}>
            Limpiar filtros
          </button>,
        ]}
      />
    );
  } else if (carga.lista.vehiculos.length === 0) {
    contenido = (
      <EstadoVacio
        nivelTitulo={3}
        icono={<IconoPortapapeles className="h-7 w-7" aria-hidden="true" strokeWidth={1.5} />}
        titulo="Aún no hay vehículos"
        mensaje="Aquí verás todas las placas de tus diagnósticos, en proceso y terminadas."
        acciones={
          puedeCrear
            ? [
                <Link key="express" href="/diagnosticos/nuevo?modo=express" className={BOTON_PRIMARIO_CLS}>
                  Diagnosticar un vehículo
                </Link>,
              ]
            : undefined
        }
      />
    );
  } else {
    const { lista: datos } = carga;
    const columnas = hayCasillas ? (["Selección", ...COLUMNAS] as const) : COLUMNAS;
    const bloqueoSanear =
      paraSanear.length === 0
        ? "Ninguno de los seleccionados se puede sanear"
        : paraSanear.length > MAXIMO_SOLICITUD_MASIVA
          ? `Máximo ${MAXIMO_SOLICITUD_MASIVA} vehículos por solicitud`
          : null;
    const bloqueoDescargar =
      paraDescargar.length === 0
        ? "Ninguno de los seleccionados tiene la deuda lista"
        : paraDescargar.length > MAXIMO_DEUDA_VARIAS
          ? `Máximo ${MAXIMO_DEUDA_VARIAS} placas por descarga`
          : null;
    contenido = (
      <div className="flex flex-col gap-3">
        {hayCasillas && (
          <div className="flex flex-col gap-2">
            <div className="flex flex-wrap items-center gap-2">
              <button
                type="button"
                onClick={() => setSeleccion(new Set(elegibles))}
                disabled={elegibles.length === 0 || sanearEnviando || descargando}
                className={BOTON_SECUNDARIO_CLS}
              >
                <Icono nombre="check" className="h-4 w-4" />
                Seleccionar todos los elegibles
              </button>
            </div>
            <BarraSeleccion
              cantidad={seleccionadas.length}
              textoSeleccion={textoSeleccionadas}
              ayuda="Cada acción cuenta solo los vehículos que cumplen su regla."
              onQuitar={sanearEnviando || descargando ? undefined : () => setSeleccion(new Set())}
              acciones={
                <>
                  {puedeSanear && (
                    <AccionBarra
                      variante="primary"
                      cantidad={paraSanear.length}
                      enviando={sanearEnviando}
                      textoEnvio="Solicitando…"
                      texto="Sanear"
                      icono={<IconoDxBandera tamano={16} />}
                      bloqueo={bloqueoSanear}
                      onClick={() => void sanear()}
                    />
                  )}
                  {puedeDescargar && (
                    <AccionBarra
                      variante={puedeSanear ? "secondary" : "primary"}
                      cantidad={paraDescargar.length}
                      enviando={descargando}
                      textoEnvio="Descargando…"
                      texto="Descargar deuda"
                      icono={<Icono nombre="nube-bajar" className="h-4 w-4" />}
                      bloqueo={bloqueoDescargar}
                      onClick={() => void descargar()}
                    />
                  )}
                </>
              }
            />
            {errorSanear && (
              <p role="alert" aria-label="Resultado de sanear" className={ERROR_CLS}>
                {errorSanear}
              </p>
            )}
            {errorDescarga && (
              <p role="alert" aria-label="Resultado de descargar deuda" className={ERROR_CLS}>
                {errorDescarga}
              </p>
            )}
            {resultadoSanear && (
              <div ref={resultadoRef} tabIndex={-1} className={`rounded-[9px] ${FOCO_CLS}`}>
                <InlineAlert tone={resultadoSanear.tono} title={resultadoSanear.titulo} vivo={false}>
                  {resultadoSanear.texto}
                </InlineAlert>
              </div>
            )}
          </div>
        )}
        <div className="overflow-x-auto rounded-xl border border-border max-[900px]:overflow-visible max-[900px]:border-0">
          <table
            ref={(el) => {
              if (el && focoTablaPendiente.current) {
                focoTablaPendiente.current = false;
                el.focus();
              }
            }}
            tabIndex={-1}
            aria-label="Vehículos de la compañía"
            className={`w-full min-w-[1080px] border-collapse text-sm ${FOCO_CLS} ${TABLA_MOVIL_TABLE_CLS}`}
          >
            <thead className={TABLA_MOVIL_THEAD_CLS}>
              <tr>
                {columnas.map((t) => (
                  <th key={t} scope="col" className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
                    {t === "Selección" ? <span className="sr-only">Selección</span> : t}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className={TABLA_MOVIL_TBODY_CLS}>
              {datos.vehiculos.map((f) => {
                // UA-5: la placa, o el VIN, o el identificador (como el tablero); sin ninguno, «Sin placa».
                const placa = identificadorFila(f);
                const seleccionFila = selecciones.get(f.vehiculo_id) ?? seleccionDe(f, puedeSanear, puedeDescargar);
                const estado = ESTADO_VEHICULO[f.estado] ?? ESTADO_VEHICULO.EN_PROCESO;
                const dictamen = dictamenDe(f);
                return (
                  <tr key={f.vehiculo_id} className={`${FILA_TABLA_CLS} bg-card ${TABLA_MOVIL_ROW_CLS}`}>
                    {hayCasillas && (
                      <td data-label="Selección" className={CELDA_CLS}>
                        <Casilla placa={placa} seleccion={seleccionFila} marcada={seleccion.has(f.vehiculo_id)} onCambiar={(m) => cambiarSeleccion(f.vehiculo_id, m)} />
                      </td>
                    )}
                    <th scope="row" data-label="Placa" className={`${CELDA_CLS} ${TABLA_MOVIL_CELL_TITULO_CLS} text-left font-semibold`}>
                      {esPlaca(placa) ? <Placa placa={placa} /> : placa ? <span className="break-all font-mono">{placa}</span> : <span>Sin placa</span>}
                    </th>
                    <td data-label="Diagnóstico" className={`${CELDA_CLS} break-words`}>
                      {f.diagnostico_nombre}
                    </td>
                    <td data-label="Fecha" className={`${CELDA_CLS} whitespace-nowrap tabular-nums`}>
                      {fechaDelDato(f.diagnostico_creado_en)}
                    </td>
                    <td data-label="Modo" className={CELDA_CLS}>
                      {nombreModo(f.modo)}
                    </td>
                    <td data-label="Estado" className={CELDA_CLS}>
                      <Etiqueta tono={estado.tono}>{estado.texto}</Etiqueta>
                    </td>
                    <td data-label="Dictamen" className={CELDA_CLS}>
                      <Etiqueta tono={dictamen.tono}>{dictamen.texto}</Etiqueta>
                    </td>
                    <td data-label="Saneamiento" className={CELDA_CLS}>
                      {f.saneamiento?.estado ? (
                        <EtiquetaSaneamiento estado={f.saneamiento.estado} />
                      ) : (
                        <span role="img" aria-label="Sin saneamiento">
                          —
                        </span>
                      )}
                    </td>
                    <td data-label="Trámite" className={`${CELDA_CLS} tabular-nums`}>
                      {f.tramite ? (
                        `#${f.tramite.numero}`
                      ) : (
                        <span role="img" aria-label="Sin trámite">
                          —
                        </span>
                      )}
                    </td>
                    <td data-label="Acciones" className={CELDA_CLS}>
                      <div className="flex flex-col items-start gap-2 max-[900px]:items-end">
                        <BotonSanear
                          diagnosticoId={f.diagnostico_id}
                          vehiculo={{ id: f.vehiculo_id, placa: f.placa, vin: f.vin, identificador: f.identificador }}
                          saneamiento={f.saneamiento}
                          puedeSanear={puedeSanear}
                          onCambio={(_estado, saneamiento) => actualizarFila(f.vehiculo_id, saneamiento)}
                          onAbrirPanel={(disparador) => setPanel({ fila: f, disparador })}
                          onRefrescar={refrescar}
                          onAnunciar={anunciar}
                        />
                        <Link href={`/diagnosticos/${encodeURIComponent(f.diagnostico_id)}`} aria-label={`Abrir el diagnóstico ${f.diagnostico_nombre}`} className={ENLACE_CLS}>
                          Abrir diagnóstico
                        </Link>
                        {terminado(f) && (
                          <button
                            type="button"
                            aria-haspopup="dialog"
                            aria-label={`Ver deuda ${deLaPlaca(placa)}`}
                            onClick={(e) => setPanel({ fila: f, disparador: e.currentTarget })}
                            className={`${ACCION_FILA_CLS} whitespace-nowrap`}
                          >
                            <IconoDxDeuda tamano={16} className="h-3.5 w-3.5" />
                            Ver deuda
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <p className="text-sm text-muted-foreground">{contarVehiculos(datos.total)}</p>
        {/* UA-2 (rechaza D-105): el pie estándar del kit, con «Filas por página» de TAMANOS_DE_PAGINA. */}
        <Pagination
          page={datos.pagina}
          pageSize={datos.tamano}
          totalCount={datos.total}
          onPageChange={(pagina) => {
            focoTablaPendiente.current = true;
            aplicar({ pagina });
          }}
          onPageSizeChange={(tamano) => aplicar({ tamano, pagina: 1 })}
          ariaLabel="Paginación de vehículos"
          noun="vehículos"
        />
      </div>
    );
  }

  return (
    <section aria-labelledby="listado-vehiculos-titulo" className={`${TARJETA_CLS} flex flex-col gap-4`}>
      <h2 id="listado-vehiculos-titulo" className="text-lg font-semibold text-flit-primary">
        Vehículos
      </h2>
      <FiltrosVehiculos valor={filtros} error={errorCampo} onCambiar={aplicar} onLimpiar={limpiar} onActualizar={recargar} />
      <div>{contenido}</div>
      <div data-region-viva-vehiculos="">{region}</div>
      {panel && (
        <PanelDeudaSaneamiento
          key={panel.fila.vehiculo_id}
          diagnosticoId={panel.fila.diagnostico_id}
          vehiculoId={panel.fila.vehiculo_id}
          identificador={identificadorFila(panel.fila) || "Sin placa"}
          puedeSanear={puedeSanear}
          puedeDescargar={puedeDescargar}
          devolverFocoA={panel.disparador}
          onClose={() => setPanel(null)}
          onCambioEstado={(estado) => actualizarFila(panel.fila.vehiculo_id, filaDeEstado(estado))}
          accionBandera={banderaPanelDeuda(panel.fila.diagnostico_id, {
            id: panel.fila.vehiculo_id,
            placa: panel.fila.placa,
            vin: panel.fila.vin,
            identificador: panel.fila.identificador,
          })}
        />
      )}
    </section>
  );
}

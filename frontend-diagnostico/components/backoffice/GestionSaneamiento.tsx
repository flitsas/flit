"use client";

import { useCallback, useEffect, useId, useRef, useState, useSyncExternalStore, type ReactNode } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Button } from "@flit/ui/Button";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { Icono } from "@flit/ui/Icono";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { NotificacionPersistente } from "@flit/ui/NotificacionPersistente";
import { TarjetaPlegable } from "@flit/ui/TarjetaPlegable";
import { SIN_PERMISO_SANEAMIENTO } from "@/components/backoffice/AccesoSaneamientos";
import { HistorialSaneamiento } from "@/components/backoffice/HistorialSaneamiento";
import { ModalAgregarConcepto, ModalConfirmar, ModalEvidencia, ModalMotivo, type ErrorModal } from "@/components/backoffice/ModalesSaneamiento";
import { ENLACE_CLS, RenglonesEditables, RenglonesLectura, type AccionesRenglon } from "@/components/backoffice/RenglonesGestion";
import { AYUDA_CLS, BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS, CAMPO_CLS, ETIQUETA_CLS, FOCO_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { EtiquetaSaneamiento } from "@/components/saneamiento/EtiquetaSaneamiento";
import { useRegionVivaPantalla } from "@/components/saneamiento/RegionVivaPantalla";
import { EtiquetaTono, SeccionesDeuda } from "@/components/saneamiento/SeccionesDeuda";
import { ApiError } from "@/lib/api";
import {
  actualizarRenglon,
  agregarRenglon,
  anularSaneamiento,
  cerrarSaneamiento,
  editarObservacion,
  obtenerDeudaBackoffice,
  obtenerSaneamientoBackoffice,
  quitarEvidencia,
  quitarRenglon,
  reabrirSaneamiento,
  subirEvidencia,
  tomarSaneamiento,
  urlEvidenciaBackoffice,
  type SaneamientoBackoffice,
} from "@/lib/backoffice/saneamientos";
import { rotuloEmpresa, useNombresEmpresas } from "@/lib/backoffice/useNombresEmpresas";
import { useResumenSaneamientos } from "@/lib/backoffice/useResumenSaneamientos";
import { descargarArchivo } from "@/lib/reportes/api";
import { mensajeErrorSaneamiento, type PanelDeuda, type RenglonSaneamiento } from "@/lib/saneamiento/api";
import { rotuloConcepto } from "@/lib/saneamiento/estados";
import { LIMITES_TEXTO, validarTextoLibre } from "@/lib/saneamiento/textoLibre";
import { fechaCorta } from "@/lib/tablero/detalle";

// PS-5, gestión de un saneamiento en el backoffice (spec-ui §6; plan §7.6; HU #14577 AC29–AC37). Pide en paralelo el
// detalle (historial completo) y la deuda del backoffice (SP-7), cada uno con sus estados. Izquierda: la deuda en lectura
// (SeccionesDeuda de PS-2, plegada por debajo de 1024 px); derecha: la gestión según el estado; pie: el historial. Un 409
// VERSION_DESACTUALIZADA abre la NotificacionPersistente con «Recargar», que trae los datos vigentes y descarta borradores.
// Todo valor visual sale de tokens del kit; sin hex.

export interface GestionSaneamientoProps {
  sid: string;
  /** Compañía dueña del saneamiento (?empresa=). */
  empresa: string;
}

type Carga = { tipo: "cargando" } | { tipo: "error"; error: unknown } | { tipo: "listo" };
type CargaDeuda = { tipo: "cargando" } | { tipo: "error"; noTerminado: boolean } | { tipo: "listo"; deuda: PanelDeuda };
type Transicion = "anular" | "reabrir" | "no_procede" | "saneado";
type ModalAbierto =
  | { tipo: Transicion }
  | { tipo: "agregar" }
  | { tipo: "evidencia"; renglon: RenglonSaneamiento }
  | { tipo: "quitar-evidencia"; renglon: RenglonSaneamiento }
  | { tipo: "quitar-renglon"; renglon: RenglonSaneamiento };
type EstadoObservacion = "sin_cambios" | "editado" | "guardando" | "guardado" | "error";

const RED = "Revisa tu conexión e intenta de nuevo.";
const GENERICO = "No pudimos guardar el cambio. Intenta de nuevo.";
const ERROR_DESCARGA = "No pudimos generar el archivo. Intenta de nuevo.";
const CIERRE_INCOMPLETO = "Para cerrar como saneado ningún concepto puede quedar pendiente y al menos uno debe quedar saneado.";
const DEMASIADOS_REEMPLAZOS = "Este concepto ya llegó al máximo de reemplazos de la evidencia.";

const ROTULO_OBSERVACION: Record<EstadoObservacion, string> = {
  sin_cambios: "Sin cambios",
  editado: "Sin guardar",
  guardando: "Guardando…",
  guardado: "Guardado",
  error: "No se guardó",
};
const TONO_OBSERVACION: Record<EstadoObservacion, string> = {
  sin_cambios: "text-[color:var(--di-text-tertiary)]",
  editado: "text-[color:var(--di-info)]",
  guardando: "text-[color:var(--di-info)]",
  guardado: "text-[color:var(--di-success)]",
  error: "text-[color:var(--di-danger)]",
};

const ANUNCIO_TRANSICION: Record<Transicion, string> = {
  saneado: "Saneamiento cerrado como saneado.",
  no_procede: "Saneamiento cerrado: no procede.",
  anular: "Saneamiento anulado.",
  reabrir: "Saneamiento reabierto.",
};

/** Copia del registro sin la clave. */
function sinClave<T>(registro: Record<string, T>, clave: string): Record<string, T> {
  const copia = { ...registro };
  delete copia[clave];
  return copia;
}

const codigo = (e: unknown) => (e instanceof ApiError ? e.code : undefined);
const es403 = (e: unknown) => e instanceof ApiError && e.status === 403;
const ENLACE_PELIGRO_CLS = `${ENLACE_CLS} text-[color:var(--di-danger)]`;

/** Por encima de 1024 px la deuda va abierta en su columna; por debajo, plegada (spec-ui §6.1, D-14). Sin matchMedia: ancha. */
function useAncha(): boolean {
  return useSyncExternalStore(
    (avisar) => {
      if (typeof window === "undefined" || typeof window.matchMedia !== "function") return () => {};
      const m = window.matchMedia("(min-width: 1024px)");
      m.addEventListener("change", avisar);
      return () => m.removeEventListener("change", avisar);
    },
    () => (typeof window.matchMedia === "function" ? window.matchMedia("(min-width: 1024px)").matches : true),
    () => true,
  );
}

/** Motivo por el que aún no se puede «Cerrar como saneado» (regla del plan §3.3), o null si se puede. */
export function motivoNoCerrarSaneado(renglones: readonly RenglonSaneamiento[], sinGuardar: boolean): string | null {
  if (sinGuardar) return "Espera a que se guarden los cambios.";
  if (renglones.length === 0) return "Para cerrar como saneado agrega al menos un concepto.";
  const pendientes = renglones.filter((r) => r.resultado === "PENDIENTE").length;
  if (pendientes > 0) return `Para cerrar como saneado ningún concepto puede quedar pendiente (faltan ${pendientes}).`;
  if (!renglones.some((r) => r.resultado === "SANEADO")) return "Para cerrar como saneado al menos un concepto debe quedar saneado.";
  return null;
}

export function GestionSaneamiento({ sid, empresa }: GestionSaneamientoProps) {
  const router = useRouter();
  const id = useId();
  const ancha = useAncha();
  const nombres = useNombresEmpresas();
  const resumen = useResumenSaneamientos();
  const { anunciar, region } = useRegionVivaPantalla();

  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [san, setSan] = useState<SaneamientoBackoffice | null>(null);
  // Cada lectura completa del detalle vuelve a montar la tabla: descarta borradores (spec-ui §6.6).
  const [generacion, setGeneracion] = useState(0);
  const [intento, setIntento] = useState(0);
  const [deuda, setDeuda] = useState<CargaDeuda>({ tipo: "cargando" });
  const [intentoDeuda, setIntentoDeuda] = useState(0);
  const [conflicto, setConflicto] = useState(false);
  const [modal, setModal] = useState<ModalAbierto | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const [errorModal, setErrorModal] = useState<ErrorModal | null>(null);
  const [errorCampo, setErrorCampo] = useState<string | null>(null);
  const [errorEvidencia, setErrorEvidencia] = useState<string | null>(null);
  const [tomando, setTomando] = useState(false);
  const [errorTomar, setErrorTomar] = useState<ErrorModal | null>(null);
  const [errores, setErrores] = useState<Record<string, string>>({});
  const [sucios, setSucios] = useState<ReadonlySet<string>>(() => new Set());
  const [descargas, setDescargas] = useState<Record<string, string>>({});
  const [obs, setObs] = useState<{ texto: string; guardado: string; estado: EstadoObservacion; error: string | null }>({
    texto: "",
    guardado: "",
    estado: "sin_cambios",
    error: null,
  });
  const [obsGeneracion, setObsGeneracion] = useState(-1);
  const foco = useRef<(() => HTMLElement | null) | null>(null);

  // La observación toma el valor del servidor en cada lectura completa (no tras su propio guardado).
  if (san && obsGeneracion !== generacion) {
    setObsGeneracion(generacion);
    const valor = san.observacion_flit ?? "";
    setObs({ texto: valor, guardado: valor, estado: "sin_cambios", error: null });
  }

  useEffect(() => {
    const control = new AbortController();
    obtenerSaneamientoBackoffice(sid, empresa, control.signal).then(
      (s) => {
        if (control.signal.aborted) return;
        setSan(s);
        setGeneracion((g) => g + 1);
        setCarga({ tipo: "listo" });
      },
      (error: unknown) => {
        if (!control.signal.aborted) setCarga({ tipo: "error", error });
      },
    );
    return () => control.abort();
  }, [sid, empresa, intento]);

  useEffect(() => {
    const control = new AbortController();
    obtenerDeudaBackoffice(sid, empresa, control.signal).then(
      (d) => {
        if (!control.signal.aborted) setDeuda({ tipo: "listo", deuda: d });
      },
      (error: unknown) => {
        if (!control.signal.aborted) setDeuda({ tipo: "error", noTerminado: codigo(error) === "VEHICULO_NO_TERMINADO" });
      },
    );
    return () => control.abort();
  }, [sid, empresa, intentoDeuda]);

  const ir403 = useCallback(() => router.replace(`/403?code=${SIN_PERMISO_SANEAMIENTO}`), [router]);
  const detalle403 = carga.tipo === "error" && es403(carga.error);
  useEffect(() => {
    if (detalle403) ir403();
  }, [detalle403, ir403]);

  // Foco tras una acción (spec-ui §6.2, §6.3.1, §6.3.2, §6.3.3, §6.5): se intenta en cada render hasta que el destino exista.
  useEffect(() => {
    if (!foco.current) return;
    const destino = foco.current();
    if (destino) {
      foco.current = null;
      destino.focus();
    }
  });

  const enfocar = (destino: () => HTMLElement | null) => {
    foco.current = destino;
  };
  const radioDe = (rid: string) => () => document.querySelector<HTMLElement>(`[data-renglon-resultado="${rid}"] [role="radio"][tabindex="0"]`);
  const tituloConceptos = () => document.getElementById(`${id}-conceptos`);
  const etiquetaEstado = () => document.querySelector<HTMLElement>(`[data-estado-saneamiento="${id}"]`);

  /** Lectura completa: datos vigentes, sin borradores ni conflicto. */
  const recargar = useCallback(() => {
    obtenerSaneamientoBackoffice(sid, empresa).then(
      (s) => {
        setSan(s);
        setErrores({});
        setSucios(new Set());
        setConflicto(false);
        setGeneracion((g) => g + 1);
      },
      (error: unknown) => setCarga({ tipo: "error", error }),
    );
  }, [sid, empresa]);

  /** Lectura tras un cambio que no devuelve el saneamiento (DELETE, alta de concepto): versiones al día, sin tocar borradores. */
  const releer = useCallback(() => {
    obtenerSaneamientoBackoffice(sid, empresa).then(setSan, () => {});
  }, [sid, empresa]);

  const ponerRenglon = (r: RenglonSaneamiento) => setSan((s) => (s ? { ...s, renglones: s.renglones.map((x) => (x.id === r.id ? r : x)) } : s));

  function cerrarModal() {
    if (ocupado) return;
    setModal(null);
    setErrorModal(null);
    setErrorCampo(null);
    setErrorEvidencia(null);
  }

  function abrir(m: ModalAbierto) {
    setErrorModal(null);
    setErrorCampo(null);
    setErrorEvidencia(null);
    setModal(m);
  }

  /** Errores comunes de las escrituras del modal. */
  function errorDeModal(e: unknown) {
    const c = codigo(e);
    if (es403(e)) return ir403();
    if (c === "VERSION_DESACTUALIZADA") {
      setModal(null);
      setConflicto(true);
      return;
    }
    if (c === "NO_EDITABLE") {
      setModal(null);
      recargar();
      return;
    }
    if (c === "TRANSICION_INVALIDA") return setErrorModal({ texto: mensajeErrorSaneamiento(e), recargar: true });
    if (c === "CIERRE_INCOMPLETO") return setErrorModal({ texto: CIERRE_INCOMPLETO });
    if (c === "MOTIVO_REQUERIDO") return setErrorCampo(mensajeErrorSaneamiento(e));
    setErrorModal({ texto: mensajeErrorSaneamiento(e, GENERICO) });
  }

  async function transicion(tipo: Transicion, motivo = "") {
    if (!san || ocupado) return;
    setOcupado(true);
    setErrorModal(null);
    setErrorCampo(null);
    try {
      const r =
        tipo === "anular"
          ? await anularSaneamiento(sid, empresa, motivo, san.version)
          : tipo === "reabrir"
            ? await reabrirSaneamiento(sid, empresa, motivo, san.version)
            : tipo === "no_procede"
              ? await cerrarSaneamiento(sid, empresa, { resultado: "NO_PROCEDE", motivo, version: san.version })
              : await cerrarSaneamiento(sid, empresa, { resultado: "SANEADO", version: san.version });
      setSan(r);
      setErrores({});
      setSucios(new Set());
      setGeneracion((g) => g + 1);
      setModal(null);
      resumen.refrescar();
      anunciar(ANUNCIO_TRANSICION[tipo]);
      enfocar(etiquetaEstado);
    } catch (e) {
      errorDeModal(e);
    } finally {
      setOcupado(false);
    }
  }

  async function tomar() {
    if (tomando) return;
    setTomando(true);
    setErrorTomar(null);
    try {
      const r = await tomarSaneamiento(sid, empresa);
      setSan(r);
      setGeneracion((g) => g + 1);
      resumen.refrescar();
      const n = r.renglones.length;
      anunciar(
        n === 0
          ? "Saneamiento tomado. No hay conceptos: agrega uno o ciérralo como no procede."
          : `Saneamiento tomado. Se ${n === 1 ? "generó 1 concepto" : `generaron ${n} conceptos`}.`,
      );
      enfocar(tituloConceptos);
    } catch (e) {
      if (es403(e)) ir403();
      else setErrorTomar({ texto: mensajeErrorSaneamiento(e, GENERICO), recargar: codigo(e) === "TRANSICION_INVALIDA" });
    } finally {
      setTomando(false);
    }
  }

  // ---------- Renglones ----------

  const quitarError = (rid: string) => setErrores((x) => (rid in x ? sinClave(x, rid) : x));

  const acciones: AccionesRenglon = {
    guardar: async (r) => {
      const problema = validarTextoLibre(r.nota ?? "", LIMITES_TEXTO.nota);
      if (problema) {
        setErrores((x) => ({ ...x, [r.id]: problema }));
        throw new Error(problema);
      }
      try {
        const nuevo = await actualizarRenglon(sid, r.id, empresa, { resultado: r.resultado, nota: r.nota, version: r.version });
        ponerRenglon(nuevo);
        quitarError(r.id);
      } catch (e) {
        const c = codigo(e);
        let texto = RED;
        if (es403(e)) ir403();
        else if (c === "VERSION_DESACTUALIZADA") {
          texto = "Otra persona cambió este concepto.";
          setConflicto(true);
        } else if (c === "NO_EDITABLE") {
          texto = "El saneamiento ya no está en gestión.";
          recargar();
        }
        setErrores((x) => ({ ...x, [r.id]: texto }));
        throw e;
      }
    },
    alEditar: (rid, sucio) =>
      setSucios((s) => {
        if (s.has(rid) === sucio) return s;
        const n = new Set(s);
        if (sucio) n.add(rid);
        else n.delete(rid);
        return n;
      }),
    cargarEvidencia: (r) => abrir({ tipo: "evidencia", renglon: r }),
    quitarEvidencia: (r) => abrir({ tipo: "quitar-evidencia", renglon: r }),
    quitarRenglon: (r) => abrir({ tipo: "quitar-renglon", renglon: r }),
    verEvidencia: (r) => {
      if (descargas[r.id] === "descargando") return;
      setDescargas((d) => ({ ...d, [r.id]: "descargando" }));
      descargarArchivo(urlEvidenciaBackoffice(sid, r.id, empresa), "evidencia.pdf").then(
        () => setDescargas((d) => sinClave(d, r.id)),
        () => setDescargas((d) => ({ ...d, [r.id]: ERROR_DESCARGA })),
      );
    },
  };

  async function subir(renglon: RenglonSaneamiento, archivo: File) {
    setOcupado(true);
    setErrorEvidencia(null);
    try {
      const nuevo = await subirEvidencia(sid, renglon.id, empresa, archivo);
      ponerRenglon(nuevo);
      setModal(null);
      anunciar(`Evidencia cargada para ${rotuloConcepto(renglon)}.`);
      enfocar(() => document.querySelector<HTMLElement>(`[data-ver-evidencia="${renglon.id}"]`));
    } catch (e) {
      const c = codigo(e);
      if (es403(e)) ir403();
      else if (c === "NO_EDITABLE") {
        setModal(null);
        recargar();
      } else if (c === "DEMASIADOS_REEMPLAZOS") setErrorEvidencia(DEMASIADOS_REEMPLAZOS);
      else setErrorEvidencia(mensajeErrorSaneamiento(e, "No pudimos subir el archivo. Intenta de nuevo."));
    } finally {
      setOcupado(false);
    }
  }

  async function confirmarQuitarEvidencia(renglon: RenglonSaneamiento) {
    setOcupado(true);
    setErrorModal(null);
    try {
      await quitarEvidencia(sid, renglon.id, empresa);
      ponerRenglon({ ...renglon, evidencia: null });
      setModal(null);
      anunciar(`Se quitó la evidencia de ${rotuloConcepto(renglon)}.`);
      enfocar(() => document.querySelector<HTMLElement>(`[aria-label="Cargar evidencia de ${CSS.escape(rotuloConcepto(renglon))}"]`));
      releer();
    } catch (e) {
      errorDeModal(e);
    } finally {
      setOcupado(false);
    }
  }

  async function confirmarQuitarRenglon(renglon: RenglonSaneamiento) {
    if (!san) return;
    setOcupado(true);
    setErrorModal(null);
    try {
      await quitarRenglon(sid, renglon.id, empresa);
      const i = san.renglones.findIndex((r) => r.id === renglon.id);
      const vecino = san.renglones[i + 1] ?? san.renglones[i - 1] ?? null;
      setSan((s) => (s ? { ...s, renglones: s.renglones.filter((r) => r.id !== renglon.id) } : s));
      quitarError(renglon.id);
      acciones.alEditar(renglon.id, false);
      setModal(null);
      anunciar(`Se quitó el concepto ${rotuloConcepto(renglon)}.`);
      enfocar(vecino ? radioDe(vecino.id) : tituloConceptos);
      releer();
    } catch (e) {
      errorDeModal(e);
    } finally {
      setOcupado(false);
    }
  }

  async function agregar(descripcion: string) {
    setOcupado(true);
    setErrorModal(null);
    try {
      const nuevo = await agregarRenglon(sid, empresa, descripcion);
      setSan((s) => (s ? { ...s, renglones: [...s.renglones, nuevo] } : s));
      setModal(null);
      anunciar(`Se agregó el concepto ${descripcion}.`);
      enfocar(radioDe(nuevo.id));
      releer();
    } catch (e) {
      if (codigo(e) === "VERSION_DESACTUALIZADA" || codigo(e) === "NO_EDITABLE" || es403(e)) errorDeModal(e);
      else setErrorModal({ texto: mensajeErrorSaneamiento(e, "No pudimos agregar el concepto. Revisa la descripción e intenta de nuevo.") });
    } finally {
      setOcupado(false);
    }
  }

  // ---------- Observación ----------

  async function guardarObservacion() {
    if (!san || obs.estado === "guardando") return;
    const texto = obs.texto;
    if (texto.trim() === obs.guardado.trim() && obs.estado !== "error") return;
    const problema = validarTextoLibre(texto, LIMITES_TEXTO.observacion);
    if (problema) {
      setObs((o) => ({ ...o, estado: "error", error: problema }));
      anunciar("Observación: No se guardó");
      return;
    }
    setObs((o) => ({ ...o, estado: "guardando", error: null }));
    anunciar("Observación: Guardando");
    try {
      const r = await editarObservacion(sid, empresa, texto, san.version);
      setSan(r);
      setObs((o) => ({ ...o, guardado: texto, estado: o.texto === texto ? "guardado" : "editado", error: null }));
      anunciar("Observación: Guardado");
    } catch (e) {
      const c = codigo(e);
      if (es403(e)) return ir403();
      if (c === "NO_EDITABLE") return recargar();
      if (c === "VERSION_DESACTUALIZADA") setConflicto(true);
      setObs((o) => ({ ...o, estado: "error", error: c === "VERSION_DESACTUALIZADA" ? mensajeErrorSaneamiento(e) : RED }));
      anunciar("Observación: No se guardó");
    }
  }

  // ---------- Pantalla ----------

  if (carga.tipo === "cargando" || detalle403) return <VehicleLoader label="Cargando saneamiento…" />;
  if (carga.tipo === "error") {
    if (carga.error instanceof ApiError && carga.error.status === 404) {
      return (
        <EstadoVacio
          icono={<Icono nombre="buscar" className="h-7 w-7" />}
          titulo="No encontramos este saneamiento"
          mensaje="Revisa la compañía o vuelve a la bandeja."
          acciones={[
            <Link key="volver" href="/backoffice/saneamientos" className={BOTON_SECUNDARIO_CLS}>
              Volver a la bandeja
            </Link>,
          ]}
        />
      );
    }
    return (
      <InlineAlert
        tone="error"
        title="No pudimos cargar el saneamiento"
        action={
          <button
            type="button"
            onClick={() => {
              setCarga({ tipo: "cargando" });
              setIntento((n) => n + 1);
            }}
            className={BOTON_REINTENTAR_CLS}
          >
            Reintentar
          </button>
        }
      >
        Intenta de nuevo en un momento.
      </InlineAlert>
    );
  }
  if (!san) return null;

  const deudaLista = deuda.tipo === "listo" ? deuda.deuda : null;
  const placa = san.placa || deudaLista?.vehiculo.placa || san.vehiculo_id || "Saneamiento";
  const veredicto = san.veredicto ?? deudaLista?.dictamen?.veredicto ?? null;
  const nombreEmpresa = rotuloEmpresa(nombres, empresa);
  const sinGuardar = sucios.size > 0 || obs.estado === "editado" || obs.estado === "guardando";
  const motivoNoSaneado = motivoNoCerrarSaneado(san.renglones, sinGuardar);
  const enGestion = san.estado === "EN_GESTION";

  const cuerpoDeuda: ReactNode =
    deuda.tipo === "cargando" ? (
      <p role="status" className={AYUDA_CLS}>
        Cargando la deuda…
      </p>
    ) : deuda.tipo === "error" ? (
      deuda.noTerminado ? (
        <InlineAlert tone="info" title="Consultas en curso">
          Este vehículo aún tiene consultas por terminar.
        </InlineAlert>
      ) : (
        <InlineAlert
          tone="error"
          title="No pudimos cargar la deuda"
          action={
            <button
              type="button"
              onClick={() => {
                setDeuda({ tipo: "cargando" });
                setIntentoDeuda((n) => n + 1);
              }}
              className={BOTON_REINTENTAR_CLS}
            >
              Reintentar
            </button>
          }
        >
          Intenta de nuevo en un momento.
        </InlineAlert>
      )
    ) : (
      <SeccionesDeuda deuda={deuda.deuda} />
    );

  const lecturaObservacion = san.observacion_flit?.trim() ? (
    <div className="flex flex-col gap-1">
      <p className={ETIQUETA_CLS}>Observación para la compañía</p>
      <blockquote className="whitespace-pre-line break-words border-l-2 border-[color:var(--di-border)] pl-3 text-sm text-flit-primary">{san.observacion_flit}</blockquote>
    </div>
  ) : null;

  const motivos = [
    ...(san.motivo_cierre?.trim() ? [{ rotulo: "Motivo del cierre", texto: san.motivo_cierre }] : []),
    ...(san.motivo_anulacion?.trim() && san.estado !== "ANULADO" ? [{ rotulo: "Motivo de la anulación", texto: san.motivo_anulacion }] : []),
  ].map((m) => (
    <div key={m.rotulo} className="flex flex-col gap-1">
      <p className={ETIQUETA_CLS}>{m.rotulo}</p>
      <p className="whitespace-pre-line break-words text-sm text-flit-primary">{m.texto}</p>
    </div>
  ));

  const botonAnular = (
    <button type="button" onClick={() => abrir({ tipo: "anular" })} className={ENLACE_PELIGRO_CLS}>
      Anular
    </button>
  );

  let gestion: ReactNode;
  if (san.estado === "SOLICITADO") {
    gestion = (
      <div className="flex flex-col gap-3">
        <p className="text-sm text-flit-primary">{`La compañía pidió el saneamiento el ${fechaCorta(san.solicitado_en)}.`}</p>
        <div className="flex flex-wrap items-center gap-3">
          <Button type="button" size="sm" cargando={tomando} onClick={() => void tomar()}>
            {tomando ? "Tomando…" : "Tomar"}
          </Button>
          {botonAnular}
        </div>
        {errorTomar && (
          <div role="alert" className="flex flex-wrap items-center gap-2 text-sm font-semibold text-[color:var(--di-danger)]">
            <span>{errorTomar.texto}</span>
            {errorTomar.recargar && (
              <Button type="button" variant="ghost" size="sm" onClick={recargar}>
                Recargar
              </Button>
            )}
          </div>
        )}
      </div>
    );
  } else if (enGestion) {
    gestion = (
      <div className="flex flex-col gap-5">
        <h3 id={`${id}-conceptos`} tabIndex={-1} className="text-base font-semibold text-flit-primary focus:outline-none">
          Conceptos a sanear
        </h3>
        <RenglonesEditables key={generacion} renglones={san.renglones} acciones={acciones} errores={errores} descargas={descargas} />
        <Button type="button" variant="secondary" size="sm" className="self-start" onClick={() => abrir({ tipo: "agregar" })}>
          <Icono nombre="mas" className="h-4 w-4" />
          Agregar otro concepto
        </Button>
        <div className="flex flex-col gap-1">
          <div className="flex flex-wrap items-center gap-2">
            <label htmlFor={`${id}-observacion`} className={ETIQUETA_CLS}>
              Observación para la compañía
            </label>
            <span data-testid="indicador-observacion" className={`text-xs font-semibold ${TONO_OBSERVACION[obs.estado]}`}>
              {ROTULO_OBSERVACION[obs.estado]}
            </span>
          </div>
          <textarea
            id={`${id}-observacion`}
            rows={3}
            maxLength={LIMITES_TEXTO.observacion.max}
            value={obs.texto}
            readOnly={obs.estado === "guardando"}
            aria-describedby={`${id}-observacion-ayuda`}
            onChange={(e) => {
              const texto = e.target.value;
              setObs((o) => ({ ...o, texto, estado: o.estado === "guardando" ? o.estado : "editado" }));
            }}
            onBlur={() => void guardarObservacion()}
            className={`${CAMPO_CLS.replace("h-11 ", "")} min-h-20 py-2 text-sm`}
          />
          <p id={`${id}-observacion-ayuda`} className={AYUDA_CLS}>
            La compañía la ve en el panel del vehículo.
          </p>
          {obs.error && (
            <div className="flex flex-wrap items-center gap-2">
              <p role="alert" className="text-xs font-semibold text-[color:var(--di-danger)]">
                {obs.error}
              </p>
              <button type="button" aria-label="Reintentar la observación" onClick={() => void guardarObservacion()} className={BOTON_REINTENTAR_CLS}>
                Reintentar
              </button>
            </div>
          )}
        </div>
        <div className="flex flex-col gap-2">
          <h3 className="text-base font-semibold text-flit-primary">Cerrar el saneamiento</h3>
          <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center">
            <Button
              type="button"
              size="sm"
              aria-disabled={motivoNoSaneado ? "true" : undefined}
              aria-describedby={motivoNoSaneado ? `${id}-regla` : undefined}
              className={motivoNoSaneado ? "cursor-not-allowed opacity-60" : undefined}
              onClick={() => {
                if (!motivoNoSaneado) abrir({ tipo: "saneado" });
              }}
            >
              Cerrar como saneado
            </Button>
            <Button type="button" variant="secondary" size="sm" onClick={() => abrir({ tipo: "no_procede" })}>
              Cerrar como no procede
            </Button>
            {botonAnular}
          </div>
          {motivoNoSaneado && (
            <p id={`${id}-regla`} className={AYUDA_CLS}>
              {motivoNoSaneado}
            </p>
          )}
        </div>
      </div>
    );
  } else if (san.estado === "ANULADO") {
    gestion = (
      <InlineAlert tone="info" title="Saneamiento anulado">
        <span className="whitespace-pre-line break-words">{san.motivo_anulacion?.trim() ? `Motivo: ${san.motivo_anulacion}` : "Sin motivo registrado."}</span>
      </InlineAlert>
    );
  } else {
    gestion = (
      <div className="flex flex-col gap-4">
        <h3 className="text-base font-semibold text-flit-primary">Conceptos a sanear</h3>
        <RenglonesLectura renglones={san.renglones} verEvidencia={acciones.verEvidencia} descargas={descargas} />
        {lecturaObservacion}
        {motivos}
        <Button type="button" variant="secondary" size="sm" className="self-start" onClick={() => abrir({ tipo: "reabrir" })}>
          Reabrir
        </Button>
      </div>
    );
  }

  return (
    <>
      <CabeceraModulo
        titulo={placa}
        tituloClassName="uppercase tracking-wider"
        icono="pago"
        miga={[{ etiqueta: "Saneamientos", href: "/backoffice/saneamientos" }, { etiqueta: placa }]}
        descripcion={
          <span className="break-words">
            {nombreEmpresa}
            {" · "}
            {san.diagnostico_id ? (
              <Link
                href={`/backoffice/diagnosticos/${encodeURIComponent(san.diagnostico_id)}?empresa=${encodeURIComponent(empresa)}`}
                className={`font-semibold text-[var(--flit-brand-ink)] underline underline-offset-2 ${FOCO_CLS}`}
              >
                {san.diagnostico_nombre || "Ver el lote"}
              </Link>
            ) : (
              (san.diagnostico_nombre ?? "Lote")
            )}
          </span>
        }
        acciones={
          <div data-estado-saneamiento={id} tabIndex={-1} className="flex flex-wrap items-center gap-2 rounded-full focus:outline-none focus-visible:outline-2 focus-visible:outline-[var(--di-action)]">
            <EtiquetaSaneamiento estado={san.estado} backoffice />
            {veredicto && <EtiquetaTono tono={veredicto === "BLOQUEADO" ? "danger" : "success"}>{veredicto === "BLOQUEADO" ? "Bloqueado" : "Viable"}</EtiquetaTono>}
          </div>
        }
      />
      <div className="grid gap-4 lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)]">
        <section aria-label="Deuda del vehículo" className={`${TARJETA_CLS} flex min-w-0 flex-col gap-3`}>
          {ancha ? (
            <>
              <h2 className="text-lg font-semibold text-flit-primary">Deuda del vehículo</h2>
              {cuerpoDeuda}
            </>
          ) : (
            <TarjetaPlegable titulo="Deuda del vehículo" nivelTitulo={2}>
              {cuerpoDeuda}
            </TarjetaPlegable>
          )}
        </section>
        <section aria-labelledby={`${id}-gestion`} className={`${TARJETA_CLS} flex min-w-0 flex-col gap-3`}>
          <h2 id={`${id}-gestion`} className="text-lg font-semibold text-flit-primary">
            Gestión
          </h2>
          {gestion}
        </section>
      </div>
      <section className={TARJETA_CLS} aria-label="Pie del saneamiento">
        <HistorialSaneamiento historial={san.historial} />
      </section>

      {modal?.tipo === "anular" && (
        <ModalMotivo
          titulo="Anular el saneamiento"
          texto="La compañía podrá volver a pedirlo."
          verbo="Anular"
          ocupado={ocupado}
          error={errorModal}
          errorCampo={errorCampo}
          onCerrar={cerrarModal}
          onConfirmar={(m) => void transicion("anular", m)}
          onRecargar={() => {
            setModal(null);
            recargar();
          }}
        />
      )}
      {modal?.tipo === "reabrir" && (
        <ModalMotivo
          titulo="Reabrir el saneamiento"
          verbo="Reabrir"
          ocupado={ocupado}
          error={errorModal}
          errorCampo={errorCampo}
          onCerrar={cerrarModal}
          onConfirmar={(m) => void transicion("reabrir", m)}
          onRecargar={() => {
            setModal(null);
            recargar();
          }}
        />
      )}
      {modal?.tipo === "no_procede" && (
        <ModalMotivo
          titulo="Cerrar como no procede"
          verbo="Cerrar como no procede"
          ocupado={ocupado}
          error={errorModal}
          errorCampo={errorCampo}
          onCerrar={cerrarModal}
          onConfirmar={(m) => void transicion("no_procede", m)}
          onRecargar={() => {
            setModal(null);
            recargar();
          }}
        />
      )}
      {modal?.tipo === "saneado" && (
        <ModalConfirmar
          titulo="¿Cerrar como saneado?"
          texto="La compañía verá el saneamiento como saneado. El dictamen no cambia."
          verbo="Cerrar como saneado"
          ocupado={ocupado}
          error={errorModal}
          onCerrar={cerrarModal}
          onConfirmar={() => void transicion("saneado")}
          onRecargar={() => {
            setModal(null);
            recargar();
          }}
        />
      )}
      {modal?.tipo === "agregar" && <ModalAgregarConcepto ocupado={ocupado} error={errorModal} onCerrar={cerrarModal} onAgregar={(d) => void agregar(d)} />}
      {modal?.tipo === "evidencia" && (
        <ModalEvidencia
          concepto={rotuloConcepto(modal.renglon)}
          ocupado={ocupado}
          error={errorEvidencia}
          onCerrar={cerrarModal}
          onSubir={(archivo) => void subir(modal.renglon, archivo)}
        />
      )}
      {modal?.tipo === "quitar-evidencia" && (
        <ModalConfirmar
          titulo="¿Quitar la evidencia?"
          texto={`${rotuloConcepto(modal.renglon)}. Quedará en el historial.`}
          verbo="Quitar"
          peligro
          ocupado={ocupado}
          error={errorModal}
          onCerrar={cerrarModal}
          onConfirmar={() => void confirmarQuitarEvidencia(modal.renglon)}
        />
      )}
      {modal?.tipo === "quitar-renglon" && (
        <ModalConfirmar
          titulo="¿Quitar el concepto?"
          texto={`${rotuloConcepto(modal.renglon)}. Quedará en el historial.`}
          verbo="Quitar"
          peligro
          ocupado={ocupado}
          error={errorModal}
          onCerrar={cerrarModal}
          onConfirmar={() => void confirmarQuitarRenglon(modal.renglon)}
        />
      )}

      {conflicto && (
        <div className="fixed right-4 top-20 z-[60] max-[640px]:inset-x-2">
          <NotificacionPersistente
            titulo="Otra persona cambió este saneamiento"
            mensaje="Recarga para ver los cambios. Lo que escribiste en el concepto con error se conserva hasta que recargues."
            accion={{ label: "Recargar", onClick: recargar }}
            onCerrar={() => setConflicto(false)}
          />
        </div>
      )}
      <div data-region-viva>{region}</div>
    </>
  );
}

"use client";

import { useCallback, useEffect, useId, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { SIN_PERMISO_SANEAMIENTO } from "@/components/backoffice/AccesoSaneamientos";
import type { ErrorModal } from "@/components/backoffice/ModalesSaneamiento";
import type { AccionesRenglon } from "@/components/backoffice/RenglonesGestion";
import { useRegionVivaPantalla } from "@/components/saneamiento/RegionVivaPantalla";
import {
  actualizarRenglon,
  agregarRenglon,
  anularSaneamiento,
  cerrarSaneamiento,
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
import { useResumenSaneamientos } from "@/lib/backoffice/useResumenSaneamientos";
import { descargarArchivo } from "@/lib/reportes/api";
import { mensajeErrorSaneamiento, type PanelDeuda, type RenglonSaneamiento } from "@/lib/saneamiento/api";
import { rotuloConcepto } from "@/lib/saneamiento/estados";
import { LIMITES_TEXTO, validarTextoLibre } from "@/lib/saneamiento/textoLibre";
import {
  CIERRE_INCOMPLETO,
  DEMASIADOS_REEMPLAZOS,
  ERROR_DESCARGA,
  GENERICO,
  RED,
  codigo,
  es403,
  motivoNoCerrarSaneado,
  sinClave,
  type ModalAbierto,
  type Transicion,
} from "./comun";
import { useObservacionSaneamiento } from "./useObservacionSaneamiento";

// PS-5 (CR-5): estado y escrituras de la gestión de un saneamiento. Pide en paralelo el detalle y la deuda del backoffice
// (SP-7), cada uno con sus estados; cada escritura manda la versión vigente y un 409 VERSION_DESACTUALIZADA levanta el
// conflicto. El foco tras cada acción (spec-ui §6.2, §6.3.1, §6.3.2, §6.3.3, §6.5) se resuelve aquí.

export type Carga = { tipo: "cargando" } | { tipo: "error"; error: unknown } | { tipo: "listo" };
export type CargaDeuda = { tipo: "cargando" } | { tipo: "error"; noTerminado: boolean } | { tipo: "listo"; deuda: PanelDeuda };

const ANUNCIO_TRANSICION: Record<Transicion, string> = {
  saneado: "Saneamiento cerrado como saneado.",
  no_procede: "Saneamiento cerrado: no procede.",
  anular: "Saneamiento anulado.",
  reabrir: "Saneamiento reabierto.",
};

export function useGestionSaneamiento(sid: string, empresa: string) {
  const router = useRouter();
  const id = useId();
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
  const foco = useRef<(() => HTMLElement | null) | null>(null);

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

  // Foco tras una acción: se intenta en cada render hasta que el destino exista.
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

  const observacion = useObservacionSaneamiento({ sid, empresa, san, generacion, setSan, setConflicto, recargar, ir403, anunciar });

  const reintentar = () => {
    setCarga({ tipo: "cargando" });
    setIntento((n) => n + 1);
  };
  const reintentarDeuda = () => {
    setDeuda({ tipo: "cargando" });
    setIntentoDeuda((n) => n + 1);
  };

  const ponerRenglon = (r: RenglonSaneamiento) => setSan((s) => (s ? { ...s, renglones: s.renglones.map((x) => (x.id === r.id ? r : x)) } : s));

  // ---------- Modales ----------

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

  /** «Recargar» del error de un modal: lo cierra y trae los datos vigentes. */
  function recargarDesdeModal() {
    setModal(null);
    recargar();
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

  // ---------- Transiciones ----------

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
      // H-1: el POST devuelve el saneamiento completo (D-65), no el renglón. El concepto nuevo es el que no estaba; con
      // las versiones al día no hace falta releer, y el foco va a una fila que ya no cambia de identidad.
      const previos = new Set(san?.renglones.map((r) => r.id));
      const tras = await agregarRenglon(sid, empresa, descripcion);
      const nuevo = tras.renglones.find((r) => !previos.has(r.id)) ?? tras.renglones.at(-1);
      setSan(tras);
      setModal(null);
      anunciar(`Se agregó el concepto ${descripcion}.`);
      enfocar(nuevo ? radioDe(nuevo.id) : tituloConceptos);
    } catch (e) {
      if (codigo(e) === "VERSION_DESACTUALIZADA" || codigo(e) === "NO_EDITABLE" || es403(e)) errorDeModal(e);
      else setErrorModal({ texto: mensajeErrorSaneamiento(e, "No pudimos agregar el concepto. Revisa la descripción e intenta de nuevo.") });
    } finally {
      setOcupado(false);
    }
  }

  const { obs } = observacion;
  const sinGuardar = sucios.size > 0 || obs.estado === "editado" || obs.estado === "guardando";
  const motivoNoSaneado = san ? motivoNoCerrarSaneado(san.renglones, sinGuardar) : null;

  return {
    id,
    region,
    carga,
    detalle403,
    san,
    generacion,
    deuda,
    conflicto,
    modal,
    ocupado,
    errorModal,
    errorCampo,
    errorEvidencia,
    tomando,
    errorTomar,
    errores,
    descargas,
    observacion,
    motivoNoSaneado,
    acciones,
    reintentar,
    reintentarDeuda,
    recargar,
    cerrarConflicto: () => setConflicto(false),
    abrir,
    cerrarModal,
    recargarDesdeModal,
    transicion,
    tomar,
    subir,
    confirmarQuitarEvidencia,
    confirmarQuitarRenglon,
    agregar,
  };
}

export type GestionSaneamientoEstado = ReturnType<typeof useGestionSaneamiento>;

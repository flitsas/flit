"use client";

import { useEffect, useState, type RefObject } from "react";
import { useRouter } from "next/navigation";
import { SANEAMIENTO_SOLICITADO } from "@/components/saneamiento/BotonSanear";
import { mensajeSolicitudSaneamiento, type ResultadoSanearVisible } from "@/components/tablero/BarraSeleccionTablero";
import { ApiError } from "@/lib/api";
import { MAXIMO_DEUDA_VARIAS, descargarExcelDeudaVarias, mensajeErrorSaneamiento, solicitarSaneamientos, type SaneamientoFila } from "@/lib/saneamiento/api";
import type { FilaVehiculoListado } from "@/lib/vehiculos/listado";
import { seleccionDe } from "./seleccion";

// PS-3 (CR-5): la selección por página con «Sanear (N)» (POST /saneamientos/solicitudes, como el tablero) y «Descargar
// deuda (N)» (Excel de 1 a 200 placas). En dos hooks porque la consulta vacía la selección: `useSeleccionVehiculos` guarda
// el estado antes de la consulta y `useAccionesSeleccion` deriva lo seleccionado con las filas ya leídas.

const ERROR_DESCARGA = "No pudimos generar el archivo. Intenta de nuevo.";
const ERROR_SANEAR = "No pudimos solicitar el saneamiento. Intenta de nuevo.";

export function useSeleccionVehiculos() {
  const [seleccion, setSeleccion] = useState<ReadonlySet<string>>(() => new Set());
  const [sanearEnviando, setSanearEnviando] = useState(false);
  const [errorSanear, setErrorSanear] = useState<string | null>(null);
  const [resultadoSanear, setResultadoSanear] = useState<ResultadoSanearVisible | null>(null);
  const [descargando, setDescargando] = useState(false);
  const [errorDescarga, setErrorDescarga] = useState<string | null>(null);

  const limpiarSeleccion = () => setSeleccion(new Set());
  function limpiarAvisos() {
    setSeleccion(new Set());
    setErrorSanear(null);
    setResultadoSanear(null);
    setErrorDescarga(null);
  }

  return {
    seleccion,
    setSeleccion,
    sanearEnviando,
    setSanearEnviando,
    errorSanear,
    setErrorSanear,
    resultadoSanear,
    setResultadoSanear,
    descargando,
    setDescargando,
    errorDescarga,
    setErrorDescarga,
    limpiarSeleccion,
    limpiarAvisos,
  };
}

export type EstadoSeleccion = ReturnType<typeof useSeleccionVehiculos>;

export interface DependenciasAcciones {
  filas: readonly FilaVehiculoListado[];
  puedeSanear: boolean;
  puedeDescargar: boolean;
  actualizarFila: (vehiculoId: string, saneamiento: SaneamientoFila) => void;
  anunciar: (texto: string) => void;
  /** Contenedor enfocable del resultado de «Sanear (N)». */
  resultadoRef: RefObject<HTMLDivElement | null>;
}

export function useAccionesSeleccion(sel: EstadoSeleccion, { filas, puedeSanear, puedeDescargar, actualizarFila, anunciar, resultadoRef }: DependenciasAcciones) {
  const router = useRouter();
  const { seleccion, setSeleccion, sanearEnviando, setSanearEnviando, setErrorSanear, resultadoSanear, setResultadoSanear, descargando, setDescargando, setErrorDescarga } = sel;

  const selecciones = new Map(filas.map((f) => [f.vehiculo_id, seleccionDe(f, puedeSanear, puedeDescargar)]));
  const elegibles = filas.filter((f) => selecciones.get(f.vehiculo_id)?.habilitada).map((f) => f.vehiculo_id);
  const seleccionadas = elegibles.filter((id) => seleccion.has(id));
  const paraSanear = seleccionadas.filter((id) => selecciones.get(id)?.sanear);
  const paraDescargar = seleccionadas.filter((id) => selecciones.get(id)?.descargar);
  const seleccionVacia = seleccionadas.length === 0;

  useEffect(() => {
    // El foco se queda en «Sanear»; si la barra desapareció (selección vacía), pasa al resultado (patrón del tablero).
    if (resultadoSanear && seleccionVacia) resultadoRef.current?.focus();
  }, [resultadoSanear, seleccionVacia, resultadoRef]);

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

  return {
    selecciones,
    elegibles,
    seleccionadas,
    paraSanear,
    paraDescargar,
    cambiarSeleccion,
    seleccionarElegibles: () => setSeleccion(new Set(elegibles)),
    quitarSeleccion: () => setSeleccion(new Set()),
    sanear,
    descargar,
  };
}

export type AccionesSeleccion = ReturnType<typeof useAccionesSeleccion>;

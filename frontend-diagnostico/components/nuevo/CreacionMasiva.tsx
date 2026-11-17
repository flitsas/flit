"use client";

import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { ArrowRight, RotateCcw } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { ApiError } from "@/lib/api";
import { accesoNegado, analizarCarga, confirmarVehiculos, type FilaCapturada, type FilaResumen, type ResumenValidacion as Resumen } from "@/lib/carga/api";
import { crearDiagnostico, editarDiagnostico, type Diagnostico } from "@/lib/diagnosticos/api";
import { correccionesAConfirmar, resumenVigente, type Analisis } from "@/components/carga/CargaMasiva";
import { EstimadoConsultas } from "@/components/carga/EstimadoConsultas";
import { ResumenValidacion } from "@/components/carga/ResumenValidacion";
import { MENSAJE_FORMATO_NO_ADMITIDO, ZonaArchivo, archivoAdmitido } from "@/components/carga/SubidaArchivo";
import { TABLA_FILAS_ID, TablaFilasCorregibles } from "@/components/carga/TablaFilasCorregibles";
import { AYUDA_CLS, BOTON_PRIMARIO_CLS, BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS } from "@/components/configuracion/estilos";
import { ParametrosDiagnostico } from "@/components/parametrizacion/ParametrosDiagnostico";
import type { Parametrizacion } from "@/components/parametrizacion/useParametrizacion";
import type { BorradorMasivo } from "./borradores";
import { DosZonas } from "./DosZonas";

type Fase = "editando" | "guardando" | "analizando";
type Confirmacion = { tipo: "inactivo" } | { tipo: "confirmando" } | { tipo: "error"; mensaje: string; reintentable: boolean };
type ErrorArchivo = { mensaje: string; reintentable: boolean };

export const MENSAJE_SIN_ARCHIVO = "Elige el archivo de vehículos (.xlsx o .csv).";

export interface CreacionMasivaProps {
  p: Parametrizacion;
  borrador: BorradorMasivo;
  onBorrador: (cambio: Partial<BorradorMasivo>) => void;
  onOcupado: (ocupado: boolean) => void;
  /** Con la cabecera creada la pantalla bloquea el cambio de modo. */
  onCabecera: (id: string) => void;
}

/**
 * Masivo en un solo paso (HU #14085; reglas de las HU #13752 y #13754): parámetros y archivo en la misma pantalla.
 * «Validar archivo» valida los parámetros y el archivo, crea la cabecera con POST /diagnosticos (o, si ya existe, la
 * actualiza con PUT /diagnosticos/{id}, sin crear otra) y luego analiza el archivo con POST /diagnosticos/{id}/carga. El
 * Resumen de Validación, la corrección de filas y «Continuar solo con los válidos» siguen en la misma pantalla; confirmar
 * reenvía el MISMO archivo con las correcciones y navega al tablero. Con el análisis a la vista los parámetros quedan
 * bloqueados: «Subir otro archivo» los libera. Un 403 de sesión lleva a la página 403.
 */
export function CreacionMasiva({ p, borrador, onBorrador, onOcupado, onCabecera }: CreacionMasivaProps) {
  const router = useRouter();
  const id = useId();
  const rejillaRef = useRef<HTMLDivElement>(null);
  const [cabeceraId, setCabeceraId] = useState<string | null>(null);
  const [fase, setFase] = useState<Fase>("editando");
  const [errorCabecera, setErrorCabecera] = useState<string | null>(null);
  const [errorArchivo, setErrorArchivo] = useState<ErrorArchivo | null>(null);
  const [faltaArchivo, setFaltaArchivo] = useState(false);
  const [analisis, setAnalisis] = useState<Analisis | null>(null);
  const [confirmacion, setConfirmacion] = useState<Confirmacion>({ tipo: "inactivo" });
  const [enfocar, setEnfocar] = useState(0);
  const ocupado = fase !== "editando" || confirmacion.tipo === "confirmando";

  useEffect(() => onOcupado(ocupado), [ocupado, onOcupado]);

  useEffect(() => {
    if (enfocar === 0) return;
    rejillaRef.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
  }, [enfocar]);

  const irA403 = (code: string) => router.replace(`/403?code=${encodeURIComponent(code)}`);

  function elegir(archivo: File) {
    setFaltaArchivo(false);
    if (!archivoAdmitido(archivo)) {
      onBorrador({ archivo: null });
      setErrorArchivo({ mensaje: MENSAJE_FORMATO_NO_ADMITIDO, reintentable: false });
      return;
    }
    setErrorArchivo(null);
    onBorrador({ archivo });
  }

  async function validar() {
    const archivo = borrador.archivo;
    const parametrosValidos = p.validar(borrador.nombre);
    setFaltaArchivo(!archivo);
    if (!parametrosValidos || !archivo) {
      setEnfocar((n) => n + 1);
      return;
    }
    setErrorCabecera(null);
    setErrorArchivo(null);
    setFase("guardando");
    let guardado: Diagnostico;
    try {
      const cabecera = p.cabecera(borrador.nombre);
      guardado = cabeceraId ? await editarDiagnostico(cabeceraId, cabecera) : await crearDiagnostico(cabecera);
    } catch (e) {
      setFase("editando");
      if (p.aplicarErrorApi(e)) setEnfocar((n) => n + 1);
      else setErrorCabecera(e instanceof Error ? e.message : "No se pudo guardar el diagnóstico.");
      return;
    }
    setCabeceraId(guardado.id);
    onCabecera(guardado.id);
    setFase("analizando");
    try {
      const resumen = await analizarCarga(guardado.id, archivo);
      alResumen(resumen, archivo);
    } catch (e) {
      const code = accesoNegado(e);
      if (code) return irA403(code);
      const reintentable = !(e instanceof ApiError) || e.status >= 500;
      setErrorArchivo({ mensaje: e instanceof Error ? e.message : "No se pudo analizar el archivo.", reintentable });
    } finally {
      setFase("editando");
    }
  }

  function alResumen(resumen: Resumen, archivo: File) {
    setConfirmacion({ tipo: "inactivo" });
    setAnalisis((previo) => ({
      archivo,
      resumen,
      filas: resumen.filas,
      capturadas: new Map(),
      erroneas: resumen.filas.filter((f) => !f.valida),
      version: (previo?.version ?? 0) + 1,
    }));
  }

  function alRevalidar(fila: FilaResumen, capturada: FilaCapturada) {
    setAnalisis(
      (actual) =>
        actual && {
          ...actual,
          filas: actual.filas.map((f) => (f.fila === fila.fila ? fila : f)),
          capturadas: new Map(actual.capturadas).set(fila.fila, capturada),
        },
    );
  }

  function corregir() {
    const tabla = document.getElementById(TABLA_FILAS_ID);
    tabla?.scrollIntoView?.({ behavior: "smooth", block: "start" });
    tabla?.focus();
  }

  async function continuar() {
    if (!analisis || !cabeceraId) return;
    setConfirmacion({ tipo: "confirmando" });
    try {
      await confirmarVehiculos(cabeceraId, analisis.archivo, correccionesAConfirmar(analisis));
      router.push(`/diagnosticos/${encodeURIComponent(cabeceraId)}`);
    } catch (e) {
      const code = accesoNegado(e);
      if (code) return irA403(code);
      const reintentable = !(e instanceof ApiError) || e.status >= 500;
      setConfirmacion({ tipo: "error", mensaje: e instanceof Error ? e.message : "No se pudieron confirmar los vehículos.", reintentable });
    }
  }

  function subirOtro() {
    setAnalisis(null);
    setConfirmacion({ tipo: "inactivo" });
    onBorrador({ archivo: null });
  }

  function alEnviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!ocupado && !analisis) void validar();
  }

  const vigente = analisis ? resumenVigente(analisis) : null;
  const cargando = fase === "guardando" ? "Guardando el diagnóstico…" : fase === "analizando" ? "Analizando el archivo…" : null;

  const vehiculos =
    analisis && vigente ? (
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className={AYUDA_CLS}>
          Archivo analizado: <span className="break-all font-semibold text-flit-primary dark:text-white">{analisis.archivo.name}</span>
        </p>
        <button type="button" onClick={subirOtro} disabled={ocupado} className={BOTON_SECUNDARIO_CLS}>
          <RotateCcw className="h-4 w-4" aria-hidden="true" />
          Subir otro archivo
        </button>
      </div>
    ) : (
      <>
        <ZonaArchivo archivo={borrador.archivo} onElegir={elegir} cargando={cargando} disabled={ocupado} error={faltaArchivo ? MENSAJE_SIN_ARCHIVO : null} />
        {errorArchivo && (
          <InlineAlert
            tone="error"
            title="No se pudo analizar el archivo"
            action={
              errorArchivo.reintentable ? (
                <button type="button" onClick={() => void validar()} className={BOTON_REINTENTAR_CLS}>
                  Reintentar
                </button>
              ) : undefined
            }
          >
            {errorArchivo.mensaje}
          </InlineAlert>
        )}
      </>
    );

  const despuesVehiculos =
    analisis && vigente && cabeceraId ? (
      <>
        <ResumenValidacion
          resumen={{ ...vigente, estimado_consultas: undefined }}
          onCorregir={corregir}
          onContinuar={() => void continuar()}
          confirmando={confirmacion.tipo === "confirmando"}
        />
        {confirmacion.tipo === "error" && (
          <InlineAlert
            tone="error"
            title="No se confirmaron los vehículos"
            action={
              confirmacion.reintentable ? (
                <button type="button" onClick={() => void continuar()} className={BOTON_REINTENTAR_CLS}>
                  Reintentar
                </button>
              ) : undefined
            }
          >
            {confirmacion.mensaje}
          </InlineAlert>
        )}
        <TablaFilasCorregibles
          key={analisis.version}
          diagnosticoId={cabeceraId}
          filas={analisis.erroneas}
          onFilaRevalidada={alRevalidar}
          onAccesoNegado={irA403}
        />
      </>
    ) : null;

  return (
    <DosZonas
      rejillaRef={rejillaRef}
      vehiculos={vehiculos}
      despuesVehiculos={despuesVehiculos}
      parametros={
        <form id={`${id}-form`} onSubmit={alEnviar} noValidate aria-label="Parámetros del diagnóstico masivo" className="flex flex-col gap-4">
          <ParametrosDiagnostico p={p} nombre={borrador.nombre} onNombre={(nombre) => onBorrador({ nombre })} disabled={ocupado || analisis !== null}>
            {errorCabecera && (
              <InlineAlert tone="error" title="No se guardó el diagnóstico">
                {errorCabecera}
              </InlineAlert>
            )}
            {vigente ? (
              <>
                <EstimadoConsultas estimado={vigente.estimado_consultas} />
                <p className={AYUDA_CLS}>Para cambiar los parámetros o el archivo, usa «Subir otro archivo».</p>
              </>
            ) : (
              <div className="flex justify-end">
                <button type="submit" disabled={ocupado} className={BOTON_PRIMARIO_CLS}>
                  {fase === "guardando" ? "Guardando…" : fase === "analizando" ? "Analizando…" : "Validar archivo"}
                  <ArrowRight className="h-4 w-4" aria-hidden="true" />
                </button>
              </div>
            )}
          </ParametrosDiagnostico>
        </form>
      }
    />
  );
}

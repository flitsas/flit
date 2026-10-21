"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { FileSpreadsheet, RotateCcw } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { useSesion } from "@/components/DiagnosticoShell";
import { ApiError } from "@/lib/api";
import {
  accesoNegado,
  confirmarVehiculos,
  esDocumentoEnmascarado,
  type CorreccionFila,
  type FilaCapturada,
  type FilaResumen,
  type ResumenValidacion as Resumen,
} from "@/lib/carga/api";
import { BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { ResumenValidacion } from "./ResumenValidacion";
import { SubidaArchivo } from "./SubidaArchivo";
import { TABLA_FILAS_ID, TablaFilasCorregibles } from "./TablaFilasCorregibles";

interface Analisis {
  /** El archivo analizado, solo en memoria: la confirmación lo reenvía (trae el documento del locatario completo). */
  archivo: File;
  /** Resumen original de la carga (duplicados, límite, estimado). */
  resumen: Resumen;
  /** Estado vigente de cada fila, con las revalidadas encima. */
  filas: FilaResumen[];
  /** Lo enviado al revalidar cada fila corregida; el documento escrito solo vive aquí, en memoria. */
  capturadas: Map<number, FilaCapturada>;
  /** Filas erróneas del archivo: la tabla las conserva aunque pasen a válidas. */
  erroneas: FilaResumen[];
  version: number;
}

type Confirmacion = { tipo: "inactivo" } | { tipo: "confirmando" } | { tipo: "error"; mensaje: string; reintentable: boolean };

/** Conteos vigentes tras las correcciones: válidos y erróneos salen de las filas; duplicados y límite, del análisis. */
export function resumenVigente({ resumen, filas }: Pick<Analisis, "resumen" | "filas">): Resumen {
  const validos = filas.filter((f) => f.valida).length;
  const supera = validos > resumen.limite_vehiculos_lote;
  return {
    ...resumen,
    filas,
    validos,
    erroneos: filas.length - validos,
    supera_limite: supera,
    puede_continuar: validos > 0 && !supera,
    avisos: resumen.avisos.filter((a) => (a.code === "SIN_VALIDOS" ? validos === 0 : supera)),
  };
}

const ERRORES_DE_DOCUMENTO: readonly string[] = ["Tipo_Documento_Locatario", "Documento_Locatario"];

/**
 * Correcciones de la confirmación: una por fila corregida que quedó válida, en el orden del archivo. El identificador
 * viaja si cambió. El documento: el reescrito viaja completo; si no se reescribió y el archivo lo traía con error, se
 * vacía (es lo que validó validar-filas); si no, se omite y el servidor conserva el del archivo. Nunca el enmascarado.
 */
export function correccionesAConfirmar({ filas, capturadas, erroneas }: Pick<Analisis, "filas" | "capturadas" | "erroneas">): CorreccionFila[] {
  const originales = new Map(erroneas.map((f) => [f.fila, f]));
  return filas.flatMap((f) => {
    const c = capturadas.get(f.fila);
    const original = originales.get(f.fila);
    if (!f.valida || !c || !original) return [];
    const correccion: CorreccionFila = { fila: f.fila };
    if (c.identificador !== original.identificador) correccion.identificador = c.identificador;
    const documento = esDocumentoEnmascarado(c.documento_locatario) ? null : c.documento_locatario;
    if (documento) {
      correccion.tipo_documento_locatario = c.tipo_documento_locatario;
      correccion.documento_locatario = documento;
    } else if (original.errores.some((e) => ERRORES_DE_DOCUMENTO.includes(e.campo))) {
      correccion.tipo_documento_locatario = null;
      correccion.documento_locatario = null;
    }
    return Object.keys(correccion).length > 1 ? [correccion] : [];
  });
}

/**
 * Carga masiva de un diagnóstico (HU #13754, F02-H7): subida del archivo, Resumen de Validación, corrección en sitio de
 * las filas erróneas y «Continuar solo con los válidos», que confirma con POST /diagnosticos/{id}/vehiculos (reenvía el
 * MISMO archivo, conservado solo en memoria, con las correcciones) y navega al
 * tablero /diagnosticos/{id}. Cuatro estados: vacío (sin archivo), cargando (analizando), error (rechazo con su motivo o
 * Reintentar) y lleno (resumen y tabla). Un 403 de sesión lleva a la página 403, como el shell.
 */
export function CargaMasiva({ diagnosticoId }: { diagnosticoId: string }) {
  const router = useRouter();
  const { sesion } = useSesion();
  const puedeCrear = sesion?.capacidades.crearDiagnosticos ?? false;
  const [analisis, setAnalisis] = useState<Analisis | null>(null);
  const [confirmacion, setConfirmacion] = useState<Confirmacion>({ tipo: "inactivo" });

  const irA403 = (code: string) => router.replace(`/403?code=${encodeURIComponent(code)}`);

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
    setAnalisis((actual) =>
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
    if (!analisis) return;
    setConfirmacion({ tipo: "confirmando" });
    try {
      await confirmarVehiculos(diagnosticoId, analisis.archivo, correccionesAConfirmar(analisis));
      router.push(`/diagnosticos/${encodeURIComponent(diagnosticoId)}`);
    } catch (e) {
      const code = accesoNegado(e);
      if (code) return irA403(code);
      const reintentable = !(e instanceof ApiError) || e.status >= 500;
      setConfirmacion({ tipo: "error", mensaje: e instanceof Error ? e.message : "No se pudieron confirmar los vehículos.", reintentable });
    }
  }

  let contenido;
  if (!puedeCrear) {
    contenido = (
      <section aria-labelledby="carga-vacio" className={`${TARJETA_CLS} flex flex-col items-center gap-3 py-12 text-center`}>
        <FileSpreadsheet className="h-10 w-10 text-flit-brand" aria-hidden="true" strokeWidth={1.5} />
        <h2 id="carga-vacio" className="text-lg font-semibold">
          No tienes permiso para cargar vehículos
        </h2>
        <p className="max-w-md text-sm text-[var(--nav-texto)] dark:text-slate-300">Pídele acceso al administrador de tu compañía.</p>
      </section>
    );
  } else if (!analisis) {
    contenido = (
      <section aria-label="Subir archivo" className={TARJETA_CLS}>
        <SubidaArchivo diagnosticoId={diagnosticoId} onResumen={alResumen} onAccesoNegado={irA403} />
      </section>
    );
  } else {
    const vigente = resumenVigente(analisis);
    contenido = (
      <>
        <ResumenValidacion resumen={vigente} onCorregir={corregir} onContinuar={() => void continuar()} confirmando={confirmacion.tipo === "confirmando"} />
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
          diagnosticoId={diagnosticoId}
          filas={analisis.erroneas}
          onFilaRevalidada={alRevalidar}
          onAccesoNegado={irA403}
        />
        <div className="flex justify-start">
          <button type="button" onClick={() => setAnalisis(null)} className={BOTON_SECUNDARIO_CLS}>
            <RotateCcw className="h-4 w-4" aria-hidden="true" />
            Subir otro archivo
          </button>
        </div>
      </>
    );
  }

  return (
    <>
      <section aria-labelledby="carga-titulo" className={TARJETA_CLS}>
        <h1 id="carga-titulo" className="text-2xl font-semibold text-flit-primary dark:text-white">
          Cargar vehículos
        </h1>
        {sesion?.empresa.nombre && <p className="mt-1 text-sm font-medium text-[var(--nav-texto)] dark:text-slate-300">{sesion.empresa.nombre}</p>}
      </section>
      {contenido}
    </>
  );
}

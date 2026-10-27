"use client";

import { useState, type ChangeEvent } from "react";
import { Download, UploadCloud } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { ApiError } from "@/lib/api";
import { EXTENSIONES_ADMITIDAS, URL_PLANTILLA, accesoNegado, analizarCarga, extensionDe, type ResumenValidacion } from "@/lib/carga/api";
import { AYUDA_CLS, BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS, FOCO_CLS } from "@/components/configuracion/estilos";
import { ERROR_CLS } from "@/components/parametrizacion/estilos";

export interface SubidaArchivoProps {
  diagnosticoId: string;
  /** El resumen y el archivo analizado: quien monta lo conserva en memoria para reenviarlo al confirmar. */
  onResumen: (resumen: ResumenValidacion, archivo: File) => void;
  /** 403 de sesión (SIN_EMPRESA, PRODUCTO_NO_HABILITADO): quien monta decide (la página lleva a /403). */
  onAccesoNegado?: (code: string) => void;
}

type Estado = { tipo: "inactivo" } | { tipo: "subiendo" } | { tipo: "error"; mensaje: string; reintentar: File | null };

/** .xls va a la API, que responde 422 con el mensaje de guardarlo como .xlsx; cualquier otra extensión se rechaza aquí. */
export const EXTENSIONES_A_LA_API: readonly string[] = [...EXTENSIONES_ADMITIDAS, ".xls"];

export const MENSAJE_FORMATO_NO_ADMITIDO = "Formato no admitido: sube el archivo en .xlsx o .csv (puedes partir de la plantilla).";

/** True si la extensión puede ir a la API (.xlsx, .csv o el .xls que la API rechaza con su mensaje). */
export const archivoAdmitido = (archivo: File): boolean => EXTENSIONES_A_LA_API.includes(extensionDe(archivo.name));

export interface ZonaArchivoProps {
  /** Archivo elegido (solo en memoria); la caja muestra su nombre. */
  archivo?: File | null;
  onElegir: (archivo: File) => void;
  /** Texto del loader que reemplaza la caja mientras se analiza; sin él, la caja. */
  cargando?: string | null;
  disabled?: boolean;
  /** Error de la zona (p. ej. falta el archivo): marca el campo y se le asocia. */
  error?: string | null;
  id?: string;
}

/**
 * Zona de archivo de la carga masiva (HU #13754; compartida con la pantalla única, HU #14085): ayuda, «Descargar
 * plantilla» y la caja punteada del prototipo que acepta .xlsx y .csv. Solo entrega el archivo elegido: quien la usa
 * decide cuándo subirlo. El campo se limpia tras cada elección para que elegir el mismo archivo vuelva a avisar.
 */
export function ZonaArchivo({ archivo, onElegir, cargando, disabled = false, error, id = "archivo-carga" }: ZonaArchivoProps) {
  const errorId = `${id}-error`;

  function alElegir(e: ChangeEvent<HTMLInputElement>) {
    const elegido = e.target.files?.[0];
    e.target.value = "";
    if (elegido) onElegir(elegido);
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className={AYUDA_CLS}>Una fila por vehículo: placa o VIN y, si lo tienes, el documento del locatario.</p>
        <a href={URL_PLANTILLA} download className={BOTON_SECUNDARIO_CLS}>
          <Download className="h-4 w-4" aria-hidden="true" />
          Descargar plantilla
        </a>
      </div>

      {cargando ? (
        <VehicleLoader label={cargando} />
      ) : (
        <label
          htmlFor={id}
          className={`flex flex-col items-center gap-2 rounded-2xl border-2 border-dashed border-[var(--flit-brand-ink)] bg-white px-6 py-8 text-center text-[var(--flit-brand-ink)] has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-60 has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-[var(--nav-focus)] dark:bg-transparent ${disabled ? "" : "cursor-pointer"} ${FOCO_CLS}`}
        >
          <UploadCloud className="h-8 w-8" aria-hidden="true" strokeWidth={1.5} />
          <span className="text-sm font-semibold">Archivo de vehículos (.xlsx o .csv)</span>
          {archivo && <span className="break-all text-sm font-semibold text-flit-primary dark:text-white">{archivo.name}</span>}
          <span className="text-xs">{archivo ? "Haz clic para cambiarlo." : "Haz clic para elegirlo."} Máximo 5 MB.</span>
          <input
            id={id}
            type="file"
            accept={EXTENSIONES_ADMITIDAS.join(",")}
            onChange={alElegir}
            disabled={disabled}
            aria-invalid={error ? true : undefined}
            aria-describedby={error ? errorId : undefined}
            className="sr-only"
          />
        </label>
      )}

      {error && (
        <p id={errorId} className={ERROR_CLS}>
          {error}
        </p>
      )}
    </div>
  );
}

/**
 * Subida del archivo de carga masiva (HU #13754, F02-C5/C6): descarga de la plantilla y caja de carga que acepta solo
 * .xlsx y .csv. Un .xls llega a la API para mostrar su mensaje (guardarlo como .xlsx); otro formato se rechaza con un
 * mensaje claro sin llamar a la API. Los rechazos 413/415/422 muestran el mensaje de la API; un 500 o una caída de red,
 * el error genérico con Reintentar sobre el mismo archivo.
 */
export function SubidaArchivo({ diagnosticoId, onResumen, onAccesoNegado }: SubidaArchivoProps) {
  const [estado, setEstado] = useState<Estado>({ tipo: "inactivo" });

  async function subir(archivo: File) {
    if (!archivoAdmitido(archivo)) {
      setEstado({ tipo: "error", mensaje: MENSAJE_FORMATO_NO_ADMITIDO, reintentar: null });
      return;
    }
    setEstado({ tipo: "subiendo" });
    try {
      const resumen = await analizarCarga(diagnosticoId, archivo);
      setEstado({ tipo: "inactivo" });
      onResumen(resumen, archivo);
    } catch (e) {
      const code = accesoNegado(e);
      if (code && onAccesoNegado) {
        setEstado({ tipo: "inactivo" });
        onAccesoNegado(code);
        return;
      }
      const reintentable = !(e instanceof ApiError) || e.status >= 500;
      setEstado({ tipo: "error", mensaje: e instanceof Error ? e.message : "No se pudo analizar el archivo.", reintentar: reintentable ? archivo : null });
    }
  }

  const subiendo = estado.tipo === "subiendo";

  return (
    <div className="flex flex-col gap-3">
      <ZonaArchivo onElegir={(archivo) => void subir(archivo)} cargando={subiendo ? "Analizando el archivo…" : null} />

      {estado.tipo === "error" && (
        <InlineAlert
          tone="error"
          title="No se pudo analizar el archivo"
          action={
            estado.reintentar ? (
              <button type="button" onClick={() => estado.reintentar && void subir(estado.reintentar)} className={BOTON_REINTENTAR_CLS}>
                Reintentar
              </button>
            ) : undefined
          }
        >
          {estado.mensaje}
        </InlineAlert>
      )}
    </div>
  );
}

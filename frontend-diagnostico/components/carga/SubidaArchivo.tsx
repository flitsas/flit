"use client";

import { useState } from "react";
import { Icono } from "@flit/ui/Icono";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { ZonaArrastre } from "@flit/ui/ZonaArrastre";
import { ApiError } from "@/lib/api";
import { EXTENSIONES_ADMITIDAS, URL_PLANTILLA, accesoNegado, analizarCarga, extensionDe, type ResumenValidacion } from "@/lib/carga/api";
import { AYUDA_CLS, BOTON_PRIMARIO_CLS, BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS } from "@/components/configuracion/estilos";

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

/** Aviso del paso de validación (AC20): validar no gasta consultas. */
export const AVISO_SIN_CONSULTAS = "La validación no dispara consultas externas: solo revisa el archivo.";

export const MENSAJE_FORMATO_NO_ADMITIDO = "Formato no admitido: sube el archivo en .xlsx o .csv (puedes partir de la plantilla).";

/** True si la extensión puede ir a la API (.xlsx, .csv o el .xls que la API rechaza con su mensaje). */
export const archivoAdmitido = (archivo: File): boolean => EXTENSIONES_A_LA_API.includes(extensionDe(archivo.name));

export interface ZonaArchivoProps {
  /** Archivo elegido (solo en memoria); la zona muestra su nombre, su tamaño y «Quitar archivo». */
  archivo?: File | null;
  onElegir: (archivo: File) => void;
  /** «Quitar archivo» de la zona; sin él, quitar no hace nada. */
  onQuitar?: () => void;
  /** Texto del loader que reemplaza la zona mientras se analiza; sin él, la zona. */
  cargando?: string | null;
  disabled?: boolean;
  /** Error de la zona (p. ej. falta el archivo o lo rechazó la API): se asocia al botón «Elegir archivo». */
  error?: string | null;
}

/** Tamaño máximo del archivo de carga (contrato de POST /diagnosticos/{id}/carga). */
export const MAX_BYTES_CARGA = 5 * 1024 * 1024;

/**
 * Zona de archivo de la carga masiva (HU #13754; pantalla única, HU #14085; kit 2026, HU #14562 AC10/AC20): ayuda,
 * «Descargar plantilla» y la ZonaArrastre del kit, que acepta .xlsx y .csv hasta 5 MB por arrastre o con «Elegir
 * archivo» por teclado, y rechaza en pantalla lo demás. Solo entrega el archivo: quien la usa decide cuándo subirlo.
 */
export function ZonaArchivo({ archivo, onElegir, onQuitar, cargando, disabled = false, error }: ZonaArchivoProps) {
  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className={AYUDA_CLS}>Una fila por vehículo: placa o VIN y, si lo tienes, el documento del locatario.</p>
        <a href={URL_PLANTILLA} download className={BOTON_SECUNDARIO_CLS}>
          <Icono nombre="nube-bajar" className="h-4 w-4" />
          Descargar plantilla
        </a>
      </div>

      {cargando ? (
        <VehicleLoader label={cargando} />
      ) : (
        <fieldset disabled={disabled} className="min-w-0 disabled:opacity-60">
          <ZonaArrastre
            etiqueta="Archivo de vehículos (.xlsx o .csv)"
            aceptar={[...EXTENSIONES_ADMITIDAS]}
            maxBytes={MAX_BYTES_CARGA}
            archivo={archivo}
            onArchivo={(f) => (f ? onElegir(f) : onQuitar?.())}
            ayuda="Máximo 5 MB."
            error={error ?? undefined}
          />
        </fieldset>
      )}
    </div>
  );
}

/**
 * Subida del archivo de carga masiva (HU #13754, F02-C5/C6; paso «Subir archivo» de la HU #14562 AC20): la ZonaArrastre
 * entrega el archivo, que queda a la vista con su tamaño y «Quitar archivo», y «Validar archivo» lo analiza con
 * POST /diagnosticos/{id}/carga. La validación no dispara consultas externas. Los rechazos 413/415/422 muestran el
 * mensaje de la API; un 500 o una caída de red, el error genérico con Reintentar sobre el mismo archivo.
 */
export function SubidaArchivo({ diagnosticoId, onResumen, onAccesoNegado }: SubidaArchivoProps) {
  const [estado, setEstado] = useState<Estado>({ tipo: "inactivo" });
  const [archivo, setArchivo] = useState<File | null>(null);

  async function subir(elegido: File) {
    setEstado({ tipo: "subiendo" });
    try {
      const resumen = await analizarCarga(diagnosticoId, elegido);
      setEstado({ tipo: "inactivo" });
      onResumen(resumen, elegido);
    } catch (e) {
      const code = accesoNegado(e);
      if (code && onAccesoNegado) {
        setEstado({ tipo: "inactivo" });
        onAccesoNegado(code);
        return;
      }
      const reintentable = !(e instanceof ApiError) || e.status >= 500;
      setEstado({ tipo: "error", mensaje: e instanceof Error ? e.message : "No se pudo analizar el archivo.", reintentar: reintentable ? elegido : null });
    }
  }

  const subiendo = estado.tipo === "subiendo";

  return (
    <div className="flex flex-col gap-3">
      <ZonaArchivo
        archivo={archivo}
        onElegir={(f) => {
          setEstado({ tipo: "inactivo" });
          setArchivo(f);
        }}
        onQuitar={() => {
          setEstado({ tipo: "inactivo" });
          setArchivo(null);
        }}
        cargando={subiendo ? "Analizando el archivo…" : null}
      />

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

      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <p className={AYUDA_CLS}>{AVISO_SIN_CONSULTAS}</p>
        <button type="button" disabled={!archivo || subiendo} onClick={() => archivo && void subir(archivo)} className={BOTON_PRIMARIO_CLS}>
          Validar archivo
          <Icono nombre="derecha" className="h-4 w-4" />
        </button>
      </div>
    </div>
  );
}

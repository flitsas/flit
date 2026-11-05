"use client";

import { useEffect, useRef, useState } from "react";
import { FileArchive, FileSpreadsheet, Download } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { BOTON_PRIMARIO_CLS, BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { ApiError } from "@/lib/api";
import type { EstadoDiagnostico } from "@/lib/diagnosticos/api";
import { INTERVALO_SONDEO_ZIP_MS, estadoZip, mensajeDescarga, pedirZip, urlArchivoZip, urlExcelLote, type EstadoEvidencia } from "@/lib/reportes/api";
import { useDescarga } from "@/lib/reportes/useDescarga";

export const MENSAJE_ZIP_ERROR = "No se pudo generar el ZIP; vuelve a pedirlo";

type Zip =
  | { fase: "inactivo" }
  | { fase: "pidiendo" }
  | { fase: "preparando"; ev: EstadoEvidencia }
  | { fase: "lista"; ev: EstadoEvidencia }
  | { fase: "error"; mensaje: string; reintentable: boolean };

export interface DescargasLoteProps {
  diagnosticoId: string;
  estadoLote: EstadoDiagnostico;
  /** Capacidad `descargarReportes` de la sesión: sin ella no se pinta nada (la API vuelve a exigir el permiso). */
  puedeDescargar: boolean;
}

const pendientesTexto = (n: number) => (n <= 0 ? "" : n === 1 ? " 1 vehículo sin terminar no se incluye." : ` ${n} vehículos sin terminar no se incluyen.`);

function textoEstado(zip: Zip): string {
  if (zip.fase === "pidiendo") return "Pidiendo el ZIP…";
  if (zip.fase === "preparando") return `Preparando ${zip.ev.vehiculos_incluidos} PDF…${pendientesTexto(zip.ev.vehiculos_pendientes)}`;
  if (zip.fase === "lista") return `El ZIP está listo: ${zip.ev.vehiculos_incluidos} PDF.${pendientesTexto(zip.ev.vehiculos_pendientes)}`;
  return "";
}

/** Siguiente fase según el estado que devolvió la API. */
function faseDe(ev: EstadoEvidencia): Zip {
  if (ev.estado === "LISTA") return { fase: "lista", ev };
  if (ev.estado === "ERROR") return { fase: "error", mensaje: MENSAJE_ZIP_ERROR, reintentable: true };
  return { fase: "preparando", ev };
}

/**
 * Barra de descargas del lote en el tablero (HU #13851, F07-C7/C11/C13): «Descargar Excel» (enlace con download por el BFF)
 * y «Descargar PDF (.zip)», que pide el ZIP en segundo plano (Q2), anuncia «Preparando N PDF…» en una región aria-live y
 * sondea el estado cada 3 s hasta LISTA (enlace del .zip) o ERROR (aviso con Reintentar). El sondeo termina en LISTA, en
 * ERROR, si falla la lectura y al desmontar. El 409 SIN_VEHICULOS_TERMINADOS se explica sin enlace.
 */
export function DescargasLote({ diagnosticoId, estadoLote, puedeDescargar }: DescargasLoteProps) {
  const [zip, setZip] = useState<Zip>({ fase: "inactivo" });
  const excel = useDescarga(`diagnostico-${diagnosticoId.slice(0, 8)}.xlsx`);
  const archivoZip = useDescarga(`diagnostico-${diagnosticoId.slice(0, 8)}.zip`);
  const montado = useRef(true);
  useEffect(() => {
    montado.current = true;
    return () => {
      montado.current = false;
    };
  }, []);

  const enCurso = zip.fase === "preparando" ? zip.ev : null;
  useEffect(() => {
    if (!enCurso) return;
    const control = new AbortController();
    const temporizador = setTimeout(() => {
      estadoZip(diagnosticoId, enCurso.id, control.signal)
        .then((ev) => {
          if (!control.signal.aborted) setZip(faseDe(ev));
        })
        .catch((e: unknown) => {
          if (!control.signal.aborted) setZip({ fase: "error", mensaje: mensajeDescarga(e), reintentable: true });
        });
    }, INTERVALO_SONDEO_ZIP_MS);
    return () => {
      clearTimeout(temporizador);
      control.abort();
    };
  }, [diagnosticoId, enCurso]);

  async function pedir() {
    setZip({ fase: "pidiendo" });
    try {
      const ev = await pedirZip(diagnosticoId);
      if (montado.current) setZip(faseDe(ev));
    } catch (e) {
      if (!montado.current) return;
      const sinTerminados = e instanceof ApiError && e.status === 409 && e.code === "SIN_VEHICULOS_TERMINADOS";
      setZip({ fase: "error", mensaje: mensajeDescarga(e), reintentable: !sinTerminados });
    }
  }

  if (!puedeDescargar) return null;

  const ocupado = zip.fase === "pidiendo" || zip.fase === "preparando";
  // Un lote sin lanzar no tiene nada que descargar (la API responde 409 LOTE_SIN_LANZAR / SIN_VEHICULOS_TERMINADOS).
  const sinLanzar = estadoLote === "CREADO";
  const estado = textoEstado(zip);

  return (
    <section aria-label="Descargas del lote" className={`${TARJETA_CLS} flex flex-col gap-3`}>
      <div className="flex flex-wrap items-center gap-3">
        <a href={urlExcelLote(diagnosticoId)} download onClick={excel.alHacerClic} aria-busy={excel.descargando || undefined} className={BOTON_SECUNDARIO_CLS}>
          <FileSpreadsheet className="h-4 w-4" aria-hidden="true" />
          Descargar Excel
        </a>
        <button type="button" onClick={() => void pedir()} disabled={ocupado || sinLanzar} aria-busy={ocupado} className={BOTON_SECUNDARIO_CLS}>
          <FileArchive className="h-4 w-4" aria-hidden="true" />
          Descargar PDF (.zip)
        </button>
        {zip.fase === "lista" && (
          <a
            href={urlArchivoZip(diagnosticoId, zip.ev.id)}
            download
            onClick={archivoZip.alHacerClic}
            aria-busy={archivoZip.descargando || undefined}
            className={BOTON_PRIMARIO_CLS}
          >
            <Download className="h-4 w-4" aria-hidden="true" />
            Descargar el ZIP
          </a>
        )}
      </div>

      {/* La región vive siempre en el DOM (vacía e invisible si no hay ZIP) para que el lector de pantalla anuncie cada cambio. */}
      <p role="status" aria-live="polite" aria-label="Estado del ZIP" className={estado ? "text-sm font-medium text-[var(--nav-texto)] dark:text-slate-300" : "sr-only"}>
        {estado}
      </p>

      {zip.fase === "error" && (
        <InlineAlert
          tone={zip.reintentable ? "error" : "warning"}
          title={zip.reintentable ? "No se completó el ZIP" : "Todavía no hay PDF para el ZIP"}
          action={
            zip.reintentable ? (
              <button type="button" onClick={() => void pedir()} className={BOTON_REINTENTAR_CLS}>
                Reintentar
              </button>
            ) : undefined
          }
        >
          {zip.mensaje}
        </InlineAlert>
      )}
      {excel.error && (
        <InlineAlert tone="error" title="No se descargó el Excel">
          {excel.error}
        </InlineAlert>
      )}
      {archivoZip.error && (
        <InlineAlert tone="error" title="No se descargó el ZIP">
          {archivoZip.error}
        </InlineAlert>
      )}
    </section>
  );
}

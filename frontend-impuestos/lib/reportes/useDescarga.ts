"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { NOMBRE_FORMATO, type FormatoExportacion, type TipoReporte } from "./catalogo";
import { descargarArchivo, mensajeExportacion, urlExportacion, type FiltrosReporte } from "./api";

/**
 * Exportación de un reporte (forma de useDescarga de Diagnóstico): un formato en vuelo a la vez por botón, el error de la
 * última exportación (literal del contrato o genérico) y un `claveConsulta` que lo borra cuando cambia el filtro.
 */
export function useDescarga(tipo: TipoReporte, filtros: FiltrosReporte, empresa: string | null, claveConsulta: string) {
  const [enVuelo, setEnVuelo] = useState<Record<FormatoExportacion, boolean>>({ excel: false, pdf: false });
  const [error, setError] = useState<string | null>(null);
  const [claveDelError, setClaveDelError] = useState(claveConsulta);
  if (claveConsulta !== claveDelError) {
    setClaveDelError(claveConsulta);
    setError(null);
  }
  const montado = useRef(true);
  useEffect(() => {
    montado.current = true;
    return () => {
      montado.current = false;
    };
  }, []);

  const exportar = useCallback(
    (formato: FormatoExportacion) => {
      setError(null);
      setEnVuelo((e) => ({ ...e, [formato]: true }));
      const extension = formato === "excel" ? "xlsx" : "pdf";
      descargarArchivo(urlExportacion(tipo, formato, filtros, empresa), `reporte-${tipo}.${extension}`)
        .catch((err: unknown) => {
          if (montado.current) setError(mensajeExportacion(err, formato));
        })
        .finally(() => {
          if (montado.current) setEnVuelo((e) => ({ ...e, [formato]: false }));
        });
    },
    [tipo, filtros, empresa],
  );

  return { enVuelo, error, exportar, nombreFormato: NOMBRE_FORMATO };
}

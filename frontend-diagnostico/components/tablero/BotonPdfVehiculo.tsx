"use client";

import { useId } from "react";
import { FileDown } from "lucide-react";
import { AYUDA_CLS, BOTON_SECUNDARIO_CLS } from "@/components/configuracion/estilos";
import type { EstadoDiagnostico } from "@/lib/diagnosticos/api";
import { puedeEmitirPdf, urlPdfVehiculo } from "@/lib/reportes/api";
import { useDescarga } from "@/lib/reportes/useDescarga";
import type { FilaVehiculo } from "@/lib/tablero/api";

export const AYUDA_PDF_EN_CURSO = "Disponible cuando terminen las consultas de este vehículo";
export const AYUDA_PDF_NO_DISPONIBLE = "Las consultas no disponibles aparecerán así en el PDF";

export interface BotonPdfVehiculoProps {
  diagnosticoId: string;
  estadoLote: EstadoDiagnostico;
  vehiculo: FilaVehiculo;
}

/**
 * «PDF» de una fila de la grilla (HU #13851, F07-C4/C13): enlace con `download` al PDF del vehículo por el BFF cuando el
 * backend lo emitiría (`puedeEmitirPdf`, la regla de EmisionPdf.Puede); si no, botón deshabilitado con la ayuda «Disponible
 * cuando terminen…». En un lote INCOMPLETO con celdas en error avisa que saldrán como «consulta no disponible». Un 409 del
 * servidor se muestra en texto, sin romper la fila. Nunca distingue celdas manuales (R-20).
 */
export function BotonPdfVehiculo({ diagnosticoId, estadoLote, vehiculo }: BotonPdfVehiculoProps) {
  const idAyuda = useId();
  const { descargando, error, alHacerClic } = useDescarga(`diagnostico-${vehiculo.identificador}.pdf`);
  const nombre = `Descargar PDF de ${vehiculo.identificador}`;
  const habilitado = puedeEmitirPdf(estadoLote, vehiculo);
  const conNoDisponibles = habilitado && estadoLote === "INCOMPLETO" && vehiculo.celdas.some((c) => c.estado === "ERROR");
  const ayuda = !habilitado ? AYUDA_PDF_EN_CURSO : conNoDisponibles ? AYUDA_PDF_NO_DISPONIBLE : null;

  return (
    <div className="flex flex-col items-start gap-1">
      {habilitado ? (
        <a
          href={urlPdfVehiculo(diagnosticoId, vehiculo.id)}
          download
          onClick={alHacerClic}
          aria-label={nombre}
          aria-describedby={ayuda ? idAyuda : undefined}
          aria-busy={descargando || undefined}
          className={BOTON_SECUNDARIO_CLS}
        >
          <FileDown className="h-4 w-4" aria-hidden="true" />
          PDF
        </a>
      ) : (
        <button type="button" disabled aria-label={nombre} aria-describedby={idAyuda} className={BOTON_SECUNDARIO_CLS}>
          <FileDown className="h-4 w-4" aria-hidden="true" />
          PDF
        </button>
      )}
      {ayuda && (
        <span id={idAyuda} className={`${AYUDA_CLS} max-w-[12rem]`}>
          {ayuda}
        </span>
      )}
      {error && (
        <p role="alert" className="max-w-[12rem] text-xs font-semibold" style={{ color: "var(--badge-danger-fg)" }}>
          {error}
        </p>
      )}
    </div>
  );
}

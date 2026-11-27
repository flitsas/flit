"use client";

import { useId } from "react";
import { buttonClass } from "@flit/ui/Button";
import { iconoComo } from "@flit/ui/iconoComo";
import { ERROR_CLS, TEXTO_SECUNDARIO_CLS } from "@/components/comun/estilos";
import { formatoEntero } from "@/lib/estadisticas/formato";
import { NOMBRE_FORMATO, TOPE_EXPORTACION, type FormatoExportacion } from "@/lib/reportes/catalogo";

const FORMATOS: FormatoExportacion[] = ["excel", "pdf"];
const IconoBajar = iconoComo("nube-bajar");

/**
 * «Exportar Excel» y «Exportar PDF» (spec-visual §3.7; forma de BotonesExportar de Diagnóstico): exportan TODO el filtro.
 * Un botón se deshabilita con `aria-disabled` y el motivo visible (cargando, vacío o por encima del tope de su formato:
 * 5.000 Excel, 1.000 PDF) y sigue enfocable para que el motivo se lea. En vuelo: «Generando…» con `aria-busy`. Un 422
 * TOPE_EXPORTACION u otro fallo se explica bajo los botones (`role="alert"`). Los dos apuntan al aviso de datos personales.
 */
export function BotonesExportar({
  total,
  cargando,
  avisoId,
  enVuelo,
  error,
  onExportar,
}: {
  total: number | null;
  cargando: boolean;
  avisoId: string;
  enVuelo: Record<FormatoExportacion, boolean>;
  error: string | null;
  onExportar: (formato: FormatoExportacion) => void;
}) {
  const id = useId();

  function motivo(formato: FormatoExportacion): string | null {
    if (cargando || total === null) return "Espera a que cargue el reporte";
    if (total === 0) return "No hay registros para exportar";
    if (total > TOPE_EXPORTACION[formato]) return `Acota el filtro: máximo ${formatoEntero(TOPE_EXPORTACION[formato])} registros en ${NOMBRE_FORMATO[formato]}`;
    return null;
  }

  const motivos = FORMATOS.map((f) => ({ formato: f, texto: motivo(f) }));
  // Un mismo motivo para los dos formatos (vacío o cargando) se escribe una sola vez.
  const unicos = [...new Map(motivos.filter((m) => m.texto).map((m) => [m.texto, m])).values()];
  const algunoEnVuelo = enVuelo.excel || enVuelo.pdf;

  return (
    <div className="flex flex-col items-start gap-1 sm:items-end">
      <div className="flex flex-wrap items-center gap-2">
        {FORMATOS.map((f) => {
          const texto = motivo(f);
          const describe = [avisoId, texto ? `${id}-motivo-${unicos.findIndex((u) => u.texto === texto)}` : null, `${id}-ayuda`].filter(Boolean).join(" ");
          return (
            <button
              key={f}
              type="button"
              onClick={() => {
                if (!texto && !enVuelo[f]) onExportar(f);
              }}
              aria-disabled={texto ? true : undefined}
              aria-busy={enVuelo[f] ? true : undefined}
              aria-describedby={describe}
              className={buttonClass({ variant: "secondary", size: "sm", className: `gap-1.5 text-sm ${texto ? "cursor-not-allowed opacity-60" : ""}` })}
            >
              <IconoBajar className="h-4 w-4" aria-hidden="true" />
              {enVuelo[f] ? "Generando…" : `Exportar ${NOMBRE_FORMATO[f]}`}
            </button>
          );
        })}
      </div>
      <p id={`${id}-ayuda`} className={`m-0 ${TEXTO_SECUNDARIO_CLS}`}>
        {algunoEnVuelo ? "Puede tardar unos segundos" : total ? `Se exportan los ${formatoEntero(total)} registros del filtro` : null}
      </p>
      {unicos.map((m, i) => (
        <p key={m.formato} id={`${id}-motivo-${i}`} className={`m-0 ${TEXTO_SECUNDARIO_CLS}`}>
          {m.texto}
        </p>
      ))}
      {error ? (
        <p role="alert" className={`m-0 ${ERROR_CLS}`}>
          {error}
        </p>
      ) : null}
    </div>
  );
}

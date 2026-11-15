"use client";

import { useEffect, useId, useRef, useState } from "react";
import { buttonClass } from "@flit/ui/Button";
import { Icono } from "@flit/ui/Icono";
import { formatoEntero } from "@/lib/estadisticas/formato";
import { NOMBRE_FORMATO, mensajeDescargaReporte } from "@/lib/reportes/api";
import type { FormatoExportacion, ReporteCatalogo } from "@/lib/reportes/tipos";

const FORMATOS: FormatoExportacion[] = ["excel", "pdf"];

/**
 * «Exportar Excel» y «Exportar PDF» (spec-ui §5.4): secundarios, exportan TODO el filtro (no solo la página). Un botón se
 * deshabilita con `aria-disabled` y el motivo visible (vacío, cargando o total por encima del máximo de ese formato, que
 * viene del catálogo); sigue enfocable para que el motivo se lea. En vuelo: «Generando…» con `aria-busy` y el otro
 * formato sigue activo. El 422 y el 429 se explican bajo los botones (`role="alert"`) y la tabla no cae. Los dos apuntan
 * al aviso de datos personales (`aria-describedby`) y, si el reporte trae documentos, se lee «Contiene datos personales».
 */
export function BotonesExportar({
  reporte,
  total,
  cargando,
  avisoId,
  onDescargar,
}: {
  reporte: ReporteCatalogo;
  total: number | null;
  cargando: boolean;
  avisoId: string;
  onDescargar: (formato: FormatoExportacion) => Promise<unknown>;
}) {
  const id = useId();
  const [enVuelo, setEnVuelo] = useState<Record<FormatoExportacion, boolean>>({ excel: false, pdf: false });
  const [error, setError] = useState<string | null>(null);
  const montado = useRef(true);
  useEffect(() => {
    montado.current = true;
    return () => {
      montado.current = false;
    };
  }, []);

  const maximo: Record<FormatoExportacion, number> = { excel: reporte.maximo_excel, pdf: reporte.maximo_pdf };

  function motivo(formato: FormatoExportacion): string | null {
    if (cargando || total === null) return "Espera a que cargue el reporte";
    if (total === 0) return "No hay registros para exportar";
    if (total > maximo[formato]) return `Acota el filtro: máximo ${formatoEntero(maximo[formato])} registros en ${NOMBRE_FORMATO[formato]}`;
    return null;
  }

  function exportar(formato: FormatoExportacion) {
    if (motivo(formato) || enVuelo[formato]) return;
    setError(null);
    setEnVuelo((e) => ({ ...e, [formato]: true }));
    onDescargar(formato)
      .catch((err: unknown) => {
        if (montado.current) setError(mensajeDescargaReporte(err, formato));
      })
      .finally(() => {
        if (montado.current) setEnVuelo((e) => ({ ...e, [formato]: false }));
      });
  }

  const motivos = FORMATOS.map((f) => ({ formato: f, texto: motivo(f) }));
  // Un mismo motivo para los dos formatos (vacío o cargando) se escribe una sola vez.
  const unicos = [...new Map(motivos.filter((m) => m.texto).map((m) => [m.texto, m])).values()];
  const algunoEnVuelo = enVuelo.excel || enVuelo.pdf;

  return (
    <div className="flex flex-col items-start gap-1 sm:items-end">
      <div className="flex flex-wrap items-center gap-2">
        {reporte.contiene_pii ? (
          <span className="inline-flex items-center gap-1 text-xs font-semibold text-[color:var(--badge-warning-fg)]">
            <Icono nombre="alerta" className="h-3.5 w-3.5" />
            Contiene datos personales
          </span>
        ) : null}
        {FORMATOS.map((f) => {
          const texto = motivo(f);
          const describe = [avisoId, texto ? `${id}-motivo-${unicos.findIndex((u) => u.texto === texto)}` : null, `${id}-ayuda`].filter(Boolean).join(" ");
          return (
            <button
              key={f}
              type="button"
              onClick={() => exportar(f)}
              aria-disabled={texto ? true : undefined}
              aria-busy={enVuelo[f] ? true : undefined}
              aria-describedby={describe}
              className={buttonClass({
                variant: "secondary",
                size: "sm",
                className: `gap-1.5 text-sm ${texto ? "cursor-not-allowed opacity-60" : ""}`,
              })}
            >
              <Icono nombre="nube-bajar" className="h-4 w-4" />
              {enVuelo[f] ? "Generando…" : `Exportar ${NOMBRE_FORMATO[f]}`}
            </button>
          );
        })}
      </div>
      <p id={`${id}-ayuda`} className="text-xs text-[color:var(--di-text-secondary)]">
        {algunoEnVuelo ? "Puede tardar unos segundos" : total ? `Se exportan los ${formatoEntero(total)} registros del filtro` : null}
      </p>
      {unicos.map((m, i) => (
        <p key={m.formato} id={`${id}-motivo-${i}`} className="text-xs text-[color:var(--di-text-secondary)]">
          {m.texto}
        </p>
      ))}
      {error ? (
        <p role="alert" className="text-xs font-medium text-[color:var(--di-danger)]">
          {error}
        </p>
      ) : null}
    </div>
  );
}

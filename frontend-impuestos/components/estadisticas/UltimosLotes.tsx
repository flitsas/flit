import { useId } from "react";
import Link from "next/link";
import { BarraProgreso } from "@flit/ui/BarraProgreso";
import { EtiquetaEnProceso } from "@flit/ui/EtiquetaEnProceso";
import { ETIQUETA_PUNTO_CLS, etiquetaKit, type StatusBadgeTone } from "@flit/ui/StatusBadge";
import { ENLACE_ACCION_CLS } from "@/components/comun/estilos";
import { hrefConEmpresa } from "@/lib/empresaSeleccionada";
import { formatoEntero, formatoFecha } from "@/lib/estadisticas/formato";
import type { ResumenLiquidacion } from "@/lib/estadisticas/tipos";
import { ETIQUETA_ESTADO_LIQUIDACION, ETIQUETA_TIPO_LIQUIDACION } from "@/lib/reportes/catalogo";
import { rotular } from "@/lib/rotulos";
import { SECCION_CLS, SUBTITULO_CLS, TITULO_SECCION_CLS } from "./Figura";

/** Tono del estado del lote (spec-visual §1.1); EN_PROCESO va con EtiquetaEnProceso. */
const TONO_LOTE: Readonly<Record<string, StatusBadgeTone>> = {
  CREADO: "neutral",
  COMPLETADO: "success",
  COMPLETADO_CON_NOVEDADES: "warning",
  CERRADO_MANUALMENTE: "tinta",
};

function ChipLote({ estado }: { estado: string }) {
  const texto = rotular({ contexto: "estado del lote", codigo: estado, respaldo: ETIQUETA_ESTADO_LIQUIDACION });
  if (estado === "EN_PROCESO") return <EtiquetaEnProceso etiqueta={texto} nombreAccesible={`Estado del lote: ${texto}`} />;
  const kit = etiquetaKit(TONO_LOTE[estado] ?? "neutral");
  return (
    <span className={kit.className} style={kit.style}>
      <span aria-hidden="true" className={ETIQUETA_PUNTO_CLS} />
      {texto}
    </span>
  );
}

/**
 * «Últimos 5 lotes» del dashboard (spec-visual §3.1): «{Tipo} · {fecha}», chip de estado, avance «{t} de {n} placas» y
 * «Ver lote», que conserva la compañía elegida (?empresa=).
 */
export function UltimosLotes({ lotes, empresa }: { lotes: ResumenLiquidacion[]; empresa: string | null }) {
  const id = useId();
  return (
    <section aria-labelledby={`${id}-titulo`} className={SECCION_CLS}>
      <h2 id={`${id}-titulo`} className={TITULO_SECCION_CLS}>
        Últimos 5 lotes
      </h2>
      {lotes.length === 0 ? (
        <p className={`m-0 ${SUBTITULO_CLS}`}>Aún no hay lotes en este periodo.</p>
      ) : (
        <ul className="m-0 flex list-none flex-col gap-3 p-0">
          {lotes.map((l) => {
            const n = Math.max(0, l.totalItems);
            const t = Math.min(n, Math.max(0, n - (l.contadores?.pendientes ?? 0)));
            const fecha = formatoFecha(l.creadaEn);
            const tipo = rotular({ contexto: "tipo de liquidación", codigo: l.tipo, respaldo: ETIQUETA_TIPO_LIQUIDACION });
            return (
              <li key={l.id} className="flex flex-col gap-2 border-b border-[color:var(--di-divider)] pb-3 last:border-b-0 last:pb-0">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="text-sm font-semibold text-flit-primary">
                    {tipo} · <time dateTime={l.creadaEn}>{fecha}</time>
                  </span>
                  <ChipLote estado={l.estado} />
                </div>
                <BarraProgreso etiqueta={`Avance del lote del ${fecha}`} valor={t} maximo={n} textoValor={`${t} de ${n} placas`} />
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className={SUBTITULO_CLS}>
                    {formatoEntero(t)} de {formatoEntero(n)} placas
                  </span>
                  <Link href={hrefConEmpresa(`/liquidaciones/${encodeURIComponent(l.id)}`, empresa)} className={ENLACE_ACCION_CLS}>
                    Ver lote<span className="sr-only"> del {fecha}</span>
                  </Link>
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

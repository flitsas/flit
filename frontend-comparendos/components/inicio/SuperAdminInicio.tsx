"use client";

import Link from "next/link";
import { InlineAlert } from "@flit/ui";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { Cargador } from "@flit/ui/Loader";
import { METRICAS_GRID_CLS, TarjetaMetrica } from "@flit/ui/TarjetaMetrica";
import { PanelInicio } from "@/components/inicio/PanelInicio";
import { apiFetch } from "@/lib/api";
import type { ResumenPlataforma } from "@/lib/reportes";
import { useCarga } from "@/lib/useCarga";

const n = (x: number) => x.toLocaleString("es-CO");

/**
 * Inicio del SuperAdmin con «Todas» las empresas (rediseño R2, GET /admin/resumen): la salud de hoy y las empresas que
 * necesitan algo, cada una con su enlace a donde se arregla. Con una empresa elegida, el SuperAdmin ve el Inicio de esa
 * gestora (lo decide la página).
 */
export function SuperAdminInicio() {
  const resumen = useCarga("inicio-superadmin", (signal) => apiFetch<ResumenPlataforma>("/admin/resumen", { signal }));
  const r = resumen.datos;
  if (resumen.error)
    return (
      <InlineAlert tone="error" title="No se pudo cargar la salud de la plataforma">
        {resumen.error}{" "}
        <button type="button" className="font-semibold underline" onClick={resumen.recargar}>
          Reintentar
        </button>
      </InlineAlert>
    );
  if (!r) return <Cargador />;
  const ingestaOk = r.ingesta24h.conError === 0;
  return (
    <>
      <PanelInicio titulo="Salud de hoy" subtitulo="Lo que FLIT tiene que mirar en todas las empresas. Cada tarjeta lleva a donde se revisa." icono="medidor" data-testid="salud">
        <div className={METRICAS_GRID_CLS}>
          <TarjetaMetrica
            etiqueta="Empresas activas"
            cifra={`${n(r.empresas.activas)} de ${n(r.empresas.total)}`}
            pendiente={r.empresas.pendientes > 0}
            pie={`${n(r.empresas.pendientes)} pendientes de configurar · ${n(r.empresas.inactivas)} inactivas`}
            href="/admin/empresas"
            enlace={Link}
            aria-label={`Empresas activas: ${n(r.empresas.activas)} de ${n(r.empresas.total)}; ${n(r.empresas.pendientes)} pendientes de configurar. Ir al Parametrizador`}
          />
          <TarjetaMetrica
            etiqueta="Consultas con error (24 h)"
            cifra={n(r.ingesta24h.conError)}
            pendiente={!ingestaOk}
            colorCifra={ingestaOk ? "var(--flit-success-ink)" : undefined}
            pie={ingestaOk ? `${n(r.ingesta24h.consultas)} consultas sin errores` : `De ${n(r.ingesta24h.consultas)} consultas · ${n(r.ingesta24h.empresasConError)} empresas afectadas`}
            href="/admin/ingesta"
            enlace={Link}
            aria-label={`Consultas con error en las últimas 24 horas: ${n(r.ingesta24h.conError)} de ${n(r.ingesta24h.consultas)}. Ir a la ingesta`}
          />
          <TarjetaMetrica
            etiqueta="Correos fallidos (7 días)"
            cifra={n(r.correosFallidos7d)}
            pendiente={r.correosFallidos7d > 0}
            pie="Fallidos o rebotados"
            href="/mensajes?estado=Fallido"
            enlace={Link}
            aria-label={`Correos fallidos o rebotados en 7 días: ${n(r.correosFallidos7d)}. Ver los mensajes`}
          />
          <TarjetaMetrica
            etiqueta="OCR por revisar"
            cifra={n(r.ocrPorRevisar)}
            pendiente={r.ocrPorRevisar > 0}
            pie="Leídos y sin registrar"
            href="/ocr"
            enlace={Link}
            aria-label={`Documentos de OCR por revisar: ${n(r.ocrPorRevisar)}. Ir a OCR`}
          />
          <TarjetaMetrica
            etiqueta="Derechos de petición del mes"
            cifra={n(r.dpDelMes.enviados)}
            pie={`${n(r.dpDelMes.favorables)} favorables este mes`}
          />
        </div>
      </PanelInicio>

      <PanelInicio
        titulo="Empresas que necesitan algo"
        subtitulo={r.necesitanAlgo.length === 0 ? undefined : `${n(r.necesitanAlgo.length)} de ${n(r.empresas.total)}. Se arreglan en el Parametrizador.`}
        icono="alerta"
        data-testid="necesitan-algo"
      >
        {r.necesitanAlgo.length === 0 ? (
          <EstadoVacio titulo="Todas las empresas están listas" mensaje="Ninguna tiene configuración pendiente." />
        ) : (
          <ul className="m-0 grid list-none grid-cols-[repeat(auto-fill,minmax(min(100%,320px),1fr))] gap-3 p-0">
            {r.necesitanAlgo.map((e) => (
              <li key={e.empresaId}>
                <Link
                  href="/admin/empresas"
                  aria-label={`${e.nombre}: ${e.motivos.join(", ")}. Ir al Parametrizador`}
                  className="flex h-full flex-col gap-2 rounded-[9px] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] p-4 text-inherit no-underline hover:border-[color:var(--kit-azul-texto)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--kit-azul-texto)]"
                >
                  <span className="text-sm font-semibold text-[color:var(--di-text-primary)]">{e.nombre}</span>
                  <ul className="m-0 flex list-none flex-wrap gap-1.5 p-0">
                    {e.motivos.map((m) => (
                      <li key={m} className="rounded-full bg-[var(--badge-warning-bg)] px-2.5 py-0.5 text-xs font-medium text-[color:var(--badge-warning-fg)]">
                        {m}
                      </li>
                    ))}
                  </ul>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </PanelInicio>
    </>
  );
}

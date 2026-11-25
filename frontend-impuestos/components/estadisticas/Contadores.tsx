"use client";

import Link from "next/link";
import { Icono } from "@flit/ui/Icono";
import { TarjetaMetrica } from "@flit/ui/TarjetaMetrica";
import { formatoEntero } from "@/lib/estadisticas/formato";
import type { DashboardImpuestos } from "@/lib/estadisticas/tipos";

export type EstadoCarga = "cargando" | "error" | "listo";

export interface EnlacesContadores {
  liquidaciones?: string;
  lotes?: string;
  errores?: string;
  deuda?: string;
}

interface Kpi {
  clave: keyof EnlacesContadores | "exitosas" | "pendientes";
  etiqueta: string;
  icono: string;
  cifra: (d: DashboardImpuestos) => number | null | undefined;
  pie?: (d: DashboardImpuestos) => string | null;
}

const pendientes = (d: DashboardImpuestos) => (d.resultados ? d.resultados.pendientesWebhook + d.resultados.pendientesConsulta : null);

/** Los 6 KPIs del dashboard en el orden de spec-visual §3.1 (etiquetas de x-flit-etiquetas donde el contrato las trae). */
const KPIS: Kpi[] = [
  {
    clave: "liquidaciones",
    etiqueta: "Liquidaciones",
    icono: "reportes-detallados",
    cifra: (d) => d.contadores?.total,
    pie: (d) => (d.contadores ? `${formatoEntero(d.contadores.individuales)} por placa · ${formatoEntero(d.contadores.masivas)} masivas` : null),
  },
  { clave: "lotes", etiqueta: "Lotes en proceso", icono: "en-curso", cifra: (d) => d.contadores?.lotesEnProceso },
  { clave: "exitosas", etiqueta: "Exitosas", icono: "finalizados", cifra: (d) => d.resultados?.exitosas },
  { clave: "errores", etiqueta: "Con error", icono: "errores", cifra: (d) => d.resultados?.conError },
  {
    clave: "pendientes",
    etiqueta: "Pendientes de resultado",
    icono: "pendientes",
    cifra: pendientes,
    pie: (d) => (d.resultados && d.resultados.inciertas > 0 ? `${formatoEntero(d.resultados.inciertas)} inciertas` : null),
  },
  {
    clave: "deuda",
    etiqueta: "Placas con deuda",
    icono: "pagos",
    cifra: (d) => d.deuda?.placasConDeuda,
    pie: (d) => (d.deuda ? `${formatoEntero(d.deuda.placasAlDia)} al día` : null),
  },
];

/**
 * Rejilla de KPIs del dashboard (spec-visual §3.1: `TarjetaMetrica`, sin `colorCifra`). Cargando: «—» y aria-busy. Error:
 * «—» con el ícono de alerta y «Error al cargar {etiqueta}» para lectores de pantalla. Un bloque null: «—», nunca 0.
 */
export function Contadores({ datos, estado, enlaces = {} }: { datos: DashboardImpuestos | null; estado: EstadoCarga; enlaces?: EnlacesContadores }) {
  return (
    <div aria-busy={estado === "cargando"} className="grid grid-cols-2 gap-4 md:grid-cols-3 xl:grid-cols-6">
      {KPIS.map((k) => {
        const listo = estado === "listo" && datos !== null;
        const valor = listo ? k.cifra(datos) : null;
        const cifra =
          estado === "error" ? (
            <span className="inline-flex items-center gap-1.5">
              —
              <Icono nombre="alerta" className="h-4 w-4 text-[color:var(--badge-danger-fg)]" />
              <span className="sr-only">Error al cargar {k.etiqueta.toLowerCase()}</span>
            </span>
          ) : (
            formatoEntero(valor)
          );
        const href = listo && k.clave in enlaces ? enlaces[k.clave as keyof EnlacesContadores] : undefined;
        const pie = listo && k.pie ? k.pie(datos) : null;
        return (
          <TarjetaMetrica
            key={k.clave}
            data-testid={`kpi-${k.clave}`}
            etiqueta={<span className="line-clamp-2">{k.etiqueta}</span>}
            cifra={cifra}
            icono={k.icono}
            pie={pie ?? undefined}
            href={href}
            enlace={href ? Link : undefined}
          />
        );
      })}
    </div>
  );
}

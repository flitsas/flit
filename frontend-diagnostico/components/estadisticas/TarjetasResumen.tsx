"use client";

import Link from "next/link";
import { Icono } from "@flit/ui/Icono";
import { TarjetaMetrica } from "@flit/ui/TarjetaMetrica";
import { formatoEntero, formatoPorcentaje, formatoSegundos, SIN_DATOS } from "@/lib/estadisticas/formato";
import type { ResumenEstadisticas } from "@/lib/estadisticas/tipos";
import type { EstadoCarga } from "./useEstadisticas";

export interface EnlacesResumen {
  diagnosticos?: string;
  vehiculos?: string;
  saneamientos?: string;
}

interface Kpi {
  clave: string;
  etiqueta: string;
  icono: string;
  cifra: (r: ResumenEstadisticas) => string;
  pie?: (r: ResumenEstadisticas) => string;
  enlace?: keyof EnlacesResumen;
}

/** Los 6 KPIs del resumen en el orden de spec-ui §4.3. */
const KPIS: Kpi[] = [
  {
    clave: "diagnosticos",
    etiqueta: "Diagnósticos",
    icono: "diagnostico",
    cifra: (r) => formatoEntero(r.diagnosticos),
    pie: (r) => `${formatoEntero(r.diagnosticos_express)} express · ${formatoEntero(r.diagnosticos_masivos)} masivos`,
    enlace: "diagnosticos",
  },
  {
    clave: "vehiculos",
    etiqueta: "Vehículos",
    icono: "consulta-placa",
    cifra: (r) => formatoEntero(r.vehiculos),
    pie: (r) => `${formatoEntero(r.vehiculos_unicos)} únicos`,
    enlace: "vehiculos",
  },
  {
    clave: "consultas",
    etiqueta: "Consultas a fuentes",
    icono: "auditoria",
    cifra: (r) => formatoEntero(r.consultas),
    pie: (r) => (r.tasa_exito == null ? SIN_DATOS : `${formatoPorcentaje(r.tasa_exito)} de éxito`),
  },
  { clave: "tiempo", etiqueta: "Tiempo promedio de consulta", icono: "tiempo", cifra: (r) => formatoSegundos(r.tiempo_promedio_consulta_ms) },
  {
    clave: "saneamientos",
    etiqueta: "Saneamientos solicitados",
    icono: "pendientes",
    cifra: (r) => formatoEntero(r.saneamientos_solicitados),
    enlace: "saneamientos",
  },
  { clave: "tramites", etiqueta: "Trámites creados", icono: "tramites", cifra: (r) => formatoEntero(r.tramites_creados) },
];

/**
 * Rejilla de KPIs del dashboard (spec-ui §4.5): `TarjetaMetrica` con cifra es-CO. Cargando: `—` y aria-busy en la rejilla (sin
 * `pendiente`, que es «algo espera atención», auditoría UI UA-2). Error: `—` con el ícono de alerta y «Error al cargar {etiqueta}» para lectores de pantalla.
 */
export function TarjetasResumen({ resumen, estado, enlaces = {} }: { resumen: ResumenEstadisticas | null; estado: EstadoCarga; enlaces?: EnlacesResumen }) {
  return (
    <div aria-busy={estado === "cargando"} className="grid grid-cols-2 gap-4 md:grid-cols-3 xl:grid-cols-6">
      {KPIS.map((k) => {
        const listo = estado === "listo" && resumen !== null;
        const cifra = listo ? (
          k.cifra(resumen)
        ) : estado === "error" ? (
          <span className="inline-flex items-center gap-1.5">
            —
            <Icono nombre="alerta" className="h-4 w-4 text-[color:var(--di-danger)]" />
            <span className="sr-only">Error al cargar {k.etiqueta.toLowerCase()}</span>
          </span>
        ) : (
          "—"
        );
        const href = listo && k.enlace ? enlaces[k.enlace] : undefined;
        return (
          <TarjetaMetrica
            key={k.clave}
            data-testid={`kpi-${k.clave}`}
            etiqueta={<span className="line-clamp-2">{k.etiqueta}</span>}
            cifra={cifra}
            icono={k.icono}
            pie={listo && k.pie ? k.pie(resumen) : undefined}
            href={href}
            enlace={href ? Link : undefined}
          />
        );
      })}
    </div>
  );
}

"use client";

import type { CSSProperties } from "react";
import { CheckCircle2, Clock, LoaderCircle, MinusCircle, XCircle, type LucideIcon } from "lucide-react";
import { BLOQUEADA_POR_RUNT, type CeldaGrilla, type EstadoCelda, type MotivoNa } from "@/lib/tablero/api";
import { BotonReprocesar, type BotonReprocesarProps } from "./BotonReprocesar";

type Tono = "neutral" | "success" | "danger" | "na";

/**
 * Semáforo por estado con los tokens de @flit/ui (tokens.css), todos AA en claro y oscuro: gris (neutral) pendiente o en
 * curso, verde completada, rojo error y gris claro con borde punteado la N/A. El texto del rojo usa la tinta
 * `--badge-pending-fg` (4,71:1 sobre el fondo rojo tintado; `--badge-danger-fg` da 4,46:1). El estado nunca depende solo
 * del color: cada chip lleva icono y texto.
 */
const ESTADOS: Record<EstadoCelda, { tono: Tono; texto: string; Icono: LucideIcon; animado?: boolean }> = {
  PENDIENTE: { tono: "neutral", texto: "Pendiente", Icono: Clock },
  EN_CURSO: { tono: "neutral", texto: "En curso", Icono: LoaderCircle, animado: true },
  COMPLETADO: { tono: "success", texto: "Completada", Icono: CheckCircle2 },
  ERROR: { tono: "danger", texto: "Error", Icono: XCircle },
  NA: { tono: "na", texto: "No aplica", Icono: MinusCircle },
};

const ESTILOS: Record<Tono, CSSProperties> = {
  neutral: { background: "var(--badge-neutral-bg)", color: "var(--badge-neutral-fg)", borderColor: "var(--badge-neutral-border)" },
  success: { background: "var(--badge-success-bg)", color: "var(--badge-success-fg)", borderColor: "var(--badge-success-border)" },
  danger: { background: "var(--badge-danger-bg)", color: "var(--badge-pending-fg)", borderColor: "var(--badge-danger-border)" },
  na: { background: "var(--color-flit-bg)", color: "var(--nav-texto)", borderColor: "var(--nav-borde)", borderStyle: "dashed" },
};

const MOTIVOS_NA: Record<MotivoNa, string> = {
  SERVICIO_PUBLICO: "Servicio público",
  DEPARTAMENTO_NO_SOPORTADO: "Departamento no soportado",
  SIN_DOCUMENTO_LOCATARIO: "Sin documento del locatario",
};

const FECHA_DATO = new Intl.DateTimeFormat("es-CO", {
  timeZone: "America/Bogota",
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  hourCycle: "h23",
});

/** «dd/mm/aaaa hh:mm» en hora de Colombia. */
export function fechaDelDato(iso: string): string {
  const partes = Object.fromEntries(FECHA_DATO.formatToParts(new Date(iso)).map((p) => [p.type, p.value]));
  return `${partes.day}/${partes.month}/${partes.year} ${partes.hour}:${partes.minute}`;
}

function detalle(celda: CeldaGrilla): string | null {
  if (celda.estado === "NA") return celda.motivo_na ? MOTIVOS_NA[celda.motivo_na] : null;
  if (celda.estado === "ERROR") return celda.tipo_error === BLOQUEADA_POR_RUNT ? "Espera al RUNT del vehículo" : "No se pudo consultar";
  if (celda.estado === "COMPLETADO" && celda.reutilizada && celda.dato_obtenido_en) return `Dato del ${fechaDelDato(celda.dato_obtenido_en)}`;
  return null;
}

/** Celda reprocesable: en ERROR y sin depender de un RUNT en error (esa la libera el reproceso del RUNT). */
export const esReprocesable = (celda: CeldaGrilla) => celda.estado === "ERROR" && celda.tipo_error !== BLOQUEADA_POR_RUNT;

export interface CeldaSemaforoProps extends Pick<BotonReprocesarProps, "onReprocesada" | "onErrorReproceso"> {
  /** Sin celda: la fuente aún no llegó en la grilla. */
  celda?: CeldaGrilla;
  nombreFuente: string;
  identificador: string;
  puedeReprocesar: boolean;
}

/** Una celda vehículo × fuente del tablero (HU #13748). Sin ningún indicador de origen ni de dato manual (R-20). */
export function CeldaSemaforo({ celda, nombreFuente, identificador, puedeReprocesar, onReprocesada, onErrorReproceso }: CeldaSemaforoProps) {
  if (!celda)
    return (
      <td className="px-3 py-2.5 align-top text-xs text-[var(--nav-texto)] dark:text-slate-300">
        Sin dato
      </td>
    );

  const { tono, texto, Icono, animado } = ESTADOS[celda.estado];
  const nota = detalle(celda);
  return (
    <td data-celda={celda.id} data-estado={celda.estado} className="px-3 py-2.5 align-top">
      <div className="flex flex-col items-start gap-1">
        <span
          data-tono={tono}
          className={`inline-flex items-center gap-1 whitespace-nowrap rounded-full border px-2.5 py-1 text-xs font-semibold ${tono === "na" ? "dark:!bg-white/[0.04]" : ""}`}
          style={ESTILOS[tono]}
        >
          <Icono className={`h-3.5 w-3.5 ${animado ? "motion-safe:animate-spin" : ""}`} aria-hidden="true" />
          {texto}
        </span>
        {nota && <span className="text-xs text-[var(--nav-texto)] dark:text-slate-300">{nota}</span>}
        {puedeReprocesar && esReprocesable(celda) && (
          <BotonReprocesar
            celda={celda}
            etiqueta={`Reprocesar ${nombreFuente} de ${identificador}`}
            onReprocesada={onReprocesada}
            onErrorReproceso={onErrorReproceso}
          />
        )}
      </div>
    </td>
  );
}

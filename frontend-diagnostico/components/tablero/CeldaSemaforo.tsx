"use client";

import type { CSSProperties, ReactNode } from "react";
import { AlertCircle, CheckCircle2, Clock, LoaderCircle, MinusCircle, XCircle, type LucideIcon } from "lucide-react";
import type { Fuente } from "@/lib/configuracion";
import { BLOQUEADA_POR_RUNT, type CeldaGrilla, type EstadoCelda, type MotivoNa } from "@/lib/tablero/api";
import { FOCO_CLS } from "@/components/configuracion/estilos";
import { BotonReprocesar, type BotonReprocesarProps } from "./BotonReprocesar";

type Tono = "neutral" | "success" | "danger" | "warning" | "na";

/** Fuente que el lote no pidió (HU #14087): no hay consulta ni celda; solo se muestra en la tabla. */
export const SIN_SOLICITAR = "SIN_SOLICITAR";

/** Estado visible de una columna de la tabla: el de la celda o «Sin solicitar». */
export type EstadoVisible = EstadoCelda | typeof SIN_SOLICITAR;

/**
 * Semáforo por estado con los tokens de @flit/ui (tokens.css), todos AA en claro y oscuro (HU #14087): gris (neutral) «Sin
 * ejecutar» para pendiente o en curso (el en curso conserva el icono animado), verde «Ejecutado (OK)», rojo «Error», ámbar
 * (warning) «Novedad» para la N/A con su motivo, y gris claro con borde punteado «Sin solicitar» para la fuente que el lote no
 * pidió. El texto del rojo usa la tinta `--badge-pending-fg` (4,71:1 sobre el fondo rojo tintado; `--badge-danger-fg` da
 * 4,46:1). El estado nunca depende solo del color: cada chip lleva icono y texto.
 */
const ESTADOS: Record<EstadoVisible, { tono: Tono; texto: string; Icono: LucideIcon; animado?: boolean }> = {
  PENDIENTE: { tono: "neutral", texto: "Sin ejecutar", Icono: Clock },
  EN_CURSO: { tono: "neutral", texto: "Sin ejecutar", Icono: LoaderCircle, animado: true },
  COMPLETADO: { tono: "success", texto: "Ejecutado (OK)", Icono: CheckCircle2 },
  ERROR: { tono: "danger", texto: "Error", Icono: XCircle },
  NA: { tono: "warning", texto: "Novedad", Icono: AlertCircle },
  SIN_SOLICITAR: { tono: "na", texto: "Sin solicitar", Icono: MinusCircle },
};

const ESTILOS: Record<Tono, CSSProperties> = {
  neutral: { background: "var(--badge-neutral-bg)", color: "var(--badge-neutral-fg)", borderColor: "var(--badge-neutral-border)" },
  success: { background: "var(--badge-success-bg)", color: "var(--badge-success-fg)", borderColor: "var(--badge-success-border)" },
  danger: { background: "var(--badge-danger-bg)", color: "var(--badge-pending-fg)", borderColor: "var(--badge-danger-border)" },
  warning: { background: "var(--badge-warning-bg)", color: "var(--badge-warning-fg)", borderColor: "var(--badge-warning-border)" },
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

/** Nota corta bajo el chip de una celda en ERROR, por tipo_error; el resto dice «No se pudo consultar». */
const NOTAS_ERROR: Record<string, string> = {
  [BLOQUEADA_POR_RUNT]: "Espera al RUNT del vehículo",
  AUTENTICACION: "Credenciales del proveedor rechazadas",
  LIMITE_PROVEEDOR: "Límite del proveedor",
};

function detalle(celda: CeldaGrilla): string | null {
  if (celda.estado === "NA") return celda.motivo_na ? MOTIVOS_NA[celda.motivo_na] : null;
  if (celda.estado === "ERROR") return (celda.tipo_error && NOTAS_ERROR[celda.tipo_error]) || "No se pudo consultar";
  if (celda.estado === "COMPLETADO" && celda.reutilizada && celda.dato_obtenido_en) return `Dato del ${fechaDelDato(celda.dato_obtenido_en)}`;
  return null;
}

/** Celda reprocesable: en ERROR y sin depender de un RUNT en error (esa la libera el reproceso del RUNT). */
export const esReprocesable = (celda: CeldaGrilla) => celda.estado === "ERROR" && celda.tipo_error !== BLOQUEADA_POR_RUNT;

/** Texto visible del estado de una celda («Ejecutado (OK)», «Novedad», «Sin solicitar»…). */
export const textoEstadoCelda = (estado: EstadoVisible) => ESTADOS[estado].texto;

/** Motivo corto de una N/A («Servicio público»…), el que va bajo el chip «Novedad». */
export const motivoNaCorto = (motivo: MotivoNa | null) => (motivo ? MOTIVOS_NA[motivo] : null);

/**
 * Chip del semáforo (icono + texto + tono por token). Lo reutilizan la grilla, el detalle de la celda (HU #13801, su
 * auditoría desde HU #14087) y la grilla de backoffice (F06-H5); nunca depende solo del color.
 */
export function ChipEstadoCelda({ estado }: { estado: EstadoVisible }) {
  const { tono, texto, Icono, animado } = ESTADOS[estado];
  return (
    <span
      data-tono={tono}
      className={`inline-flex items-center gap-1 whitespace-nowrap rounded-full border px-2.5 py-1 text-xs font-semibold ${tono === "na" ? "dark:!bg-white/[0.04]" : ""}`}
      style={ESTILOS[tono]}
    >
      <Icono className={`h-3.5 w-3.5 ${animado ? "motion-safe:animate-spin" : ""}`} aria-hidden="true" />
      {texto}
    </span>
  );
}

export interface CeldaSemaforoProps extends Pick<BotonReprocesarProps, "onReprocesada" | "onErrorReproceso"> {
  /** Fuente de la columna (siempre, aunque no haya celda). */
  fuente: Fuente;
  /** Sin celda: la fuente aún no llegó en la grilla («Sin ejecutar») o el lote no la pidió («Sin solicitar»). */
  celda?: CeldaGrilla;
  /** false: el lote no pidió esta fuente (no está en `grilla.columnas`); la celda dice «Sin solicitar» y no es botón. */
  solicitada?: boolean;
  nombreFuente: string;
  identificador: string;
  puedeReprocesar: boolean;
  /**
   * Abre el detalle de la celda (HU #13801). Con él, el semáforo es un botón (clic, Enter o Espacio); se le pasa el botón
   * para devolverle el foco al cerrar el diálogo.
   */
  onAbrirDetalle?: (celda: CeldaGrilla, disparador: HTMLElement) => void;
  /** Marca bajo el semáforo. Solo la pasa la grilla del backoffice (origen manual, HU #13802); la compañía nunca (Q5). */
  marca?: ReactNode;
}

const TD_CLS = "px-3 py-2.5 align-top";

/**
 * Una celda vehículo × fuente del tablero (HU #13748; estados de HU #14087). En la vista de la compañía, sin ningún indicador
 * de origen ni de dato manual (R-20); solo el backoffice pasa `marca` (HU #13802). Sin consulta (no pedida o aún sin llegar)
 * el chip no es un botón: no hay detalle que abrir.
 */
export function CeldaSemaforo({
  fuente,
  celda,
  solicitada = true,
  nombreFuente,
  identificador,
  puedeReprocesar,
  onReprocesada,
  onErrorReproceso,
  onAbrirDetalle,
  marca,
}: CeldaSemaforoProps) {
  if (!solicitada || !celda)
    return (
      <td data-fuente={fuente} data-estado={solicitada ? "SIN_CELDA" : SIN_SOLICITAR} className={TD_CLS}>
        <ChipEstadoCelda estado={solicitada ? "PENDIENTE" : SIN_SOLICITAR} />
      </td>
    );

  const nota = detalle(celda);
  return (
    <td data-fuente={fuente} data-celda={celda.id} data-estado={celda.estado} className={TD_CLS}>
      <div className="flex flex-col items-start gap-1">
        {onAbrirDetalle ? (
          <button
            type="button"
            aria-haspopup="dialog"
            aria-label={`Ver detalle de ${nombreFuente} de ${identificador}: ${ESTADOS[celda.estado].texto}`}
            onClick={(e) => onAbrirDetalle(celda, e.currentTarget)}
            className={`cursor-pointer rounded-full transition hover:opacity-90 ${FOCO_CLS}`}
          >
            <ChipEstadoCelda estado={celda.estado} />
          </button>
        ) : (
          <ChipEstadoCelda estado={celda.estado} />
        )}
        {marca}
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

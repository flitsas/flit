"use client";

import type { ComponentType, ReactNode } from "react";
import { iconoComo } from "@flit/ui/iconoComo";
import { etiquetaKit } from "@flit/ui/StatusBadge";
import { TOOLTIP_SECUNDARIO_CLS, Tooltip } from "@flit/ui/Tooltip";
import { TABLA_MOVIL_CELL_CLS } from "@flit/ui/table-styles";
import type { Fuente } from "@/lib/configuracion";
import { BLOQUEADA_POR_RUNT, type CeldaGrilla, type EstadoCelda, type MotivoNa } from "@/lib/tablero/api";
import { FOCO_CLS } from "@/components/configuracion/estilos";
import { rotular } from "@/lib/rotulos";
import { BotonReprocesar, type BotonReprocesarProps } from "./BotonReprocesar";
import { IconoDxDeuda } from "@/components/dx/iconos/IconoDxDeuda";
import { IconoDxEjecutado } from "@/components/dx/iconos/IconoDxEjecutado";
import { IconoDxEnCurso } from "@/components/dx/iconos/IconoDxEnCurso";
import { IconoDxError } from "@/components/dx/iconos/IconoDxError";
import { IconoDxNovedad } from "@/components/dx/iconos/IconoDxNovedad";
import { IconoDxPendiente } from "@/components/dx/iconos/IconoDxPendiente";

/** HU #14562 AC11: tono del semáforo = sufijo del token --dx-estado-<tono> (app/dx/tokens-dx.css). */
type Tono = "ejecutado" | "deuda" | "error" | "en-curso" | "pendiente" | "novedad" | "sin-solicitar";

type Icono = ComponentType<{ className?: string; tamano?: 16 | 20 | 24 }>;

/** Fuente que el lote no pidió (HU #14087): no hay consulta ni celda; solo se muestra en la tabla. */
export const SIN_SOLICITAR = "SIN_SOLICITAR";

/** Estado visible de una columna de la tabla: el de la celda o «Sin solicitar». */
export type EstadoVisible = EstadoCelda | typeof SIN_SOLICITAR;

/** «Sin solicitar» no tiene glifo IconoDx: el «menos» lineal del kit, al mismo tamaño. */
const IconoSinSolicitar = iconoComo("menos");
function IconoMenos({ className }: { className?: string }) {
  return <IconoSinSolicitar className={className} aria-hidden="true" />;
}

/**
 * Semáforo del kit 2026 (HU #14562 AC11/AC12): etiqueta del kit con su IconoDx y el tono --dx-estado-*. Pendiente toma el
 * amarillo del kit (D4(4)); «Sin solicitar» es el estado visual NA con rótulo y motivo propios, distintos de «Novedad», sin
 * valor nuevo en EstadoCelda ni en MotivoNa (D4(3), O3). Ningún ícono gira dentro de la grilla (AC5). El estado nunca
 * depende solo del color: cada chip lleva ícono y texto.
 */
const ESTADOS: Record<EstadoVisible, { tono: Tono; texto: string; Icono: Icono }> = {
  PENDIENTE: { tono: "pendiente", texto: "Sin ejecutar", Icono: IconoDxPendiente },
  EN_CURSO: { tono: "en-curso", texto: "Sin ejecutar (en curso)", Icono: IconoDxEnCurso },
  COMPLETADO: { tono: "ejecutado", texto: "Ejecutado", Icono: IconoDxEjecutado },
  ERROR: { tono: "error", texto: "Error", Icono: IconoDxError },
  NA: { tono: "novedad", texto: "Novedad", Icono: IconoDxNovedad },
  SIN_SOLICITAR: { tono: "sin-solicitar", texto: "Sin solicitar", Icono: IconoMenos },
};

/** Motivo propio de «Sin solicitar» (AC12), distinto de los motivos de la Novedad. */
export const MOTIVO_SIN_SOLICITAR = "No se pidió en este lote";

const estiloDe = (tono: Tono) => ({ c: `var(--dx-estado-${tono}-borde)`, ct: `var(--dx-estado-${tono})` });

const MOTIVOS_NA: Record<MotivoNa, string> = {
  SERVICIO_PUBLICO: "Servicio público",
  DEPARTAMENTO_NO_SOPORTADO: "Departamento no soportado",
  // Contrato 1.6.0 §1.1 (decisión del orquestador): el código se conserva; el locatario sale solo del RUNT.
  SIN_DOCUMENTO_LOCATARIO: "El RUNT no reporta locatario",
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

/**
 * Línea secundaria del estado (HU #14584, spec-ui §6.2): va en el tooltip del chip y, por debajo de 900 px, en línea bajo él.
 * Ejecutado / Con deuda: la fecha del dato (o «Consultado en este lote»); Error: la nota; Novedad: el motivo; Sin ejecutar:
 * «La consulta aún no termina».
 */
function detalle(celda: CeldaGrilla): string | null {
  if (celda.estado === "NA") return celda.motivo_na || celda.motivo_na_etiqueta ? motivoNaDeCelda(celda) : null;
  if (celda.estado === "ERROR") return (celda.tipo_error && NOTAS_ERROR[celda.tipo_error]) || "No se pudo consultar";
  if (celda.estado === "COMPLETADO") return celda.dato_obtenido_en ? `Dato del ${fechaDelDato(celda.dato_obtenido_en)}` : "Consultado en este lote";
  if (celda.estado === "PENDIENTE" || celda.estado === "EN_CURSO") return LA_CONSULTA_NO_TERMINA;
  return null;
}

const LA_CONSULTA_NO_TERMINA = "La consulta aún no termina";

/** Contenido del tooltip: el estado y su línea secundaria (spec-ui §6.2). Sin PII: solo textos fijos y la fecha (SP-19). */
function ContenidoTooltip({ texto, nota }: { texto: string; nota: string | null }) {
  return (
    <>
      {texto}
      {nota && <span className={TOOLTIP_SECUNDARIO_CLS}>{nota}</span>}
    </>
  );
}

/** La línea secundaria en la celda solo por debajo de 900 px (tarjeta); arriba la lleva el tooltip, que ya la anuncia. */
function LineaMovil({ children }: { children: ReactNode }) {
  return (
    <span aria-hidden="true" className="text-xs text-muted-foreground min-[900px]:hidden">
      {children}
    </span>
  );
}

/** Chip sin enlace como disparador enfocable del tooltip (tradeoff aceptado en spec-ui §6.2: una parada de Tab más). */
function ChipEnfocable({ etiqueta, contenido, children }: { etiqueta: string; contenido: ReactNode; children: ReactNode }) {
  return (
    <Tooltip contenido={contenido}>
      <span tabIndex={0} aria-label={etiqueta} className={`inline-flex rounded-full ${FOCO_CLS}`}>
        {children}
      </span>
    </Tooltip>
  );
}

/** Celda reprocesable: en ERROR y sin depender de un RUNT en error (esa la libera el reproceso del RUNT). */
export const esReprocesable = (celda: CeldaGrilla) => celda.estado === "ERROR" && celda.tipo_error !== BLOQUEADA_POR_RUNT;

/** Texto visible del estado de una celda («Ejecutado», «Novedad», «Sin solicitar»…). */
export const textoEstadoCelda = (estado: EstadoVisible) => vistaDe(estado, false).texto;

/** Motivo corto de una N/A («Servicio público»…), el que va bajo el chip «Novedad». Desconocido: «Novedad sin clasificar». */
export const motivoNaCorto = (motivo: MotivoNa | null, etiqueta?: string | null) =>
  motivo || etiqueta ? rotular({ contexto: "motivo de N/A", codigo: motivo, etiqueta, respaldo: MOTIVOS_NA }) : null;

/** Nota de la N/A de una celda: `motivo_na_etiqueta` de la API primero y el motivo corto local de respaldo. */
const motivoNaDeCelda = (celda: CeldaGrilla) => motivoNaCorto(celda.motivo_na, celda.motivo_na_etiqueta);

/** Impuesto completado con deuda: «Con deuda» en el tono deuda, no verde. El resto de estados no cambia. */
function vistaDe(estado: EstadoVisible, conDeuda: boolean): { tono: Tono; texto: string; Icono: Icono } {
  if (conDeuda && estado === "COMPLETADO") return { tono: "deuda", texto: "Con deuda", Icono: IconoDxDeuda };
  // Punto 5: un estado que el frontend no conoce no rompe el chip ni pinta el código.
  return ESTADOS[estado] ?? { tono: "novedad", texto: rotular({ contexto: "estado de la celda", codigo: estado }), Icono: IconoDxNovedad };
}

/**
 * Chip del semáforo (icono + texto + tono por token). Lo reutilizan la grilla, el detalle de la celda (HU #13801, su
 * auditoría desde HU #14087) y la grilla de backoffice (F06-H5); nunca depende solo del color.
 */
export function ChipEstadoCelda({ estado, conDeuda = false }: { estado: EstadoVisible; conDeuda?: boolean }) {
  const { tono, texto, Icono } = vistaDe(estado, conDeuda);
  return (
    <span data-tono={tono} {...etiquetaKit(estiloDe(tono))}>
      <Icono className="h-3.5 w-3.5 shrink-0" tamano={16} />
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
   * Página del detalle de la celda (HU #13801; en pestaña nueva desde HU #14087). Con ella, el semáforo es un enlace con
   * target="_blank" y rel="noopener noreferrer" que avisa a los lectores de pantalla que abre una pestaña nueva.
   */
  hrefDetalle?: string;
  /** Marca bajo el semáforo. Solo la pasa la grilla del backoffice (origen manual, HU #13802); la compañía nunca (Q5). */
  marca?: ReactNode;
  /** Acción extra bajo el semáforo. Solo la pasa el backoffice («Llenar a mano», HU #13802). */
  accion?: ReactNode;
}

/** Celda de la grilla; por debajo de 900 px, renglón de la tarjeta del vehículo con el nombre de la fuente (HU #14562 AC56). */
const TD_CLS = `px-3 py-2.5 align-top ${TABLA_MOVIL_CELL_CLS}`;
/** Contenido de la celda: arriba a la izquierda en la tabla, a la derecha en la tarjeta. */
const CONTENIDO_CLS = "flex flex-col items-start gap-1 max-[900px]:items-end";

/** Aviso para lectores de pantalla del enlace al detalle (HU #14087): va en el nombre accesible y como texto sr-only. */
const NUEVA_PESTANA = "(se abre en una pestaña nueva)";

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
  hrefDetalle,
  marca,
  accion,
}: CeldaSemaforoProps) {
  if (!solicitada) {
    const texto = textoEstadoCelda(SIN_SOLICITAR);
    return (
      <td data-fuente={fuente} data-label={nombreFuente} data-estado="NA" data-motivo={SIN_SOLICITAR} className={TD_CLS}>
        <div className={CONTENIDO_CLS}>
          <ChipEnfocable etiqueta={`${nombreFuente} de ${identificador}: ${texto}`} contenido={<ContenidoTooltip texto={texto} nota={MOTIVO_SIN_SOLICITAR} />}>
            <ChipEstadoCelda estado={SIN_SOLICITAR} />
          </ChipEnfocable>
          <LineaMovil>{MOTIVO_SIN_SOLICITAR}</LineaMovil>
        </div>
      </td>
    );
  }
  if (!celda) {
    const texto = textoEstadoCelda("PENDIENTE");
    return (
      <td data-fuente={fuente} data-label={nombreFuente} data-estado="SIN_CELDA" className={TD_CLS}>
        <div className={CONTENIDO_CLS}>
          <ChipEnfocable etiqueta={`${nombreFuente} de ${identificador}: ${texto}`} contenido={<ContenidoTooltip texto={texto} nota={LA_CONSULTA_NO_TERMINA} />}>
            <ChipEstadoCelda estado="PENDIENTE" />
          </ChipEnfocable>
          <LineaMovil>{LA_CONSULTA_NO_TERMINA}</LineaMovil>
        </div>
      </td>
    );
  }

  const nota = detalle(celda);
  const conDeuda = fuente === "IMPUESTO_DEPARTAMENTAL" && celda.estado === "COMPLETADO" && celda.tiene_deuda === true;
  const texto = vistaDe(celda.estado, conDeuda).texto;
  return (
    <td data-fuente={fuente} data-label={nombreFuente} data-celda={celda.id} data-estado={celda.estado} className={TD_CLS}>
      <div className={CONTENIDO_CLS}>
        {hrefDetalle ? (
          // HU #14584 §6.2: el enlace del chip es el disparador del tooltip (ya es enfocable).
          <Tooltip contenido={<ContenidoTooltip texto={texto} nota={nota} />}>
            <a
              href={hrefDetalle}
              target="_blank"
              rel="noopener noreferrer"
              aria-label={`Ver detalle de ${nombreFuente} de ${identificador}: ${texto} ${NUEVA_PESTANA}`}
              className={`inline-flex cursor-pointer items-center gap-1 rounded-full transition duration-[var(--dx-motion-rapido)] hover:opacity-90 motion-reduce:transition-none ${FOCO_CLS}`}
            >
              <ChipEstadoCelda estado={celda.estado} conDeuda={conDeuda} />
              <span className="sr-only">{NUEVA_PESTANA}</span>
            </a>
          </Tooltip>
        ) : (
          <ChipEnfocable etiqueta={`${nombreFuente} de ${identificador}: ${texto}`} contenido={<ContenidoTooltip texto={texto} nota={nota} />}>
            <ChipEstadoCelda estado={celda.estado} conDeuda={conDeuda} />
          </ChipEnfocable>
        )}
        {marca}
        {nota && <LineaMovil>{nota}</LineaMovil>}
        {accion}
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

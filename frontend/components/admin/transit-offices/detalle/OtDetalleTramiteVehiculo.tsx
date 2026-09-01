"use client";

import { StatusBadge } from "@/components/atom/StatusBadge";
import {
  CampoValor,
  ListaCampos,
  SeccionVacia,
  TarjetaDetalle,
} from "@/components/operacion/detalle/primitivos";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import { plateFlowChipStyle, plateFlowLabel } from "@/lib/tramites/estados";
import type { TransformacionTipo } from "@/lib/tramites/transformaciones-vehiculo";
import {
  OtDetalleTransformaciones,
  transformacionesDelTramite,
} from "./OtDetalleTransformaciones";
import { formatOtDate, formatOtProcedureStatus, procedureStatusTone } from "../ot-utils";

/** Etiqueta legible del estado del SOAT en el RUNT. */
function soatEstadoLabel(value: string | null | undefined): string {
  if (!value) return "—";
  if (value === "vigente") return "Vigente";
  if (value === "vencido") return "Vencido";
  if (value === "unknown") return "Desconocido";
  return value;
}

/**
 * Especificaciones técnicas del vehículo, en el mismo orden que el detalle del gestor.
 *
 * Un atributo que el trámite transforma se OMITE aquí: su sitio es el bloque de transformaciones,
 * donde se ve junto al valor del RUNT. Mostrarlo además como valor suelto lo haría pasar por el
 * dato oficial del vehículo, que es justo la confusión que se está corrigiendo (HU #11931).
 */
function especificaciones(
  procedure: OtClientProcedure,
  transformados: Set<TransformacionTipo>,
): { campo: string; valor: string }[] {
  const cilindraje = procedure.cilindraje?.trim() ?? "";

  return [
    { campo: "Clase", valor: procedure.clase },
    { campo: "Servicio", valor: procedure.servicio },
    { campo: "Color", valor: transformados.has("color") ? "" : procedure.color },
    { campo: "Combustible", valor: transformados.has("combustible") ? "" : procedure.combustible },
    { campo: "Carrocería", valor: transformados.has("carroceria") ? "" : procedure.carroceria },
    {
      campo: "Cilindraje",
      valor: cilindraje && !cilindraje.includes("cc") ? `${cilindraje} cc` : cilindraje,
    },
    { campo: "Capacidad", valor: procedure.capacidad },
    { campo: "Ejes", valor: procedure.ejes },
    { campo: "Estado", valor: procedure.estadoVehiculo },
    { campo: "N. Motor", valor: procedure.numeroMotor },
    { campo: "N. Chasis", valor: procedure.numeroChasis },
    { campo: "N. Serie", valor: procedure.numeroSerie },
    // Una clave que el trámite no capturó se OMITE; nunca se rellena con el valor de otra.
  ]
    .map((s) => ({ campo: s.campo, valor: s.valor?.trim() ?? "" }))
    .filter((s) => s.valor !== "");
}

/** Pendientes que el OT debe tener a la vista antes de decidir (Bug #11585: solo si hay alguno). */
function pendientes(procedure: OtClientProcedure, totalDocumentos: number): string[] {
  const items: string[] = [];

  if (procedure.plateFlowStatus === "preasignado") {
    items.push("Pendiente asignar placa por el OT.");
  }
  if (procedure.plateFlowStatus === "asignado") {
    items.push("Pendiente proceso del gestor (Asignado → Terminado) antes de decidir.");
  }
  if (procedure.soatEstado && procedure.soatEstado !== "vigente") {
    items.push(`SOAT RUNT no vigente (${soatEstadoLabel(procedure.soatEstado)}).`);
  }
  if (totalDocumentos === 0) {
    items.push("El expediente aún no tiene documentos.");
  }

  return items;
}

/**
 * Sección «Trámite y vehículo» del modal de detalle del OT: la ficha del radicado y las
 * especificaciones técnicas del vehículo, más los pendientes que condicionan la decisión.
 *
 * Todo sale del detalle que ya trae el modal: la sección no pide red por su cuenta.
 */
export function OtDetalleTramiteVehiculo({
  procedure,
  totalDocumentos,
}: {
  procedure: OtClientProcedure;
  /** Documentos del expediente ya cargados por el modal; alimenta el pendiente «sin documentos». */
  totalDocumentos: number;
}) {
  const transformados = new Set(transformacionesDelTramite(procedure).map((t) => t.tipo));
  const specs = especificaciones(procedure, transformados);
  const avisos = pendientes(procedure, totalDocumentos);
  const plateChip = plateFlowChipStyle(procedure.plateFlowStatus);

  return (
    <div className="grid gap-4 md:grid-cols-2">
      <TarjetaDetalle titulo="Datos del trámite">
        <ListaCampos>
          <CampoValor campo="Radicado" valor={procedure.referenceNumber} />
          <CampoValor campo="Tipo" valor={procedure.procedureTypeName ?? procedure.procedureTypeId} />
          <CampoValor campo="Empresa" valor={procedure.clientTenantName ?? procedure.clientTenantId} />
          <CampoValor
            campo="Estado"
            valor={
              <span className="flex flex-wrap items-center gap-1.5">
                <StatusBadge
                  label={formatOtProcedureStatus(procedure.status)}
                  tone={procedureStatusTone(procedure.status)}
                />
                {plateChip ? (
                  <span
                    className="rounded-full px-2 py-0.5 text-[10px] font-semibold"
                    style={{
                      background: plateChip.bg,
                      color: plateChip.color,
                      border: `1px solid ${plateChip.border}`,
                    }}
                  >
                    {plateFlowLabel(procedure.plateFlowStatus)}
                  </span>
                ) : null}
                {procedure.prioritario ? (
                  <StatusBadge label="Prioritario" tone="warning" />
                ) : null}
              </span>
            }
          />
          <CampoValor campo="Radicación" valor={formatOtDate(procedure.createdAt)} />
          <CampoValor
            campo="Entrega"
            valor={procedure.submittedAt ? formatOtDate(procedure.submittedAt) : ""}
          />
          <CampoValor campo="SOAT RUNT" valor={soatEstadoLabel(procedure.soatEstado)} />
          <CampoValor
            campo="Pagos"
            valor={
              <span className="flex flex-wrap gap-1.5">
                <StatusBadge
                  label={`SOAT ${procedure.soatPagado ? "pagado" : "pendiente"}`}
                  tone={procedure.soatPagado ? "success" : "neutral"}
                />
                <StatusBadge
                  label={`Impuesto ${procedure.impuestoDepartamentalPagado ? "pagado" : "pendiente"}`}
                  tone={procedure.impuestoDepartamentalPagado ? "info" : "neutral"}
                />
              </span>
            }
          />
          <CampoValor campo="Dígito placa" valor={procedure.platePreferredLastDigit} />
        </ListaCampos>

        {avisos.length > 0 ? (
          <div className="mt-4 border-t pt-3 border-[#DFE5ED] dark:border-white/10">
            <h5 className="text-xs font-semibold text-[#162744] dark:text-white">
              Pendientes antes de decidir
            </h5>
            <ul className="mt-1.5 list-disc space-y-1 pl-4 text-xs text-[#162744]/80 dark:text-white/80">
              {avisos.map((aviso) => (
                <li key={aviso}>{aviso}</li>
              ))}
            </ul>
          </div>
        ) : null}
      </TarjetaDetalle>

      <TarjetaDetalle titulo="Especificaciones del vehículo">
        {specs.length === 0 && transformados.size === 0 ? (
          <SeccionVacia mensaje="Este trámite no tiene especificaciones técnicas del vehículo registradas todavía." />
        ) : (
          <>
            {specs.length > 0 ? (
              <ListaCampos>
                {specs.map((s) => (
                  <CampoValor key={s.campo} campo={s.campo} valor={s.valor} />
                ))}
              </ListaCampos>
            ) : null}
            <OtDetalleTransformaciones procedure={procedure} />
          </>
        )}
      </TarjetaDetalle>
    </div>
  );
}

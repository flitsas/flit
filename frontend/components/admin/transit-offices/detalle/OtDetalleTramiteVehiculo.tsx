"use client";

import type { ReactNode } from "react";
import { StatusBadge } from "@/components/atom/StatusBadge";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import { plateFlowChipStyle, plateFlowLabel } from "@/lib/tramites/estados";
import type { TransformacionTipo } from "@/lib/tramites/transformaciones-vehiculo";
import { OtFichaCampos, OtRejilla, OtVacio } from "./OtDetallePrimitivos";
import {
  OtDetalleTransformaciones,
  transformacionesDelTramite,
} from "./OtDetalleTransformaciones";
import { soatEstadoLabel } from "./ot-detalle-pendientes";
import { formatOtDate } from "../ot-utils";

/**
 * Catálogo cerrado de decisiones de prenda, el mismo que captura el gestor.
 *
 * Llega aquí desde la desaparecida sección «Datos comerciales»: el prototipo pone la prenda en la
 * ficha del vehículo —es un gravamen sobre el bien, no una condición de la compraventa— y ese es
 * también el único dato de aquella sección que el organismo necesita para decidir.
 */
const PRENDA_DECISION_LABELS: Record<string, string> = {
  solicitar: "Solicitar constitución de prenda",
  registrar: "Registrar prenda",
  levantar: "Levantar gravamen",
  omitir: "Continuar sin gestionar (riesgo asumido)",
  sin_prenda: "Sin prenda",
};

function prendaTexto(procedure: OtClientProcedure): string {
  const prenda = procedure.prenda;
  if (!prenda?.decision) return "";
  const decision = PRENDA_DECISION_LABELS[prenda.decision] ?? prenda.decision;
  // El acreedor solo se nombra si lo hay: en `sin_prenda` y `levantar` puede no existir.
  return [decision, prenda.acreedorNombre?.trim()].filter(Boolean).join(" · ");
}

/**
 * Campos del vehículo, en el orden del prototipo (VIN · Placa · Marca · Línea, luego Clase · Color…).
 *
 * Dos reglas gobiernan qué entra:
 *
 *  - **Lo que no tenemos, no se pone.** «Peso» sale en el prototipo pero no existe en
 *    `OtClientProcedure`, así que no se pinta ni con un guion: un hueco rotulado sugiere un dato
 *    que se perdió, y aquí simplemente no lo hay. Igual con cualquier campo vacío del trámite.
 *  - **Un atributo que el trámite transforma se OMITE.** Su sitio es el bloque de transformaciones,
 *    donde se ve junto al valor del RUNT. Repetirlo suelto lo haría pasar por el dato oficial del
 *    vehículo, que es justo la confusión que se corrigió en la HU #11931.
 */
function camposVehiculo(
  procedure: OtClientProcedure,
  transformados: Set<TransformacionTipo>,
): { campo: string; valor: string }[] {
  const cilindraje = procedure.cilindraje?.trim() ?? "";

  return [
    { campo: "VIN", valor: procedure.vin },
    { campo: "Placa", valor: procedure.placa },
    { campo: "Marca", valor: procedure.marca },
    { campo: "Línea", valor: procedure.linea },
    { campo: "Clase", valor: procedure.clase },
    { campo: "Color", valor: transformados.has("color") ? "" : procedure.color },
    { campo: "Prenda", valor: prendaTexto(procedure) },
    { campo: "Servicio", valor: procedure.servicio },
    { campo: "Modelo", valor: procedure.modelo },
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
  ]
    .map((s) => ({ campo: s.campo, valor: s.valor?.trim() ?? "" }))
    .filter((s) => s.valor !== "");
}

/** Empresa responsable y persona con la que hablar, en la misma celda que el prototipo. */
function empresaGestor(procedure: OtClientProcedure): string {
  return [procedure.clientTenantName ?? procedure.clientTenantId, procedure.gestorNombre?.trim()]
    .filter(Boolean)
    .join(" · ");
}

/** Sellos que acompañan al estado: sub-estado de placa, prioridad y pagos declarados. */
function EstadoDelTramite({ procedure }: { procedure: OtClientProcedure }): ReactNode {
  const plateChip = plateFlowChipStyle(procedure.plateFlowStatus);

  return (
    <span className="flex flex-wrap items-center gap-1.5">
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
      {procedure.prioritario ? <StatusBadge label="Prioritario" tone="warning" /> : null}
      <StatusBadge
        label={`SOAT ${procedure.soatPagado ? "pagado" : "pendiente"}`}
        tone={procedure.soatPagado ? "success" : "neutral"}
      />
      <StatusBadge
        label={`Impuesto ${procedure.impuestoDepartamentalPagado ? "pagado" : "pendiente"}`}
        tone={procedure.impuestoDepartamentalPagado ? "info" : "neutral"}
      />
    </span>
  );
}

/**
 * Acordeón «Detalles del trámite y vehículo» (HU #12061).
 *
 * Rejillas del prototipo en vez de las dos tarjetas de campo/valor: la ficha del radicado, la del
 * estado y las características del vehículo. El bloque de transformaciones RUNT-vs-nuevo (HU
 * #11931) cierra la sección, porque es lo único de aquí que no es un dato sino una comparación.
 *
 * Todo sale del detalle que ya trae el modal: la sección no pide red por su cuenta.
 */
export function OtDetalleTramiteVehiculo({
  procedure,
}: {
  procedure: OtClientProcedure;
}) {
  const transformaciones = transformacionesDelTramite(procedure);
  const transformados = new Set(transformaciones.map((t) => t.tipo));
  const campos = camposVehiculo(procedure, transformados);

  return (
    <div className="space-y-4">
      <OtRejilla
        etiqueta="Datos del trámite"
        columnas={[
          "Radicado",
          "Fecha radicación",
          "Empresa / Gestor",
          "Tipo trámite solicitado",
          "Transformaciones solicitadas",
        ]}
        filas={[
          [
            procedure.referenceNumber,
            formatOtDate(procedure.createdAt),
            empresaGestor(procedure),
            procedure.procedureTypeName ?? procedure.procedureTypeId,
            // «Cambio de color», no «Color» a secas: en esta celda el rótulo nombra la
            // transformación pedida, y usar el mismo literal que la especificación del vehículo
            // haría pasar una por la otra (HU #11931).
            transformaciones.length > 0
              ? transformaciones.map((t) => `Cambio de ${t.label.toLocaleLowerCase("es")}`).join(" · ")
              : "Ninguna",
          ],
        ]}
      />

      <OtRejilla
        etiqueta="Estado del trámite"
        columnas={["Estado", "SOAT RUNT", "Entrega al organismo", "Dígito de placa preferido"]}
        filas={[
          [
            <EstadoDelTramite key="estado" procedure={procedure} />,
            soatEstadoLabel(procedure.soatEstado),
            procedure.submittedAt ? formatOtDate(procedure.submittedAt) : "Sin entregar",
            procedure.platePreferredLastDigit?.trim() || "Sin preferencia",
          ],
        ]}
      />

      {campos.length === 0 ? (
        <OtVacio mensaje="Este trámite no tiene especificaciones técnicas del vehículo registradas todavía." />
      ) : (
        <OtFichaCampos etiqueta="Especificaciones del vehículo" campos={campos} />
      )}

      <OtDetalleTransformaciones procedure={procedure} />
    </div>
  );
}

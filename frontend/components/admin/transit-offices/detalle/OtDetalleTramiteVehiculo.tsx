"use client";

import type { ReactNode } from "react";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import type { TransformacionTipo } from "@/lib/tramites/transformaciones-vehiculo";
import { OtFichaCampos, OtRejilla, OtSello, OtVacio } from "./OtDetallePrimitivos";
import { OT_BLUE, OT_ORANGE } from "./ot-detalle-visual";
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
    // Primera banda del prototipo, tal cual.
    { campo: "VIN", valor: procedure.vin },
    { campo: "Placa", valor: procedure.placa },
    { campo: "Marca", valor: procedure.marca },
    { campo: "Línea", valor: procedure.linea },
    // Segunda banda: el prototipo pone «Peso», que no existe en el contrato del OT; su hueco lo
    // ocupa «Modelo», el dato del mismo orden de importancia que sí tenemos.
    { campo: "Clase", valor: procedure.clase },
    { campo: "Color", valor: transformados.has("color") ? "" : procedure.color },
    { campo: "Modelo", valor: procedure.modelo },
    { campo: "Prenda", valor: prendaTexto(procedure) },
    // De aquí en adelante, lo que el detalle anterior ya mostraba y el organismo usa para decidir.
    { campo: "Servicio", valor: procedure.servicio },
    { campo: "Combustible", valor: transformados.has("combustible") ? "" : procedure.combustible },
    { campo: "Carrocería", valor: transformados.has("carroceria") ? "" : procedure.carroceria },
    {
      campo: "Cilindraje",
      // Un eléctrico llega con cilindraje 0: eso no es un dato del vehículo, es un «no aplica»
      // que el RUNT guarda como cero. Pintarlo como «0 cc» solo ensucia la ficha.
      valor:
        !cilindraje || Number(cilindraje.replace(/\D/g, "")) === 0
          ? ""
          : cilindraje.includes("cc")
            ? cilindraje
            : `${cilindraje} cc`,
    },
    { campo: "Capacidad", valor: procedure.capacidad },
    { campo: "Ejes", valor: procedure.ejes },
    { campo: "Estado", valor: procedure.estadoVehiculo },
    { campo: "SOAT RUNT", valor: soatEstadoLabel(procedure.soatEstado) },
    { campo: "N. Motor", valor: procedure.numeroMotor },
    { campo: "N. Chasis", valor: procedure.numeroChasis },
    { campo: "N. Serie", valor: procedure.numeroSerie },
  ]
    .map((s) => ({ campo: s.campo, valor: s.valor?.trim() ?? "" }))
    // El guion no es un valor: un campo sin dato se va, no se pinta vacío.
    .filter((s) => s.valor !== "" && s.valor !== "—");
}

/** Empresa responsable y persona con la que hablar, en la misma celda que el prototipo. */
function empresaGestor(procedure: OtClientProcedure): string {
  return [procedure.clientTenantName ?? procedure.clientTenantId, procedure.gestorNombre?.trim()]
    .filter(Boolean)
    .join(" · ");
}

/**
 * Celda «Placa» de la ficha del vehículo.
 *
 * Es la única celda con acción, y el prototipo la pone justo aquí: un trámite puede llegar al
 * organismo SIN placa —radicado con dígito de preferencia— y asignarla es lo primero que hay que
 * hacer con él. Tenerlo en la misma celda que lo reporta ahorra ir a buscar el botón a otro sitio.
 *
 * No inventa flujo: pulsa el mismo manejador que la acción «Asignar placa» de la bandeja, que abre
 * el diálogo con las placas disponibles del rango de la compañía.
 */
function CeldaPlaca({
  procedure,
  onAssignPlate,
}: {
  procedure: OtClientProcedure;
  onAssignPlate?: (procedure: OtClientProcedure) => void;
}) {
  const placa = procedure.placa?.trim();
  if (placa) {
    return <span className="font-semibold tracking-wider">{placa}</span>;
  }

  const digito = procedure.platePreferredLastDigit?.trim();

  return (
    <span className="flex flex-col items-start gap-1">
      <span className="text-[10px] font-semibold uppercase tracking-wide" style={{ color: OT_ORANGE }}>
        Sin preasignar
      </span>
      {digito ? <OtSello texto={`Dígito preferido: ${digito}`} color={OT_BLUE} soft /> : null}
      {onAssignPlate ? (
        <button
          type="button"
          onClick={() => onAssignPlate(procedure)}
          className="rounded-lg border px-2 py-1 text-[11px] font-semibold transition hover:bg-[#557EFF]/10"
          style={{ borderColor: OT_BLUE, color: OT_BLUE }}
        >
          Asignar placa
        </button>
      ) : null}
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
  onAssignPlate,
}: {
  procedure: OtClientProcedure;
  /**
   * Asignar la placa desde la propia celda, como en el prototipo. Ausente cuando la sesión no
   * decide (SuperAdmin supervisando, Quipux de solo lectura): entonces la celda solo informa.
   */
  onAssignPlate?: (procedure: OtClientProcedure) => void;
}) {
  const transformaciones = transformacionesDelTramite(procedure);
  const transformados = new Set(transformaciones.map((t) => t.tipo));
  const celdaPlaca: { campo: string; valor: ReactNode } = {
    campo: "Placa",
    valor: <CeldaPlaca procedure={procedure} onAssignPlate={onAssignPlate} />,
  };

  // «Placa» NUNCA se filtra por estar vacía: sin placa es cuando más falta hace la celda, porque
  // es la que lleva la preasignación. Por eso se inserta aquí y no se deja pasar por el filtro.
  const campos: { campo: string; valor: ReactNode }[] = camposVehiculo(procedure, transformados)
    .filter((c) => c.campo !== "Placa")
    .map((c) => ({ campo: c.campo, valor: c.valor as ReactNode }));
  const camposConPlaca = [...campos.slice(0, 1), celdaPlaca, ...campos.slice(1)];

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

      {/* La ficha se pinta SIEMPRE, aunque el trámite no traiga ni una especificación: la celda de
          la placa es la que lleva la preasignación, y esconderla justo cuando no hay datos del
          vehículo dejaría sin salida al trámite que más la necesita. El aviso de vacío acompaña. */}
      {campos.length === 0 ? (
        <OtVacio mensaje="Este trámite no tiene especificaciones técnicas del vehículo registradas todavía." />
      ) : null}
      <OtFichaCampos etiqueta="Especificaciones del vehículo" campos={camposConPlaca} />

      <OtDetalleTransformaciones procedure={procedure} />
    </div>
  );
}

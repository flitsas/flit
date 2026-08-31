"use client";

import { StatusBadge, type StatusTone } from "@/components/atom/StatusBadge";
import {
  CampoValor,
  ListaCampos,
  SeccionVacia,
  TarjetaDetalle,
} from "@/components/operacion/detalle/primitivos";
import { formatCOP } from "@/lib/format/currency";
import type { OtClientProcedure, OtClientProcedureCommercial } from "@/lib/api/types-ot";

/** Catálogo cerrado de causales, el mismo que captura el gestor. */
const CAUSAL_LABELS: Record<string, string> = {
  COMPRAVENTA: "Compraventa",
  DONACION: "Donación",
  DACION_EN_PAGO: "Dación en pago",
  ADJUDICACION: "Adjudicación",
};

/** Catálogo cerrado de decisiones de prenda, el mismo que captura el gestor. */
const PRENDA_DECISION_LABELS: Record<string, string> = {
  solicitar: "Solicitar constitución de prenda",
  registrar: "Registrar prenda",
  levantar: "Levantar gravamen",
  omitir: "Continuar sin gestionar (riesgo asumido)",
  sin_prenda: "Sin prenda",
};

/**
 * `sin_prenda` y `levantar` dejan el vehículo sin gravamen; `solicitar` y `registrar` señalan uno
 * activo o en trámite; `omitir` es un riesgo que el gestor asumió y el OT debe ver como tal.
 */
const PRENDA_DECISION_TONES: Record<string, StatusTone> = {
  sin_prenda: "success",
  levantar: "success",
  registrar: "warning",
  solicitar: "warning",
  omitir: "danger",
};

function causalLabel(causal: string | null | undefined): string {
  if (!causal) return "";
  return CAUSAL_LABELS[causal] ?? causal;
}

/** Una tarifa se guarda como número; sin valor no se inventa un cero. */
function porcentaje(value: number | null | undefined): string {
  return value == null ? "" : `${value} %`;
}

function comercialVacio(comercial: OtClientProcedureCommercial | null | undefined): boolean {
  if (!comercial) return true;
  return (
    comercial.valorVenta == null &&
    !comercial.causal &&
    comercial.tasaImpuesto == null &&
    comercial.derechos == null &&
    !comercial.metodoPago
  );
}

/**
 * Sección «Datos comerciales» del modal de detalle del OT: valor de la operación y decisión de
 * prenda. Es de solo lectura, como todo el detalle — quien captura estos datos es el gestor.
 */
export function OtDetalleComercial({ procedure }: { procedure: OtClientProcedure }) {
  const comercial = procedure.comercial;
  const prenda = procedure.prenda;

  if (comercialVacio(comercial) && !prenda) {
    return (
      <SeccionVacia mensaje="Este trámite no tiene datos comerciales ni decisión de prenda registrados." />
    );
  }

  return (
    <div className="grid gap-4 md:grid-cols-2">
      <TarjetaDetalle titulo="Operación comercial">
        {comercialVacio(comercial) ? (
          <SeccionVacia mensaje="Este trámite no tiene datos comerciales registrados." />
        ) : (
          <ListaCampos>
            <CampoValor campo="Valor de venta" valor={formatCOP(comercial?.valorVenta)} />
            <CampoValor campo="Causal" valor={causalLabel(comercial?.causal)} />
            <CampoValor campo="Tasa de impuesto" valor={porcentaje(comercial?.tasaImpuesto)} />
            <CampoValor campo="Derechos" valor={formatCOP(comercial?.derechos)} />
            <CampoValor campo="Método de pago" valor={comercial?.metodoPago} />
          </ListaCampos>
        )}
      </TarjetaDetalle>

      <TarjetaDetalle
        titulo="Prenda"
        accion={
          prenda ? (
            <StatusBadge
              label={PRENDA_DECISION_LABELS[prenda.decision] ?? prenda.decision}
              tone={PRENDA_DECISION_TONES[prenda.decision] ?? "neutral"}
            />
          ) : null
        }
      >
        {prenda ? (
          <ListaCampos>
            <CampoValor campo="Estado" valor={prenda.estado} />
            <CampoValor campo="Acreedor" valor={prenda.acreedorNombre} />
            <CampoValor campo="Documento acreedor" valor={prenda.acreedorDocumento} />
            <CampoValor campo="Entidad de levantamiento" valor={prenda.levantamientoEntidad} />
          </ListaCampos>
        ) : (
          <SeccionVacia mensaje="Este trámite no tiene una decisión de prenda registrada." />
        )}
      </TarjetaDetalle>
    </div>
  );
}

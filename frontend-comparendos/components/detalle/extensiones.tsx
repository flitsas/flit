import type { ReactNode } from "react";
import { AccionesAviso } from "@/components/detalle/AccionesAviso";
import { AccionesCambio } from "@/components/detalle/AccionesCambio";
import { AccionesConductor } from "@/components/detalle/AccionesConductor";
import { AccionesDp } from "@/components/detalle/AccionesDp";
import { AccionesPago } from "@/components/detalle/AccionesPago";
import type { SesionComparendos } from "@/lib/sesion";
import type { DetalleComparendo } from "@/lib/tipos";

/** Lo que recibe cada extensión del detalle: el comparendo cargado, cómo recargarlo y la sesión. */
export interface ContextoDetalle {
  detalle: DetalleComparendo;
  recargar: () => void;
  sesion: SesionComparendos | null;
  /** El SuperAdmin solo lee: las extensiones de operación no deben mostrarse (Excel S2). */
  soloLectura: boolean;
}

/**
 * Puntos de extensión del detalle del comparendo. Cada Feature agrega aquí su parte sin tocar el modal:
 * - `acciones`: botones del encabezado (asignar contraventor, enviar aviso, generar DP, registrar pago…).
 * - `contraventor`: dentro del acordeón Contraventor (editar, consultar API, cargar documentos).
 * - `cambioContraventor`: bloque de cambio de contraventor (solicitar, registrar rechazo o aceptación).
 * - `dp`: acordeón Resolución OT / DP (tipologías procedentes, generar DP, registrar respuesta).
 * - `pago`: bloque Pago (registrar pago).
 */
export interface ExtensionesDetalle {
  acciones?: (ctx: ContextoDetalle) => ReactNode;
  contraventor?: (ctx: ContextoDetalle) => ReactNode;
  cambioContraventor?: (ctx: ContextoDetalle) => ReactNode;
  dp?: (ctx: ContextoDetalle) => ReactNode;
  pago?: (ctx: ContextoDetalle) => ReactNode;
}

/**
 * Extensiones registradas por defecto: conductor y cambio de contraventor (HU #13481), derecho de petición (HU #13491) y
 * pago (HU #13494).
 */
export const EXTENSIONES_DETALLE: ExtensionesDetalle = {
  acciones: (ctx) => <AccionesAviso ctx={ctx} />,
  contraventor: (ctx) => <AccionesConductor ctx={ctx} />,
  cambioContraventor: (ctx) => <AccionesCambio ctx={ctx} />,
  dp: (ctx) => <AccionesDp ctx={ctx} />,
  pago: (ctx) => <AccionesPago ctx={ctx} />,
};

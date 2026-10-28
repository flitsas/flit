import { expect } from "vitest";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import type { ActorEstado, Adjunto, DefinicionTipo, EstadoIdentidad, Solicitud } from "@/lib/api/cliente";
import { definicionDe, solicitudDe } from "./fixtures-wizard";
import { DIAGNOSTICO_OK, nodo, wizardAbierto, type Manejador } from "./montar-wizard";
import { json } from "./utilidades";

// Arnés compartido de las pruebas de los pasos Resumen (F5) y Envío (F6): un trámite con todo lo pedido ya lleno, de modo que el
// wizard reabre directamente en el paso 4 (los tres primeros están completos y Resumen no se da por completo desde un borrador).

export const RUTA_DEFINICION = "GET /tipos-tramite/TIPO_A/definicion?organismoId=o-1";

const comun = { firma: false, personaNaturalPermitida: true };
const ROLES: DefinicionTipo["actores"] = [
  { rol: "vendedor", etiqueta: "Propietario (vendedor)", min: 1, max: 1, biometria: true, ...comun },
  { rol: "comprador", etiqueta: "Comprador", min: 1, max: 1, biometria: true, ...comun },
];
const DOCS: DefinicionTipo["documentos"] = [
  { docCode: "paz_salvo", nombre: "Paz y salvo de impuestos", obligatorio: true, orden: 1, instruccionCargue: "", condicion: "" },
  { docCode: "comprobante_pago", nombre: "Comprobante de pago", obligatorio: true, orden: 2, instruccionCargue: "", condicion: "" },
];
const CAMPOS: DefinicionTipo["pasos"][number]["campos"] = [
  { fieldKey: "cilindraje", etiqueta: "Cilindraje", tipo: "number", obligatorio: true, opciones: [], validationSchema: "" },
  { fieldKey: "color", etiqueta: "Color", tipo: "select", obligatorio: false, opciones: ["ROJO", "AZUL"], validationSchema: "" },
];

export function definicionCompleta(): DefinicionTipo {
  const d = definicionDe([["vehicle_query"], ["actor_form"], ["commercial", "prenda_decision", "document_checklist", "generic_form"]], {
    actores: ROLES,
    documentos: DOCS,
    flags: { entryMode: "PLATE", requiereComercial: true, requierePrenda: false, permiteTransformaciones: true, permitePrendaComplementaria: true, requiereBiometria: true, requiereSolicitudPlaca: false },
  });
  return { ...d, pasos: d.pasos.map((p) => (p.clave === "p3" ? { ...p, titulo: "Datos del vehículo", campos: CAMPOS } : p)) };
}

const actor = (rol: string, doc: string, nombre: string, extra: Partial<ActorEstado> = {}): ActorEstado => ({
  rol,
  ordinal: 1,
  validacionEstado: "aprobada",
  tipoDocumento: "CC",
  numeroDocumento: doc,
  nombre,
  correo: "ana.ruiz@correo.co",
  celular: "3001234567",
  direccion: null,
  ...extra,
});

export const adjunto = (id: string, tipo: string, nombre: string, extra: Partial<Adjunto> = {}): Adjunto => ({
  id,
  tipo,
  nombreArchivo: nombre,
  mimeType: "application/pdf",
  tamanoBytes: 2_621_440,
  sha256: "x",
  creadoEn: "2026-10-28T10:00:00Z",
  ...extra,
});

export const DOC_VENDEDOR = "1020456789";
export const DOC_COMPRADOR = "52987654";

/** Un borrador sin nada pendiente: el wizard reabre en el paso Resumen. */
export function solicitudCompleta(extra: Partial<Solicitud> = {}): Solicitud {
  return solicitudDe({
    version: 7,
    datos: {
      diagnostico: { ...DIAGNOSTICO_OK, fecha: "2026-10-28T20:23:00Z" },
      comercial: { valor: 45_000_000 },
      prenda: { tiene: true, acreedor: "BANCO DE PRUEBA", nitAcreedor: "900123456" },
      requisitos: { transformaciones: ["CAMBIO_COLOR"], observaciones: "Entrega la semana próxima" },
    },
    fieldValues: { cilindraje: "1600", color: "AZUL" },
    actores: [actor("vendedor", DOC_VENDEDOR, "ANA RUIZ"), actor("comprador", DOC_COMPRADOR, "LUIS PEREZ", { celular: "3109876543", correo: "luis.perez@mail.co", direccion: "Calle 1 # 2-3" })],
    adjuntos: [adjunto("a1", "paz_salvo", "paz-y-salvo.pdf"), adjunto("a2", "comprobante_pago", "soporte.pdf")],
    pendientes: [],
    completo: true,
    ...extra,
  });
}

const identidadAprobada = (): EstadoIdentidad => ({
  solicitudId: "s-1",
  completa: true,
  partes: ["vendedor", "comprador"].map((parte) => ({ parte, ordinal: 1, estado: "aprobada", puedeReenviar: false, reenviosRestantes: 5, esperaSegundos: 0 })),
});

export const resumenVisible = () => screen.getByRole("region", { name: "Resumen de tu trámite" });

/** Abre el wizard en el paso Resumen. `estado.actual` es el borrador que devuelve GET: las pruebas lo cambian como lo haría el back. */
export async function abrirResumen(rutas: Record<string, Manejador> = {}, inicial: Solicitud = solicitudCompleta()) {
  const estado = { actual: inicial };
  const m = await wizardAbierto({
    [RUTA_DEFINICION]: () => json(definicionCompleta()),
    "GET /solicitudes/s-1": () => json(estado.actual),
    "GET /solicitudes/s-1/identidad": () => json(identidadAprobada()),
    ...rutas,
  });
  await screen.findByRole("region", { name: "Resumen de tu trámite" });
  return { ...m, estado };
}

/** Confirma el resumen y pasa al paso Envío. */
export async function irAlEnvio() {
  fireEvent.click(screen.getByLabelText("Revisé la información y autorizo el envío"));
  fireEvent.click(screen.getByRole("button", { name: "Continuar" }));
  await waitFor(() => expect(nodo(5)).toHaveAttribute("aria-current", "step"));
  return nodo(5);
}

export const ACEPTADA = { solicitudId: "s-1", codigoPublico: "PN-2026-0042", estado: "en_cola" };
export const problema = (status: number, code: string, extra: Record<string, unknown> = {}, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify({ type: "about:blank", title: "t", status, code, ...extra }), { status, headers: { "content-type": "application/problem+json", ...headers } });

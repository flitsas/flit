// Datos ficticios del Diagnóstico Express (HU #13755): respuestas inventadas de POST /express y POST /express/estimado.
// La placa ABC123 y el documento 12345678 son de prueba; no corresponden a ningún vehículo ni persona.
import type { ExpressCreado } from "@/lib/express/api";

export { configuracion, simularApi, tiposOk, llamadas, cuerpoDe } from "@/components/parametrizacion/__tests__/datos";

export const ID_EXPRESS = "00000000-0000-7000-8000-00000000e001";
export const FECHA = new Date("2026-10-21T15:30:00Z");

export function expressCreado(cambios: Partial<ExpressCreado> = {}): ExpressCreado {
  return {
    id: ID_EXPRESS,
    nombre: "Express ABC123 21/10/2026",
    modo: "SIMPLE",
    es_express: true,
    estado: "CREADO",
    creado_en: "2026-10-21T15:30:00Z",
    vehiculo: {
      id: "00000000-0000-7000-8000-00000000f001",
      orden: 1,
      identificador: "ABC123",
      identificador_norm: "ABC123",
      tipo_identificador: "PLACA_CARRO",
      tipo_documento_locatario: null,
      documento_locatario: null,
    },
    ...cambios,
  };
}

export const creadoOk = () => Response.json(expressCreado(), { status: 201 });

// Detalles de celda ficticios (HU #13801): forma exacta de obtenerDetalleCelda (HU #13798) con placas TST### y documentos
// inventados, ya enmascarados como los entrega la API. Los reutiliza la grilla de backoffice (F06-H5).
import type { Fuente } from "@/lib/configuracion";
import type { Comparendo, DetalleCelda, PersonaDetalle } from "@/lib/tablero/detalle";

export const DATO_EN = "2026-10-22T15:00:00+00:00";

export function detalle(fuente: Fuente, cambios: Partial<DetalleCelda> = {}): DetalleCelda {
  return {
    id: `celda-1-${fuente.toLowerCase()}`,
    vehiculo_id: "vehiculo-1",
    fuente,
    estado: "COMPLETADO",
    motivo_na: null,
    tipo_error: null,
    dato_obtenido_en: DATO_EN,
    reutilizada: false,
    resultado: null,
    personas: null,
    ...cambios,
  };
}

export function comparendo(n: number, cambios: Partial<Comparendo> = {}): Comparendo {
  return {
    numero: `C-${String(n).padStart(4, "0")}`,
    fecha: "2026-01-02",
    valor: "500000",
    estado: "PENDIENTE",
    organismo: "ORGANISMO PRUEBA",
    ...cambios,
  };
}

export const simitVehiculo = (comparendos: Comparendo[] = [comparendo(1)], truncados = false, total = comparendos.length) =>
  detalle("SIMIT_VEHICULO", {
    resultado: { semaforo: comparendos.length ? "ROJO" : "VERDE", cantidad_comparendos: total, comparendos, comparendos_truncados: truncados },
  });

export const runtVehiculo = () =>
  detalle("RUNT", {
    resultado: {
      placa: "TST001",
      vin: "9ZZPRUEBA00000001",
      servicio: "PARTICULAR",
      clase: "AUTOMOVIL",
      organismo: "STRIA PRUEBA",
      propietarios: [{ tipo_documento: "C", tipo_propiedad: "PROPIA" }],
      locatarios: 0,
    },
  });

export const avaluoSinValor = () =>
  detalle("AVALUO", {
    resultado: {
      sin_valor_comercial: true,
      valor_sugerido: null,
      fuente_principal: null,
      fuentes: [{ fuente: "fasecolda", estado: "SIN_DATOS", valor: null, muestras: null, fecha: "2026-10-22" }],
    },
  });

export const avaluoConValor = () =>
  detalle("AVALUO", {
    resultado: {
      sin_valor_comercial: false,
      valor_sugerido: 45000000,
      fuente_principal: "fasecolda",
      fuentes: [
        { fuente: "fasecolda", estado: "OK", valor: 45000000, muestras: null, fecha: "2026-10-22" },
        { fuente: "mercado_libre", estado: "OK", valor: 47000000, muestras: 12, fecha: "2026-10-22" },
      ],
    },
  });

export function persona(orden: number, cambios: Partial<PersonaDetalle> = {}): PersonaDetalle {
  return {
    orden,
    rol: "PROPIETARIO",
    tipo_documento: "C",
    documento_enmascarado: `****${String(1000 + orden)}`,
    declarado_por_cliente: false,
    consulta: "RUNT_PERSONA",
    estado: "OK",
    tipo_error: null,
    consultado_en: DATO_EN,
    resultado: { inscrito: true, estado_conductor: "ACTIVO", licencias: [{ categoria: "B1", estado: "VIGENTE", vigente_hasta: "2030-01-01" }] },
    ...cambios,
  };
}

/** RUNT_LOCATARIO con el documento que declaró el cliente. */
export const runtLocatarioDeclarado = () =>
  detalle("RUNT_LOCATARIO", {
    personas: [persona(1, { rol: "LOCATARIO", documento_enmascarado: "****5678", declarado_por_cliente: true })],
  });

/** RUNT_PROPIETARIO con tres propietarios: dos naturales y uno con NIT consultado en el RUES. */
export const runtTresPropietarios = () =>
  detalle("RUNT_PROPIETARIO", {
    personas: [
      persona(1, { documento_enmascarado: "****1222" }),
      persona(2, { documento_enmascarado: "****3344", resultado: { inscrito: false, estado_conductor: null, licencias: [] } }),
      persona(3, {
        tipo_documento: "NIT",
        documento_enmascarado: "****4567",
        consulta: "RUES",
        resultado: { encontrada: true, estado_matricula: "ACTIVA", ultima_renovacion: "2026" },
      }),
    ],
  });

/** Celda ERROR PERSONAS_CON_ERROR: un natural respondió y el NIT falló por tiempo. */
export const runtPropietarioConError = () =>
  detalle("RUNT_PROPIETARIO", {
    estado: "ERROR",
    tipo_error: "PERSONAS_CON_ERROR",
    dato_obtenido_en: null,
    personas: [
      persona(1, { documento_enmascarado: "****1222" }),
      persona(2, { tipo_documento: "NIT", documento_enmascarado: "****4567", consulta: "RUES", estado: "ERROR", tipo_error: "TIMEOUT", consultado_en: null, resultado: null }),
    ],
  });

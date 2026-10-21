// Datos ficticios de las pruebas de la carga masiva (HU #13754): resúmenes de validación inventados (placas de prueba y
// documentos enmascarados), con el fetch del navegador simulado por URL como en la parametrización.
import type { FilaResumen, ResumenValidacion } from "@/lib/carga/api";

export { cuerpoDe, llamadas, sesion, simularApi } from "@/components/parametrizacion/__tests__/datos";

export const ID = "00000000-0000-7000-8000-00000000c001";

export function fila(cambios: Partial<FilaResumen> = {}): FilaResumen {
  return {
    fila: 2,
    identificador: "TST123",
    identificador_norm: "TST123",
    tipo_identificador: "PLACA_CARRO",
    tipo_documento_locatario: null,
    documento_locatario: null,
    valida: true,
    filas_duplicadas: [],
    errores: [],
    ...cambios,
  };
}

export const FILA_ERRONEA = fila({
  fila: 3,
  identificador: "ZZ-1",
  identificador_norm: "ZZ1",
  tipo_identificador: null,
  valida: false,
  errores: [{ campo: "Identificador_Vehiculo", code: "IDENTIFICADOR_INVALIDO", mensaje: "No es una placa ni un VIN válido." }],
});

export const FILA_CON_LOCATARIO = fila({ fila: 4, identificador: "PRB45A", identificador_norm: "PRB45A", tipo_identificador: "PLACA_MOTO", tipo_documento_locatario: "CC", documento_locatario: "****4050" });

export function resumen(cambios: Partial<ResumenValidacion> = {}): ResumenValidacion {
  return {
    total_filas: 4,
    validos: 2,
    erroneos: 1,
    duplicados: 1,
    puede_continuar: true,
    limite_vehiculos_lote: 500,
    supera_limite: false,
    avisos: [],
    filas: [fila({ filas_duplicadas: [5] }), FILA_ERRONEA, FILA_CON_LOCATARIO],
    ...cambios,
  };
}

export function archivo(nombre: string, tipo = "application/octet-stream"): File {
  return new File(["Identificador_Vehiculo\nTST123\n"], nombre, { type: tipo });
}

export const ruta = (sufijo: string) => `/diagnosticos/${ID}${sufijo}`;

/** Multipart enviado en una llamada al fetch simulado (la confirmación reenvía el archivo, HU #13754 «Reenviar el archivo»). */
export function formularioDe(llamada: unknown[]): FormData {
  const body = (llamada[1] as RequestInit | undefined)?.body;
  if (!(body instanceof FormData)) throw new Error("La llamada no envió multipart/form-data.");
  return body;
}

/** Correcciones del multipart de la confirmación ya parseadas, o undefined si el campo no viaja. */
export function correccionesDe(llamada: unknown[]): unknown {
  const texto = formularioDe(llamada).get("correcciones");
  return texto === null ? undefined : JSON.parse(String(texto));
}

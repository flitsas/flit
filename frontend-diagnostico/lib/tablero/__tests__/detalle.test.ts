import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api";
import {
  FRASE_DEUDA_DATO_PARCIAL,
  formatearPesos,
  fraseImpuesto,
  obtenerDetalleCelda,
  rutaDetalleCelda,
  textoAdvertenciaImpuesto,
  textoMotivoNa,
  textoTipoError,
  traducirErrorDetalle,
} from "@/lib/tablero/detalle";
import { simitVehiculo } from "./datosDetalle";

// Uso de ejemplo: const detalle = await obtenerDetalleCelda(loteId, vehiculoId, "SIMIT_VEHICULO", signal);
// si falla, traducirErrorDetalle(error) dice si no está disponible (404), si falta permiso (403) o el mensaje de la API.

afterEach(() => vi.unstubAllGlobals());

const espacios = (texto: string) => texto.replace(/\s/g, " ");

describe("detalle — URL por vehículo y fuente", () => {
  it("arma /diagnosticos/{id}/vehiculos/{vid}/consultas/{fuente}", () => {
    expect(rutaDetalleCelda("lote-1", "vehiculo-2", "SIMIT_VEHICULO")).toBe("/diagnosticos/lote-1/vehiculos/vehiculo-2/consultas/SIMIT_VEHICULO");
  });

  it("codifica los segmentos de la ruta", () => {
    expect(rutaDetalleCelda("a/b", "c d", "RUNT")).toBe("/diagnosticos/a%2Fb/vehiculos/c%20d/consultas/RUNT");
  });

  it("obtenerDetalleCelda hace GET por el BFF y devuelve el detalle tal cual", async () => {
    const cuerpo = simitVehiculo();
    const fetchMock = vi.fn(async () => Response.json(cuerpo));
    vi.stubGlobal("fetch", fetchMock);
    await expect(obtenerDetalleCelda("lote-1", "vehiculo-2", "SIMIT_VEHICULO")).resolves.toEqual(cuerpo);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("/api/v1/diagnostico/diagnosticos/lote-1/vehiculos/vehiculo-2/consultas/SIMIT_VEHICULO");
    expect(init.method).toBe("GET");
  });

  it("HU #14562 (seguridad M2, D19): la lectura del detalle no queda en la caché del navegador (cache: no-store)", async () => {
    const fetchMock = vi.fn(async () => Response.json(simitVehiculo()));
    vi.stubGlobal("fetch", fetchMock);
    await obtenerDetalleCelda("lote-1", "vehiculo-2", "SIMIT_VEHICULO");
    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(init.cache).toBe("no-store");
  });
});

describe("detalle — traduce 403 y 404", () => {
  it("404 NO_ENCONTRADO: el detalle no está disponible", () => {
    const e = traducirErrorDetalle(new ApiError(404, "No existe", "NO_ENCONTRADO"));
    expect(e.tipo).toBe("NO_DISPONIBLE");
    expect(e.titulo).toMatch(/no está disponible/i);
  });

  it("403: sin permiso, con o sin código", () => {
    expect(traducirErrorDetalle(new ApiError(403, "x")).tipo).toBe("SIN_PERMISO");
    const e = traducirErrorDetalle(new ApiError(403, "x", "SIN_EMPRESA"));
    expect(e.tipo).toBe("SIN_PERMISO");
    expect(e.titulo).toMatch(/permiso/i);
  });

  it("otro error: FALLO con el mensaje de la API", () => {
    const e = traducirErrorDetalle(new ApiError(500, "Diagnóstico no respondió. Intenta de nuevo en un momento."));
    expect(e).toEqual({ tipo: "FALLO", titulo: "No se pudo cargar el detalle", mensaje: "Diagnóstico no respondió. Intenta de nuevo en un momento." });
  });

  it("un error que no es de la API no rompe la traducción", () => {
    expect(traducirErrorDetalle("raro").tipo).toBe("FALLO");
  });
});

describe("detalle — textos de error, N/A y pesos", () => {
  it.each([
    ["TIMEOUT", /no respondió a tiempo/i],
    ["RED", /conexión/i],
    ["PERSONAS_CON_ERROR", /personas no se pudieron consultar/i],
    ["BLOQUEADA_POR_RUNT", /RUNT del vehículo/i],
    ["SIN_PLACA", /no tiene placa/i],
    ["SIN_IDENTIFICADOR", /ni placa ni VIN/i],
    ["NO_CONECTADO", /no está conectada/i],
    ["NO_CONFIRMADO", /no quedó confirmado/i],
    ["MODELO_NO_ACEPTADO", /modelo/i],
    ["DOCUMENTO_NO_COINCIDE", /no coincide/i],
    ["DOCUMENTO_NO_HOMOLOGADO", /no está homologado/i],
    ["VEHICULO_NO_ENCONTRADO", /no aparece/i],
    ["VEHICULO_NO_CONSULTABLE", /no se puede consultar/i],
    ["REQUIERE_ACTUALIZACION", /actualizar/i],
    ["SIN_DOCUMENTO", /documento del propietario/i],
    ["SIN_MODELO", /modelo del vehículo/i],
    ["ORGANISMO_INCOMPLETO", /municipio/i],
    ["SIN_NOMBRE_PROPIETARIO", /nombre del propietario/i],
  ])("%s se traduce", (codigo, texto) => {
    expect(textoTipoError(codigo)).toMatch(texto);
  });

  it.each([
    ["AUTENTICACION", "El proveedor rechazó las credenciales de FLIT. Avísale al administrador para que las renueve; luego puedes reprocesar la consulta."],
    ["LIMITE_PROVEEDOR", "El proveedor limitó las consultas por ahora. Se reintentará automáticamente."],
    ["FUENTE_INACTIVA", "La consulta del impuesto de este departamento no está disponible en este momento. Intenta de nuevo más tarde."],
  ])("%s (fallo del proveedor) tiene su texto exacto", (codigo, texto) => {
    expect(textoTipoError(codigo)).toBe(texto);
  });

  it("un código desconocido o nulo da un texto genérico, sin mostrar el código crudo", () => {
    expect(textoTipoError("ALGO_NUEVO")).toBe("La fuente respondió con un error.");
    expect(textoTipoError(null)).toBe("La fuente respondió con un error.");
  });

  it("motivos de N/A", () => {
    expect(textoMotivoNa("SERVICIO_PUBLICO")).toMatch(/servicio público/i);
    expect(textoMotivoNa("DEPARTAMENTO_NO_SOPORTADO")).toBe(
      "La liquidación del impuesto aún no está disponible para el departamento donde está matriculado el vehículo.",
    );
    expect(textoMotivoNa("SIN_DOCUMENTO_LOCATARIO")).toMatch(/locatario/i);
    expect(textoMotivoNa(null)).toBe("Esta consulta no aplica a este vehículo.");
  });

  it("pesos sin decimales a partir de texto o número; vacío como guion", () => {
    expect(espacios(formatearPesos("500000"))).toBe("$ 500.000");
    expect(espacios(formatearPesos(45000000))).toBe("$ 45.000.000");
    expect(formatearPesos(null)).toBe("—");
    expect(formatearPesos("no-numero")).toBe("—");
  });
});

// Uso de ejemplo (ola impuesto-fuente-real, diseno.md §5.2): fraseImpuesto(resultado) dice «Con deuda (dato parcial)…» si la
// fuente no leyó todas las vigencias; textoAdvertenciaImpuesto(advertencia) nombra el campo que no coincide, nunca su valor.
describe("detalle — impuesto contra la fuente real: dato parcial, advertencias y errores nuevos", () => {
  it("deuda con dato parcial: la frase lo dice aunque traiga monto", () => {
    expect(fraseImpuesto({ tiene_deuda: true, monto_total: 350000, dato_parcial: true, vigencias: [] })).toBe(FRASE_DEUDA_DATO_PARCIAL);
    expect(FRASE_DEUDA_DATO_PARCIAL).toBe("Con deuda (dato parcial): la fuente no leyó todas las vigencias.");
  });

  it("sin dato parcial, las frases de siempre", () => {
    expect(fraseImpuesto({ tiene_deuda: true, monto_total: null, vigencias: [] })).toBe("Deuda sin valor informado");
    expect(fraseImpuesto({ tiene_deuda: false, monto_total: 0, vigencias: [] })).toBe("Sin deuda");
    expect(fraseImpuesto({ tiene_deuda: true, monto_total: 350000, dato_parcial: false, vigencias: [] })).toBeNull();
  });

  it.each([
    ["vehicleType", "Dato que no coincide con el portal: Clase del vehículo"],
    ["secretaryCity", "Dato que no coincide con el portal: Ciudad del organismo"],
    ["vehicleServiceType", "Dato que no coincide con el portal: Servicio"],
    ["vehicleFuelType", "Dato que no coincide con el portal: Combustible"],
    ["vehicleBrand", "Dato que no coincide con el portal: Marca"],
    ["vehicleModel", "Dato que no coincide con el portal: Modelo"],
    ["OwnerName", "Dato que no coincide con el portal: Nombre del propietario"],
    ["campoNuevoDelProveedor", "Dato que no coincide con el portal: Otro dato"],
    ["otro", "Dato que no coincide con el portal: Otro dato"],
    ["", "Dato que no coincide con el portal: Otro dato"],
  ])("validación de %s: nombra el campo en español", (campo, texto) => {
    expect(textoAdvertenciaImpuesto({ tipo: "VALIDACION", campo, anio: null, texto: "valor enviado X / portal Y" })).toBe(texto);
  });

  it("aviso: dice la vigencia si la trae y nunca repite el texto del servidor", () => {
    expect(textoAdvertenciaImpuesto({ tipo: "AVISO", campo: "", anio: 2025, texto: "algo del portal" })).toBe(
      "La fuente del impuesto dejó un aviso sobre la vigencia 2025.",
    );
    expect(textoAdvertenciaImpuesto({ tipo: "AVISO", campo: "", anio: null, texto: "algo del portal" })).toBe("La fuente del impuesto dejó un aviso.");
  });

  it("tipo_error nuevos de la fuente real tienen su texto exacto", () => {
    expect(textoTipoError("CREDENCIAL_NO_CONFIGURADA")).toBe("Credencial de impuesto no configurada para la compañía.");
    expect(textoTipoError("NO_CONFIRMADO_TIEMPO")).toBe("No confirmado (tiempo): la fuente no terminó la consulta a tiempo.");
  });
});

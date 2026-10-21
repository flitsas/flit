// Datos ficticios del tablero del lote (HU #13748): grillas inventadas con placas de prueba TST### y el fetch del
// navegador simulado por ruta, con la consulta (?desde=) disponible para cada respuesta.
import { vi } from "vitest";
import type { Fuente } from "@/lib/configuracion";
import type { CeldaGrilla, EstadoCelda, FilaVehiculo, Grilla } from "@/lib/tablero/api";

export { sesion } from "@/__tests__/configuracion/datos";

export const ID = "00000000-0000-7000-8000-00000000e001";

export function celda(vehiculo: number, fuente: Fuente, estado: EstadoCelda, cambios: Partial<CeldaGrilla> = {}): CeldaGrilla {
  return {
    id: `celda-${vehiculo}-${fuente.toLowerCase()}`,
    fuente,
    estado,
    motivo_na: null,
    tipo_error: null,
    dato_obtenido_en: estado === "COMPLETADO" ? "2026-10-21T15:00:00Z" : null,
    reutilizada: false,
    version: 1,
    ...cambios,
  };
}

export function vehiculo(n: number, celdas: CeldaGrilla[], cambios: Partial<FilaVehiculo> = {}): FilaVehiculo {
  const placa = `TST${String(n).padStart(3, "0")}`;
  return {
    id: `vehiculo-${n}`,
    orden: n,
    identificador: placa,
    tipo_identificador: "PLACA_CARRO",
    placa,
    vin: null,
    terminado: celdas.every((c) => c.estado === "COMPLETADO" || c.estado === "NA"),
    celdas,
    ...cambios,
  };
}

/** TST001: terminado (RUNT reutilizado, N/A por servicio público y sin documento del locatario). */
export const V1 = vehiculo(1, [
  celda(1, "RUNT", "COMPLETADO", { reutilizada: true, dato_obtenido_en: "2026-10-21T14:05:00Z" }),
  celda(1, "IMPUESTO_DEPARTAMENTAL", "NA", { motivo_na: "SERVICIO_PUBLICO" }),
  celda(1, "SIMIT_PROPIETARIO", "COMPLETADO"),
  celda(1, "RUNT_LOCATARIO", "NA", { motivo_na: "SIN_DOCUMENTO_LOCATARIO" }),
]);

/** TST002: un error reprocesable en SIMIT propietario y RUNT locatario en curso. */
export const V2 = vehiculo(2, [
  celda(2, "RUNT", "COMPLETADO"),
  celda(2, "IMPUESTO_DEPARTAMENTAL", "NA", { motivo_na: "DEPARTAMENTO_NO_SOPORTADO" }),
  celda(2, "SIMIT_PROPIETARIO", "ERROR", { tipo_error: "TIMEOUT" }),
  celda(2, "RUNT_LOCATARIO", "EN_CURSO"),
]);

/** TST003: todo pendiente. */
export const V3 = vehiculo(3, [
  celda(3, "RUNT", "PENDIENTE"),
  celda(3, "IMPUESTO_DEPARTAMENTAL", "PENDIENTE"),
  celda(3, "SIMIT_PROPIETARIO", "PENDIENTE"),
  celda(3, "RUNT_LOCATARIO", "PENDIENTE"),
]);

export function grilla(cambios: Partial<Grilla> = {}): Grilla {
  const vehiculos = cambios.vehiculos ?? [V1, V2, V3];
  return {
    id: ID,
    nombre: "Flota norte",
    estado: "EN_PROCESO",
    version: 12,
    desde: 0,
    columnas: { por_vehiculo: ["RUNT", "IMPUESTO_DEPARTAMENTAL"], por_propietario: ["SIMIT_PROPIETARIO"], por_locatario: ["RUNT_LOCATARIO"] },
    avance: { vehiculos: vehiculos.length, terminados: vehiculos.filter((v) => v.terminado).length },
    vehiculos,
    ...cambios,
  };
}

/** Lote terminado con un error: RUNT de TST004 falló y bloquea su impuesto; SIMIT de TST005 falló por tiempo. */
export function loteTerminadoConErrores(cambios: Partial<Grilla> = {}): Grilla {
  const v4 = vehiculo(4, [
    celda(4, "RUNT", "ERROR", { tipo_error: "TIMEOUT", version: 20 }),
    celda(4, "IMPUESTO_DEPARTAMENTAL", "ERROR", { tipo_error: "BLOQUEADA_POR_RUNT", version: 20 }),
    celda(4, "SIMIT_PROPIETARIO", "ERROR", { tipo_error: "BLOQUEADA_POR_RUNT", version: 20 }),
    celda(4, "RUNT_LOCATARIO", "NA", { motivo_na: "SIN_DOCUMENTO_LOCATARIO", version: 20 }),
  ]);
  return grilla({ vehiculos: [V1, v4], version: 20, ...cambios });
}

type Manejador = (url: URL, init?: RequestInit) => Response | Promise<Response>;

/** Simula el BFF: cada clave es «MÉTODO ruta» bajo /api/v1/diagnostico, sin la consulta. Lo no previsto responde 404. */
export function simularApi(rutas: Record<string, Manejador>) {
  const fetchMock = vi.fn(async (entrada: string, init?: RequestInit) => {
    const url = new URL(entrada, "http://localhost");
    const clave = `${init?.method ?? "GET"} ${url.pathname.replace("/api/v1/diagnostico", "")}`;
    const manejador = rutas[clave];
    return manejador ? manejador(url, init) : Response.json({ code: "NO_ENCONTRADO", error: "No existe" }, { status: 404 });
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

/** Valores de ?desde= de cada GET de la grilla, en orden. */
export function desdesPedidos(fetchMock: { mock: { calls: unknown[][] } }): number[] {
  return fetchMock.mock.calls
    .map(([entrada, init]) => ({ url: new URL(String(entrada), "http://localhost"), metodo: (init as RequestInit | undefined)?.method ?? "GET" }))
    .filter(({ url, metodo }) => metodo === "GET" && url.pathname.endsWith("/grilla"))
    .map(({ url }) => Number(url.searchParams.get("desde")));
}

/** Llamadas al fetch simulado con un método y una ruta (sin consulta). */
export function llamadas(fetchMock: { mock: { calls: unknown[][] } }, metodo: string, ruta: string): unknown[][] {
  return fetchMock.mock.calls.filter(([entrada, init]) => {
    const url = new URL(String(entrada), "http://localhost");
    return url.pathname === `/api/v1/diagnostico${ruta}` && ((init as RequestInit | undefined)?.method ?? "GET") === metodo;
  });
}

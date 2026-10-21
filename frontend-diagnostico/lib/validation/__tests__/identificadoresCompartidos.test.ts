import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import {
  PLATE_PATTERN,
  VIN_PATTERN,
  sanitizePlate,
  sanitizeVin,
  validarIdentificador,
  validarLongitudIdentificador,
} from "../fieldRules";

// Uso de ejemplo:
//   validarIdentificador(" mc029554 ")        → { normalizado: "MC029554", tipo: "PLACA_MAQUINARIA" }
//   validarLongitudIdentificador("AB1234567") → { codigo: "EXCEDE_LONGITUD", clase: "PLACA", longitud: 9, maximo: 8, ... }
//
// HU #13745 (F02-H3): la tabla de casos es la MISMA que consume el validador .NET de core-diagnostico y los
// tests de Trámites (frontend/lib/validation). Se lee desde su ruta en el repo, sin copiarla. La copia de
// Diagnóstico replica la limpieza de Trámites (sanitizeVin / sanitizePlate) y añade, aparte, el rechazo por
// longitud de la carga masiva y el express (decisión del usuario 2026-10-21).

interface CasoIdentificador {
  entrada: string;
  normalizado: string;
  tipo: string;
  valido: boolean;
  nota?: string;
}

interface TablaCasos {
  version: number;
  tipos: string[];
  casos: CasoIdentificador[];
}

const RAIZ = path.resolve(__dirname, "../../../..");
const tabla = JSON.parse(
  readFileSync(path.join(RAIZ, "contracts/fixtures/validacion/identificadores-vehiculo.json"), "utf-8"),
) as TablaCasos;

/** Lo que las reglas de Diagnóstico dicen de una entrada, en el vocabulario de la tabla. */
function resultadoDeLaApp(entrada: string) {
  const r = validarIdentificador(entrada);
  return { normalizado: r.normalizado, tipo: r.tipo ?? "INVALIDO", valido: r.tipo !== null };
}

function divergencias(casos: CasoIdentificador[]): CasoIdentificador[] {
  return casos.filter((c) => {
    const app = resultadoDeLaApp(c.entrada);
    return app.normalizado !== c.normalizado || app.tipo !== c.tipo || app.valido !== c.valido;
  });
}

/** Literal de una regex exportada por el fieldRules.ts de Trámites, leído como texto (Diagnóstico no lo importa). */
function patronDeTramites(nombre: string): string {
  const fuente = readFileSync(path.join(RAIZ, "frontend/lib/validation/fieldRules.ts"), "utf-8");
  const m = fuente.match(new RegExp(`export const ${nombre} = /(.+)/;`));
  if (!m) throw new Error(`No se encontró ${nombre} en frontend/lib/validation/fieldRules.ts`);
  return m[1];
}

describe("tabla compartida identificadores-vehiculo.json", () => {
  it("es la tabla del repo, con filas y los seis tipos", () => {
    expect(tabla.version).toBe(1);
    expect(tabla.casos.length).toBeGreaterThan(0);
    for (const tipo of ["PLACA_CARRO", "PLACA_MOTO", "PLACA_REMOLQUE", "PLACA_MAQUINARIA", "VIN", "INVALIDO"]) {
      expect(tabla.casos.some((c) => c.tipo === tipo)).toBe(true);
    }
  });
});

describe("AC1 — Diagnóstico da en cada fila el mismo resultado que el validador .NET y Trámites", () => {
  it.each(tabla.casos.map((c) => [JSON.stringify(c.entrada), c] as const))("entrada %s", (_etiqueta, caso) => {
    expect(resultadoDeLaApp(caso.entrada)).toEqual({
      normalizado: caso.normalizado,
      tipo: caso.tipo,
      valido: caso.valido,
    });
  });

  it("los patrones son los mismos literales que en Trámites", () => {
    expect(VIN_PATTERN.source).toBe(patronDeTramites("VIN_PATTERN"));
    expect(PLATE_PATTERN.source).toBe(patronDeTramites("PLATE_PATTERN"));
  });

  it("sanitizeVin y sanitizePlate limpian como Trámites (mayúsculas, charset y tope)", () => {
    expect(sanitizeVin("io q1")).toBe("1");
    expect(sanitizeVin("1111111111111111122")).toBe("11111111111111111");
    expect(sanitizePlate(" abc-123 ")).toBe("ABC123");
    expect(sanitizePlate("ab1234567")).toBe("AB123456");
  });

  it("null y undefined son INVALIDO con normalizado vacío", () => {
    expect(validarIdentificador(null)).toEqual({ normalizado: "", tipo: null });
    expect(validarIdentificador(undefined)).toEqual({ normalizado: "", tipo: null });
  });
});

describe("AC2 — una divergencia entre la tabla y fieldRules rompe el CI", () => {
  it("la tabla del repo no tiene divergencias", () => {
    expect(divergencias(tabla.casos)).toEqual([]);
  });

  it("un caso nuevo que fieldRules no acepta se detecta como divergencia", () => {
    const casoNuevo: CasoIdentificador = { entrada: "ABCD12", normalizado: "ABCD12", tipo: "PLACA_CARRO", valido: true };
    expect(divergencias([...tabla.casos, casoNuevo])).toEqual([casoNuevo]);
  });
});

describe("Longitud en carga masiva y express — se rechaza, no se recorta en silencio", () => {
  it("AB1234567 (placa de 9) se rechaza con EXCEDE_LONGITUD aunque el corte a 8 daría una maquinaria", () => {
    const error = validarLongitudIdentificador("AB1234567");
    expect(error).toMatchObject({ codigo: "EXCEDE_LONGITUD", clase: "PLACA", longitud: 9, maximo: 8 });
    expect(error?.mensaje).toMatch(/placa/i);
    expect(error?.mensaje).toMatch(/8/);
  });

  it("un VIN de 18 caracteres se rechaza con EXCEDE_LONGITUD aunque el corte a 17 daría un VIN", () => {
    const error = validarLongitudIdentificador("9FBTEST12345678901");
    expect(error).toMatchObject({ codigo: "EXCEDE_LONGITUD", clase: "VIN", longitud: 18, maximo: 17 });
    expect(error?.mensaje).toMatch(/VIN/);
    expect(error?.mensaje).toMatch(/17/);
  });

  it("se mide el valor limpio: espacios y guiones no cuentan", () => {
    expect(validarLongitudIdentificador(" ab-123 ")).toBeNull();
    expect(validarLongitudIdentificador("MC.029.554")).toBeNull();
    expect(validarLongitudIdentificador("9fb-test-1234567890")).toBeNull();
  });

  it("los identificadores de la longitud justa pasan", () => {
    expect(validarLongitudIdentificador("MC029554")).toBeNull();
    expect(validarLongitudIdentificador("9FBTEST1234567890")).toBeNull();
  });

  it("una entrada que no es placa ni VIN no es error de longitud (ya es INVALIDO)", () => {
    expect(validarLongitudIdentificador("ABC123456")).toBeNull();
    expect(validarLongitudIdentificador("")).toBeNull();
  });

  it("validarIdentificador sigue idéntico a Trámites: AB1234567 da maquinaria truncada", () => {
    expect(validarIdentificador("AB1234567")).toEqual({ normalizado: "AB123456", tipo: "PLACA_MAQUINARIA" });
  });
});

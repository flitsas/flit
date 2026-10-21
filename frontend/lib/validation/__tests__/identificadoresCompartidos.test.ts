import { describe, expect, it } from "vitest";
import tablaCompartida from "../../../../contracts/fixtures/validacion/identificadores-vehiculo.json";
import { PLATE_PATTERN, VIN_PATTERN, sanitizePlate, sanitizeVin } from "../fieldRules";

// Uso de ejemplo: clasificar(" mc029554 ") → { normalizado: "MC029554", tipo: "PLACA_MAQUINARIA", valido: true }
//
// HU #13745 (F02-H3): la tabla de casos es la MISMA que consume el validador .NET de core-diagnostico
// (ValidadorIdentificadorTests). Se importa desde su ruta en el repo, sin copiarla. A cada fila se le aplica
// lo que ya exporta fieldRules.ts (sanitizeVin + VIN_PATTERN y, si no es VIN, sanitizePlate + las ramas de
// PLATE_PATTERN). Este test NO cambia fieldRules.ts: si una fila diverge, es un cambio de regla de Trámites
// y se reporta al Líder Técnico; no se ajusta ni el test ni la tabla.

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

const tabla: TablaCasos = tablaCompartida;

const TIPOS_DE_PLACA = ["PLACA_CARRO", "PLACA_MOTO", "PLACA_REMOLQUE", "PLACA_MAQUINARIA"] as const;

/** Ramas de PLATE_PATTERN tal como están en fieldRules.ts, en su orden: carro, moto, remolque, maquinaria. */
function ramasDePlatePattern(): RegExp[] {
  const fuente = PLATE_PATTERN.source;
  const interior = fuente.replace(/^\^\(/, "").replace(/\)\$$/, "");
  return interior.split("|").map((rama) => new RegExp(`^${rama}$`));
}

/** Lo que dice fieldRules.ts de una entrada, en el vocabulario de la tabla. */
function clasificar(entrada: string) {
  const vin = sanitizeVin(entrada);
  if (VIN_PATTERN.test(vin)) return { normalizado: vin, tipo: "VIN", valido: true };

  const placa = sanitizePlate(entrada);
  if (PLATE_PATTERN.test(placa)) {
    const indice = ramasDePlatePattern().findIndex((rama) => rama.test(placa));
    return { normalizado: placa, tipo: TIPOS_DE_PLACA[indice] ?? "SIN_RAMA", valido: true };
  }

  return { normalizado: entrada.toUpperCase().replace(/[^A-Z0-9]/g, ""), tipo: "INVALIDO", valido: false };
}

/** Filas en las que fieldRules.ts no da lo que la tabla espera. */
function divergencias(casos: CasoIdentificador[]): CasoIdentificador[] {
  return casos.filter((c) => {
    const r = clasificar(c.entrada);
    return r.normalizado !== c.normalizado || r.tipo !== c.tipo || r.valido !== c.valido;
  });
}

describe("tabla compartida identificadores-vehiculo.json", () => {
  it("está conectada: versión 1, filas y los seis tipos", () => {
    expect(tabla.version).toBe(1);
    expect(tabla.casos.length).toBeGreaterThan(0);
    for (const tipo of [...TIPOS_DE_PLACA, "VIN", "INVALIDO"]) {
      expect(tabla.casos.some((c) => c.tipo === tipo)).toBe(true);
    }
  });

  it("PLATE_PATTERN de fieldRules.ts tiene las cuatro ramas que la tabla supone", () => {
    expect(ramasDePlatePattern()).toHaveLength(TIPOS_DE_PLACA.length);
  });
});

describe("AC1 — cada fila da en fieldRules.ts el mismo resultado que en el validador .NET", () => {
  it.each(tabla.casos.map((c) => [JSON.stringify(c.entrada), c] as const))("entrada %s", (_etiqueta, caso) => {
    expect(clasificar(caso.entrada)).toEqual({
      normalizado: caso.normalizado,
      tipo: caso.tipo,
      valido: caso.valido,
    });
  });
});

describe("AC2 — un caso nuevo que fieldRules.ts no acepta rompe el test", () => {
  it("la tabla del repo no tiene divergencias", () => {
    expect(tabla.casos.length).toBeGreaterThan(0);
    expect(divergencias(tabla.casos)).toEqual([]);
  });

  it("un caso nuevo que fieldRules.ts no acepta se detecta como divergencia", () => {
    const casoNuevo: CasoIdentificador = {
      entrada: "ABCD12",
      normalizado: "ABCD12",
      tipo: "PLACA_CARRO",
      valido: true,
    };
    expect(tabla.casos.length).toBeGreaterThan(0);
    expect(divergencias([...tabla.casos, casoNuevo])).toEqual([casoNuevo]);
  });

  it("toda fila del repo con el resultado invertido se detecta como divergencia", () => {
    const invertidas = tabla.casos.map((c) => ({ ...c, valido: !c.valido }));
    expect(invertidas.length).toBeGreaterThan(0);
    expect(divergencias(invertidas)).toHaveLength(invertidas.length);
  });
});

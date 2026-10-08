import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  CAMPOS_PARAMETROS_MOTOR,
  getParametrosMotorLote,
  interpretarErrorParametrosMotor,
  putParametrosMotorLote,
  validarParametrosMotor,
  type ParametroMotorLoteLimite,
  type ParametrosMotorLote,
} from "../admin-plataforma-consolidado-lotes";
import { ApiError } from "../types";

// Uso de ejemplo (HU #13420):
//   const p = await getParametrosMotorLote();
//   const errores = validarParametrosMotor(valoresDelFormulario, p.limites);   // {} = válido
//   if (!Object.keys(errores).length) await putParametrosMotorLote({ ...valores, isActive, rowVersion: p.rowVersion });
//   catch (e) { const r = interpretarErrorParametrosMotor(e); r.tipo === "conflicto" → ofrecer recargar }

const RUTA = "/api/v1/admin/plataforma/consolidados/lotes/parametros";

const LIMITES: ParametroMotorLoteLimite[] = [
  { campo: "maxItemsPerBatch", minimo: 1, maximo: 32766, mayorQue: null },
  { campo: "maxPdfsPerPart", minimo: 1, maximo: 5000, mayorQue: null },
  { campo: "maxMbPerPart", minimo: 10, maximo: 2048, mayorQue: null },
  { campo: "itemSlots", minimo: 1, maximo: 6, mayorQue: null },
  { campo: "itemTimeoutSeconds", minimo: 1, maximo: null, mayorQue: null },
  { campo: "itemLeaseSeconds", minimo: null, maximo: null, mayorQue: "itemTimeoutSeconds" },
  { campo: "maxItemAttempts", minimo: 1, maximo: 10, mayorQue: null },
  { campo: "retryDelaySeconds", minimo: 5, maximo: null, mayorQue: null },
  { campo: "partTimeoutSeconds", minimo: 1, maximo: null, mayorQue: null },
  { campo: "partLeaseSeconds", minimo: null, maximo: null, mayorQue: "partTimeoutSeconds" },
  { campo: "maxPartAttempts", minimo: 1, maximo: 10, mayorQue: null },
  { campo: "retentionHours", minimo: 1, maximo: 168, mayorQue: null },
];

const PARAMETROS: ParametrosMotorLote = {
  maxItemsPerBatch: 10000,
  maxPdfsPerPart: 500,
  maxMbPerPart: 512,
  itemSlots: 2,
  itemTimeoutSeconds: 120,
  itemLeaseSeconds: 300,
  maxItemAttempts: 3,
  retryDelaySeconds: 30,
  partTimeoutSeconds: 600,
  partLeaseSeconds: 900,
  maxPartAttempts: 3,
  retentionHours: 72,
  isActive: true,
  updatedAt: "2026-10-07T15:30:00Z",
  updatedBy: "00000000-0000-0000-0000-0000000000aa",
  updatedByName: "Super Admin Demo",
  rowVersion: 7,
  limites: LIMITES,
};

const valoresDe = (p: ParametrosMotorLote) =>
  Object.fromEntries(CAMPOS_PARAMETROS_MOTOR.map((c) => [c, String(p[c])])) as Record<
    (typeof CAMPOS_PARAMETROS_MOTOR)[number],
    string
  >;

function respuesta(status: number, cuerpo?: unknown): Response {
  return new Response(cuerpo === undefined ? null : JSON.stringify(cuerpo), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

describe("admin-plataforma-consolidado-lotes — contrato GET/PUT", () => {
  const fetchMock = vi.fn();
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal("fetch", fetchMock);
  });
  afterEach(() => vi.unstubAllGlobals());

  it("AC1 — GET a la ruta del contrato devuelve los parámetros con autor, fecha, rowVersion y límites", async () => {
    fetchMock.mockResolvedValue(respuesta(200, PARAMETROS));
    const p = await getParametrosMotorLote();
    const url = String(fetchMock.mock.calls[0][0]);
    expect(new URL(url).pathname).toBe(RUTA);
    expect(fetchMock.mock.calls[0][1].method).toBe("GET");
    expect(p).toEqual(PARAMETROS);
  });

  it("AC2 — PUT envía los 12 campos + isActive + rowVersion y devuelve la fila releída", async () => {
    fetchMock.mockResolvedValue(respuesta(200, { ...PARAMETROS, maxItemsPerBatch: 5000, rowVersion: 8 }));
    const numeros = Object.fromEntries(CAMPOS_PARAMETROS_MOTOR.map((c) => [c, PARAMETROS[c]])) as Record<
      (typeof CAMPOS_PARAMETROS_MOTOR)[number],
      number
    >;
    const r = await putParametrosMotorLote({ ...numeros, maxItemsPerBatch: 5000, isActive: true, rowVersion: 7 });
    const [url, init] = fetchMock.mock.calls[0];
    expect(new URL(String(url)).pathname).toBe(RUTA);
    expect(init.method).toBe("PUT");
    const enviado = JSON.parse(init.body);
    expect(Object.keys(enviado).sort()).toEqual([...CAMPOS_PARAMETROS_MOTOR, "isActive", "rowVersion"].sort());
    expect(enviado.maxItemsPerBatch).toBe(5000);
    expect(enviado.rowVersion).toBe(7);
    expect(r.rowVersion).toBe(8);
  });
});

describe("validarParametrosMotor — rangos desde `limites` (AC3)", () => {
  it("valores vigentes: sin errores", () => {
    expect(validarParametrosMotor(valoresDe(PARAMETROS), LIMITES)).toEqual({});
  });

  it("fuera de rango por arriba y por abajo: error en ese campo con el rango del contrato", () => {
    const e = validarParametrosMotor(
      { ...valoresDe(PARAMETROS), maxItemsPerBatch: "40000", maxMbPerPart: "5" },
      LIMITES,
    );
    expect(e.maxItemsPerBatch).toMatch(/1.*32\.766/);
    expect(e.maxMbPerPart).toMatch(/10.*2\.048/);
    expect(Object.keys(e).sort()).toEqual(["maxItemsPerBatch", "maxMbPerPart"]);
  });

  it("solo mínimo (sin máximo): mensaje de mínimo", () => {
    const e = validarParametrosMotor({ ...valoresDe(PARAMETROS), retryDelaySeconds: "2" }, LIMITES);
    expect(e.retryDelaySeconds).toMatch(/al menos 5/i);
  });

  it("lease ≤ timeout: error en el lease que nombra el campo que debe superar", () => {
    const igual = validarParametrosMotor({ ...valoresDe(PARAMETROS), itemLeaseSeconds: "120" }, LIMITES);
    expect(igual.itemLeaseSeconds).toMatch(/mayor que/i);
    expect(igual.itemLeaseSeconds).toMatch(/tiempo máximo por trámite/i);
    const menor = validarParametrosMotor({ ...valoresDe(PARAMETROS), partLeaseSeconds: "10" }, LIMITES);
    expect(menor.partLeaseSeconds).toMatch(/tiempo máximo por parte/i);
  });

  it("vacío, decimal o texto: «número entero»", () => {
    const e = validarParametrosMotor(
      { ...valoresDe(PARAMETROS), itemSlots: "", maxItemAttempts: "2.5", retentionHours: "abc" },
      LIMITES,
    );
    expect(e.itemSlots).toMatch(/entero/i);
    expect(e.maxItemAttempts).toMatch(/entero/i);
    expect(e.retentionHours).toMatch(/entero/i);
  });

  it("los rangos salen de `limites`, no de literales: otro máximo cambia el resultado", () => {
    const limites = LIMITES.map((l) => (l.campo === "itemSlots" ? { ...l, maximo: 12 } : l));
    expect(validarParametrosMotor({ ...valoresDe(PARAMETROS), itemSlots: "10" }, limites)).toEqual({});
    expect(validarParametrosMotor({ ...valoresDe(PARAMETROS), itemSlots: "10" }, LIMITES).itemSlots).toBeTruthy();
  });
});

describe("validarParametrosMotor — enteros sin máximo (code review Obs2)", () => {
  // Uso de ejemplo: validarParametrosMotor({ ...valores, itemTimeoutSeconds: "3000000000" }, limites).itemTimeoutSeconds
  //   → «No puede superar 2.147.483.647.» (el servidor guarda `int`; `maximo: null` = máximo de int32).
  const SIN_MAXIMO = [
    "itemTimeoutSeconds",
    "itemLeaseSeconds",
    "retryDelaySeconds",
    "partTimeoutSeconds",
    "partLeaseSeconds",
  ] as const;

  it.each(SIN_MAXIMO)("%s = 3.000.000.000: error en ese campo con el máximo de int32", (campo) => {
    const e = validarParametrosMotor({ ...valoresDe(PARAMETROS), [campo]: "3000000000" }, LIMITES);
    expect(e[campo]).toBe("No puede superar 2.147.483.647.");
  });

  it("borde: 2.147.483.647 exacto es válido en un campo sin máximo", () => {
    const e = validarParametrosMotor(
      { ...valoresDe(PARAMETROS), retryDelaySeconds: "2147483647", partTimeoutSeconds: "2147483646", partLeaseSeconds: "2147483647" },
      LIMITES,
    );
    expect(e).toEqual({});
  });

  it("contrato: con mínimo y máximo declarados el mensaje sigue siendo el rango del contrato", () => {
    const e = validarParametrosMotor({ ...valoresDe(PARAMETROS), maxItemsPerBatch: "3000000000" }, LIMITES);
    expect(e.maxItemsPerBatch).toBe("Debe estar entre 1 y 32.766.");
  });
});

describe("interpretarErrorParametrosMotor — errores problem+json", () => {
  it("AC3 — 400 parametros_invalidos: errores por campo (camelCase o PascalCase) y generales aparte", () => {
    const err = new ApiError(400, "parametros_invalidos", {
      error: "parametros_invalidos",
      errors: { maxItemsPerBatch: ["Fuera de rango."], ItemLeaseSeconds: ["Debe superar el timeout."], body: ["El cuerpo es obligatorio."] },
    });
    const r = interpretarErrorParametrosMotor(err);
    expect(r.tipo).toBe("invalido");
    if (r.tipo !== "invalido") return;
    expect(r.errores).toEqual({ maxItemsPerBatch: "Fuera de rango.", itemLeaseSeconds: "Debe superar el timeout." });
    expect(r.general).toMatch(/cuerpo es obligatorio/);
  });

  it("AC4 — 409 row_version_conflict", () => {
    const r = interpretarErrorParametrosMotor(new ApiError(409, "row_version_conflict", { error: "row_version_conflict" }));
    expect(r.tipo).toBe("conflicto");
    expect(r.mensaje).toMatch(/otro super admin/i);
  });

  it("404 parametros_no_encontrados: mensaje claro", () => {
    const r = interpretarErrorParametrosMotor(new ApiError(404, "x", { error: "parametros_no_encontrados" }));
    expect(r.tipo).toBe("no_encontrado");
    expect(r.mensaje).toMatch(/no existen los parámetros/i);
  });

  it("AC5 — 403: sin permiso", () => {
    expect(interpretarErrorParametrosMotor(new ApiError(403, "Forbidden")).tipo).toBe("sin_permiso");
  });

  it("edge — red u otro error: genérico sin filtrar detalles técnicos", () => {
    const r = interpretarErrorParametrosMotor(new TypeError("Failed to fetch"));
    expect(r.tipo).toBe("otro");
    expect(r.mensaje).not.toMatch(/fetch|api\/v1/i);
  });
});

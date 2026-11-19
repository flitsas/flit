import { describe, expect, it } from "vitest";
import { ApiError } from "@/lib/api";
import { mensajeErrorFijo, TEXTO_SIN_PERMISO } from "@/lib/errores";
import { ERRORES_CARGA } from "@/lib/carga/api";
import { ERRORES_CONTRATO, ERRORES_OPERACION } from "@/lib/configuracion";
import { ERRORES_CABECERA } from "@/lib/diagnosticos/api";
import { ERRORES_EXPRESS } from "@/lib/express/api";
import { EmpresaNoElegida, mensajeErrorBackoffice } from "@/lib/backoffice/api";

// Uso de ejemplo: mensajeErrorFijo(error, "No se pudo guardar el contrato.", ERRORES_CONTRATO) → texto fijo del front.
// AP-9 (SF-6): la UI nunca muestra el `message` de un ApiError ni de un Error; se traduce por `status`/`code`.

const SECRETO = "mensaje del servidor que no debe verse";
const RESPALDO = "No se pudo completar la acción.";

describe("mensajeErrorFijo — AP-9: traductor común del front", () => {
  it("ApiError con un code del catálogo → el texto fijo de ese code", () => {
    expect(mensajeErrorFijo(new ApiError(422, SECRETO, "SIN_VALIDOS"), RESPALDO, { SIN_VALIDOS: "Texto fijo." })).toBe("Texto fijo.");
  });

  it("el code del catálogo gana al 403 genérico (CAMPO_NO_EDITABLE llega como 403)", () => {
    expect(mensajeErrorFijo(new ApiError(403, SECRETO, "CAMPO_NO_EDITABLE"), RESPALDO, { CAMPO_NO_EDITABLE: "Campo fijo." })).toBe("Campo fijo.");
  });

  it("ApiError 403 sin code del catálogo → texto fijo de permiso", () => {
    expect(mensajeErrorFijo(new ApiError(403, SECRETO), RESPALDO)).toBe(TEXTO_SIN_PERMISO);
    expect(TEXTO_SIN_PERMISO).toBe("No tienes permiso para esta acción.");
  });

  it.each([
    ["ApiError 500", new ApiError(500, SECRETO, "ERROR_INTERNO")],
    ["ApiError 503 sin code", new ApiError(503, SECRETO)],
    ["ApiError 422 con code fuera del catálogo", new ApiError(422, SECRETO, "OTRO", "campo")],
    ["Error", new Error(SECRETO)],
    ["TypeError de red", new TypeError(SECRETO)],
    ["un valor que no es Error", SECRETO],
    ["undefined", undefined],
  ] as const)("%s → el respaldo fijo del sitio", (_n, error) => {
    expect(mensajeErrorFijo(error, RESPALDO)).toBe(RESPALDO);
  });

  it("un code heredado de Object.prototype no se toma como del catálogo", () => {
    expect(mensajeErrorFijo(new ApiError(422, SECRETO, "constructor"), RESPALDO, {})).toBe(RESPALDO);
  });

  it("el backoffice sigue igual: mensajeErrorBackoffice delega y conserva EmpresaNoElegida", () => {
    expect(mensajeErrorBackoffice(new ApiError(422, SECRETO, "X"), RESPALDO, { X: "Fijo." })).toBe("Fijo.");
    expect(mensajeErrorBackoffice(new ApiError(500, SECRETO), RESPALDO)).toBe(RESPALDO);
    expect(mensajeErrorBackoffice(new EmpresaNoElegida(), RESPALDO)).toBe("Elige una compañía antes de consultar el backoffice.");
  });
});

// Los `code` que contracts/openapi/diagnostico.v1.yaml define para cada ruta (y que core-diagnostico emite) tienen texto fijo.
describe("catálogos por ruta — AP-9: cada code del contrato tiene texto fijo", () => {
  const casos: [string, Readonly<Record<string, string>>, string[]][] = [
    [
      "carga (POST …/carga, …/carga/validar-filas, …/vehiculos)",
      ERRORES_CARGA,
      [
        "NO_ENCONTRADO",
        "DIAGNOSTICO_NO_EDITABLE",
        "ARCHIVO_DEMASIADO_GRANDE",
        "FORMATO_NO_ADMITIDO",
        "ARCHIVO_ILEGIBLE",
        "FORMATO_XLS_NO_ADMITIDO",
        "ARCHIVO_REQUERIDO",
        "COLUMNA_REQUERIDA",
        "SIN_VALIDOS",
        "LIMITE_SUPERADO",
        "CORRECCIONES_INVALIDAS",
        "VEHICULOS_YA_CONFIRMADOS",
      ],
    ],
    [
      "cabecera (POST /diagnosticos, PUT /diagnosticos/{id})",
      ERRORES_CABECERA,
      [
        "NOMBRE_INVALIDO",
        "FUENTE_EN_GRUPO_AJENO",
        "ALCANCE_SIN_RUNT",
        "FUENTE_NO_HABILITADA",
        "FUENTE_NO_DISPONIBLE",
        "TIPO_TRAMITE_REQUERIDO",
        "TIPO_TRAMITE_NO_DISPONIBLE",
        "FUENTES_OBLIGATORIAS_FALTANTES",
        "VALOR_FUERA_DE_CONJUNTO",
        "NO_ENCONTRADO",
        "DIAGNOSTICO_NO_EDITABLE",
        "CATALOGO_NO_DISPONIBLE",
      ],
    ],
    [
      "express (POST /express, /express/estimado)",
      ERRORES_EXPRESS,
      [
        "IDENTIFICADOR_REQUERIDO",
        "IDENTIFICADOR_INVALIDO",
        "IDENTIFICADOR_DEMASIADO_LARGO",
        "TIPO_DOCUMENTO_INVALIDO",
        "DOCUMENTO_INVALIDO",
        "DOCUMENTO_INCOMPLETO",
        "NOMBRE_INVALIDO",
        "FUENTE_NO_HABILITADA",
        "VALOR_FUERA_DE_CONJUNTO",
        "CATALOGO_NO_DISPONIBLE",
      ],
    ],
    [
      "contrato (GET/PUT /backoffice/empresas/{empresa}/configuracion/contrato)",
      ERRORES_CONTRATO,
      ["EMPRESA_REQUERIDA", "CUERPO_INVALIDO", "CAMPO_NO_EDITABLE", "VALOR_FUERA_DE_CONJUNTO", "LIMITE_INVALIDO", "FUENTE_NO_DISPONIBLE", "VALOR_INVALIDO"],
    ],
    [
      "operación (PUT /configuracion)",
      ERRORES_OPERACION,
      ["CUERPO_INVALIDO", "CAMPO_NO_EDITABLE", "VALOR_FUERA_DE_CONJUNTO", "POR_DEFECTO_FUERA_DE_HABILITADAS", "VALOR_INVALIDO"],
    ],
  ];

  it.each(casos)("%s", (_n, catalogo, codigos) => {
    for (const code of codigos) {
      const texto = catalogo[code];
      expect(texto, code).toMatch(/^[A-ZÁÉÍÓÚÑ¿].+\.$/);
      expect(texto, code).not.toMatch(/\b[A-Z]+_[A-Z_]+\b/);
      expect(mensajeErrorFijo(new ApiError(422, SECRETO, code), RESPALDO, catalogo), code).toBe(texto);
    }
  });
});

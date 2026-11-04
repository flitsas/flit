import { describe, expect, it } from "vitest";
import type { Adjunto } from "@/lib/api/cliente";
import { accionGuardada, decisionDe, documentoDeDecision, DOCS_DE_PRENDA, errorDeDocumentoDePrenda } from "@/lib/tramite/prenda";
import { erroresDeRequisitos } from "@/lib/tramite/requisitos";
import { definicionDe } from "./fixtures-wizard";

const doc = (docCode: string, obligatorio: boolean) => ({ docCode, nombre: docCode, obligatorio, orden: 1, instruccionCargue: "", condicion: "prenda" });
const def = (obligatorio = true) =>
  definicionDe([["prenda_decision"], ["document_checklist"]], {
    flags: { entryMode: "PLATE", requiereComercial: false, requierePrenda: false, permiteTransformaciones: false, permitePrendaComplementaria: true, requiereBiometria: false, requiereSolicitudPlaca: false },
    documentos: [doc("prenda_registro", obligatorio), doc("prenda_levantamiento", obligatorio)],
  });
const adjunto = (tipo: string): Adjunto => ({ id: tipo, tipo, nombreArchivo: `${tipo}.pdf`, mimeType: "application/pdf", tamanoBytes: 1, sha256: "x", creadoEn: "2026-10-28T10:00:00Z" });
const entrada = { campos: {}, valorVenta: "", observaciones: "", acreedor: "", nitAcreedor: "" };

describe("prenda: decisión y documento por decisión", () => {
  it("la decisión sale de tener prenda y de la acción; sin prenda no hay acción", () => {
    expect(decisionDe(false, "levantar")).toBe("sin_prenda");
    expect(decisionDe(true, "registrar")).toBe("registrar");
    expect(decisionDe(true, "levantar")).toBe("levantar");
  });

  it("al reabrir un borrador solo «levantar» se conserva; cualquier otra cosa vuelve a registrar", () => {
    expect(accionGuardada("levantar")).toBe("levantar");
    expect(accionGuardada("registrar")).toBe("registrar");
    expect(accionGuardada("sin_prenda")).toBe("registrar");
    expect(accionGuardada(undefined)).toBe("registrar");
    expect(accionGuardada(null)).toBe("registrar");
  });

  it("cada decisión pide su documento y solo si la matriz del tipo lo trae", () => {
    expect(documentoDeDecision(def(), "registrar")?.docCode).toBe("prenda_registro");
    expect(documentoDeDecision(def(), "levantar")?.docCode).toBe("prenda_levantamiento");
    expect(documentoDeDecision(def(), "sin_prenda")).toBeUndefined();
    expect(documentoDeDecision({ ...def(), documentos: [] }, "levantar")).toBeUndefined();
    expect([...DOCS_DE_PRENDA].sort()).toEqual(["prenda_levantamiento", "prenda_registro"]);
  });

  it("falta el documento de la decisión solo si es obligatorio y no está cargado", () => {
    expect(errorDeDocumentoDePrenda(def(), [], "levantar")).toEqual({ "documento.prenda_levantamiento": "Adjunta el archivo requerido" });
    expect(errorDeDocumentoDePrenda(def(), [adjunto("prenda_registro")], "levantar")).toEqual({ "documento.prenda_levantamiento": "Adjunta el archivo requerido" });
    expect(errorDeDocumentoDePrenda(def(), [adjunto("prenda_levantamiento")], "levantar")).toEqual({});
    expect(errorDeDocumentoDePrenda(def(false), [], "registrar")).toEqual({});
    expect(errorDeDocumentoDePrenda(def(), [], "sin_prenda")).toEqual({});
  });

  it("con prenda (registrar o levantar) se exigen acreedor y NIT; sin prenda no", () => {
    const d = { ...def(), documentos: [] };
    expect(Object.keys(erroresDeRequisitos(d, [], { ...entrada, tienePrenda: true })).sort()).toEqual(["prenda.acreedor", "prenda.nitAcreedor"]);
    expect(erroresDeRequisitos(d, [], { ...entrada, tienePrenda: false })).toEqual({});
  });
});

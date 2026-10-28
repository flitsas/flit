import { describe, expect, it } from "vitest";
import type { Adjunto, CampoDefinicion, DocumentoRequerido } from "@/lib/api/cliente";
import {
  adjuntoDe,
  erroresDeRequisitos,
  errorDeArchivo,
  errorDeCampo,
  esExigible,
  MAX_BYTES_ARCHIVO,
  tarjetasDeRequisitos,
  tamanoLegible,
  type EntradaRequisitos,
} from "@/lib/tramite/requisitos";
import { definicionDe } from "./fixtures-wizard";

const campo = (extra: Partial<CampoDefinicion> = {}): CampoDefinicion => ({ fieldKey: "k", etiqueta: "K", tipo: "text", obligatorio: false, opciones: [], validationSchema: "", ...extra });
const doc = (extra: Partial<DocumentoRequerido> = {}): DocumentoRequerido => ({ docCode: "d", nombre: "D", obligatorio: true, orden: 1, instruccionCargue: "", condicion: "", ...extra });
const adjunto = (tipo: string, creadoEn: string, id = tipo): Adjunto => ({ id, tipo, nombreArchivo: `${id}.pdf`, mimeType: "application/pdf", tamanoBytes: 10, sha256: "x", creadoEn });
const entrada = (extra: Partial<EntradaRequisitos> = {}): EntradaRequisitos => ({ campos: {}, valorVenta: "", tienePrenda: false, acreedor: "", nitAcreedor: "", observaciones: "", ...extra });
const flags = (extra: Record<string, boolean> = {}) => ({ entryMode: "PLATE", requiereComercial: false, requierePrenda: false, permiteTransformaciones: false, permitePrendaComplementaria: false, requiereBiometria: false, requiereSolicitudPlaca: false, ...extra });
const SECCIONES = [["commercial", "prenda_decision", "document_checklist", "generic_form"]];

describe("archivos del cargador", () => {
  it("acepta PDF, JPG, PNG y WEBP de hasta 20 MB y rechaza lo demás con un motivo claro", () => {
    expect(errorDeArchivo({ name: "a.PDF", size: 100 })).toBeNull();
    expect(errorDeArchivo({ name: "a.jpeg", size: 100 })).toBeNull();
    expect(errorDeArchivo({ name: "a.webp", size: MAX_BYTES_ARCHIVO })).toBeNull();
    expect(errorDeArchivo({ name: "a.exe", size: 100 })).toBe("Solo se aceptan archivos PDF, JPG, PNG o WEBP.");
    expect(errorDeArchivo({ name: "a.pdf", size: MAX_BYTES_ARCHIVO + 1 })).toBe("El archivo supera los 20 MB permitidos.");
    expect(errorDeArchivo({ name: "a.pdf", size: 0 })).toBe("El archivo está vacío.");
    expect(tamanoLegible(2.5 * 1024 * 1024)).toBe("2.5 MB");
  });

  it("el archivo vigente de un documento es el más reciente y un documento con condición no se exige", () => {
    const lista = [adjunto("a", "2026-10-01T00:00:00Z", "viejo"), adjunto("a", "2026-10-02T00:00:00Z", "nuevo"), adjunto("b", "2026-10-03T00:00:00Z")];

    expect(adjuntoDe(lista, "a")?.id).toBe("nuevo");
    expect(adjuntoDe(lista, "z")).toBeUndefined();
    expect(esExigible(doc())).toBe(true);
    expect(esExigible(doc({ obligatorio: false }))).toBe(false);
    expect(esExigible(doc({ condicion: "si hay prenda" }))).toBe(false);
  });
});

describe("tarjetas que aplican al tipo", () => {
  it("muestra solo lo que la definición habilita con sus flags y secciones", () => {
    const todas = definicionDe(SECCIONES, { flags: flags({ requiereComercial: true, requierePrenda: true, permiteTransformaciones: true }), documentos: [doc()] });
    const t = tarjetasDeRequisitos(todas);

    expect(t).toMatchObject({ comercial: true, prenda: true, prendaObligatoria: true, documentos: true, observaciones: true, transformaciones: true });

    const ninguna = tarjetasDeRequisitos(definicionDe(SECCIONES, { flags: flags() }));
    expect(ninguna).toMatchObject({ comercial: false, prenda: false, documentos: false, transformaciones: false, observaciones: true });
  });

  it("la prenda complementaria se pregunta; sin la sección no hay tarjeta aunque el flag esté activo", () => {
    expect(tarjetasDeRequisitos(definicionDe(SECCIONES, { flags: flags({ permitePrendaComplementaria: true }) }))).toMatchObject({ prenda: true, prendaObligatoria: false });
    expect(tarjetasDeRequisitos(definicionDe([["document_checklist"]], { flags: flags({ requierePrenda: true }) })).prenda).toBe(false);
  });

  it("los campos del tipo salen de los pasos de requisitos que los traen", () => {
    const d = definicionDe([["actor_form"], ["generic_form"]], {});
    d.pasos[0]!.campos = [campo({ fieldKey: "de-actores" })];
    d.pasos[1]!.campos = [campo({ fieldKey: "de-requisitos" })];

    expect(tarjetasDeRequisitos(d).campos.map((p) => p.clave)).toEqual(["p2"]);
  });
});

describe("validación de campos del tipo", () => {
  it("valida cada tipo y la obligatoriedad como el back", () => {
    expect(errorDeCampo(campo({ obligatorio: true }), " ")).toBe("Este campo es obligatorio");
    expect(errorDeCampo(campo(), "")).toBeUndefined();
    expect(errorDeCampo(campo({ tipo: "number" }), "12.5")).toBeUndefined();
    expect(errorDeCampo(campo({ tipo: "number" }), "doce")).toBe("Escribe un número");
    expect(errorDeCampo(campo({ tipo: "date" }), "2026-10-28")).toBeUndefined();
    expect(errorDeCampo(campo({ tipo: "date" }), "28/10/2026")).toBe("Elige una fecha válida");
    expect(errorDeCampo(campo({ tipo: "select", opciones: ["A", "B"] }), "C")).toBe("Elige una de las opciones");
    expect(errorDeCampo(campo({ tipo: "select", opciones: ["A", "B"] }), "B")).toBeUndefined();
    expect(errorDeCampo(campo({ tipo: "checkbox" }), "true")).toBeUndefined();
    expect(errorDeCampo(campo({ tipo: "textarea" }), "x".repeat(2001))).toContain("2000");
    expect(errorDeCampo(campo(), "x".repeat(501))).toContain("500");
  });

  it("reúne los errores del paso con la misma llave con la que el back nombra cada cosa", () => {
    const d = definicionDe(SECCIONES, { flags: flags({ requiereComercial: true, permitePrendaComplementaria: true }), documentos: [doc({ docCode: "paz" }), doc({ docCode: "opc", obligatorio: false })] });
    d.pasos[0]!.campos = [campo({ fieldKey: "cil", obligatorio: true })];

    expect(Object.keys(erroresDeRequisitos(d, [], entrada({ tienePrenda: true }))).sort()).toEqual(["campo.cil", "documento.paz", "prenda.acreedor", "prenda.nitAcreedor", "valorComercial"]);
    expect(erroresDeRequisitos(d, [adjunto("paz", "2026-10-01T00:00:00Z")], entrada({ campos: { cil: "1600" }, valorVenta: "5000000", tienePrenda: true, acreedor: "Banco", nitAcreedor: "860002964" }))).toEqual({});
    // Sin prenda, el acreedor y el NIT no se exigen ni se validan.
    expect(erroresDeRequisitos(d, [adjunto("paz", "2026-10-01T00:00:00Z")], entrada({ campos: { cil: "1" }, valorVenta: "1" }))).toEqual({});
  });
});

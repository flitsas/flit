import { describe, expect, it } from "vitest";
import type { Adjunto, DeclaracionTipo } from "@/lib/api/cliente";
import {
  claveDeCampoDeclaracion,
  declaracionActiva,
  declaracionesActivas,
  declaracionesDe,
  erroresDeDeclaraciones,
  errorIgualAlRunt,
  nombreLegibleDeDocumento,
  valorActualEnRunt,
  soportesDe,
  valoresDeDeclaraciones,
} from "@/lib/tramite/declaraciones";
import { tarjetasDeRequisitos } from "@/lib/tramite/requisitos";
import { definicionDe } from "./fixtures-wizard";

const decl = (extra: Partial<DeclaracionTipo> = {}): DeclaracionTipo => ({
  clave: "color",
  control: "seleccion_catalogo",
  etiqueta: "Nuevo color",
  obligatoria: true,
  opciones: [],
  fieldKey: "vehicle_color",
  fieldKeysExtra: ["cambio_color"],
  docCodeSoporte: "soporte_cambio_color",
  soporteObligatorio: true,
  catalogo: "colores",
  ...extra,
});
const CAUSAL = decl({
  clave: "causal",
  control: "seleccion",
  etiqueta: "Causal de cancelación",
  fieldKey: "cancelacion_causal",
  fieldKeysExtra: [],
  docCodeSoporte: "",
  soporteObligatorio: false,
  catalogo: "",
  opciones: [
    { codigo: "DECISION_JUDICIAL", etiqueta: "Decisión judicial", docCodes: ["oficio_judicial"], exigeSoporte: true },
    { codigo: "PERDIDA_TOTAL", etiqueta: "Pérdida total", docCodes: ["certificado_dijin", "certificado_aseguradora"], exigeSoporte: true },
    { codigo: "VOLUNTARIA", etiqueta: "Decisión voluntaria", docCodes: [], exigeSoporte: false },
  ],
});
const SIMULTANEO = decl({ clave: "color_simultaneo", obligatoria: false });
const adjunto = (tipo: string): Adjunto => ({ id: tipo, tipo, nombreArchivo: `${tipo}.pdf`, mimeType: "application/pdf", tamanoBytes: 1, sha256: "x", creadoEn: "2026-10-28T10:00:00Z" });
const def = (declaraciones: DeclaracionTipo[]) => definicionDe([["vehicle_query"], ["document_checklist"]], { declaraciones });

describe("declaraciones del tipo (lógica pura)", () => {
  it("una definición sin declaraciones (borrador anterior) se toma como vacía y no pinta tarjeta", () => {
    const d = definicionDe([["document_checklist"]]);
    expect(declaracionesDe(d)).toEqual([]);
    expect(tarjetasDeRequisitos(d).declaraciones).toBe(false);
    expect(erroresDeDeclaraciones(d, [], {})).toEqual({});
    expect(valoresDeDeclaraciones(d, {})).toEqual({});
  });

  it("la tarjeta aplica con declaraciones propias pero no con solo la secretaría de destino", () => {
    expect(tarjetasDeRequisitos(def([decl()])).declaraciones).toBe(true);
    expect(tarjetasDeRequisitos(def([decl({ control: "organismo_destino" })])).declaraciones).toBe(false);
    expect(declaracionesDe(def([decl({ control: "organismo_destino" })]))).toEqual([]);
  });

  it("una declaración opcional atada a un trámite simultáneo solo se pide con ese trámite activado; una obligatoria siempre", () => {
    expect(declaracionActiva(SIMULTANEO, [])).toBe(false);
    expect(declaracionActiva(SIMULTANEO, ["CAMBIO_COLOR"])).toBe(true);
    expect(declaracionActiva(SIMULTANEO, ["CAMBIO_CARROCERIA"])).toBe(false);
    expect(declaracionActiva(decl(), [])).toBe(true);
    expect(declaracionActiva(decl({ obligatoria: false, fieldKeysExtra: [] }), [])).toBe(true);
    expect(declaracionesActivas(def([decl(), SIMULTANEO, CAUSAL]), []).map((x) => x.clave)).toEqual(["color", "causal"]);
  });

  it("activadaPor del back manda sobre la deducción por banderas: ata también a una obligatoria y vacío la deja siempre activa", () => {
    const atada = decl({ activadaPor: "conversion_combustible", obligatoria: true });
    expect(declaracionActiva(atada, [])).toBe(false);
    expect(declaracionActiva(atada, ["CONVERSION_COMBUSTIBLE"])).toBe(true);
    expect(declaracionActiva(decl({ activadaPor: "", obligatoria: false }), [])).toBe(true);
  });

  it("el soporte de un valor de catálogo es el general de la declaración; sin valor no se pide ninguno", () => {
    const d = def([decl()]);
    expect(soportesDe(decl(), "", d)).toEqual([]);
    expect(soportesDe(decl(), "ROJO", d)).toEqual([{ docCode: "soporte_cambio_color", nombre: "Soporte cambio color", obligatorio: true }]);
    expect(soportesDe(decl({ soporteObligatorio: false }), "ROJO", d)[0]?.obligatorio).toBe(false);
  });

  it("un valor con opciones en línea sin documentos propios usa el soporte general: opcional si la declaración no lo obliga", () => {
    const combustible = decl({
      clave: "transformacion_combustible",
      fieldKey: "vehicle_fuel",
      fieldKeysExtra: ["cambio_combustible"],
      docCodeSoporte: "soporte_conversion_combustible",
      soporteObligatorio: false,
      catalogo: "combustibles",
      opciones: [
        { codigo: "GAS", etiqueta: "Gas", docCodes: [], exigeSoporte: false },
        { codigo: "GASOLINA", etiqueta: "Gasolina", docCodes: [], exigeSoporte: false },
      ],
    });
    const d = def([combustible]);
    expect(soportesDe(combustible, "GAS", d)).toEqual([{ docCode: "soporte_conversion_combustible", nombre: "Soporte conversion combustible", obligatorio: false }]);
    expect(erroresDeDeclaraciones(d, [], { vehicle_fuel: "GAS" })).toEqual({});
    // Si la declaración obliga al soporte (blindaje), lo exige aunque la opción no traiga documentos propios.
    expect(soportesDe({ ...combustible, soporteObligatorio: true }, "GAS", d)[0]?.obligatorio).toBe(true);
  });

  it("el nombre del soporte sale de la matriz del tipo si lo trae", () => {
    const d = { ...def([decl()]), documentos: [{ docCode: "soporte_cambio_color", nombre: "Factura del cambio de color", obligatorio: false, orden: 1, instruccionCargue: "", condicion: "" }] };
    expect(soportesDe(decl(), "ROJO", d)[0]?.nombre).toBe("Factura del cambio de color");
    expect(nombreLegibleDeDocumento("factura_carroceria")).toBe("Factura carroceria");
  });

  it("la causal pide los documentos de SU opción; una opción sin soporte no pide ninguno", () => {
    const d = def([CAUSAL]);
    expect(soportesDe(CAUSAL, "DECISION_JUDICIAL", d).map((s) => s.docCode)).toEqual(["oficio_judicial"]);
    expect(soportesDe(CAUSAL, "PERDIDA_TOTAL", d).map((s) => [s.docCode, s.obligatorio])).toEqual([
      ["certificado_dijin", true],
      ["certificado_aseguradora", true],
    ]);
    expect(soportesDe(CAUSAL, "VOLUNTARIA", d)).toEqual([]);
  });

  it("errores: falta el valor, falta el soporte del valor elegido y valor fuera de las opciones", () => {
    const d = def([decl(), CAUSAL]);
    expect(erroresDeDeclaraciones(d, [], {})).toEqual({ "declaracion.color": "Este campo es obligatorio", "declaracion.causal": "Este campo es obligatorio" });
    expect(erroresDeDeclaraciones(d, [], { vehicle_color: "ROJO", cancelacion_causal: "PERDIDA_TOTAL" })).toEqual({
      "documento.soporte_cambio_color": "Adjunta el archivo requerido",
      "documento.certificado_dijin": "Adjunta el archivo requerido",
      "documento.certificado_aseguradora": "Adjunta el archivo requerido",
    });
    expect(erroresDeDeclaraciones(d, [adjunto("soporte_cambio_color"), adjunto("certificado_dijin"), adjunto("certificado_aseguradora")], { vehicle_color: "ROJO", cancelacion_causal: "PERDIDA_TOTAL" })).toEqual({});
    expect(erroresDeDeclaraciones(d, [], { vehicle_color: "ROJO", cancelacion_causal: "OTRA" })["declaracion.causal"]).toBe("Elige una de las opciones");
    // Voluntaria no exige nada más.
    expect(erroresDeDeclaraciones(def([CAUSAL]), [], { cancelacion_causal: "VOLUNTARIA" })).toEqual({});
  });

  it("una declaración opcional atada a un simultáneo activado exige su valor", () => {
    const d = def([SIMULTANEO]);
    expect(erroresDeDeclaraciones(d, [], {}, [])).toEqual({});
    expect(erroresDeDeclaraciones(d, [], {}, ["CAMBIO_COLOR"])).toEqual({ "declaracion.color_simultaneo": "Este campo es obligatorio" });
  });

  it("valores a guardar: solo la clave principal (las banderas las deriva el servidor); una obligatoria vacía no se envía", () => {
    const d = def([decl(), CAUSAL]);
    expect(valoresDeDeclaraciones(d, { vehicle_color: " ROJO ", cancelacion_causal: "" }, [])).toEqual({ vehicle_color: "ROJO" });
    expect(valoresDeDeclaraciones(d, {}, [])).toEqual({});
  });

  it("una opcional vacía envía null; una desactivada solo se limpia si había algo guardado", () => {
    const soloSimultaneo = def([SIMULTANEO]);
    expect(valoresDeDeclaraciones(soloSimultaneo, { vehicle_color: "ROJO" }, ["CAMBIO_COLOR"])).toEqual({ vehicle_color: "ROJO" });
    expect(valoresDeDeclaraciones(soloSimultaneo, {}, ["CAMBIO_COLOR"])).toEqual({ vehicle_color: null });
    expect(valoresDeDeclaraciones(soloSimultaneo, { vehicle_color: "ROJO" }, [], {})).toEqual({});
    expect(valoresDeDeclaraciones(soloSimultaneo, { vehicle_color: "ROJO" }, [], { vehicle_color: "ROJO" })).toEqual({ vehicle_color: null });
    // Una obligatoria desactivada no se puede limpiar (el back la rechazaría).
    expect(valoresDeDeclaraciones(def([decl({ activadaPor: "CAMBIO_COLOR" })]), {}, [], { vehicle_color: "ROJO" })).toEqual({});
  });

  it("un 422 sobre un campo de una declaración (o su bandera) se asocia a la declaración", () => {
    const d = def([decl(), CAUSAL]);
    expect(claveDeCampoDeclaracion(d, "vehicle_color")).toBe("declaracion.color");
    expect(claveDeCampoDeclaracion(d, "cambio_color")).toBe("declaracion.color");
    expect(claveDeCampoDeclaracion(d, "cilindraje")).toBeUndefined();
  });
});

describe("el valor nuevo debe ser distinto al del RUNT", () => {
  const d = def([decl()]);
  const runt = { color: "AZUL MATE" };

  it("igual (normalizado) al del RUNT es error y no se guarda; distinto pasa; sin valor RUNT no bloquea", () => {
    expect(erroresDeDeclaraciones(d, [], { vehicle_color: "azul  máte" }, [], runt)["declaracion.color"]).toBe("El nuevo color debe ser distinto al registrado en el RUNT.");
    expect(valoresDeDeclaraciones(d, { vehicle_color: "AZUL MATE" }, [], {}, runt)).toEqual({});
    expect(erroresDeDeclaraciones(d, [adjunto("soporte_cambio_color")], { vehicle_color: "ROJO" }, [], runt)).toEqual({});
    expect(valoresDeDeclaraciones(d, { vehicle_color: "ROJO" }, [], {}, runt)).toEqual({ vehicle_color: "ROJO" });
    expect(erroresDeDeclaraciones(d, [adjunto("soporte_cambio_color")], { vehicle_color: "AZUL MATE" }, [], {})).toEqual({});
    expect(erroresDeDeclaraciones(d, [adjunto("soporte_cambio_color")], { vehicle_color: "AZUL MATE" }, [], undefined)).toEqual({});
  });

  it("muestra el valor actual y aplica a combustible y carrocería; el blindaje no se compara", () => {
    expect(valorActualEnRunt(decl(), runt)?.texto).toBe("Color actual en el RUNT: AZUL MATE");
    const comb = decl({ clave: "comb", fieldKey: "vehicle_fuel", catalogo: "combustibles", opciones: [{ codigo: "GAS", etiqueta: "Gas", docCodes: [], exigeSoporte: false }], soporteObligatorio: false });
    expect(errorIgualAlRunt(comb, "GAS", { combustible: "GAS" })).toBe("El nuevo combustible debe ser distinto al registrado en el RUNT.");
    expect(errorIgualAlRunt(decl({ fieldKey: "vehicle_body_type", catalogo: "carrocerias" }), "Furgón", { carroceria: "FURGON" })).toMatch(/carrocería/);
    expect(errorIgualAlRunt(CAUSAL, "VOLUNTARIA", { color: "VOLUNTARIA" })).toBeUndefined();
  });

  it("el 422 del servidor sobre valores.<fieldKey> cae en la declaración", () => {
    expect(claveDeCampoDeclaracion(d, "vehicle_color")).toBe("declaracion.color");
  });
});

describe("filas de declaraciones en el resumen", () => {
  it("etiqueta y valor legible, más el soporte cargado", async () => {
    const { filasDeDeclaraciones } = await import("@/lib/tramite/resumen");
    const { solicitudCompleta } = await import("./fixtures-envio");
    const base = solicitudCompleta();
    const sol = { ...base, fieldValues: { ...base.fieldValues, vehicle_color: "ALUMINIO ROJO", cancelacion_causal: "VOLUNTARIA" }, adjuntos: [adjunto("soporte_cambio_color")] };
    const filas = filasDeDeclaraciones(def([decl({ soporteObligatorio: false }), CAUSAL]), sol);
    expect(filas).toEqual([
      { etiqueta: "Nuevo color", valor: "ALUMINIO ROJO" },
      { etiqueta: "Soporte cambio color", valor: "Cargado" },
      { etiqueta: "Causal de cancelación", valor: "Decisión voluntaria" },
    ]);
  });
});

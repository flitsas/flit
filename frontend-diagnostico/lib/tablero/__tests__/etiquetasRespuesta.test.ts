import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { CLAVES_APLANADAS, CLAVES_OCULTAS, CLAVES_OCULTAS_EN_CHEQUEOS, ETIQUETAS_RESPUESTA, camposVisibles, etiquetaCampo, textoValor } from "@/lib/tablero/etiquetasRespuesta";

// Uso de ejemplo: etiquetaCampo("document_type") → «Tipo de documento»; textoValor(true) → «Sí»; camposVisibles(respuesta)
// devuelve los pares [clave, valor] que se pintan (sin la auditoría del proveedor, con `resultado` aplanado).
//
// Contrato (HU #14087): todo campo de los mensajes de respuesta alcanzables desde los .proto de Consultas debe estar traducido
// en ETIQUETAS_RESPUESTA o en la lista de ocultos/aplanados. Si core-consultas agrega un campo nuevo, este test falla hasta que
// se traduzca.

const RAIZ_PROTO = resolve(__dirname, "../../../../contracts/proto/flit/consultas/v1");
const ARCHIVOS = ["runt_intempo.proto", "resultado.proto", "certificaciones.proto", "consultas.proto", "fuentes_comparendos.proto"];

interface CampoProto {
  nombre: string;
  json: string | null;
  tipo: string;
}

/** Mensajes de primer nivel de los .proto (los de Consultas no anidan mensajes) con sus campos. */
function leerMensajes(): Map<string, CampoProto[]> {
  const mensajes = new Map<string, CampoProto[]>();
  for (const archivo of ARCHIVOS) {
    const texto = readFileSync(resolve(RAIZ_PROTO, archivo), "utf8").replace(/\/\/.*$/gm, "");
    for (const m of texto.matchAll(/message\s+(\w+)\s*\{([^{}]*)\}/g)) {
      const campos: CampoProto[] = [];
      for (const c of m[2].matchAll(/(?:optional\s+|repeated\s+)?(map<\s*\w+\s*,\s*[\w.]+\s*>|[\w.]+)\s+(\w+)\s*=\s*\d+\s*(?:\[\s*json_name\s*=\s*"(\w+)"\s*\])?\s*;/g)) {
        campos.push({ tipo: c[1], nombre: c[2], json: c[3] ?? null });
      }
      mensajes.set(m[1], campos);
    }
  }
  return mensajes;
}

/** Campos de los mensajes alcanzables desde las respuestas; no se desciende por los ocultos (su contenido nunca se pinta). */
function camposAlcanzables(): CampoProto[] {
  const mensajes = leerMensajes();
  const raices = ["ConsultarVehiculoIntempoResponse", "ResultadoConsulta", "Chequeo", "Comparendo", "Dato", "Avaluo", "ConsultarAvaluosResponse"];
  for (const r of raices) expect(mensajes.has(r), `no se encontró el mensaje ${r}`).toBe(true);
  const vistos = new Set<string>();
  const pendientes = [...raices];
  const salida: CampoProto[] = [];
  while (pendientes.length) {
    const nombre = pendientes.pop()!;
    if (vistos.has(nombre)) continue;
    vistos.add(nombre);
    for (const campo of mensajes.get(nombre) ?? []) {
      salida.push(campo);
      // `campos` de ResultadoConsulta (repeated Campo) se oculta; el de AccidenteIntempo es un mapa libre que se aplana.
      if (CLAVES_OCULTAS.has(campo.nombre) || (campo.nombre === "campos" && !campo.tipo.startsWith("map<"))) continue;
      const tipo = campo.tipo.replace(/^map<.*,\s*/, "").replace(/>$/, "").split(".").pop()!;
      if (mensajes.has(tipo)) pendientes.push(tipo);
    }
  }
  return salida;
}

describe("etiquetasRespuesta — contrato con los .proto de Consultas", () => {
  it("cada campo de las respuestas alcanzables (snake_case y json_name) está traducido u oculto", () => {
    const campos = camposAlcanzables();
    expect(campos.length).toBeGreaterThan(150);
    const cubierto = (clave: string) => clave in ETIQUETAS_RESPUESTA || CLAVES_OCULTAS.has(clave) || CLAVES_APLANADAS.has(clave) || CLAVES_OCULTAS_EN_CHEQUEOS.has(clave);
    const faltantes = campos.flatMap((c) => [c.nombre, ...(c.json ? [c.json] : [])]).filter((clave) => !cubierto(clave));
    expect([...new Set(faltantes)]).toEqual([]);
  });

  it("los mensajes de RUNT Intempo, certificaciones y avalúos están en el recorrido", () => {
    const nombres = new Set(camposAlcanzables().map((c) => c.nombre));
    for (const n of ["plate", "cilidraje", "no_motor", "accident_rate", "owner", "lessee", "matriculas_mercantiles", "representantes_legales", "valor_sugerido", "avaluos", "chequeos"])
      expect(nombres.has(n), n).toBe(true);
  });

  it("las etiquetas son español con mayúscula inicial y nunca repiten la clave cruda", () => {
    for (const [clave, etiqueta] of Object.entries(ETIQUETAS_RESPUESTA)) {
      expect(etiqueta, clave).toMatch(/^[A-ZÁÉÍÓÚÑ0-9]/);
      expect(etiqueta, clave).not.toContain("_");
    }
  });
});

describe("etiquetaCampo / textoValor / camposVisibles", () => {
  it.each([
    ["plate", "Placa"],
    ["vin", "VIN"],
    ["document_type", "Tipo de documento"],
    ["document_number", "Número de documento"],
    ["owner", "Propietarios"],
    ["lessee", "Locatarios"],
    ["accident_rate", "Siniestralidad"],
    ["first_name", "Primer nombre"],
    ["code", "Código"],
    ["description", "Descripción"],
    ["cilidraje", "Cilindraje"],
    ["no_motor", "N.º de motor"],
    ["informacion_general", "Información general"],
    ["propietarios_actuales", "Propietarios actuales"],
    ["valor_sugerido", "Valor sugerido"],
    ["verValidaDIAN", "Ver validación DIAN"],
  ])("%s → %s", (clave, etiqueta) => expect(etiquetaCampo(clave)).toBe(etiqueta));

  it("clave desconocida: guiones bajos a espacios y mayúscula inicial; camelCase separado", () => {
    expect(etiquetaCampo("numero_de_siniestros")).toBe("Numero de siniestros");
    expect(etiquetaCampo("fechaUltimoReporte")).toBe("Fecha ultimo reporte");
  });

  it("valores: Sí/No, «—», «Sin registros», enums y textos tal cual", () => {
    expect(textoValor(true)).toBe("Sí");
    expect(textoValor(false)).toBe("No");
    expect(textoValor(null)).toBe("—");
    expect(textoValor(undefined)).toBe("—");
    expect(textoValor("")).toBe("—");
    expect(textoValor([])).toBe("Sin registros");
    expect(textoValor(0)).toBe("0");
    expect(textoValor("ESTADO_CHEQUEO_WARN")).toBe("Advertencia");
    expect(textoValor("SEMAFORO_VERDE")).toBe("Verde");
    expect(textoValor("ESTADO_AVALUO_OK")).toBe("OK");
    expect(textoValor("TIPO_ERROR_FUENTE_TIMEOUT")).toBe("Tiempo de espera agotado");
    expect(textoValor("VIGENCIA_VENCIDO")).toBe("Vencido");
    expect(textoValor("PARTICULAR")).toBe("PARTICULAR");
  });

  it("camposVisibles oculta la auditoría, aplana `resultado` objeto y oculta el enum", () => {
    expect(camposVisibles({ proveedor: "x", id: 1, id_ciudad: "1", resultado: { semaforo: "SEMAFORO_ROJO", chequeos: [] }, ciudad: "C" })).toEqual([
      ["chequeos", []],
      ["ciudad", "C"],
    ]);
    expect(camposVisibles({ resultado: "RESULTADO_RUNT_INTEMPO_ERROR", vehiculo: {} })).toEqual([["vehiculo", {}]]);
    expect(camposVisibles({ clave: "k", fuente: "f", etiqueta: "e" }, true)).toEqual([["etiqueta", "e"]]);
    expect(camposVisibles({ fuente: "fasecolda" })).toEqual([["fuente", "fasecolda"]]);
  });
});

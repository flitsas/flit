import { afterEach, describe, expect, it, vi } from "vitest";
import {
  derivacionDeFila,
  leerProgreso,
  marcarConTramite,
  marcarEncoladas,
  rotuloMotivo,
  urlTramite,
} from "@/lib/tablero/derivacion";
import { ROTULO_SIN_CLASIFICAR } from "@/lib/rotulos";
import type { FilaVehiculo } from "@/lib/tablero/api";
import { V1, grilla } from "./datos";

// Uso de ejemplo: derivacionDeFila(fila) → { conocida, elegible, motivo, estado, tramite }; rotuloMotivo("OT_NO_HABILITADO")
// → «OT no habilitado para la compañía»; leerProgreso(respuesta) normaliza el progreso de la operación masiva.

afterEach(() => vi.restoreAllMocks());

const conDerivacion = (derivacion: unknown) => ({ ...V1, derivacion }) as unknown as FilaVehiculo;

describe("derivacionDeFila — contrato real del incremento 2 (`derivacion` anidada)", () => {
  it("fila elegible", () => {
    expect(derivacionDeFila(conDerivacion({ elegible: true, motivo_no_elegible: null, estado: null, tramite: null }))).toMatchObject({
      conocida: true,
      elegible: true,
      motivo: null,
      estado: null,
      tramite: null,
    });
  });

  it("fila con trámite: no elegible, conserva la url del backend", () => {
    const tramite = { id: "t-1", numero: "TR-1", url: "https://tramites.prueba/tramites/t-1?t=e" };
    const d = derivacionDeFila(conDerivacion({ elegible: false, motivo_no_elegible: "YA_TIENE_TRAMITE", estado: "CREADO", tramite }));
    expect(d).toMatchObject({ conocida: true, elegible: false, estado: "CREADO", tramite });
  });

  it("`derivacion: null` (apagada) o ausente: no conocida", () => {
    expect(derivacionDeFila(conDerivacion(null)).conocida).toBe(false);
    expect(derivacionDeFila(V1).conocida).toBe(false);
  });
});

describe("urlTramite", () => {
  it("usa la url absoluta del backend si viene", () => {
    expect(urlTramite("https://otro", { id: "t-1", numero: "1", url: "https://tramites.prueba/tramites/t-1?t=e" }, "e")).toBe(
      "https://tramites.prueba/tramites/t-1?t=e",
    );
  });
  it("con url relativa o sin url, arma la del origen de Trámites", () => {
    expect(urlTramite("https://tramites.prueba/", { id: "t-1", numero: "1", url: "/tramites/t-1?t=e" }, "e")).toBe("https://tramites.prueba/tramites/t-1?t=e");
    expect(urlTramite("https://tramites.prueba", { id: "t-1", numero: "1" }, null)).toBe("https://tramites.prueba/tramites/t-1");
  });
});

describe("marcarConTramite / marcarEncoladas", () => {
  it("marcarConTramite deja la fila con el trámite y no elegible (en la forma anidada)", () => {
    const g = marcarConTramite(grilla({ vehiculos: [conDerivacion({ elegible: true, motivo_no_elegible: null, estado: null, tramite: null })] }), V1.id, {
      id: "t-9",
      numero: "9",
    });
    expect(derivacionDeFila(g.vehiculos[0])).toMatchObject({ elegible: false, tramite: { id: "t-9" } });
  });

  it("marcarEncoladas pasa las filas a EN_CURSO (no seleccionables) y deja las demás", () => {
    const g = marcarEncoladas(grilla({ vehiculos: [conDerivacion({ elegible: true, motivo_no_elegible: null, estado: null, tramite: null })] }), [V1.id]);
    expect(derivacionDeFila(g.vehiculos[0])).toMatchObject({ elegible: false, motivo: "EN_CURSO", estado: "PENDIENTE" });
  });
});

describe("rotuloMotivo — lenguaje de negocio (Punto 5), nunca el código", () => {
  const codigos = [
    "NO_TERMINADO",
    "NO_VIABLE",
    "YA_TIENE_TRAMITE",
    "EN_CURSO",
    "TIPO_NO_DISPONIBLE",
    "FAMILIA_BLOQUEADA",
    "USUARIO_SIN_PERMISO",
    "OT_NO_HABILITADO",
    "REGLA_COMPANIA",
    "TRAMITE_ACTIVO_DUPLICADO",
    "EMPRESA_INACTIVA",
    "REFERENCIA_DE_OTRO_ORIGEN",
    "DATOS_INVALIDOS",
    "TRAMITES_NO_DISPONIBLE",
    "RECHAZO_TRAMITES",
  ];

  it.each(codigos)("%s tiene rótulo propio", (codigo) => {
    const rotulo = rotuloMotivo(codigo);
    expect(rotulo).not.toBe(codigo);
    expect(rotulo).not.toBe(ROTULO_SIN_CLASIFICAR);
    expect(rotulo).not.toMatch(/[A-Z]{2,}_[A-Z]/);
  });

  it("OT_NO_HABILITADO dice exactamente «OT no habilitado para la compañía»; YA_TIENE_TRAMITE, «ya tenía trámite»", () => {
    expect(rotuloMotivo("OT_NO_HABILITADO")).toBe("OT no habilitado para la compañía");
    expect(rotuloMotivo("YA_TIENE_TRAMITE")).toMatch(/ya tenía trámite/i);
  });

  it("la etiqueta de la API manda sobre el diccionario (regla 5.1)", () => {
    expect(rotuloMotivo("REGLA_COMPANIA", "La compañía exige el SOAT vigente")).toBe("La compañía exige el SOAT vigente");
  });

  it("código desconocido: «Novedad sin clasificar», nunca el código", () => {
    vi.spyOn(console, "warn").mockImplementation(() => {});
    expect(rotuloMotivo("CODIGO_NUEVO")).toBe(ROTULO_SIN_CLASIFICAR);
  });

  it("sin motivo: texto neutro", () => {
    expect(rotuloMotivo(null)).toMatch(/no cumple las condiciones/i);
  });
});

describe("leerProgreso — contrato provisional del masivo (diseño §5.1)", () => {
  it("normaliza contadores, estado e ítems; `en curso` = pendientes", () => {
    const p = leerProgreso({
      operacion_id: "op-1",
      estado: "EN_CURSO",
      total: 3,
      creadas: 1,
      omitidas: 1,
      fallidas: 0,
      pendientes: 1,
      items: [{ vehiculo_id: "v", placa: "TST001", estado: "OMITIDO", motivo_codigo: "NO_VIABLE", motivo: null, tramite_numero: null }],
    });
    expect(p).toMatchObject({ id: "op-1", estado: "EN_CURSO", total: 3, creadas: 1, omitidas: 1, fallidas: 0, enCurso: 1, terminada: false });
    expect(p.items).toHaveLength(1);
  });

  it("TERMINADA o todos procesados: terminada; falta un contador: 0", () => {
    expect(leerProgreso({ operacion_id: "op", estado: "TERMINADA", total: 1, creadas: 1 }).terminada).toBe(true);
    expect(leerProgreso({ operacion_id: "op", total: 2, creadas: 1, omitidas: 1 })).toMatchObject({ fallidas: 0, enCurso: 0, terminada: true });
  });
});

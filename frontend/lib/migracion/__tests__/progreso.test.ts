import { beforeEach, describe, expect, it } from "vitest";
import {
  cargarLote,
  clasificar,
  estaTerminada,
  guardarLote,
  nuevoLote,
  type Lote,
} from "@/lib/migracion/progreso";
import type { MigracionRespuesta } from "@/lib/migracion/types";

const CLAVE = "flit:migracion:progreso";

function respuesta(parcial: Partial<MigracionRespuesta> = {}): MigracionRespuesta {
  return {
    origen: {
      tramite: "transfer",
      tablaV1: "transfers",
      tipoV2: "TRASPASO",
      lote: "l1",
      baseV1: "v1 @ h:5432",
      baseV2: "v2 @ h:5432",
      v1Id: 26350,
      dryRun: false,
    },
    yaMigrado: null,
    instancias: [],
    destino: null,
    conProblemas: false,
    ...parcial,
  };
}

describe("clasificar", () => {
  it("marca fallido lo que el host reporta con problemas", () => {
    expect(clasificar(respuesta({ conProblemas: true }))).toBe("fallido");
  });

  /**
   * La distinción que pide quien reporta el avance de una ola: «migré veinte» no es lo mismo que
   * «migré tres, diecisiete ya estaban». Se decide por el bloque `yaMigrado`, que es la foto de
   * ANTES de la petición.
   */
  it("distingue lo que ya estaba migrado de lo que se migró ahora", () => {
    const previo = {
      v2Id: "11111111-1111-1111-1111-111111111111",
      tenantId: "22222222-2222-2222-2222-222222222222",
      lote: "anterior",
      estadoFinal: "aprobado",
      migradoEl: "2026-07-01T00:00:00Z",
      avisos: [],
    };

    expect(clasificar(respuesta({ yaMigrado: previo }))).toBe("ya_estaba");
    expect(clasificar(respuesta())).toBe("migrado");
  });

  it("separa la migración limpia de la que dejó avisos", () => {
    const conAviso = respuesta({
      instancias: [
        {
          instancia: "documentos",
          estado: "Loaded",
          v2Id: null,
          motivo: null,
          conProblemas: false,
          conteos: {},
          avisos: ["V1 no entregó: contrato"],
        },
      ],
    });

    expect(clasificar(conAviso)).toBe("con_avisos");
  });

  /** Un fallo gana sobre todo lo demás: no se puede reportar como "ya estaba" algo que reventó. */
  it("el problema pesa más que el ya-migrado", () => {
    const previo = {
      v2Id: "11111111-1111-1111-1111-111111111111",
      tenantId: "22222222-2222-2222-2222-222222222222",
      lote: "anterior",
      estadoFinal: "aprobado",
      migradoEl: "2026-07-01T00:00:00Z",
      avisos: [],
    };

    expect(clasificar(respuesta({ yaMigrado: previo, conProblemas: true }))).toBe("fallido");
  });
});

describe("estaTerminada", () => {
  it.each([
    ["migrado", true],
    ["con_avisos", true],
    ["ya_estaba", true],
    ["pendiente", false],
    ["en_curso", false],
    ["fallido", false],
  ] as const)("%s → terminada: %s", (estado, esperado) => {
    expect(estaTerminada({ tramite: "transfer", v1Id: 1, fila: 1, estado })).toBe(esperado);
  });
});

describe("persistencia", () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it("guarda y recupera un lote intacto", () => {
    const lote = nuevoLote(
      "ola.csv",
      [{ fila: 2, tramite: "transfer", v1Id: 26350 }],
      ["datos"],
      false,
    );

    guardarLote(lote);

    expect(cargarLote()).toEqual(lote);
  });

  it("devuelve null si no hay nada guardado", () => {
    expect(cargarLote()).toBeNull();
  });

  /**
   * Todo lo que sale de localStorage es entrada no confiable: puede venir de una versión anterior
   * de la consola o de alguien que lo editó a mano. Debe descartarse, nunca reventar la pantalla.
   */
  it.each([
    ["no es JSON", "{{{"],
    ["no es un objeto", '"hola"'],
    ["viene de otra versión", JSON.stringify({ version: 99, filas: [] })],
    ["no trae filas", JSON.stringify({ version: 1 })],
    ["trae filas vacías", JSON.stringify({ version: 1, filas: [] })],
  ])("descarta lo guardado si %s", (_caso, crudo) => {
    window.localStorage.setItem(CLAVE, crudo);

    expect(cargarLote()).toBeNull();
  });

  it("filtra las filas corruptas y conserva las buenas", () => {
    const guardado: Lote = {
      version: 1,
      archivo: "ola.csv",
      creadoEl: "2026-08-01T00:00:00Z",
      instancias: [],
      dryRun: false,
      filas: [
        { tramite: "transfer", v1Id: 26350, fila: 2, estado: "migrado" },
        { tramite: "cancelacion", v1Id: 1, fila: 3, estado: "pendiente" },
        { tramite: "registration", v1Id: Number.NaN, fila: 4, estado: "pendiente" },
      ] as never,
    };
    window.localStorage.setItem(CLAVE, JSON.stringify(guardado));

    const recuperado = cargarLote();

    expect(recuperado?.filas).toHaveLength(1);
    expect(recuperado?.filas[0].v1Id).toBe(26350);
  });
});

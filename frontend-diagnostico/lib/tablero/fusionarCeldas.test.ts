import { describe, expect, it } from "vitest";
import { fusionarCeldas, hayPendientes } from "@/lib/tablero/fusionarCeldas";
import { V1, V2, V3, celda, grilla, vehiculo } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: const vigente = fusionarCeldas(anterior, await obtenerGrilla(id, anterior.version)) → la grilla con las
// celdas recibidas encima de las que ya tenía, por (vehículo, fuente); lo no recibido se conserva tal cual.

describe("fusionarCeldas — HU #13748 AC2", () => {
  it("sin grilla previa devuelve los cambios con los vehículos en su orden", () => {
    const cambios = grilla({ vehiculos: [V3, V1, V2] });
    expect(fusionarCeldas(null, cambios).vehiculos.map((v) => v.identificador)).toEqual(["TST001", "TST002", "TST003"]);
  });

  it("fusiona por (vehículo, fuente) y conserva las celdas y los vehículos no recibidos", () => {
    const anterior = grilla();
    const runt = celda(3, "RUNT", "COMPLETADO", { version: 13 });
    const cambios = grilla({ version: 13, desde: 12, vehiculos: [vehiculo(3, [runt], { terminado: false })], avance: { vehiculos: 3, terminados: 1 } });

    const vigente = fusionarCeldas(anterior, cambios);

    const v3 = vigente.vehiculos.find((v) => v.identificador === "TST003")!;
    expect(v3.celdas).toHaveLength(4);
    expect(v3.celdas.find((c) => c.fuente === "RUNT")).toEqual(runt);
    expect(v3.celdas.filter((c) => c.fuente !== "RUNT").every((c) => c.estado === "PENDIENTE")).toBe(true);
    expect(vigente.vehiculos.find((v) => v.identificador === "TST001")).toEqual(V1);
    expect(vigente.vehiculos.find((v) => v.identificador === "TST002")).toEqual(V2);
  });

  it("conserva el orden de las columnas del vehículo aunque la celda recibida venga sola", () => {
    const cambios = grilla({ version: 13, vehiculos: [vehiculo(3, [celda(3, "SIMIT_PROPIETARIO", "EN_CURSO", { version: 13 })])] });
    expect(fusionarCeldas(grilla(), cambios).vehiculos[2].celdas.map((c) => c.fuente)).toEqual([
      "RUNT",
      "IMPUESTO_DEPARTAMENTAL",
      "SIMIT_PROPIETARIO",
      "RUNT_LOCATARIO",
    ]);
  });

  it("agrega un vehículo nuevo en su orden y actualiza «terminado» del recibido", () => {
    const v4 = vehiculo(4, [celda(4, "RUNT", "PENDIENTE", { version: 14 })]);
    const v3Terminado = vehiculo(3, [celda(3, "RUNT", "COMPLETADO", { version: 14 })], { terminado: true });
    const vigente = fusionarCeldas(grilla(), grilla({ version: 14, vehiculos: [v4, v3Terminado] }));
    expect(vigente.vehiculos.map((v) => v.identificador)).toEqual(["TST001", "TST002", "TST003", "TST004"]);
    expect(vigente.vehiculos[2].terminado).toBe(true);
  });

  it("toma de la respuesta el estado, la versión, las columnas, el avance y el estimado", () => {
    const anterior = grilla({ estimado_consultas: [{ fuente: "RUNT", estimadas: 3, consumidas: 3 }] });
    const cambios = grilla({ estado: "INCOMPLETO", version: 30, desde: 12, vehiculos: [], avance: { vehiculos: 3, terminados: 2 } });
    delete cambios.estimado_consultas;

    const vigente = fusionarCeldas(anterior, cambios);

    expect(vigente).toMatchObject({ estado: "INCOMPLETO", version: 30, desde: 12, avance: { vehiculos: 3, terminados: 2 }, columnas: cambios.columnas });
    expect(vigente.estimado_consultas).toBeUndefined();
    expect(vigente.vehiculos).toHaveLength(3);
  });

  it("una celda recibida con versión menor que la que ya tiene no la pisa", () => {
    const anterior = grilla({ vehiculos: [vehiculo(1, [celda(1, "RUNT", "COMPLETADO", { version: 20 })])] });
    const vieja = grilla({ vehiculos: [vehiculo(1, [celda(1, "RUNT", "PENDIENTE", { version: 5 })])] });
    expect(fusionarCeldas(anterior, vieja).vehiculos[0].celdas[0]).toMatchObject({ estado: "COMPLETADO", version: 20 });
  });

  it("no muta la grilla anterior", () => {
    const anterior = grilla();
    const copia = structuredClone(anterior);
    fusionarCeldas(anterior, grilla({ version: 13, vehiculos: [vehiculo(3, [celda(3, "RUNT", "ERROR", { version: 13 })])] }));
    expect(anterior).toEqual(copia);
  });
});

describe("hayPendientes — HU #13748 AC2 (cuándo detener el sondeo)", () => {
  it("hay pendientes con alguna celda PENDIENTE o EN_CURSO", () => {
    expect(hayPendientes(grilla({ vehiculos: [V1, V3] }))).toBe(true);
    expect(hayPendientes(grilla({ vehiculos: [vehiculo(2, [celda(2, "RUNT", "EN_CURSO")])] }))).toBe(true);
  });

  it("sin pendientes cuando todo está en COMPLETADO, ERROR o NA", () => {
    expect(hayPendientes(grilla({ vehiculos: [V1, vehiculo(2, [celda(2, "RUNT", "ERROR")])] }))).toBe(false);
  });

  it("sin grilla no hay nada que sondear", () => {
    expect(hayPendientes(null)).toBe(false);
  });
});

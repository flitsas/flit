import { describe, expect, it } from "vitest";
import {
  DEFAULT_OT_PROCEDURES_VISIBLE_COLUMNS,
  OT_PROCEDURES_COLUMNS,
  otColumnToSortBy,
} from "@/lib/admin/ot-procedures-columns";

describe("ot-procedures-columns", () => {
  it("incluye VIN, placa, propietario/vendedor, comprador y gestor", () => {
    const keys = OT_PROCEDURES_COLUMNS.map((c) => c.key);
    expect(keys).toEqual(
      expect.arrayContaining(["vin", "placa", "vendedor", "comprador", "gestor"]),
    );
    expect(OT_PROCEDURES_COLUMNS.find((c) => c.key === "vendedor")?.label).toBe(
      "Propietario / vendedor",
    );
  });

  it("marca como ordenables las columnas pedidas", () => {
    for (const key of ["vin", "placa", "vendedor", "comprador", "gestor"]) {
      expect(OT_PROCEDURES_COLUMNS.find((c) => c.key === key)?.sortable).toBe(true);
    }
  });

  it("mapea claves UI al sortBy del API", () => {
    expect(otColumnToSortBy("fechaRadicacion")).toBe("createdAt");
    expect(otColumnToSortBy("vendedor")).toBe("vendedor");
    expect(otColumnToSortBy("placa")).toBe("placa");
  });

  it("todas las columnas arrancan visibles por defecto", () => {
    expect(DEFAULT_OT_PROCEDURES_VISIBLE_COLUMNS).toEqual(
      OT_PROCEDURES_COLUMNS.map((c) => c.key),
    );
  });
});

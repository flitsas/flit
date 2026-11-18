import { describe, expect, it } from "vitest";
import { ATAJOS_GESTOR } from "@/lib/tramites/busquedaRapida";
import { FILTRO_RECHAZADO_PREASIGNACION } from "@/lib/tramites/estados";
import { ACCIONES_BUSCADOR, PLACA_BUSCADOR, tramitesPorAtajo, tramitesPorEstado, tramitesPorPlaca } from "../destinos";

// HU #14480 / #14481 — contrato de enlaces con el listado de /tramites (acordado con la sesión coordinadora).
describe("destinos hacia /tramites", () => {
  it("placa: mayúsculas, sin espacios ni guion", () => {
    expect(tramitesPorPlaca(" abc 12d ")).toBe("/tramites?placa=ABC12D");
    expect(tramitesPorPlaca("abc-123")).toBe("/tramites?placa=ABC123");
    expect(PLACA_BUSCADOR.href("xyz987")).toBe("/tramites?placa=XYZ987");
  });

  it("estado: la clave del filtro, incluido el pseudo-estado de rechazo en preasignación", () => {
    expect(tramitesPorEstado("borrador")).toBe("/tramites?estado=borrador");
    expect(tramitesPorEstado(FILTRO_RECHAZADO_PREASIGNACION)).toBe("/tramites?estado=rechazado_preasignacion");
  });

  it("atajo: la clave de ATAJOS_GESTOR, que coincide con su busquedaRapida cuando lo tiene", () => {
    expect(tramitesPorAtajo("sin_firmas")).toBe("/tramites?atajo=sin_firmas");
    for (const a of ATAJOS_GESTOR) if (a.busquedaRapida) expect(a.busquedaRapida).toBe(a.key);
  });

  it("el buscador ofrece exactamente los atajos existentes, con su texto, y dependen de ver Trámites", () => {
    expect(ACCIONES_BUSCADOR.map((a) => a.label)).toEqual(ATAJOS_GESTOR.map((a) => a.label));
    expect(new Set(ACCIONES_BUSCADOR.map((a) => a.requires))).toEqual(new Set(["tramites"]));
    expect(PLACA_BUSCADOR.requires).toBe("tramites");
  });
});

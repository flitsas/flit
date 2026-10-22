import { describe, expect, it } from "vitest";
import { textoCambioOrden, textoOrden } from "@/lib/orden";

describe("orden de la bandeja (HU #13785)", () => {
  it("dice cómo está ordenada, no lo que hace el clic", () => {
    expect(textoOrden("valor", true)).toBe("De mayor a menor valor");
    expect(textoOrden("valor", false)).toBe("De menor a mayor valor");
    expect(textoCambioOrden("valor", false)).toBe("Cambiar a: de mayor a menor valor");
  });

  it("habla de fechas en las fechas", () => {
    expect(textoOrden("notificacion", true)).toBe("Más recientes primero");
    expect(textoOrden("actividad", false)).toBe("Más antiguas primero");
  });

  it("el número es ascendente o descendente", () => {
    expect(textoOrden("numero", false)).toBe("Ascendente");
  });
});

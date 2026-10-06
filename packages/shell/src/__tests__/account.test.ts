import { describe, expect, it } from "vitest";
import { suiteAccountLinks, suiteRoleLabel } from "../account";

// Lo que la barra muestra igual en todos los productos: un producto nuevo no lo vuelve a escribir.

describe("opciones de cuenta de la suite", () => {
  it("en la app donde viven las pantallas, enlaces relativos", () => {
    expect(suiteAccountLinks().map((l) => [l.label, l.href])).toEqual([
      ["Ayuda", "/manual"],
      ["Cambio de contraseña", "/profile/change-password"],
    ]);
  });

  it("desde otra app, absolutos a donde viven", () => {
    expect(suiteAccountLinks("https://dev.tramites.flitsas.online/").map((l) => l.href)).toEqual([
      "https://dev.tramites.flitsas.online/manual",
      "https://dev.tramites.flitsas.online/profile/change-password",
    ]);
  });
});

describe("nombre del rol en la barra", () => {
  it("los de plataforma con su rótulo, aunque no sean el primero", () => {
    expect(suiteRoleLabel(["SuperAdmin"])).toBe("Super Admin");
    expect(suiteRoleLabel(["Radicador", "AdminCompany"])).toBe("Admin de Compañía");
    expect(suiteRoleLabel(["ot_admin"])).toBe("Admin OT");
  });

  it("cualquier otro tal cual, y sin roles «Usuario»", () => {
    expect(suiteRoleLabel(["Radicador"])).toBe("Radicador");
    expect(suiteRoleLabel([])).toBe("Usuario");
  });
});

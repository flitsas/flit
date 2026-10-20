import { describe, expect, it } from "vitest";
import { decideHome } from "../home";

// B-11 (HU #12987) — opción 4 «Hub con entrada directa».
const app = (code: string) => ({ code, name: code, icon: "box", url: `https://dev.${code}.flitsas.online`, current: false });

describe("decideHome", () => {
  it("con un solo producto entra directo a él", () => {
    expect(decideHome([app("plataforma"), app("tramites")], false)).toEqual({ kind: "direct", url: "https://dev.tramites.flitsas.online" });
  });

  it("con dos o más productos muestra el inicio con sus tarjetas", () => {
    const decision = decideHome([app("plataforma"), app("tramites"), app("comparendos")], false);
    expect(decision.kind).toBe("home");
    expect(decision.kind === "home" && decision.products.map((p) => p.code)).toEqual(["tramites", "comparendos"]);
  });

  it("sin productos muestra el inicio (administración)", () => {
    expect(decideHome([app("plataforma")], false).kind).toBe("home");
  });

  it("con ?inicio=1 (desde el menú de productos) nunca redirige", () => {
    expect(decideHome([app("plataforma"), app("tramites")], true).kind).toBe("home");
  });
});

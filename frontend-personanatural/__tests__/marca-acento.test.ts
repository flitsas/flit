// Marca blanca del punto: el color secundario es el acento de títulos y números de paso (solo si se lee sobre blanco), y los
// íconos de los 4 pasos toman el color primario. Solo valores #RRGGBB: nada del usuario entra en la hoja.
import { describe, expect, it } from "vitest";
import { cssDeMarca, type MarcaAplicable } from "@/lib/marca/marca";

const marca = (secundario: string): MarcaAplicable => ({
  nombre: "Tránsito X",
  colores: { primario: "#001682", secundario, sobrePrimario: "#FFFFFF" },
  logoUrl: null,
  bannerUrl: null,
  version: 1,
});

describe("cssDeMarca · acento e íconos de los pasos", () => {
  it("con un secundario legible lo usa en títulos, kicker y números de paso", () => {
    const css = cssDeMarca(marca("#CC4E0A"));
    expect(css).toContain("--paso-claro:#001682");
    expect(css).toMatch(/--paso-oscuro:#[0-9A-F]{6}/);
    expect(css).toContain(".dashboard-card .dashboard-title:not(.flit-gradient-title)");
    expect(css).toContain("color:#CC4E0A");
    expect(css).toContain(".paso-num{background:#CC4E0A");
    expect(css).toContain(".somos-card-num{color:#CC4E0A}");
    expect(css).toContain("--gradient-heading:linear-gradient(to right,#CC4E0A,#CC4E0A)");
  });

  it("con un secundario que no se lee sobre blanco deja los títulos como están", () => {
    const css = cssDeMarca(marca("#FFE98A"));
    expect(css).toContain("--paso-claro:#001682");
    expect(css).not.toContain(".paso-num");
    // Los números de «Somos» sí siguen la marca: con el primario cuando el secundario no se lee.
    expect(css).toContain(".somos-card-num{color:#001682}");
    expect(css).not.toContain("--gradient-heading");
  });

  it("sin colores válidos no genera nada", () => {
    expect(cssDeMarca({ ...marca("#CC4E0A"), colores: { primario: "rojo", secundario: "#CC4E0A", sobrePrimario: "#FFFFFF" } })).toBe("");
  });
});

describe("cssDeMarca · paleta base del diseño del OT", () => {
  it("el texto toma el primario, el secundario es el acento y los neutros son los del diseño", () => {
    const css = cssDeMarca(marca("#CC4E0A"));
    expect(css).toContain("--foreground:#001682");
    expect(css).toContain("--card-foreground:#001682");
    expect(css).toContain("--secondary:#CC4E0A");
    expect(css).toContain("--accent:#CC4E0A");
    expect(css).toContain("--secondary-foreground:#FFFFFF");
    expect(css).toContain("--border:#D7D7D7");
    expect(css).toContain("--background:#F8F9FA");
    expect(css).toContain("--muted-foreground:#62666C");
    expect(css).toContain("--shadow-float:3px 3px 3px color-mix(in oklab,#001682 30%,transparent)");
  });

  it("un primario claro no se usa tal cual como color de texto: se oscurece hasta leerse", () => {
    const css = cssDeMarca({ ...marca("#CC4E0A"), colores: { primario: "#6FA8FF", secundario: "#CC4E0A", sobrePrimario: "#000000" } });
    expect(css).not.toContain("--foreground:#6FA8FF");
    expect(css).toMatch(/--foreground:#[0-9A-F]{6}/);
    expect(css).toContain("--flit-blue:#6FA8FF");
  });

  it("un secundario claro lleva texto negro encima", () => {
    expect(cssDeMarca(marca("#FFE98A"))).toContain("--secondary-foreground:#000000");
  });
});

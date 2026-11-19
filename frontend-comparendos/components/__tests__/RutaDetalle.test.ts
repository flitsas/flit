import { describe, expect, it } from "vitest";
import { escapeVuelve, hayCapaAbierta, nombreOrigen, rutaDetalle, volverSeguro } from "@/components/detalle/ruta";

describe("ruta del detalle y «volver» (rediseño S1)", () => {
  it("arma la ruta con sección y origen", () => {
    expect(rutaDetalle("c1")).toBe("/comparendos/c1");
    expect(rutaDetalle("c1", { seccion: "dp", volver: "/bandeja?pagado=false&pagina=2" })).toBe(
      "/comparendos/c1?seccion=dp&volver=%2Fbandeja%3Fpagado%3Dfalse%26pagina%3D2",
    );
  });

  it("vuelve solo a rutas de la app; lo demás va a la bandeja en silencio", () => {
    expect(volverSeguro("/bandeja?rapido=Radar")).toBe("/bandeja?rapido=Radar");
    expect(volverSeguro("/ocr/paquetes/p1")).toBe("/ocr/paquetes/p1");
    for (const malo of [null, "", "bandeja", "//evil.test", "/\\evil.test", "https://evil.test", "javascript:alert(1)", "/javascript:x", "/a\nb", "/a\\b"]) {
      expect(volverSeguro(malo)).toBe("/bandeja");
    }
  });

  it("nombra la pantalla de origen", () => {
    expect(nombreOrigen("/bandeja?x=1")).toBe("Bandeja");
    expect(nombreOrigen("/mensajes")).toBe("Centro de mensajes");
    expect(nombreOrigen("/otra")).toBe("Volver");
  });

  it("Escape vuelve solo sin capa abierta al pulsarlo y sin estar escribiendo", () => {
    const div = document.createElement("div");
    const campo = document.createElement("textarea");
    expect(escapeVuelve({ key: "Escape", defaultPrevented: false, target: div }, false)).toBe(true);
    expect(escapeVuelve({ key: "Escape", defaultPrevented: false, target: div }, true)).toBe(false);
    expect(escapeVuelve({ key: "Escape", defaultPrevented: false, target: campo }, false)).toBe(false);
    expect(escapeVuelve({ key: "Escape", defaultPrevented: true, target: div }, false)).toBe(false);
    expect(escapeVuelve({ key: "Enter", defaultPrevented: false, target: div }, false)).toBe(false);
  });

  it("reconoce como capa los diálogos, menús, la cuenta, los productos y el grupo abierto del dock", () => {
    const capas = [
      '<div role="dialog"></div>',
      '<div role="alertdialog"></div>',
      '<ul role="menu"></ul>',
      '<div id="flit-suite-account" role="region"></div>',
      '<div id="flit-suite-products" role="region"></div>',
      '<nav aria-label="Navegación principal"><button aria-expanded="true"></button></nav>',
    ];
    for (const html of capas) {
      document.body.innerHTML = html;
      expect(hayCapaAbierta(document)).toBe(true);
    }
    // Una tarjeta plegable abierta del contenido o el dock cerrado no son capas.
    document.body.innerHTML = '<main><button aria-expanded="true"></button></main><nav aria-label="Navegación principal"><button aria-expanded="false"></button></nav>';
    expect(hayCapaAbierta(document)).toBe(false);
    document.body.innerHTML = "";
  });
});

import { describe, expect, it } from "vitest";
import { escapeVuelve, nombreOrigen, rutaDetalle, volverSeguro } from "@/components/detalle/ruta";

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

  it("Escape vuelve, salvo con un diálogo abierto o escribiendo", () => {
    const sinDialogo = { querySelector: () => null };
    const conDialogo = { querySelector: () => ({}) as Element };
    const div = document.createElement("div");
    const input = document.createElement("textarea");
    expect(escapeVuelve({ key: "Escape", defaultPrevented: false, target: div }, sinDialogo)).toBe(true);
    expect(escapeVuelve({ key: "Escape", defaultPrevented: false, target: div }, conDialogo)).toBe(false);
    expect(escapeVuelve({ key: "Escape", defaultPrevented: false, target: input }, sinDialogo)).toBe(false);
    expect(escapeVuelve({ key: "Escape", defaultPrevented: true, target: div }, sinDialogo)).toBe(false);
    expect(escapeVuelve({ key: "Enter", defaultPrevented: false, target: div }, sinDialogo)).toBe(false);
  });
});

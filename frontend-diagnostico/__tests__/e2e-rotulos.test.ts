import { readdirSync, readFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { textoEstadoCelda } from "@/components/tablero/CeldaSemaforo";

// Uso de ejemplo: los specs de Playwright (e2e/) no corren en Vitest; esta guarda lee su texto y avisa cuando un selector
// sigue con un rótulo que la pantalla ya no muestra (HU #14562, hallazgo B1 de la auditoría de UI: «Ejecutado (OK)»).

const DIR = resolve(__dirname, "../e2e");
const archivos = readdirSync(DIR).filter((n) => /\.ts$/.test(n));
const texto = (nombre: string) => readFileSync(join(DIR, nombre), "utf8");

describe("HU14562 B1 · los E2E usan los rótulos vigentes del semáforo y de los botones", () => {
  it("ningún spec busca el rótulo viejo «Ejecutado (OK)»", () => {
    expect(textoEstadoCelda("COMPLETADO")).toBe("Ejecutado");
    for (const archivo of archivos) {
      expect(texto(archivo), archivo).not.toMatch(/Ejecutado \\?\(OK\\?\)/);
    }
  });

  it("el semáforo del impuesto acepta Ejecutado, Con deuda y Novedad", () => {
    const spec = texto("impuesto.spec.ts");
    expect(spec).toContain("(Ejecutado|Con deuda|Novedad) \\(se abre en una pestaña nueva\\)$");
  });

  it("el helper del Express usa «Diagnosticar en línea» y acepta «Iniciar diagnóstico» además de «Continuar»", () => {
    const helper = texto("express.ts");
    expect(helper).toContain('{ name: "Diagnosticar en línea" }');
    expect(helper).toContain("/^(Continuar|Iniciar diagnóstico)$/");
  });
});

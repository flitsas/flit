import { existsSync } from "node:fs";
import path from "node:path";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { IconoAviso, avisoComo, type TipoAviso } from "../IconoAviso";

// Discos de aviso del PDF definitivo de íconos: cada tipo pinta su SVG del kit, que debe existir en las 4 apps.
const REPO_ROOT = path.resolve(__dirname, "..", "..", "..", "..");
const APPS = ["frontend", "frontend-hub", "frontend-comparendos", "frontend-diagnostico"];
const ARCHIVO: Record<TipoAviso, string> = {
  error: "aviso-error",
  alerta: "aviso-alerta",
  info: "aviso-info",
  ok: "aviso-ok",
  pendiente: "estado-pendiente",
};

describe("IconoAviso", () => {
  it.each(Object.entries(ARCHIVO))("%s pinta /assets/kit/%s.svg y el archivo existe en las 4 apps", (tipo, archivo) => {
    const html = renderToStaticMarkup(createElement(IconoAviso, { tipo: tipo as TipoAviso }));
    expect(html).toContain(`src="/assets/kit/${archivo}.svg"`);
    for (const app of APPS) expect(existsSync(path.join(REPO_ROOT, app, "public", "assets", "kit", `${archivo}.svg`))).toBe(true);
  });

  it("es decorativo sin etiqueta y lleva alt con etiqueta", () => {
    expect(renderToStaticMarkup(createElement(IconoAviso, { tipo: "error" }))).toMatch(/alt=""[^>]*aria-hidden="true"/);
    const conNombre = renderToStaticMarkup(createElement(IconoAviso, { tipo: "error", etiqueta: "Error" }));
    expect(conNombre).toContain('alt="Error"');
    expect(conNombre).not.toContain("aria-hidden");
  });

  it("avisoComo devuelve el mismo componente por tipo", () => {
    expect(avisoComo("ok")).toBe(avisoComo("ok"));
    expect(avisoComo("ok")).not.toBe(avisoComo("error"));
  });
});

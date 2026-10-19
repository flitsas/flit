import { describe, expect, it } from "vitest";
import { limpiarHtml, variablesUsadas } from "@/lib/editorHtml";

describe("limpiarHtml", () => {
  it("conserva las etiquetas permitidas y la alineación, sin otros atributos", () => {
    expect(limpiarHtml('<h2 class="x" style="text-align:center;color:red">Título</h2><p style="text-align:left">a <b>b</b></p>')).toBe(
      '<h2 style="text-align:center">Título</h2><p>a <b>b</b></p>',
    );
  });

  it("desenvuelve lo no permitido, traduce los spans de formato y quita scripts", () => {
    expect(limpiarHtml('<p><span>uno</span> <span style="font-weight:700">dos</span> <a href="x">tres</a></p><script>alert(1)</script>')).toBe(
      "<p>uno <b>dos</b> tres</p>",
    );
  });

  it("deja intactas las variables y las tablas", () => {
    expect(limpiarHtml('<table><tbody><tr><td style="text-align:right">{{conductor.nombre}}</td></tr></tbody></table>')).toBe(
      '<table><tbody><tr><td style="text-align:right">{{conductor.nombre}}</td></tr></tbody></table>',
    );
  });
});

describe("variablesUsadas", () => {
  it("lista cada variable una vez, sin espacios", () => {
    expect(variablesUsadas("Hola {{ conductor.nombre }}, placa {{comparendo.placa}} {{conductor.nombre}}")).toEqual(["conductor.nombre", "comparendo.placa"]);
  });

  it("de los bloques condicionales cuenta solo la variable que se evalúa", () => {
    expect(variablesUsadas('{{#si empresa.correo_respuesta}}Responde a {{empresa.correo_respuesta}}{{#sino}}No respondas.{{/si}}')).toEqual(["empresa.correo_respuesta"]);
    expect(variablesUsadas('{{#si comparendo.tipo == "FOTOMULTA"}}foto{{/si}} {{#si dp.estado != "x"}}{{/si}}')).toEqual(["comparendo.tipo", "dp.estado"]);
  });
});

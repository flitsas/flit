import { describe, expect, it } from "vitest";
import { AVISO_DEMOSTRACION, buscarTema, PREGUNTAS_SUGERIDAS, responder, SALUDO, SIN_INFORMACION, TEMAS } from "@/lib/chat/respuestas";
import { DOCUMENTOS_LEGALES, TEXTO_DOCUMENTO_NO_DISPONIBLE, TEXTO_POLITICAS } from "@/lib/legal";

const CONTEXTO = { tipos: ["Trámite A", "Trámite C"], organismos: ["Ciudad Uno", "Villa Dos"] };

describe("chat simulado: respuestas por tema", () => {
  it("el saludo es el del diseño", () => {
    expect(SALUDO).toBe("¡Hola! Soy DR. FLIT, tu asistente virtual. ¿En qué puedo ayudarte?");
  });

  it.each([
    ["¿Cómo inicio mi trámite?", "iniciar"],
    ["Que documentos necesito", "documentos"],
    ["¿Cuánto tarda mi trámite?", "tiempo"],
    ["quiero consultar mi radicado", "consultar"],
    ["¿Mis datos están seguros?", "seguridad"],
    ["¿Cómo es la validación de identidad?", "identidad"],
    ["¿Qué trámites puedo realizar?", "tramites"],
    ["¿En qué organismos puedo radicar?", "organismos"],
    ["CÓMO VA MI TRÁMITE", "consultar"],
  ])("«%s» cae en el tema %s", (texto, id) => {
    expect(buscarTema(texto)?.id).toBe(id);
  });

  it("cada pregunta ofrecida como botón llega a su propio tema", () => {
    for (const t of TEMAS) expect(buscarTema(t.pregunta)?.id).toBe(t.id);
    expect(PREGUNTAS_SUGERIDAS).toHaveLength(TEMAS.length);
  });

  it("lista los trámites y organismos que recibe, sin escribirlos a mano", () => {
    expect(responder("¿Qué trámites puedo realizar?", CONTEXTO).texto).toBe("Trámites habilitados: Trámite A, Trámite C.");
    expect(responder("organismos", CONTEXTO).texto).toBe("Organismos de tránsito habilitados: Ciudad Uno, Villa Dos.");
  });

  it("si las listas no cargaron, lo dice sin inventar; si están vacías, también", () => {
    expect(responder("¿Qué trámites puedo realizar?").texto).toMatch(/No pude cargar la lista de trámites/);
    expect(responder("organismos", { organismos: [] }).texto).toBe("Organismos de tránsito habilitados: por ahora no hay ninguno habilitado.");
  });

  it("sin tema responde que no tiene la información y no inventa un canal", () => {
    const r = responder("¿Quién ganó el partido de ayer?");
    expect(r.texto).toBe(SIN_INFORMACION);
    expect(r.texto).toBe("Por ahora no tengo esa información; escribe a soporte.");
    expect(r.texto).not.toMatch(/@|https?:|\d{4,}/);
    expect(r.sugerencias.length).toBeGreaterThan(0);
  });

  it("texto vacío no encaja en ningún tema", () => {
    expect(buscarTema("   ")).toBeNull();
  });

  it("es determinista y ofrece sugerencias relacionadas que existen como pregunta", () => {
    expect(responder("documentos", CONTEXTO)).toEqual(responder("documentos", CONTEXTO));
    for (const t of TEMAS) for (const s of responder(t.pregunta, CONTEXTO).sugerencias) expect(PREGUNTAS_SUGERIDAS).toContain(s);
  });

  it("ninguna respuesta promete pago, aprobación, plazos ni habla de tarifas o texto legal", () => {
    for (const t of TEMAS) {
      const texto = t.respuesta(CONTEXTO);
      expect(texto, t.id).not.toMatch(/\$|\bCOP\b|\d+\s*(a\s*\d+\s*)?(horas|días|dias)|garantiz|te aprobar|será aprobad|PSE|tarifa/i);
      expect(texto, t.id).not.toMatch(/\*\*|__|^#|`/m);
    }
    expect(responder("¿Cuánto tarda mi trámite?").texto).toMatch(/no puedo asegurarte un plazo ni una aprobación/);
  });

  it("avisa que es una demostración sin IA ni datos personales", () => {
    expect(AVISO_DEMOSTRACION).toMatch(/demostración/);
    expect(AVISO_DEMOSTRACION).toMatch(/no usa inteligencia artificial/);
    expect(AVISO_DEMOSTRACION).toMatch(/No escribas datos personales/);
  });
});

describe("documentos legales: un único módulo", () => {
  it("las políticas figuran como no disponibles y sin URL, listas para recibir la oficial", () => {
    expect(DOCUMENTOS_LEGALES.politicas).toEqual({ clave: "politicas", titulo: "Políticas de Privacidad y Términos de Uso", url: null, disponible: false });
    expect(TEXTO_POLITICAS).toBe(DOCUMENTOS_LEGALES.politicas.titulo);
    expect(TEXTO_DOCUMENTO_NO_DISPONIBLE).toBe("El documento oficial aún no está disponible.");
  });
});

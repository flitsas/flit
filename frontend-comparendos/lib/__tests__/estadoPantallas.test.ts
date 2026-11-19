import { describe, expect, it } from "vitest";
import { MENSAJES_POR_DEFECTO, mensajesAParams, mensajesDeParams, type EstadoMensajes } from "@/lib/estadoMensajes";
import { productividadAParams, productividadDeParams } from "@/lib/estadoProductividad";

describe("Centro de mensajes en la URL (rediseño S1)", () => {
  it("de fábrica deja la URL limpia", () => {
    expect(mensajesAParams(MENSAJES_POR_DEFECTO).toString()).toBe("");
    expect(mensajesDeParams(new URLSearchParams())).toEqual(MENSAJES_POR_DEFECTO);
  });

  it("ida y vuelta: filtros, página, tamaño, empresa y mensaje abierto", () => {
    const e: EstadoMensajes = {
      ...MENSAJES_POR_DEFECTO,
      empresa: "e1",
      tipo: "Aviso1",
      estado: "Fallido",
      canal: "Empresa",
      desde: "2026-10-01",
      hasta: "2026-10-31",
      comparendo: "0508800",
      placa: "ABC123",
      pagina: 3,
      tamano: 50,
      mensaje: "m1",
    };
    expect(mensajesDeParams(mensajesAParams(e))).toEqual(e);
  });

  it("alertas internas no llevan empresa, y lo inválido se ignora", () => {
    const internas = { ...MENSAJES_POR_DEFECTO, internas: true, empresa: "e1" };
    expect(mensajesAParams(internas).toString()).toBe("internas=true");
    expect(mensajesDeParams(new URLSearchParams("desde=ayer&pagina=-1&tamano=999&placa=abc123"))).toEqual({ ...MENSAJES_POR_DEFECTO, placa: "ABC123" });
  });

  it("respeta el ?mensaje= de siempre (enlace desde el timeline)", () => {
    expect(mensajesDeParams(new URLSearchParams("mensaje=m9")).mensaje).toBe("m9");
  });
});

describe("Productividad en la URL (rediseño S1)", () => {
  const mes = { desde: "2026-11-01", hasta: "2026-11-19", empresa: null };

  it("el mes en curso sin cifra abierta deja la URL limpia", () => {
    expect(productividadAParams(mes, null, mes).toString()).toBe("");
    expect(productividadDeParams(new URLSearchParams(), mes)).toEqual({ periodo: mes, cifra: null });
  });

  it("ida y vuelta: periodo, empresa y la cifra abierta", () => {
    const periodo = { desde: "2026-10-01", hasta: "2026-10-31", empresa: "e1" };
    const cifra = { usuarioId: "u1", metrica: "pagos" as const };
    expect(productividadDeParams(productividadAParams(periodo, cifra, mes), mes)).toEqual({ periodo, cifra });
  });

  it("un periodo al revés o una métrica desconocida no rompen la pantalla", () => {
    expect(productividadDeParams(new URLSearchParams("desde=2026-10-31&hasta=2026-10-01&usuario=u1&metrica=inventada"), mes)).toEqual({ periodo: mes, cifra: null });
  });
});

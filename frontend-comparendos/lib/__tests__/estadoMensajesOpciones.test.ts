import { describe, expect, it } from "vitest";
import { mensajesAParams, mensajesDeParams, opcionesEstadoMensajes } from "@/lib/estadoMensajes";

// R4: «Correos fallidos» del Inicio abre /mensajes?estado=Fallido,Rebotado. La lista ya salía filtrada (el backend
// parte por comas), pero el <select> de Estado mostraba «Todos» porque no tenía esa opción.
describe("opciones del filtro de Estado del Centro de mensajes", () => {
  const valores = (actual: string) => opcionesEstadoMensajes(actual).map((o) => o.valor);

  it("trae los estados sueltos y «Fallidos o rebotados»", () => {
    const o = opcionesEstadoMensajes("");
    expect(o).toContainEqual({ valor: "Fallido", texto: "Fallido" });
    expect(o).toContainEqual({ valor: "Fallido,Rebotado", texto: "Fallidos o rebotados" });
  });

  it("el estado del enlace del Inicio llega intacto desde la URL y tiene su opción", () => {
    const { estado } = mensajesDeParams(new URLSearchParams("estado=Fallido,Rebotado&desde=2026-11-12"));
    expect(estado).toBe("Fallido,Rebotado");
    expect(valores(estado)).toContain(estado);
    expect(mensajesAParams({ ...mensajesDeParams(new URLSearchParams()), estado }).get("estado")).toBe("Fallido,Rebotado");
  });

  it("una combinación sin nombre propio igual se muestra, nombrada por sus partes", () => {
    const o = opcionesEstadoMensajes("Pendiente,Enviado");
    expect(o.at(-1)).toEqual({ valor: "Pendiente,Enviado", texto: "Pendiente o Enviado" });
    expect(opcionesEstadoMensajes("Fallido")).toHaveLength(opcionesEstadoMensajes("").length);
  });
});

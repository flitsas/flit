import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { AddressField, TEXTO_ENTREGA } from "@/components/tramite/AddressField";
import { crearCliente } from "@/lib/api/cliente";
import { ApiError } from "@/lib/api/errores";
import { json } from "./utilidades";

describe("AddressField con ayuda de entrega (F3 AC4)", () => {
  it("el ícono de 44 px abre y cierra la ayuda con clic y teclado, con aria-expanded", () => {
    render(<AddressField id="d" label="Dirección" value="" onChange={() => undefined} entrega />);
    const info = screen.getByRole("button", { name: "Información sobre la dirección de entrega" });

    expect(info).toHaveClass("h-11", "w-11");
    expect(info).toHaveAttribute("aria-expanded", "false");
    fireEvent.click(info);
    expect(info).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("tooltip")).toHaveTextContent(TEXTO_ENTREGA);
    expect(TEXTO_ENTREGA).toBe("Esta dirección será la dirección de envío por defecto de la tarjeta de propiedad.");
    fireEvent.keyDown(info, { key: "Escape" });
    expect(screen.queryByRole("tooltip")).toBeNull();
    fireEvent.focus(info);
    fireEvent.click(info);
    fireEvent.blur(info);
    expect(info).toHaveAttribute("aria-expanded", "false");
  });

  it("sin la marca de entrega no hay ícono y el error se asocia al campo", () => {
    render(<AddressField id="d" label="Dirección" value="x" onChange={() => undefined} error="La dirección es demasiado larga" />);

    expect(screen.queryByRole("button")).toBeNull();
    expect(screen.getByLabelText("Dirección")).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByRole("alert")).toHaveTextContent("La dirección es demasiado larga");
  });
});

describe("cliente: identidad y adjuntos", () => {
  it("pide, reenvía y sondea la identidad por rol con la posición de la persona", async () => {
    const fetchImpl = vi.fn(async () => json({ parte: "comprador", ordinal: 2, estado: "pendiente" }));
    const c = crearCliente({ fetchImpl });

    await c.identidad.solicitar("s1", "comprador", 2);
    await c.identidad.reenviar("s1", "comprador", 2);
    await c.identidad.estado("s1");

    const llamadas = fetchImpl.mock.calls as unknown as [string, RequestInit][];
    expect(llamadas.map(([u, i]) => `${i.method} ${u}`)).toEqual([
      "POST /api/v1/persona-natural/solicitudes/s1/identidad/comprador",
      "POST /api/v1/persona-natural/solicitudes/s1/identidad/comprador/reenviar",
      "GET /api/v1/persona-natural/solicitudes/s1/identidad",
    ]);
    expect(llamadas[0]?.[1].body).toBe(JSON.stringify({ ordinal: 2 }));
  });

  it("sube un archivo como multipart (`archivo` y `tipo`) sin fijar el content-type y lo elimina por id", async () => {
    const fetchImpl = vi.fn(async (_u: string, init: RequestInit) => (init.method === "DELETE" ? new Response(null, { status: 204 }) : json({ id: "a1" }, 201)));
    const c = crearCliente({ fetchImpl: fetchImpl as unknown as typeof fetch });
    const archivo = new File(["%PDF-1.4"], "paz y salvo.pdf", { type: "application/pdf" });

    await c.adjuntos.subir("s1", "paz_salvo", archivo);
    await c.adjuntos.eliminar("s1", "a/1");

    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("/api/v1/persona-natural/solicitudes/s1/adjuntos");
    expect(init.body).toBeInstanceOf(FormData);
    expect((init.body as FormData).get("tipo")).toBe("paz_salvo");
    expect(((init.body as FormData).get("archivo") as File).name).toBe("paz y salvo.pdf");
    expect(Object.keys(init.headers as Record<string, string>)).not.toContain("content-type");
    const [urlBorrado, borrado] = fetchImpl.mock.calls[1] as unknown as [string, RequestInit];
    expect(`${borrado.method} ${urlBorrado}`).toBe("DELETE /api/v1/persona-natural/solicitudes/s1/adjuntos/a%2F1");
  });

  it("un 422 conserva los errores por campo y el estado del conflicto", async () => {
    const fetchImpl = async () =>
      new Response(JSON.stringify({ code: "datos_invalidos", title: "t", errors: { "actores[0].correo": ["El correo no es válido."], malo: "x" }, state: "pendiente" }), {
        status: 422,
        headers: { "content-type": "application/problem+json" },
      });

    const error = await crearCliente({ fetchImpl }).solicitudes.guardarActores("s1", { version: 1, actores: [] }).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ code: "datos_invalidos", errores: { "actores[0].correo": ["El correo no es válido."] }, estado: "pendiente" });
  });
});

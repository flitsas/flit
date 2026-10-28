import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { DrFlit } from "@/components/chat/DrFlit";
import { Landing } from "@/components/landing/Landing";
import { crearCliente, type ConfiguracionPortal } from "@/lib/api/cliente";
import { textoSinInformacion } from "@/lib/chat/respuestas";
import { resolverContenido, textoPlano } from "@/lib/configuracion";
import { PREGUNTAS_FRECUENTES } from "@/lib/landing/contenido";
import { urlPublicable } from "@/lib/legal";
import { fetchFalso, json, ORGANISMOS, renderConQuery, TIPOS } from "./utilidades";

const IMG = "/api/v1/persona-natural/configuracion/banners/11111111-1111-1111-1111-111111111111/imagen?v=abc123";

function publicada(parcial: Partial<ConfiguracionPortal> = {}): ConfiguracionPortal {
  return {
    version: "7",
    publicadoEn: "2026-10-27T10:00:00Z",
    preguntas: [
      { id: "p2", pregunta: "¿Segunda publicada?", respuesta: "Respuesta dos", orden: 2 },
      { id: "p1", pregunta: "¿Primera publicada?", respuesta: "Respuesta uno", orden: 1 },
    ],
    banners: [{ id: "b1", titulo: "Aviso", textoAlternativo: "Banner publicado de prueba", enlaceUrl: null, orden: 1, imagenUrl: IMG }],
    enlaces: [
      { codigo: "POLITICA_DATOS", url: "https://flit.example/politica", conDocumento: true },
      { codigo: "TERMINOS", url: null, conDocumento: false },
      { codigo: "VIDEO", url: "https://video.example/v", conDocumento: true },
      { codigo: "CONTACTO_VENTAS", url: "https://flit.example/ventas", conDocumento: true },
    ],
    soporte: { correo: "ayuda@flit.example", telefono: "+57 601 555 0100", horario: "L-V 8:00 a 17:00" },
    borradorVigenciaDias: 30,
    ...parcial,
  };
}

const base = { "/organismos": () => json(ORGANISMOS), "/tipos-tramite": () => json(TIPOS) };
const conConfig = (c: () => Response | Promise<Response>) => fetchFalso({ ...base, "/configuracion": c });

function esFija() {
  expect(screen.getByRole("button", { name: PREGUNTAS_FRECUENTES[0].pregunta })).toBeInTheDocument();
  expect(screen.getAllByRole("button", { name: /Ir al banner/ })).toHaveLength(2);
  expect(screen.getByRole("button", { name: "Políticas de Privacidad y Términos de Uso" })).toBeInTheDocument();
  expect(screen.queryByLabelText("Soporte")).not.toBeInTheDocument();
}

describe("landing: configuración publicada (HU #14073)", () => {
  it("cargando: ya muestra el contenido fijo", () => {
    const { cliente } = conConfig(() => new Promise<Response>(() => {}));
    renderConQuery(<Landing cliente={cliente} />);
    esFija();
  });

  it("versión «0»: conserva el contenido fijo", async () => {
    const { cliente, llamadas } = conConfig(() => json({ version: "0", preguntas: [], banners: [], enlaces: [], soporte: {} }));
    renderConQuery(<Landing cliente={cliente} />);
    await waitFor(() => expect(llamadas).toContain("/configuracion"));
    await screen.findByText("Villa Dos");
    esFija();
  });

  it.each([503, 500, 404])("error %i: contenido fijo, sin mensaje de error", async (estado) => {
    const { cliente, llamadas } = conConfig(() => json({ code: "no_disponible", title: "No disponible" }, estado));
    renderConQuery(<Landing cliente={cliente} />);
    await waitFor(() => expect(llamadas).toContain("/configuracion"));
    await screen.findByText("Villa Dos");
    esFija();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("falla de red: contenido fijo", async () => {
    const { cliente } = conConfig(() => Promise.reject(new TypeError("red")));
    renderConQuery(<Landing cliente={cliente} />);
    await screen.findByText("Villa Dos");
    esFija();
  });

  it("versión publicada: preguntas, banners, enlaces y soporte reemplazan a lo fijo", async () => {
    const { cliente } = conConfig(() => json(publicada()));
    renderConQuery(<Landing cliente={cliente} />);

    const primera = await screen.findByRole("button", { name: "¿Primera publicada?" });
    const faq = screen.getByRole("region", { name: "Preguntas Frecuentes" });
    expect(within(faq).getAllByRole("button").map((b) => b.textContent)).toEqual(["¿Primera publicada?", "¿Segunda publicada?"]);
    expect(screen.queryByRole("button", { name: PREGUNTAS_FRECUENTES[0].pregunta })).not.toBeInTheDocument();
    fireEvent.click(primera);
    expect(screen.getByRole("region", { name: "¿Primera publicada?" })).toHaveTextContent("Respuesta uno");

    // Banner publicado: imagen del BFF con su texto alternativo, sin los locales.
    const img = screen.getByRole("img", { name: "Banner publicado de prueba", hidden: true });
    expect(img.getAttribute("src")).toContain("/configuracion/banners/11111111-1111-1111-1111-111111111111/imagen");
    expect(screen.queryByRole("button", { name: /Ir al banner/ })).not.toBeInTheDocument(); // uno solo: sin puntos

    // Enlace legal con documento: pestaña nueva con noopener.
    const politica = screen.getByRole("link", { name: /Política de tratamiento de datos personales/ });
    expect(politica).toHaveAttribute("href", "https://flit.example/politica");
    expect(politica).toHaveAttribute("target", "_blank");
    expect(politica).toHaveAttribute("rel", "noopener noreferrer");
    expect(screen.queryByRole("button", { name: "Políticas de Privacidad y Términos de Uso" })).not.toBeInTheDocument();

    expect(screen.getByRole("link", { name: /Ver video/ })).toHaveAttribute("href", "https://video.example/v");

    const soporte = screen.getByLabelText("Soporte");
    expect(within(soporte).getByRole("link", { name: "ayuda@flit.example" })).toHaveAttribute("href", "mailto:ayuda@flit.example");
    expect(within(soporte).getByRole("link", { name: "+57 601 555 0100" })).toHaveAttribute("href", "tel:+576015550100");
    expect(soporte).toHaveTextContent("L-V 8:00 a 17:00");
  });

  it("Dr. FLIT incluye el canal publicado en su respuesta por defecto", async () => {
    vi.useFakeTimers();
    try {
      render(<DrFlit contexto={{ soporte: resolverContenido(publicada()).soporte }} />);
      fireEvent.click(screen.getByRole("button", { name: /Abrir Dr. FLIT/ }));
      fireEvent.change(screen.getByLabelText("Mensaje para DR. FLIT"), { target: { value: "hablemos de futbol" } });
      fireEvent.click(screen.getByRole("button", { name: "Enviar mensaje" }));
      act(() => { vi.advanceTimersByTime(700); });
      expect(screen.getByRole("log")).toHaveTextContent("escribe a ayuda@flit.example o llama al +57 601 555 0100 (L-V 8:00 a 17:00)");
    } finally {
      vi.useRealTimers();
    }
  });

  it("texto con HTML se muestra como texto, no se interpreta", async () => {
    const html = '<img src=x onerror="alert(1)"><b>negrita</b>';
    const { cliente } = conConfig(() =>
      json(publicada({ preguntas: [{ id: "p", pregunta: "¿Con html?", respuesta: html, orden: 1 }], soporte: { horario: "<i>24/7</i>" } })),
    );
    const { container } = renderConQuery(<Landing cliente={cliente} />);
    fireEvent.click(await screen.findByRole("button", { name: "¿Con html?" }));

    expect(screen.getByRole("region", { name: "¿Con html?" })).toHaveTextContent(html);
    expect(container.querySelector("#faq b, #faq img[onerror]")).toBeNull();
    expect(screen.getByLabelText("Soporte")).toHaveTextContent("<i>24/7</i>");
  });

  it("URL javascript:, ftp: o data: se rechaza y el pie conserva el aviso de documento no disponible", async () => {
    const { cliente } = conConfig(() =>
      json(
        publicada({
          enlaces: [
            { codigo: "POLITICA_DATOS", url: "javascript:alert(1)", conDocumento: true },
            { codigo: "TERMINOS", url: "ftp://flit.example/t.pdf", conDocumento: true },
            { codigo: "VIDEO", url: "data:text/html,x", conDocumento: true },
          ],
        }),
      ),
    );
    renderConQuery(<Landing cliente={cliente} />);
    await screen.findByRole("button", { name: "¿Primera publicada?" });

    expect(screen.queryByRole("link", { name: /Política de tratamiento/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Ver video/ })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Políticas de Privacidad y Términos de Uso" }));
    expect(await screen.findByText("El documento oficial aún no está disponible.")).toBeInTheDocument();
  });

  it("la petición es un GET anónimo: sin cuerpo ni datos del ciudadano", async () => {
    const pedidos: { url: string; init?: RequestInit }[] = [];
    const fetchImpl = (async (url: string, init?: RequestInit) => {
      pedidos.push({ url: String(url), init });
      return String(url).endsWith("/configuracion") ? json(publicada()) : json(ORGANISMOS);
    }) as unknown as typeof fetch;
    renderConQuery(<Landing cliente={crearCliente({ fetchImpl })} />);
    await screen.findByRole("button", { name: "¿Primera publicada?" });

    const p = pedidos.find((x) => x.url.endsWith("/configuracion"))!;
    expect(p.url).toBe("/api/v1/persona-natural/configuracion");
    expect(p.init?.method).toBe("GET");
    expect(p.init?.body).toBeUndefined();
    expect(Object.keys(p.init?.headers ?? {}).map((h) => h.toLowerCase())).toEqual(["accept"]);
  });
});

describe("resolverContenido", () => {
  it("descarta banners con imagen fuera del BFF, sin texto alternativo o con enlace inválido; conserva el resto", () => {
    const c = resolverContenido(
      publicada({
        banners: [
          { id: "x", titulo: "t", textoAlternativo: "alt", orden: 1, imagenUrl: "https://evil.example/a.png" },
          { id: "y", titulo: "t", textoAlternativo: "  ", orden: 2, imagenUrl: IMG },
          { id: "z", titulo: "t", textoAlternativo: "alt ok", orden: 3, imagenUrl: IMG, enlaceUrl: "javascript:1" },
        ],
      }),
    );
    expect(c.banners).toHaveLength(1);
    expect(c.banners?.[0]).toMatchObject({ id: "z", alt: "alt ok", enlace: null });
  });

  it("soporte con correo o teléfono mal formados no se publica; sin nada válido es null", () => {
    expect(resolverContenido(publicada({ soporte: { correo: "no es correo", telefono: "abc" } })).soporte).toBeNull();
    expect(resolverContenido(publicada({ soporte: { correo: "a@b.co" } })).soporte).toEqual({ correo: "a@b.co", telefono: null, horario: null });
  });

  it("estructura inesperada no rompe: queda el contenido fijo", () => {
    const raro = { version: "3", preguntas: "x", banners: null, enlaces: 5, soporte: null } as unknown as ConfiguracionPortal;
    expect(resolverContenido(raro)).toEqual({ preguntas: null, banners: null, enlaces: {}, soporte: null });
    expect(resolverContenido(undefined).preguntas).toBeNull();
  });

  it("textoPlano quita caracteres de control y acota el largo", () => {
    expect(textoPlano("a\u0000b\u0007c")).toBe("abc");
    expect(textoPlano("x".repeat(50), 10)).toHaveLength(10);
    expect(textoPlano(42)).toBe("");
  });
});

describe("urlPublicable", () => {
  it.each(["https://flit.example/a", "http://flit.example/a?b=1"])("acepta %s", (u) => expect(urlPublicable(u)).not.toBeNull());
  it.each(["javascript:alert(1)", "ftp://x.example/a", "data:text/html,x", "//x.example", "/relativa", "https://u:p@x.example", "https://x .example", "", null, 5, "  JAVASCRIPT:1"])(
    "rechaza %s",
    (u) => expect(urlPublicable(u)).toBeNull(),
  );
});

describe("textoSinInformacion", () => {
  it("sin soporte conserva el texto de siempre; con soporte nombra los canales", () => {
    expect(textoSinInformacion()).toBe("Por ahora no tengo esa información; escribe a soporte.");
    expect(textoSinInformacion({ soporte: { correo: "a@b.co" } })).toBe("Por ahora no tengo esa información; escribe a a@b.co.");
    expect(textoSinInformacion({ soporte: { horario: "L-V" } })).toBe("Por ahora no tengo esa información; escribe a soporte.");
  });
});

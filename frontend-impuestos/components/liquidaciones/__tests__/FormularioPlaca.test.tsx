import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { FormularioPlaca } from "@/components/liquidaciones/FormularioPlaca";
import { errorRegla, fetchPorRuta, item, lote } from "./fixtures";

// Uso de ejemplo: <FormularioPlaca empresa={empresa} bloqueo={null} esSuperAdmin /> en /liquidaciones y
// /liquidaciones/nueva. Pide solo la placa (F2a-CF1) y hace POST /liquidaciones/individual; el Super Admin liquida para
// la compañía de ?empresa (contrato 1.1.0, delta 2) y, sin ella, ve «Elige una compañía.» con el botón deshabilitado.
// Los errores de compañía y de regla van en línea con el literal del contrato; nunca a la 403.

const push = vi.fn();
const replace = vi.fn();
vi.mock("next/navigation", () => ({
  usePathname: () => "/liquidaciones/nueva",
  useRouter: () => ({ replace, push, refresh: vi.fn() }),
  useSearchParams: () => new URLSearchParams(),
}));

const CREADA = () =>
  Response.json({ ...lote({ id: "L-1", tipo: "INDIVIDUAL", totalItems: 1, itemId: "I-1" }), items: [item({ id: "I-1", liquidacionId: "L-1" })] }, { status: 201 });

function montar(props: Partial<Parameters<typeof FormularioPlaca>[0]> = {}) {
  return render(<FormularioPlaca empresa={null} bloqueo={null} {...props} />);
}

function liquidar(placa = "ABC123") {
  fireEvent.change(screen.getByRole("textbox", { name: "Placa" }), { target: { value: placa } });
  fireEvent.click(screen.getByRole("button", { name: "Liquidar placa" }));
}

beforeEach(() => {
  push.mockReset();
  replace.mockReset();
});
afterEach(() => vi.unstubAllGlobals());

describe("FormularioPlaca — Super Admin y ?empresa (delta 2)", () => {
  it("el Super Admin sin compañía ve el literal «Elige una compañía.» y el botón deshabilitado, asociado al motivo", () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    montar({ esSuperAdmin: true, empresa: null });

    const boton = screen.getByRole("button", { name: "Liquidar placa" });
    expect(boton).toBeDisabled();
    const motivo = screen.getByText("Elige una compañía.");
    expect(boton.getAttribute("aria-describedby")).toContain(motivo.id);
    fireEvent.submit(screen.getByRole("form", { name: "Liquidar placa" }));
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("el Super Admin con compañía envía ?empresa en el POST y navega al detalle de la placa conservándola", async () => {
    const fetchMock = fetchPorRuta([[/^POST \/api\/v1\/impuestos\/liquidaciones\/individual\?empresa=e-9$/, CREADA]]);
    vi.stubGlobal("fetch", fetchMock);
    montar({ esSuperAdmin: true, empresa: "e-9" });

    liquidar(" abc-123 ");

    await waitFor(() => expect(push).toHaveBeenCalledWith("/liquidaciones/L-1/items/I-1?empresa=e-9"));
    expect(JSON.parse(String(fetchMock.mock.calls[0][1]?.body))).toEqual({ placa: "ABC123" });
  });

  it("quien no es Super Admin no envía ?empresa", async () => {
    const fetchMock = fetchPorRuta([[/^POST \/api\/v1\/impuestos\/liquidaciones\/individual$/, CREADA]]);
    vi.stubGlobal("fetch", fetchMock);
    montar({ esSuperAdmin: false, empresa: null });
    liquidar();
    await waitFor(() => expect(push).toHaveBeenCalledWith("/liquidaciones/L-1/items/I-1"));
  });

  it.each([
    [400, "EMPRESA_REQUERIDA", "Elige una compañía."],
    [404, "EMPRESA_NO_ENCONTRADA", "La compañía no existe o no tiene Impuestos."],
    [403, "EMPRESA_SOLO_SUPER_ADMIN", "Solo el Super Admin puede elegir la compañía."],
    [403, "EMPRESA_NO_PERMITIDA", "Solo el Super Admin puede elegir la compañía."],
  ])("un %i %s va en línea con el literal del contrato, sin ir a la 403 ni perder lo digitado", async (status, code, literal) => {
    vi.stubGlobal("fetch", fetchPorRuta([[/^POST .*individual/, () => errorRegla(status, code, "detalle interno")]]));
    montar({ esSuperAdmin: true, empresa: "e-9" });
    liquidar();

    expect(await screen.findByText(literal)).toBeInTheDocument();
    expect(screen.queryByText("No se pudo crear la liquidación")).toBeNull();
    expect(document.body).not.toHaveTextContent("detalle interno");
    expect(replace).not.toHaveBeenCalled();
    expect(push).not.toHaveBeenCalled();
    expect(screen.getByRole("textbox", { name: "Placa" })).toHaveValue("ABC123");
  });
});

describe("FormularioPlaca — errores del contrato de POST /liquidaciones/individual", () => {
  it("422 PLACA_INVALIDA (servidor): el literal junto al campo, con aria-invalid y aria-describedby", async () => {
    vi.stubGlobal("fetch", fetchPorRuta([[/^POST .*individual/, () => errorRegla(422, "PLACA_INVALIDA", "x", { campo: "placa" })]]));
    montar();
    liquidar("ABCD12345");

    const texto = await screen.findByText("Escribe una placa válida: de 4 a 10 letras o números.");
    const campo = screen.getByRole("textbox", { name: "Placa" });
    expect(campo).toHaveAttribute("aria-invalid", "true");
    expect(campo.getAttribute("aria-describedby")).toContain(texto.id);
    expect(campo).toHaveValue("ABCD12345");
  });

  it.each([
    ["APROVISIONAMIENTO_NO_LISTO", "La compañía todavía no puede liquidar porque su configuración con el proveedor no está lista."],
    ["PRODUCTO_SUSPENDIDO", "Impuestos está suspendido para la compañía: solo lectura."],
  ])("409 %s: aviso warning con el literal del contrato", async (code, literal) => {
    vi.stubGlobal("fetch", fetchPorRuta([[/^POST .*individual/, () => errorRegla(409, code, "x")]]));
    montar();
    liquidar();
    expect(await screen.findByText(literal)).toBeInTheDocument();
    expect(screen.getByText("No se pudo liquidar la placa")).toBeInTheDocument();
  });

  it("otro error: «No se pudo crear la liquidación» + Reintentar, que vuelve a enviar la misma placa", async () => {
    let n = 0;
    const fetchMock = fetchPorRuta([[/^POST .*individual/, () => (++n === 1 ? new Response("", { status: 500 }) : CREADA())]]);
    vi.stubGlobal("fetch", fetchMock);
    montar();
    liquidar();

    expect(await screen.findByText("No se pudo crear la liquidación")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    await waitFor(() => expect(push).toHaveBeenCalledWith("/liquidaciones/L-1/items/I-1"));
    expect(JSON.parse(String(fetchMock.mock.calls[1][1]?.body))).toEqual({ placa: "ABC123" });
  });
});

// HU #14460 — Cobros del día + sesión y lógica de la caja (HU #14458: usuario, cierre de sesión, organismo propio).
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CobrosDelDia } from "@/components/caja/CobrosDelDia";
import { paginasVisibles } from "@/components/caja/PaginacionCaja";
import { PortalCaja } from "@/components/caja/PortalCaja";
import { CajaError } from "@/lib/caja/cliente";
import { esperaLegible, mensajeDeCaja } from "@/lib/caja/mensajes";
import { sesionDeCajaVencida } from "@/lib/caja/sesion";
import { routerPush } from "./router";

const irAlLogin = vi.hoisted(() => vi.fn());
const cerrarSesion = vi.hoisted(() => vi.fn());
vi.mock("@/lib/caja/navegacion", () => ({ irAlLogin, cerrarSesion }));

const json = (cuerpo: unknown, status = 200) => new Response(JSON.stringify(cuerpo), { status, headers: { "content-type": "application/json" } });
const item = (codigo: string, extra: object = {}) => ({ codigo, valorCop: 185000, medio: "efectivo", hora: "09:30", usuario: "Ana Caja", estado: "pagado", ...extra });

const SESION = { usuario: { id: "u1", nombre: "Ana Caja" }, organismo: { id: "o1", codigo: "5631000", nombre: "Tránsito de Prueba" }, expiraEn: "2026-11-11T00:00:00Z" };

let fetchMock: ReturnType<typeof vi.fn>;

function montar(ui: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

beforeEach(() => {
  irAlLogin.mockClear();
  fetchMock = vi.fn();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => vi.unstubAllGlobals());

describe("Caja OT · cobros del día", () => {
  it("AC: lista los cobros con ID, valor, medio, hora y usuario, y el total del día", async () => {
    fetchMock.mockResolvedValue(json({ fecha: "2026-11-10", totalDiaCop: 370000, items: [item("PN-2026-000001"), item("PN-2026-000002", { medio: "otro", medioDetalle: "Nequi", hora: "10:05" })], total: 2, pagina: 1, tamano: 10 }));
    montar(<CobrosDelDia />);

    expect(await screen.findByText("PN-2026-000001")).toBeInTheDocument();
    expect(screen.getByText("Otro (Nequi)")).toBeInTheDocument();
    expect(screen.getByText("10:05")).toBeInTheDocument();
    expect(screen.getAllByText("Ana Caja")).toHaveLength(2);
    expect(screen.getByTestId("total-dia")).toHaveTextContent("$ 370.000,00 COP");
    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/persona-natural/caja/cobros/hoy?pagina=1&tamano=10");
  });

  it("AC: un cobro anulado se marca y su valor aparece tachado", async () => {
    fetchMock.mockResolvedValue(json({ fecha: "2026-11-10", totalDiaCop: 0, items: [item("PN-2026-000003", { estado: "anulado" })], total: 1, pagina: 1, tamano: 10 }));
    montar(<CobrosDelDia />);

    expect(await screen.findByText("Anulado")).toBeInTheDocument();
    expect(screen.getByText("PN-2026-000003")).toHaveClass("line-through");
  });

  it("AC: sin cobros muestra «Hoy no hay cobros registrados.»", async () => {
    fetchMock.mockResolvedValue(json({ fecha: "2026-11-10", totalDiaCop: 0, items: [], total: 0, pagina: 1, tamano: 10 }));
    montar(<CobrosDelDia />);
    expect(await screen.findByText("Hoy no hay cobros registrados.")).toBeInTheDocument();
  });

  it("AC: cargando muestra el cargador y el error permite reintentar", async () => {
    fetchMock.mockResolvedValueOnce(json({ code: "x", message: "x" }, 500));
    montar(<CobrosDelDia />);
    expect(screen.getByText("Cargando los cobros del día…")).toBeInTheDocument();

    expect(await screen.findByRole("alert")).toHaveTextContent("No pudimos completar la acción");
    fetchMock.mockResolvedValueOnce(json({ fecha: "2026-11-10", totalDiaCop: 0, items: [], total: 0, pagina: 1, tamano: 10 }));
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByText("Hoy no hay cobros registrados.")).toBeInTheDocument();
  });

  it("AC: «Filas por página» ofrece 10/20/30/50, vuelve a la página 1", async () => {
    fetchMock.mockImplementation(async () => json({ fecha: "2026-11-10", totalDiaCop: 1, items: [item("PN-2026-000001")], total: 45, pagina: 1, tamano: 10 }));
    montar(<CobrosDelDia />);
    await screen.findByText("PN-2026-000001");

    const selector = screen.getByLabelText("Filas por página") as HTMLSelectElement;
    expect([...selector.options].map((o) => o.value)).toEqual(["10", "20", "30", "50"]);
    expect(screen.getByText("Mostrando 1–10 de 45")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Página 2" }));
    await waitFor(() => expect(fetchMock.mock.calls.at(-1)?.[0]).toContain("pagina=2&tamano=10"));
    fireEvent.change(selector, { target: { value: "30" } });
    await waitFor(() => expect(fetchMock.mock.calls.at(-1)?.[0]).toContain("pagina=1&tamano=30"));
  });

  it("paginasVisibles deja primera, última y vecindad con saltos", () => {
    expect(paginasVisibles(1, 3)).toEqual([1, 2, 3]);
    expect(paginasVisibles(5, 10)).toEqual([1, null, 4, 5, 6, null, 10]);
  });
});

describe("Caja OT · sesión y cabecera", () => {
  it("AC: muestra el usuario, el organismo y «Cerrar sesión» (POST, no enlace), sin menú de la web", async () => {
    fetchMock.mockResolvedValue(json(SESION));
    montar(<PortalCaja codigo="5631000" />);

    expect(await screen.findByTestId("usuario-caja")).toHaveTextContent("Ana Caja");
    expect(screen.queryByRole("link", { name: "Cerrar sesión" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Cerrar sesión" }));
    expect(cerrarSesion).toHaveBeenCalledTimes(1);
    for (const prohibido of ["Iniciar", "Consultar", "Cotizar"]) expect(screen.queryByRole("link", { name: prohibido })).not.toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Cobrar" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "Cobros del día" })).toBeInTheDocument();
  });

  it("AC: si entró por la URL de otro organismo lo lleva al suyo", async () => {
    fetchMock.mockResolvedValue(json(SESION));
    montar(<PortalCaja codigo="9999999" />);
    await waitFor(() => expect(routerPush).toHaveBeenCalledWith("/o/5631000/caja"));
    expect(screen.queryByRole("tab", { name: "Cobrar" })).not.toBeInTheDocument();
  });

  it("AC: sin sesión (401) lleva al login", async () => {
    fetchMock.mockResolvedValue(json({ code: "sin_sesion", message: "x" }, 401));
    montar(<PortalCaja codigo="5631000" />);
    await waitFor(() => expect(irAlLogin).toHaveBeenCalled());
  });

  it("AC: sin permiso muestra el motivo y permite reintentar", async () => {
    fetchMock.mockResolvedValue(json({ code: "sin_permiso", message: "x" }, 403));
    montar(<PortalCaja codigo="5631000" />);
    expect(await screen.findByText("Su usuario no puede cobrar en este organismo.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reintentar" })).toBeInTheDocument();
  });

  it("cambiar de sección no pierde la búsqueda en curso", async () => {
    fetchMock.mockImplementation(async (url: string) => (url.includes("/sesion") ? json(SESION) : json({ fecha: "2026-11-10", totalDiaCop: 0, items: [], total: 0, pagina: 1, tamano: 10 })));
    montar(<PortalCaja codigo="5631000" />);
    const campo = (await screen.findByLabelText("ID del trámite")) as HTMLInputElement;
    fireEvent.change(campo, { target: { value: "PN-2026-000123" } });

    fireEvent.click(screen.getByRole("tab", { name: "Cobros del día" }));
    expect(await screen.findByText("Hoy no hay cobros registrados.")).toBeVisible();
    fireEvent.click(screen.getByRole("tab", { name: "Cobrar" }));
    expect((screen.getByLabelText("ID del trámite") as HTMLInputElement).value).toBe("PN-2026-000123");
  });
});

describe("lógica de la caja", () => {
  it("la espera se dice en segundos o minutos", () => {
    expect(esperaLegible(30)).toBe("30 segundos");
    expect(esperaLegible(60)).toBe("1 minuto");
    expect(esperaLegible(181)).toBe("4 minutos");
    expect(esperaLegible(undefined)).toBe("unos minutos");
  });

  it("los mensajes fijos y el genérico", () => {
    expect(mensajeDeCaja(new CajaError("ya_pagado", "x", 409))).toEqual({ texto: "Este trámite ya fue pagado.", tono: "aviso" });
    expect(mensajeDeCaja(new CajaError("raro", "x", 500)).texto).toBe("No pudimos completar la acción. Intente de nuevo.");
    expect(mensajeDeCaja(new Error("x")).texto).toBe("No pudimos completar la acción. Intente de nuevo.");
  });

  it("la sesión de caja vence a las 12 h desde auth_time; sin auth_time (o ilegible) se da por vencida", () => {
    const token = (c: object) => `e30.${btoa(JSON.stringify(c)).replace(/=+$/, "")}.`;
    const ahora = 1_800_000_000;
    expect(sesionDeCajaVencida(token({ auth_time: ahora - 11 * 3600 }), ahora)).toBe(false);
    expect(sesionDeCajaVencida(token({ auth_time: ahora - 12 * 3600 - 1 }), ahora)).toBe(true);
    expect(sesionDeCajaVencida(token({ sub: "u" }), ahora)).toBe(true);
    expect(sesionDeCajaVencida("basura", ahora)).toBe(true);
  });
});

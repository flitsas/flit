import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import ConfiguracionPagina from "@/app/(app)/configuracion/page";
import type { CapacidadesDiagnostico } from "@/lib/sesion";
import { ADMIN, BACKOFFICE, USUARIO, configuracion, sesion } from "./datos";

// Uso de ejemplo: dentro del shell, <ConfiguracionPagina /> pide GET /configuracion y arma las pestañas Contrato y
// Operación según las capacidades de GET /sesion.

vi.mock("next/navigation", () => ({
  usePathname: () => "/configuracion",
  useRouter: () => ({ replace: vi.fn(), push: vi.fn(), refresh: vi.fn() }),
}));

afterEach(() => vi.unstubAllGlobals());

const USUARIO_SHELL = { email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: false };

function simularApi(capacidades: Partial<CapacidadesDiagnostico>, respuestaConfiguracion: () => Promise<Response> = async () => Response.json(configuracion())) {
  const fetchMock = vi.fn((url: string) => {
    if (url === "/api/v1/diagnostico/sesion") return Promise.resolve(Response.json(sesion(capacidades, capacidades.editarContrato ? "FLIT" : "COMPANY")));
    if (url === "/api/v1/diagnostico/configuracion") return respuestaConfiguracion();
    return Promise.resolve(Response.json({}, { status: 404 }));
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function montar() {
  return render(
    <DiagnosticoShell user={USUARIO_SHELL} tramitesUrl="">
      <ConfiguracionPagina />
    </DiagnosticoShell>,
  );
}

const AJENOS = /usuario|rol(es)?\b|suscripci|retenci|purga/i;

describe("pantalla 10", () => {
  it("admin de la compañía: pestañas Contrato (solo lectura) y Operación, sin editor del backoffice", async () => {
    simularApi(ADMIN);
    montar();

    const tablist = await screen.findByRole("tablist", { name: "Configuración de Diagnóstico" });
    const pestanas = within(tablist).getAllByRole("tab");
    expect(pestanas.map((t) => t.textContent)).toEqual(["Contrato", "Operación"]);
    expect(pestanas[0]).toHaveAttribute("aria-selected", "true");
    const panel = screen.getByRole("tabpanel", { name: "Contrato" });
    expect(within(panel).getByText("500")).toBeInTheDocument();
    expect(within(panel).queryAllByRole("checkbox")).toHaveLength(0);
    expect(screen.queryByLabelText("Identificador de la empresa")).toBeNull();
  });

  it("backoffice FLIT: la pestaña Contrato trae el editor de la empresa indicada", async () => {
    simularApi(BACKOFFICE);
    montar();

    await screen.findByRole("tablist");
    expect(screen.getByLabelText("Identificador de la empresa")).toBeInTheDocument();
  });

  it("navegación por teclado entre pestañas (flechas, Inicio y Fin) con roles ARIA de tabs", async () => {
    simularApi(ADMIN);
    montar();

    const [contrato, operacion] = within(await screen.findByRole("tablist")).getAllByRole("tab");
    expect(contrato).toHaveAttribute("tabindex", "0");
    expect(operacion).toHaveAttribute("tabindex", "-1");
    expect(contrato).toHaveAttribute("aria-controls", screen.getByRole("tabpanel").id);

    contrato.focus();
    fireEvent.keyDown(contrato, { key: "ArrowRight" });
    expect(operacion).toHaveFocus();
    expect(operacion).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tabpanel", { name: "Operación" })).toBeInTheDocument();

    fireEvent.keyDown(operacion, { key: "ArrowRight" });
    expect(contrato).toHaveFocus();
    fireEvent.keyDown(contrato, { key: "End" });
    expect(operacion).toHaveFocus();
    fireEvent.keyDown(operacion, { key: "Home" });
    expect(contrato).toHaveFocus();
  });

  it("usuario sin diagnostico.configuracion.editar ve Operación deshabilitada", async () => {
    simularApi(USUARIO);
    montar();

    fireEvent.click(within(await screen.findByRole("tablist")).getByRole("tab", { name: "Operación" }));
    expect(screen.getByRole("button", { name: "Guardar operación" })).toBeDisabled();
  });

  it.each([
    ["admin", ADMIN],
    ["usuario", USUARIO],
    ["backoffice", BACKOFFICE],
  ])("no existen campos de usuarios, roles, suscripción, retención ni purga (%s)", async (_perfil, capacidades) => {
    simularApi(capacidades);
    const { container } = montar();

    const tabs = within(await screen.findByRole("tablist")).getAllByRole("tab");
    for (const tab of tabs) {
      fireEvent.click(tab);
      const panel = screen.getByRole("tabpanel");
      for (const control of panel.querySelectorAll("input, select, textarea, [role=switch], [role=checkbox], [role=radio]")) {
        const nombre = control.getAttribute("aria-label") ?? control.closest("label")?.textContent ?? control.getAttribute("name") ?? "";
        expect(nombre, nombre).not.toMatch(AJENOS);
      }
      expect(within(panel).queryByText(AJENOS)).toBeNull();
    }
    if (!capacidades.editarContrato) expect(container.textContent).not.toMatch(/redacción/i);
  });

  it("mientras carga muestra el loader y, si la API falla, el error con Reintentar", async () => {
    let intento = 0;
    simularApi(ADMIN, async () => (++intento === 1 ? Response.json({}, { status: 500 }) : Response.json(configuracion())));
    montar();

    expect(await screen.findByRole("status", { name: "Cargando configuración…" })).toBeInTheDocument();
    expect(await screen.findByText(/Diagnóstico no respondió/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    await waitFor(() => expect(screen.getByRole("tablist")).toBeInTheDocument());
  });

  it("sin ninguna capacidad de configuración muestra el estado vacío y no consulta la configuración", async () => {
    const fetchMock = simularApi({ verDiagnosticos: true });
    montar();

    expect(await screen.findByText(/no tienes acceso a la configuración/i)).toBeInTheDocument();
    expect(screen.queryByRole("tablist")).toBeNull();
    expect(fetchMock.mock.calls.map((c) => c[0])).not.toContain("/api/v1/diagnostico/configuracion");
  });

  it("el dock lleva a la pantalla 10 solo a quien tiene un permiso de configuración", async () => {
    simularApi(ADMIN);
    const { unmount } = render(
      <DiagnosticoShell user={{ ...USUARIO_SHELL, permissions: ["diagnostico.configuracion.ver"] }} tramitesUrl="">
        <p>pantalla</p>
      </DiagnosticoShell>,
    );
    await screen.findByText("pantalla");
    expect(screen.getAllByRole("link", { name: /Configuración/ }).some((a) => a.getAttribute("href") === "/configuracion")).toBe(true);
    unmount();

    render(
      <DiagnosticoShell user={{ ...USUARIO_SHELL, permissions: ["diagnostico.diagnosticos.ver"] }} tramitesUrl="">
        <p>pantalla</p>
      </DiagnosticoShell>,
    );
    await screen.findByText("pantalla");
    expect(screen.queryAllByRole("link", { name: /Configuración/ })).toHaveLength(0);
  });

  it("solo backoffice sin verConfiguracion: muestra Contrato con el editor y no la pestaña Operación", async () => {
    const fetchMock = simularApi({ backoffice: true, editarContrato: true });
    montar();

    const tabs = within(await screen.findByRole("tablist")).getAllByRole("tab");
    expect(tabs.map((t) => t.textContent)).toEqual(["Contrato"]);
    expect(screen.getByLabelText("Identificador de la empresa")).toBeInTheDocument();
    expect(fetchMock.mock.calls.map((c) => c[0])).not.toContain("/api/v1/diagnostico/configuracion");
  });
});

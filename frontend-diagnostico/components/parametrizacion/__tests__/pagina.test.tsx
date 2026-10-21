import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import NuevoDiagnostico from "@/app/(app)/diagnosticos/nuevo/page";
import type { CapacidadesDiagnostico } from "@/lib/sesion";
import { ID, configuracion, diagnostico, sesion, simularApi, tiposOk } from "./datos";

// Uso de ejemplo: dentro del shell, /diagnosticos/nuevo pide GET /configuracion y pinta el formulario en tres secciones.

vi.mock("next/navigation", () => ({
  usePathname: () => "/diagnosticos/nuevo",
  useRouter: () => ({ replace: vi.fn(), push: vi.fn(), refresh: vi.fn() }),
}));

afterEach(() => vi.unstubAllGlobals());

const CREADOR: Partial<CapacidadesDiagnostico> = { crearDiagnosticos: true, verDiagnosticos: true, verConfiguracion: true };

function montar(capacidades: Partial<CapacidadesDiagnostico>, respuestaConfiguracion: () => Response = () => Response.json(configuracion())) {
  const api = simularApi({
    "GET /sesion": () => Response.json(sesion(capacidades)),
    "GET /configuracion": respuestaConfiguracion,
    "GET /tipos-tramite": tiposOk,
  });
  render(
    <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: false }} tramitesUrl="">
      <NuevoDiagnostico />
    </DiagnosticoShell>,
  );
  return api;
}

describe("/diagnosticos/nuevo — los cuatro estados", () => {
  it("lleno: tarjeta título y el formulario en tres secciones", async () => {
    montar(CREADOR);
    expect(await screen.findByRole("heading", { level: 1, name: "Nuevo diagnóstico" })).toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: "Información general" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Alcance" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Modo" })).toBeInTheDocument();
  });

  it("cargando y error: el loader y, si GET /configuracion falla, el error con Reintentar", async () => {
    let falla = true;
    montar(CREADOR, () => (falla ? Response.json({}, { status: 500 }) : Response.json(configuracion())));
    expect(await screen.findByRole("status", { name: "Cargando el formulario…" })).toBeInTheDocument();
    expect(await screen.findByText(/diagnóstico no respondió/i)).toBeInTheDocument();

    falla = false;
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("heading", { name: "Información general" })).toBeInTheDocument();
  });

  it("vacío: sin crearDiagnosticos explica que no tiene permiso y no pide la configuración", async () => {
    const api = montar({ verDiagnosticos: true });
    expect(await screen.findByText(/no tienes permiso para crear diagnósticos/i)).toBeInTheDocument();
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(api.mock.calls.some(([url]) => url === "/api/v1/diagnostico/configuracion")).toBe(false);
  });
});

describe("/diagnosticos/nuevo — paso de carga (HU #13754)", () => {
  it("al crear la cabecera ofrece ir a la carga de vehículos de ese diagnóstico", async () => {
    const api = montar(CREADOR);
    api.mockImplementation(async (url: string, init?: RequestInit) => {
      const metodo = init?.method ?? "GET";
      if (url.endsWith("/sesion")) return Response.json(sesion(CREADOR));
      if (url.endsWith("/configuracion")) return Response.json(configuracion());
      if (url.endsWith("/tipos-tramite")) return tiposOk();
      if (metodo === "POST" && url.endsWith("/diagnosticos")) return Response.json(diagnostico(), { status: 201 });
      return Response.json({}, { status: 404 });
    });
    fireEvent.change(await screen.findByRole("textbox", { name: "Nombre del diagnóstico" }), { target: { value: "Flota norte" } });
    fireEvent.click(screen.getByRole("button", { name: "Crear diagnóstico" }));
    expect(await screen.findByRole("link", { name: /cargar vehículos/i })).toHaveAttribute("href", `/diagnosticos/${ID}/carga`);
  });
});

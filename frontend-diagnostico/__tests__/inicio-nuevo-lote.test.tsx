import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import Inicio from "@/app/(app)/page";
import { sesion } from "@/__tests__/configuracion/datos";

// Uso de ejemplo (HU #14085): la tarjeta del título del inicio trae «Placa o VIN» + «Diagnosticar», que valida y abre
// /diagnosticos/nuevo en Express con la placa precargada, y «Carga masiva», que la abre en Masivo. El listado sigue debajo.

const router = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn() }));
vi.mock("next/navigation", () => ({
  usePathname: () => "/",
  useSearchParams: () => new URLSearchParams(),
  useRouter: () => router,
}));

beforeEach(() => router.push.mockReset());
afterEach(() => vi.unstubAllGlobals());

function montarInicio(crearDiagnosticos: boolean) {
  // GET /sesion y el listado del inicio (GET /diagnosticos?…, HU #13852) vacío.
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string) =>
      String(url).includes("/diagnosticos")
        ? Response.json({ pagina: 1, tamano: 10, total: 0, diagnosticos: [] })
        : Response.json(sesion({ crearDiagnosticos, verDiagnosticos: true })),
    ),
  );
  render(
    <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: false }} tramitesUrl="">
      <Inicio />
    </DiagnosticoShell>,
  );
}

const tarjetaTitulo = async () => (await screen.findByRole("heading", { level: 1, name: "Diagnóstico vehicular" })).closest("section") as HTMLElement;

describe("Inicio — entrada a la creación desde la tarjeta del título (AC6)", () => {
  it("AC13 (HU #14562): tarjeta Express con «Placa o VIN» y «Diagnosticar» y tarjeta Masivo con «Nuevo diagnóstico masivo» y la plantilla", async () => {
    montarInicio(true);
    await tarjetaTitulo();
    const express = await screen.findByRole("region", { name: "Diagnóstico Express" });
    expect(express).toContainElement(screen.getByRole("textbox", { name: "Placa o VIN" }));
    expect(express).toContainElement(screen.getByRole("button", { name: "Diagnosticar" }));
    const masivo = screen.getByRole("region", { name: "Diagnóstico masivo" });
    expect(within(masivo).getByRole("link", { name: "Nuevo diagnóstico masivo" })).toHaveAttribute("href", "/diagnosticos/nuevo?modo=masivo");
    expect(within(masivo).getByRole("link", { name: /descargar plantilla/i })).toHaveAttribute("href", "/api/v1/diagnostico/plantilla");
  });

  it("con una placa válida, «Diagnosticar» abre /diagnosticos/nuevo en Express con la placa precargada", async () => {
    montarInicio(true);
    fireEvent.change(await screen.findByRole("textbox", { name: "Placa o VIN" }), { target: { value: "tst 123" } });
    fireEvent.click(screen.getByRole("button", { name: "Diagnosticar" }));
    expect(router.push).toHaveBeenCalledWith("/diagnosticos/nuevo?modo=express&placa=TST123");
  });

  it("«Nuevo diagnóstico masivo» abre /diagnosticos/nuevo en Masivo, con foco visible", async () => {
    montarInicio(true);
    const enlace = await screen.findByRole("link", { name: "Nuevo diagnóstico masivo" });
    expect(enlace).toHaveAttribute("href", "/diagnosticos/nuevo?modo=masivo");
    expect(enlace.className).toContain("focus-visible:outline");
  });

  it("AC49 (HU #14562): sin diagnósticos y sin filtros, el vacío de primer uso ofrece sus 2 acciones", async () => {
    montarInicio(true);
    expect(await screen.findByRole("heading", { name: "Aún no tienes diagnósticos" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Diagnosticar un vehículo" })).toHaveAttribute("href", "/diagnosticos/nuevo?modo=express");
    expect(screen.getByRole("link", { name: "Cargar archivo" })).toHaveAttribute("href", "/diagnosticos/nuevo?modo=masivo");
  });

  it("AC57: sin crearDiagnosticos no aparecen las tarjetas Express y Masivo ni las acciones del vacío; el listado sigue debajo", async () => {
    montarInicio(false);
    await tarjetaTitulo();
    expect(await screen.findByRole("heading", { name: "Aún no tienes diagnósticos" })).toBeInTheDocument();
    expect(screen.queryByRole("textbox", { name: "Placa o VIN" })).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Diagnóstico Express" })).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Diagnóstico masivo" })).not.toBeInTheDocument();
    expect(document.querySelector('a[href^="/diagnosticos/nuevo"]')).toBeNull();
  });
});

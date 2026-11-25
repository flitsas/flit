import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { DetalleAprovisionamiento } from "@/components/backoffice/DetalleAprovisionamiento";
import type { EstadoSesion } from "@/components/ImpuestosShell";

// Uso de ejemplo: <DetalleAprovisionamiento tenantId="…" /> en /backoffice/empresas/[tenantId] (solo Super Admin). La
// cabecera sale de AprovisionamientoDetalle.empresa (EmpresaIdentificada, contrato impuestos.v1.yaml 1.1.0, delta 7): ya
// no se recorre la lista del backoffice. Una compañía suspendida puede llegar con nombre "" y NIT null: nunca un título
// vacío, cae al id de la compañía.

const replace = vi.fn();
const notFound = vi.fn();
vi.mock("next/navigation", () => ({
  usePathname: () => "/backoffice/empresas/x",
  useRouter: () => ({ replace, push: vi.fn(), refresh: vi.fn() }),
  useSearchParams: () => new URLSearchParams(),
  notFound: () => notFound(),
}));

let estadoSesion: EstadoSesion;
vi.mock("@/components/ImpuestosShell", () => ({ useSesion: () => estadoSesion }));

const TENANT = "00000000-0000-7000-8000-0000000000e1";
const BASE = {
  estado: "LISTO",
  motivo: null,
  accion: null,
  firmaConfirmada: true,
  modoRespaldoFirma: false,
  actualizadoEn: "2026-11-20T15:00:00Z",
  historial: [],
};
const CREDENCIALES = { tieneApiKey: true, tieneWebhookSecret: true, origen: "ALTA", apiKeyPrefijo: "fk_live_", actualizadoEn: "2026-11-20T15:00:00Z" };

let rutas: string[];
function api(detalle: unknown) {
  return vi.fn(async (u: RequestInfo | URL) => {
    const url = new URL(String(u), "http://app");
    rutas.push(url.pathname);
    if (url.pathname.endsWith("/credenciales")) return Response.json(CREDENCIALES);
    if (url.pathname.endsWith("/aprovisionamiento")) return Response.json(detalle);
    return Response.json({ items: [{ id: TENANT, nombre: "Nombre de la lista", nit: "1", aprovisionamiento: BASE, suspendida: false }], total: 1, page: 1, pageSize: 200 });
  });
}

beforeEach(() => {
  replace.mockReset();
  notFound.mockClear();
  rutas = [];
  estadoSesion = {
    sesion: {
      usuario: { id: "u-1", email: "nombre@empresa.com", nombre: "Persona Demo" },
      empresa: { id: "e-flit", nombre: "FLIT" },
      roles: [],
      permisos: [],
      esSuperAdmin: true,
      aprovisionamiento: null,
    },
    cargando: false,
    error: null,
    recargar: vi.fn(),
  };
});
afterEach(() => vi.unstubAllGlobals());

const miga = () => screen.getByRole("navigation", { name: "Miga de pan" });

describe("Backoffice — detalle con la compañía del contrato (1.1.0, delta 7)", () => {
  it("título, miga y NIT salen de AprovisionamientoDetalle.empresa, sin pedir la lista del backoffice", async () => {
    vi.stubGlobal("fetch", api({ ...BASE, empresa: { id: TENANT, nombre: "Compañía Identificada", nit: "900555111" } }));

    render(<DetalleAprovisionamiento tenantId={TENANT} />);

    expect(await screen.findByRole("heading", { level: 1, name: "Compañía Identificada" })).toBeInTheDocument();
    expect(miga()).toHaveTextContent("Backoffice");
    expect(miga().querySelector('[aria-current="page"]')?.textContent).toBe("Compañía Identificada");
    expect(screen.getByText("NIT 900555111")).toBeInTheDocument();
    expect(rutas).not.toContain("/api/v1/impuestos/backoffice/empresas");
    expect(screen.queryByText("Nombre de la lista")).toBeNull();
  });

  it("compañía suspendida con nombre vacío y NIT null: el título y la miga caen al id, sin «NIT»", async () => {
    vi.stubGlobal("fetch", api({ ...BASE, estado: "SUSPENDIDO", empresa: { id: TENANT, nombre: "", nit: null } }));

    render(<DetalleAprovisionamiento tenantId={TENANT} />);

    const titulo = await screen.findByRole("heading", { level: 1 });
    expect(titulo.textContent?.trim()).toBe(TENANT);
    expect(miga().querySelector('[aria-current="page"]')?.textContent).toBe(TENANT);
    expect(document.body).not.toHaveTextContent("null");
    expect(screen.queryByText(/^NIT/)).toBeNull();
  });

  it("nombre solo con espacios también cae al id", async () => {
    vi.stubGlobal("fetch", api({ ...BASE, empresa: { id: TENANT, nombre: "   ", nit: "900" } }));

    render(<DetalleAprovisionamiento tenantId={TENANT} />);

    expect((await screen.findByRole("heading", { level: 1 })).textContent?.trim()).toBe(TENANT);
    expect(screen.getByText("NIT 900")).toBeInTheDocument();
  });

  it("si el detalle no trae `empresa` (campo opcional), el título es el id de la ruta y no un genérico", async () => {
    vi.stubGlobal("fetch", api(BASE));

    render(<DetalleAprovisionamiento tenantId={TENANT} />);

    expect((await screen.findByRole("heading", { level: 1 })).textContent?.trim()).toBe(TENANT);
    expect(screen.queryByRole("heading", { level: 1, name: "Compañía" })).toBeNull();
  });
});

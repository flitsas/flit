import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
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
let posts: string[];
function api(detalle: unknown) {
  return vi.fn(async (u: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(u), "http://app");
    rutas.push(url.pathname);
    if (init?.method === "POST") {
      posts.push(url.pathname);
      return new Response(null, { status: 202 });
    }
    if (url.pathname.endsWith("/credenciales")) return Response.json(CREDENCIALES);
    if (url.pathname.endsWith("/aprovisionamiento")) return Response.json(detalle);
    return Response.json({ items: [{ id: TENANT, nombre: "Nombre de la lista", nit: "1", aprovisionamiento: BASE, suspendida: false }], total: 1, page: 1, pageSize: 200 });
  });
}

beforeEach(() => {
  replace.mockReset();
  notFound.mockClear();
  rutas = [];
  posts = [];
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

describe("Backoffice — detalle con el contrato 1.2.0 (alta idempotente, plan §5.8)", () => {
  const SIN_WEBHOOK = { ...BASE, estado: "LISTO_SIN_WEBHOOK", motivo: "SIN_WEBHOOK_SECRET", accion: "REINTENTAR", empresa: { id: TENANT, nombre: "Compañía Uno", nit: "900" } };

  it("LISTO_SIN_WEBHOOK con accion REINTENTAR: «Reintentar aprovisionamiento» llama al POST …/aprovisionamiento/reintentar; ya no se pide «Cargar secreto»", async () => {
    vi.stubGlobal("fetch", api(SIN_WEBHOOK));

    render(<DetalleAprovisionamiento tenantId={TENANT} />);

    const estado = await screen.findByRole("group", { name: "Estado" });
    expect(within(estado).getByText("Listo sin webhook")).toBeInTheDocument();
    expect(screen.queryByText("Cargar secreto")).toBeNull();
    fireEvent.click(within(estado).getByRole("button", { name: "Reintentar aprovisionamiento" }));
    expect(await screen.findByText("Reintento en cola. El estado se actualiza en unos segundos.")).toBeInTheDocument();
    expect(posts).toEqual([`/api/v1/impuestos/backoffice/empresas/${TENANT}/aprovisionamiento/reintentar`]);
  });

  it("LISTO_SIN_WEBHOOK: la carga manual de credenciales sigue disponible como respaldo", async () => {
    vi.stubGlobal("fetch", api(SIN_WEBHOOK));

    render(<DetalleAprovisionamiento tenantId={TENANT} />);

    const credenciales = await screen.findByRole("group", { name: "Credenciales" });
    expect(await within(credenciales).findByRole("button", { name: "Reemplazar" })).toBeInTheDocument();
  });

  it("historial con CLAVE_ROTADA (desde = hacia) y un motivo retirado: cada uno con su literal del contrato", async () => {
    vi.stubGlobal(
      "fetch",
      api({
        ...BASE,
        historial: [
          { desde: "LISTO", hacia: "LISTO", motivo: "CLAVE_ROTADA", actorTipo: "SISTEMA", en: "2026-11-25T15:00:00Z" },
          { desde: "PENDIENTE", hacia: "ERROR", motivo: "EMPRESA_INACTIVA_EN_PROVEEDOR", actorTipo: "SISTEMA", en: "2026-11-24T15:00:00Z" },
          { desde: "PENDIENTE", hacia: "INCIERTO", motivo: "ALTA_SIN_CONFIRMAR", actorTipo: "SISTEMA", en: "2026-11-20T15:00:00Z" },
        ],
      }),
    );

    render(<DetalleAprovisionamiento tenantId={TENANT} />);

    const historial = await screen.findByRole("table", { name: "Historial del aprovisionamiento" });
    expect(within(historial).getByText("El proveedor reemplazó la API key de la compañía; la nueva quedó guardada.")).toBeInTheDocument();
    expect(within(historial).getByText("El proveedor tiene la compañía desactivada: pide su reactivación y luego reintenta.")).toBeInTheDocument();
    expect(within(historial).getByText("El proveedor no confirmó el alta: puede haberla recibido. Reintenta para verificar.")).toBeInTheDocument();
    expect(historial).not.toHaveTextContent("CLAVE_ROTADA");
    expect(historial).not.toHaveTextContent("Sin clasificar");
  });

  it("modoRespaldoFirma=true: chip «Respaldo de firma» y la etiqueta del contrato en la tarjeta «Estado»", async () => {
    vi.stubGlobal("fetch", api({ ...BASE, modoRespaldoFirma: true }));

    render(<DetalleAprovisionamiento tenantId={TENANT} />);

    const estado = await screen.findByRole("group", { name: "Estado" });
    expect(within(estado).getByText("Respaldo de firma")).toBeInTheDocument();
    expect(within(estado).getByText("Respaldo de firma: los resultados llegan por consulta mientras se revisa el secreto.")).toBeInTheDocument();
  });

  it("modoRespaldoFirma=false: sin la etiqueta del respaldo", async () => {
    vi.stubGlobal("fetch", api(BASE));

    render(<DetalleAprovisionamiento tenantId={TENANT} />);

    await screen.findByRole("group", { name: "Estado" });
    expect(screen.queryByText(/Respaldo de firma/)).toBeNull();
  });
});

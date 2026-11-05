import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";

// Uso de ejemplo: <DiagnosticoShell user={usuarioDelToken} tramitesUrl="https://tramites">{pantalla}</DiagnosticoShell>
// pide GET /api/v1/diagnostico/sesion y, con la sesión, dibuja la barra de la suite con la compañía y la pantalla.

const replace = vi.fn();
vi.mock("next/navigation", () => ({ usePathname: () => "/", useRouter: () => ({ replace, push: vi.fn(), refresh: vi.fn() }) }));

const usuario = { email: "ana@empresa-prueba.co", tenantName: "Nombre del token", permissions: ["diagnostico.diagnosticos.ver"], roles: ["usuario_diagnostico"], isSuperAdmin: false };

const sesion = {
  usuario: { id: "00000000-0000-7000-8000-000000000001", email: "ana@empresa-prueba.co", esSuperAdmin: false },
  empresa: { id: "00000000-0000-7000-8000-0000000000e1", nombre: "Transportes Prueba SAS", tipo: "COMPANY" },
  roles: ["usuario_diagnostico"],
  permisos: ["diagnostico.diagnosticos.ver"],
  capacidades: {
    crearDiagnosticos: false, verDiagnosticos: true, descargarReportes: false, derivar: false, verConfiguracion: false,
    editarConfiguracion: false, reprocesarConsultas: false, backoffice: false, editarContrato: false, llenarConsultas: false,
    corregirDictamen: false, verAuditoria: false, verVolumetria: false,
  },
};

function montar() {
  return render(
    <DiagnosticoShell user={usuario} tramitesUrl="https://tramites.prueba">
      <p>pantalla base</p>
    </DiagnosticoShell>,
  );
}

beforeEach(() => replace.mockReset());
afterEach(() => vi.unstubAllGlobals());

describe("DiagnosticoShell", () => {
  it("muestra la compañía de GET /sesion y no tiene selector de compañía", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json(sesion));
    vi.stubGlobal("fetch", fetchMock);

    montar();

    expect(await screen.findByText("pantalla base")).toBeInTheDocument();
    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/diagnostico/sesion");
    const barra = screen.getByRole("banner");
    expect(within(barra).getByText("Transportes Prueba SAS")).toBeInTheDocument();
    expect(within(barra).queryByText("Nombre del token")).toBeNull();
    expect(screen.queryAllByRole("combobox")).toHaveLength(0);
    expect(screen.queryAllByRole("listbox")).toHaveLength(0);
    expect(screen.queryByLabelText(/compañía|empresa/i)).toBeNull();
  });

  it("es la barra de la suite (@flit/shell) con el producto Diagnóstico", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(Response.json(sesion)));

    montar();

    await screen.findByText("pantalla base");
    const barra = screen.getByRole("banner");
    expect(within(barra).getByText("Diagnóstico")).toBeInTheDocument();
    expect(within(barra).getByRole("button", { name: "Productos" })).toBeInTheDocument();
    expect(within(barra).getByRole("button", { name: "Menú de usuario" })).toBeInTheDocument();
  });

  it("mientras carga la sesión muestra el loader de @flit/ui y no la pantalla", () => {
    vi.stubGlobal("fetch", vi.fn().mockReturnValue(new Promise(() => {})));

    montar();

    const loader = screen.getByRole("status", { name: "Abriendo Diagnóstico…" });
    expect(loader).toHaveClass("flit-loader");
    expect(screen.queryByText("pantalla base")).toBeNull();
  });

  it("con 403 PRODUCTO_NO_HABILITADO lleva a la página 403 y no muestra la pantalla base", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(Response.json({ code: "PRODUCTO_NO_HABILITADO", error: "x" }, { status: 403 })));

    montar();

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/403?code=PRODUCTO_NO_HABILITADO"));
    expect(screen.queryByText("pantalla base")).toBeNull();
  });

  it("con 403 sin permisos de Diagnóstico va a la 403 (no al login: sin bucle)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 403 }));
    vi.stubGlobal("fetch", fetchMock);

    montar();

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/403?code=SIN_PERMISOS_DIAGNOSTICO"));
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(screen.queryByText("pantalla base")).toBeNull();
  });

  it("si la API falla muestra el error con Reintentar y, al reintentar, abre la pantalla", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(Response.json({}, { status: 503 }))
      .mockResolvedValueOnce(Response.json(sesion));
    vi.stubGlobal("fetch", fetchMock);

    montar();

    expect(await screen.findByRole("alert")).toHaveTextContent("Diagnóstico no respondió");
    expect(screen.queryByText("pantalla base")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByText("pantalla base")).toBeInTheDocument();
    expect(replace).not.toHaveBeenCalled();
  });
});

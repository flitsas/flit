import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { ImpuestosShell } from "@/components/ImpuestosShell";

// Uso de ejemplo: <ImpuestosShell user={usuarioDelToken} tramitesUrl="https://tramites">{pantalla}</ImpuestosShell>
// pide GET /api/v1/impuestos/sesion y, con la sesión, dibuja la barra de la suite con los módulos que el rol permite
// (F6-CF3 / AC-FE-09), los banners de estado del producto, la campana y, solo para el Super Admin, el selector de compañía.

const replace = vi.fn();
let pathname = "/";
vi.mock("next/navigation", () => ({
  usePathname: () => pathname,
  useRouter: () => ({ replace, push: vi.fn(), refresh: vi.fn() }),
  useSearchParams: () => new URLSearchParams(),
}));

const usuario = { email: "ana@empresa-prueba.co", tenantName: "Nombre del token", permissions: [], roles: [], isSuperAdmin: false };

const PERMISOS_GESTOR = [
  "impuestos.dashboard.ver",
  "impuestos.liquidaciones.ver",
  "impuestos.liquidaciones.crear",
  "impuestos.recibos.descargar",
  "impuestos.reportes.ver",
  "impuestos.reportes.exportar",
];
const PERMISOS_ADMIN = [
  ...PERMISOS_GESTOR,
  "impuestos.liquidaciones.cerrar",
  "impuestos.usuarios.gestionar",
  "impuestos.configuracion.ver",
  "impuestos.configuracion.editar",
];

type Aprovisionamiento = { estado: string; firmaConfirmada: boolean };

function sesion(rol: "gestor" | "admin" | "superadmin", aprovisionamiento: Aprovisionamiento = { estado: "LISTO", firmaConfirmada: true }) {
  return {
    usuario: { id: "00000000-0000-7000-8000-000000000001", email: "ana@empresa-prueba.co", nombre: "Ana Prueba" },
    empresa: { id: "00000000-0000-7000-8000-0000000000e1", nombre: "Transportes Prueba SAS" },
    roles: rol === "gestor" ? ["gestor_impuestos"] : rol === "admin" ? ["admin_impuestos"] : [],
    permisos: rol === "gestor" ? PERMISOS_GESTOR : rol === "admin" ? PERMISOS_ADMIN : [],
    esSuperAdmin: rol === "superadmin",
    aprovisionamiento,
  };
}

const EMPRESAS = { items: [{ id: "e-1", nombre: "Empresa Uno", nit: "900000001" }], total: 1, page: 1, pageSize: 50 };

/** fetch simulado por ruta: sesión, avisos y compañías del selector. */
function api(cuerpoSesion: unknown, avisos: unknown = { items: [] }) {
  return vi.fn(async (u: RequestInfo | URL) => {
    const url = String(u);
    if (url.startsWith("/api/v1/impuestos/notificaciones")) return Response.json(avisos);
    if (url.startsWith("/api/v1/impuestos/backoffice/empresas")) return Response.json(EMPRESAS);
    return Response.json(cuerpoSesion);
  });
}

function montar() {
  return render(
    <ImpuestosShell user={usuario} tramitesUrl="https://tramites.prueba">
      <p>pantalla base</p>
    </ImpuestosShell>,
  );
}

const enlaces = (nombre: string) => screen.queryAllByRole("link", { name: nombre }).map((a) => a.getAttribute("href"));

beforeEach(() => {
  replace.mockReset();
  pathname = "/";
});
afterEach(() => vi.unstubAllGlobals());

describe("ImpuestosShell — sesión", () => {
  it("pide GET /api/v1/impuestos/sesion, es la barra de la suite con el producto Impuestos y la compañía de la sesión", async () => {
    const fetchMock = api(sesion("gestor"));
    vi.stubGlobal("fetch", fetchMock);

    montar();

    expect(await screen.findByText("pantalla base")).toBeInTheDocument();
    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/impuestos/sesion");
    const barra = screen.getByRole("banner");
    expect(within(barra).getByText("Impuestos")).toBeInTheDocument();
    expect(within(barra).getByText("Transportes Prueba SAS")).toBeInTheDocument();
    expect(within(barra).getByRole("button", { name: "Productos" })).toBeInTheDocument();
  });

  it("mientras carga muestra el loader de @flit/ui, no la pantalla y ningún módulo", () => {
    vi.stubGlobal("fetch", vi.fn().mockReturnValue(new Promise(() => {})));

    montar();

    expect(screen.getByRole("status", { name: "Abriendo Impuestos…" })).toBeInTheDocument();
    expect(screen.queryByText("pantalla base")).toBeNull();
    for (const modulo of ["Impuestos", "Reportes", "Usuarios", "Configuración", "Backoffice"]) expect(enlaces(modulo)).toHaveLength(0);
  });

  it.each([
    ["PRODUCTO_NO_HABILITADO", Response.json({ code: "PRODUCTO_NO_HABILITADO" }, { status: 403 }), "/403?code=PRODUCTO_NO_HABILITADO"],
    ["PRODUCTO_SUSPENDIDO", Response.json({ code: "PRODUCTO_SUSPENDIDO" }, { status: 403 }), "/403?code=PRODUCTO_SUSPENDIDO"],
    ["sin code (sin rol en Impuestos)", new Response(null, { status: 403 }), "/403?code=SIN_PERMISOS_IMPUESTOS"],
  ])("AC-FE-08: 403 %s lleva a la 403 con el motivo y no muestra módulos ni la pantalla", async (_n, respuesta, destino) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(respuesta));

    montar();

    await waitFor(() => expect(replace).toHaveBeenCalledWith(destino));
    expect(screen.queryByText("pantalla base")).toBeNull();
    expect(enlaces("Impuestos")).toHaveLength(0);
  });

  it("si la API falla muestra el error fijo (nunca el del servidor) con Reintentar y, al reintentar, abre la pantalla", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(Response.json({ error: "mensaje del servidor que no debe verse" }, { status: 503 }))
      .mockImplementation(api(sesion("gestor")));
    vi.stubGlobal("fetch", fetchMock);

    montar();

    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("No se pudo abrir Impuestos.");
    expect(alerta).not.toHaveTextContent("mensaje del servidor que no debe verse");
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByText("pantalla base")).toBeInTheDocument();
    expect(replace).not.toHaveBeenCalled();
  });
});

describe("F6-CF3 / AC-FE-09: módulos según el rol", () => {
  it("Gestor Impuestos: Dashboard, Impuestos y Reportes; sin Usuarios, Configuración, Backoffice ni selector", async () => {
    vi.stubGlobal("fetch", api(sesion("gestor")));

    montar();
    await screen.findByText("pantalla base");

    expect(enlaces("Inicio Impuestos")).toContain("/");
    expect(enlaces("Impuestos")).toContain("/liquidaciones");
    expect(enlaces("Reportes")).toContain("/reportes");
    expect(enlaces("Usuarios")).toHaveLength(0);
    expect(enlaces("Configuración")).toHaveLength(0);
    expect(enlaces("Backoffice")).toHaveLength(0);
    expect(screen.queryByRole("combobox", { name: "Compañía" })).toBeNull();
  });

  it("Administrador Impuestos: Dashboard, Impuestos, Reportes, Usuarios y Configuración; sin Backoffice ni selector", async () => {
    vi.stubGlobal("fetch", api(sesion("admin")));

    montar();
    await screen.findByText("pantalla base");

    expect(enlaces("Inicio Impuestos")).toContain("/");
    expect(enlaces("Impuestos")).toContain("/liquidaciones");
    expect(enlaces("Reportes")).toContain("/reportes");
    expect(enlaces("Usuarios")).toContain("/usuarios");
    expect(enlaces("Configuración")).toContain("/configuracion");
    expect(enlaces("Backoffice")).toHaveLength(0);
    expect(screen.queryByRole("combobox", { name: "Compañía" })).toBeNull();
  });

  it("Super Admin: todos los módulos, el Backoffice y el selector de compañía en la barra", async () => {
    vi.stubGlobal("fetch", api(sesion("superadmin")));

    montar();
    await screen.findByText("pantalla base");

    expect(enlaces("Inicio Impuestos")).toContain("/");
    expect(enlaces("Impuestos")).toContain("/liquidaciones");
    expect(enlaces("Reportes")).toContain("/reportes");
    expect(enlaces("Usuarios")).toContain("/usuarios");
    expect(enlaces("Configuración")).toContain("/configuracion");
    expect(enlaces("Backoffice")).toContain("/backoffice/empresas");
    const barra = screen.getByRole("banner");
    expect(await within(barra).findByRole("combobox", { name: "Compañía" })).toBeInTheDocument();
  });

  it("los módulos salen de los permisos de GET /sesion, no de los del token", async () => {
    vi.stubGlobal("fetch", api(sesion("gestor")));

    render(
      <ImpuestosShell user={{ ...usuario, permissions: PERMISOS_ADMIN }} tramitesUrl="https://tramites.prueba">
        <p>pantalla base</p>
      </ImpuestosShell>,
    );
    await screen.findByText("pantalla base");

    expect(enlaces("Usuarios")).toHaveLength(0);
    expect(enlaces("Configuración")).toHaveLength(0);
  });
});

describe("Banners de estado del producto", () => {
  it.each([
    ["LISTO con la firma sin confirmar", { estado: "LISTO", firmaConfirmada: false }, "Firma sin confirmar"],
    ["LISTO_SIN_WEBHOOK", { estado: "LISTO_SIN_WEBHOOK", firmaConfirmada: false }, "Modo sin webhook"],
    ["SUSPENDIDO", { estado: "SUSPENDIDO", firmaConfirmada: true }, "Producto suspendido: solo lectura"],
  ])("%s muestra «%s» sobre la pantalla", async (_n, aprovisionamiento, titulo) => {
    vi.stubGlobal("fetch", api(sesion("admin", aprovisionamiento)));

    montar();
    await screen.findByText("pantalla base");

    expect(screen.getByText(titulo)).toBeInTheDocument();
  });

  it("LISTO con la firma confirmada no muestra ningún banner", async () => {
    vi.stubGlobal("fetch", api(sesion("admin")));

    montar();
    await screen.findByText("pantalla base");

    for (const titulo of ["Firma sin confirmar", "Modo sin webhook", "Producto suspendido: solo lectura"]) expect(screen.queryByText(titulo)).toBeNull();
  });
});

describe("Campana de avisos", () => {
  const avisos = {
    items: [
      { id: "n-1", tipo: "LOTE_TERMINADO", liquidacionId: "l-1", creadaEn: "2026-11-20T15:00:00Z", leida: false },
      { id: "n-2", tipo: "LOTE_TERMINADO", liquidacionId: "l-2", creadaEn: "2026-11-19T15:00:00Z", leida: false },
      { id: "n-3", tipo: "LOTE_TERMINADO", liquidacionId: "l-3", creadaEn: "2026-11-18T15:00:00Z", leida: true },
    ],
  };

  it("con la sesión aparece en la barra con las no leídas y lista los lotes terminados con enlace al detalle", async () => {
    vi.stubGlobal("fetch", api(sesion("gestor"), avisos));

    montar();
    const barra = await screen.findByRole("banner");
    const boton = await within(barra).findByRole("button", { name: "Avisos, 2 avisos sin leer" });

    fireEvent.click(boton);

    const panel = screen.getByRole("region", { name: "Avisos" });
    const verLote = within(panel).getAllByRole("link", { name: /^Ver lote/ });
    expect(verLote.map((a) => a.getAttribute("href"))).toEqual(["/liquidaciones/l-1", "/liquidaciones/l-2", "/liquidaciones/l-3"]);
    expect(within(panel).getAllByText("Lote terminado")).toHaveLength(3);
  });

  it("Escape cierra el panel y devuelve el foco al botón", async () => {
    vi.stubGlobal("fetch", api(sesion("gestor"), avisos));

    montar();
    const boton = await screen.findByRole("button", { name: "Avisos, 2 avisos sin leer" });
    fireEvent.click(boton);
    fireEvent.keyDown(screen.getByRole("region", { name: "Avisos" }), { key: "Escape" });

    expect(screen.queryByRole("region", { name: "Avisos" })).toBeNull();
    expect(boton).toHaveFocus();
  });

  it("se vuelve a pedir al cambiar de ruta, sin sondeo", async () => {
    const fetchMock = api(sesion("gestor"), { items: [] });
    vi.stubGlobal("fetch", fetchMock);
    const pedidas = () => fetchMock.mock.calls.filter(([u]) => String(u).startsWith("/api/v1/impuestos/notificaciones")).length;

    const { rerender } = montar();
    await screen.findByRole("button", { name: "Avisos" });
    await waitFor(() => expect(pedidas()).toBe(1));

    pathname = "/liquidaciones";
    rerender(
      <ImpuestosShell user={usuario} tramitesUrl="https://tramites.prueba">
        <p>pantalla base</p>
      </ImpuestosShell>,
    );

    await waitFor(() => expect(pedidas()).toBe(2));
  });

  it("mientras no hay sesión no se piden avisos", () => {
    const fetchMock = vi.fn(() => new Promise<Response>(() => {}));
    vi.stubGlobal("fetch", fetchMock);

    montar();

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("button", { name: /^Avisos/ })).toBeNull();
  });
});

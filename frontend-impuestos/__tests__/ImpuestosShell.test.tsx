import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { BannerEstadoProducto } from "@/components/BannerEstadoProducto";
import { ImpuestosShell } from "@/components/ImpuestosShell";
import { bannersDeSesion } from "@/lib/sesion";

// Uso de ejemplo: <ImpuestosShell user={usuarioDelToken} tramitesUrl="https://tramites">{pantalla}</ImpuestosShell>
// pide GET /api/v1/impuestos/sesion y, con la sesión, dibuja la barra de la suite con los módulos que el rol permite
// (F6-CF3 / AC-FE-09), los banners de estado del producto, la campana y, solo para el Super Admin, el selector de compañía.

const replace = vi.fn();
let pathname = "/";
let busqueda = new URLSearchParams();
vi.mock("next/navigation", () => ({
  usePathname: () => pathname,
  useRouter: () => ({ replace, push: vi.fn(), refresh: vi.fn() }),
  useSearchParams: () => busqueda,
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

/** fetch simulado por ruta: sesión, avisos, compañías del selector y aprovisionamiento de la compañía elegida (SA). */
function api(cuerpoSesion: unknown, avisos: unknown = { items: [] }, aprovisionamiento?: unknown) {
  return vi.fn(async (u: RequestInfo | URL) => {
    const url = String(u);
    if (url.startsWith("/api/v1/impuestos/notificaciones")) return Response.json(avisos);
    if (/\/backoffice\/empresas\/[^/]+\/aprovisionamiento/.test(url)) return Response.json(aprovisionamiento ?? {});
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
  busqueda = new URLSearchParams();
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

const TITULOS_BANNER = {
  SUSPENDIDO: "Impuestos está suspendido: solo lectura",
  NO_LISTO: "Impuestos todavía no está listo para liquidar",
  SIN_WEBHOOK: "Los resultados pueden tardar un poco más",
  FIRMA_SIN_CONFIRMAR: "Conexión con el proveedor en verificación",
} as const;

const TEXTOS_BANNER = {
  SUSPENDIDO: "Puedes consultar las liquidaciones, los recibos y los reportes, pero no crear liquidaciones nuevas. Los lotes que estaban en curso terminan solos.",
  NO_LISTO:
    "La conexión de tu compañía con el proveedor se está configurando. Puedes consultar lo que ya existe; para liquidar, espera a que termine o pídele ayuda a tu administrador.",
  SIN_WEBHOOK: "Ya puedes liquidar. Los resultados se consultan con el proveedor cada pocos minutos, en lugar de llegar al instante.",
  FIRMA_SIN_CONFIRMAR: "Ya puedes liquidar. Se confirma sola con el primer resultado que llegue.",
} as const;

/** Contenedor del InlineAlert que lleva el título (el tono viaja en su `style`: `var(--badge-<tono>-bg)`). */
const alertaDe = (titulo: string) => screen.getByText(titulo).closest("div[style]") as HTMLElement;

describe("Banners de estado del producto (spec-visual §4.3)", () => {
  it.each([
    ["LISTO con la firma sin confirmar", "FIRMA_SIN_CONFIRMAR", "info", { estado: "LISTO", firmaConfirmada: false }],
    ["LISTO_SIN_WEBHOOK", "SIN_WEBHOOK", "info", { estado: "LISTO_SIN_WEBHOOK", firmaConfirmada: false }],
    ["SUSPENDIDO", "SUSPENDIDO", "warning", { estado: "SUSPENDIDO", firmaConfirmada: true }],
    ["PENDIENTE", "NO_LISTO", "pending", { estado: "PENDIENTE", firmaConfirmada: false }],
    ["ERROR", "NO_LISTO", "pending", { estado: "ERROR", firmaConfirmada: false }],
    ["INCIERTO", "NO_LISTO", "pending", { estado: "INCIERTO", firmaConfirmada: false }],
    ["REQUIERE_CREDENCIALES", "NO_LISTO", "pending", { estado: "REQUIERE_CREDENCIALES", firmaConfirmada: false }],
    ["REQUIERE_REVISION_NIT", "NO_LISTO", "pending", { estado: "REQUIERE_REVISION_NIT", firmaConfirmada: false }],
  ] as const)("%s muestra el banner %s (tono %s) con sus literales, sin anunciarse", async (_n, clave, tono, aprovisionamiento) => {
    vi.stubGlobal("fetch", api(sesion("admin", aprovisionamiento)));

    montar();
    await screen.findByText("pantalla base");

    const alerta = alertaDe(TITULOS_BANNER[clave]);
    expect(alerta).toHaveTextContent(TEXTOS_BANNER[clave]);
    expect(alerta.getAttribute("style")).toContain(`--badge-${tono}-bg`);
    expect(alerta).not.toHaveAttribute("role");
    for (const otro of Object.values(TITULOS_BANNER).filter((t) => t !== TITULOS_BANNER[clave])) expect(screen.queryByText(otro)).toBeNull();
  });

  it("LISTO con la firma confirmada no muestra ningún banner", async () => {
    vi.stubGlobal("fetch", api(sesion("admin")));

    montar();
    await screen.findByText("pantalla base");

    for (const titulo of Object.values(TITULOS_BANNER)) expect(screen.queryByText(titulo)).toBeNull();
  });

  it("bannersDeSesion: NO_LISTO para los cinco estados no listos y nada sin bloque de aprovisionamiento", () => {
    const de = (estado: string, firmaConfirmada = false) => bannersDeSesion({ ...sesion("admin"), aprovisionamiento: { estado, firmaConfirmada } });
    for (const estado of ["PENDIENTE", "ERROR", "INCIERTO", "REQUIERE_CREDENCIALES", "REQUIERE_REVISION_NIT"]) expect(de(estado)).toEqual(["NO_LISTO"]);
    expect(de("LISTO", true)).toEqual([]);
    expect(bannersDeSesion({ ...sesion("admin"), aprovisionamiento: null })).toEqual([]);
  });

  it.each([
    [["FIRMA_SIN_CONFIRMAR", "SIN_WEBHOOK", "NO_LISTO", "SUSPENDIDO"], "SUSPENDIDO"],
    [["FIRMA_SIN_CONFIRMAR", "SIN_WEBHOOK", "NO_LISTO"], "NO_LISTO"],
    [["FIRMA_SIN_CONFIRMAR", "SIN_WEBHOOK"], "SIN_WEBHOOK"],
    [["FIRMA_SIN_CONFIRMAR"], "FIRMA_SIN_CONFIRMAR"],
  ] as const)("con varios (%j) muestra solo el de mayor prioridad: %s", (banners, ganador) => {
    render(<BannerEstadoProducto banners={banners} />);

    expect(screen.getByText(TITULOS_BANNER[ganador])).toBeInTheDocument();
    for (const otro of Object.values(TITULOS_BANNER).filter((t) => t !== TITULOS_BANNER[ganador])) expect(screen.queryByText(otro)).toBeNull();
  });
});

describe("Banners de la compañía elegida por el Super Admin (spec-visual §4: observación para M18)", () => {
  const detalle = (estado: string, firmaConfirmada = true) => ({
    estado,
    motivo: null,
    accion: null,
    firmaConfirmada,
    modoRespaldoFirma: false,
    actualizadoEn: "2026-11-20T15:00:00Z",
    historial: [],
  });

  it("con ?empresa= los banners salen del aprovisionamiento de esa compañía, no del de la sesión", async () => {
    busqueda = new URLSearchParams("empresa=e-1");
    const fetchMock = api(sesion("superadmin", { estado: "LISTO", firmaConfirmada: true }), { items: [] }, detalle("SUSPENDIDO"));
    vi.stubGlobal("fetch", fetchMock);

    montar();

    expect(await screen.findByText(TITULOS_BANNER.SUSPENDIDO)).toBeInTheDocument();
    expect(fetchMock.mock.calls.map(([u]) => String(u))).toContain("/api/v1/impuestos/backoffice/empresas/e-1/aprovisionamiento");
  });

  it.each([
    ["SUSPENDIDO", "SUSPENDIDO"],
    ["NO_LISTO", "REQUIERE_CREDENCIALES"],
  ] as const)("%s lleva «Ver en el backoffice» a la compañía elegida, después del texto", async (clave, estado) => {
    busqueda = new URLSearchParams("empresa=e-1");
    vi.stubGlobal("fetch", api(sesion("superadmin"), { items: [] }, detalle(estado)));

    montar();

    await screen.findByText(TITULOS_BANNER[clave]);
    const alerta = alertaDe(TITULOS_BANNER[clave]);
    const enlace = within(alerta).getByRole("link", { name: "Ver en el backoffice" });
    expect(enlace).toHaveAttribute("href", "/backoffice/empresas/e-1?empresa=e-1");
    expect(alerta.textContent?.indexOf(TEXTOS_BANNER[clave])).toBeLessThan(alerta.textContent?.indexOf("Ver en el backoffice") ?? -1);
  });

  it("SIN_WEBHOOK de la compañía elegida no lleva enlace", async () => {
    busqueda = new URLSearchParams("empresa=e-1");
    vi.stubGlobal("fetch", api(sesion("superadmin"), { items: [] }, detalle("LISTO_SIN_WEBHOOK", false)));

    montar();

    expect(await screen.findByText(TITULOS_BANNER.SIN_WEBHOOK)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Ver en el backoffice" })).toBeNull();
  });

  it("sin ?empresa= el Super Admin ve los banners de la sesión, sin enlace y sin pedir el aprovisionamiento", async () => {
    const fetchMock = api(sesion("superadmin", { estado: "SUSPENDIDO", firmaConfirmada: true }));
    vi.stubGlobal("fetch", fetchMock);

    montar();

    expect(await screen.findByText(TITULOS_BANNER.SUSPENDIDO)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Ver en el backoffice" })).toBeNull();
    expect(fetchMock.mock.calls.some(([u]) => String(u).includes("/aprovisionamiento"))).toBe(false);
  });

  it("un usuario que no es Super Admin ignora ?empresa= y nunca ve el enlace", async () => {
    busqueda = new URLSearchParams("empresa=e-1");
    const fetchMock = api(sesion("admin", { estado: "SUSPENDIDO", firmaConfirmada: true }));
    vi.stubGlobal("fetch", fetchMock);

    montar();

    expect(await screen.findByText(TITULOS_BANNER.SUSPENDIDO)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Ver en el backoffice" })).toBeNull();
    expect(fetchMock.mock.calls.some(([u]) => String(u).includes("/aprovisionamiento"))).toBe(false);
  });
});

describe("Dock (spec-visual §2 y §4: dockDistribucion=\"equilibrada\")", () => {
  it("Gestor: los dos módulos se reparten 1 | 1 alrededor del inicio, sin espaciadores", async () => {
    vi.stubGlobal("fetch", api(sesion("gestor")));

    montar();
    await screen.findByText("pantalla base");

    const nav = screen.getByRole("navigation", { name: "Navegación principal" });
    const hijos = Array.from(nav.children);
    const inicio = hijos.findIndex((el) => el.getAttribute("aria-label") === "Inicio Impuestos");
    expect(inicio).toBeGreaterThan(0);
    expect(hijos.slice(0, inicio).map((el) => el.textContent)).toEqual(["Impuestos"]);
    expect(hijos.slice(inicio + 1).map((el) => el.textContent)).toEqual(["Reportes"]);
    expect(hijos.filter((el) => el.tagName === "SPAN" && el.getAttribute("aria-hidden") === "true")).toHaveLength(0);
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

  it("cada aviso lleva la línea de mensaje bajo el título (§4.4), también el de tipo desconocido", async () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
    const conDesconocido = { items: [...avisos.items.slice(0, 1), { id: "n-9", tipo: "TIPO_NUEVO", liquidacionId: "l-9", creadaEn: "2026-11-17T15:00:00Z", leida: false }] };
    vi.stubGlobal("fetch", api(sesion("gestor"), conDesconocido));

    montar();
    fireEvent.click(await screen.findByRole("button", { name: "Avisos, 2 avisos sin leer" }));

    const panel = screen.getByRole("region", { name: "Avisos" });
    const [terminado, desconocido] = within(panel).getAllByRole("listitem");
    expect(within(terminado).getByText("Lote terminado")).toBeInTheDocument();
    expect(within(terminado).getByText("Ya está el resultado de cada placa del lote.")).toBeInTheDocument();
    expect(within(desconocido).getByText("Sin clasificar")).toBeInTheDocument();
    expect(within(desconocido).getByText("Abre el lote para ver el detalle.")).toBeInTheDocument();
    expect(within(desconocido).queryByText(/TIPO_NUEVO/)).toBeNull();
    warn.mockRestore();
  });

  it("el panel entra con la transición del token de movimiento y la quita con movimiento reducido", async () => {
    vi.stubGlobal("fetch", api(sesion("gestor"), avisos));

    montar();
    fireEvent.click(await screen.findByRole("button", { name: "Avisos, 2 avisos sin leer" }));

    const panel = screen.getByRole("region", { name: "Avisos" });
    expect(panel).toHaveClass("transition-opacity", "duration-[var(--di-motion-base)]", "motion-reduce:transition-none");
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

import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { PaginaUsuarios } from "@/components/usuarios/PaginaUsuarios";
import { AVISO_INVITACION } from "@/lib/usuarios/tipos";

// Uso de ejemplo: app/(app)/usuarios/page.tsx monta <PaginaUsuarios /> bajo Suspense. Con gestionarUsuarios: el admin de
// compañía ve sus usuarios (GET /usuarios); el SuperAdmin ve primero las compañías (GET /backoffice/usuarios/empresas) y con
// ?empresa=<id> los usuarios de esa compañía (GET /backoffice/empresas/{id}/usuarios) con migas. Sin la capacidad, la 403.
// Contrato: contrato-api.md §3; spec-ui §10 con los ajustes H-UI. Datos: placeholders.

const nav = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn(), params: new URLSearchParams() }));
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: nav.push, replace: nav.replace, refresh: nav.refresh }),
  useSearchParams: () => nav.params,
  usePathname: () => "/usuarios",
}));

const sesionMock = vi.hoisted(() => ({ actual: null as unknown }));
vi.mock("@/components/DiagnosticoShell", () => ({
  useSesion: () => ({ sesion: sesionMock.actual, cargando: false, error: null, recargar: () => {} }),
}));

const PROPIO = "00000000-0000-4000-8000-0000000000aa";
const EMPRESA = { id: "00000000-0000-4000-8000-0000000000e1", nombre: "Compañía Demo", nit: "900000001" };

function sesion({ gestionar = true, superAdmin = false } = {}) {
  return {
    usuario: { id: PROPIO, email: "admin.demo@ejemplo.com", esSuperAdmin: superAdmin },
    empresa: { id: "00000000-0000-4000-8000-0000000000f1", nombre: "Mi Compañía" },
    roles: [],
    permisos: [],
    capacidades: { gestionarUsuarios: gestionar, backoffice: superAdmin },
  };
}

const fila = (id: string, nombre: string, extra: Record<string, unknown> = {}) => ({
  id,
  tipo: "USUARIO",
  nombre,
  correo: `${nombre.toLowerCase().replace(/\s+/g, ".")}@ejemplo.com`,
  rol: "usuario_diagnostico",
  rol_etiqueta: "Usuario de Diagnóstico",
  estado: "ACTIVO",
  acciones: [],
  motivo_sin_acciones: null,
  ...extra,
});
const USUARIOS = [
  fila(PROPIO, "Admin Demo", { rol: "admin_diagnostico", rol_etiqueta: "Administrador de Diagnóstico", motivo_sin_acciones: "USTED_MISMO" }),
  fila("00000000-0000-4000-8000-000000000001", "Usuario Uno", { acciones: ["CAMBIAR_ROL", "SUSPENDER"] }),
  fila("00000000-0000-4000-8000-000000000002", "Usuario Dos", { estado: "SUSPENDIDO", acciones: ["REACTIVAR"] }),
];
const lista = (usuarios: unknown[], roles = ["usuario_diagnostico"]) =>
  Response.json({ pagina: 1, tamano: 20, total: usuarios.length, roles_asignables: roles, usuarios });

type Ruta = (url: URL, init?: RequestInit) => Response | Promise<Response>;
function simular(rutas: Record<string, Ruta>) {
  const f = vi.fn(async (u: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(u), "http://x");
    const clave = `${init?.method ?? "GET"} ${url.pathname.replace("/api/v1/diagnostico", "")}`;
    const ruta = rutas[clave];
    if (!ruta) return Response.json({ code: "NO_SIMULADA", error: clave }, { status: 599 });
    return ruta(url, init);
  });
  vi.stubGlobal("fetch", f);
  return f;
}
const pedidos = (f: ReturnType<typeof simular>, prefijo: string) =>
  f.mock.calls.map(([u, init]) => `${(init as RequestInit | undefined)?.method ?? "GET"} ${new URL(String(u), "http://x").pathname}`).filter((c) => c.includes(prefijo));

beforeEach(() => {
  nav.push.mockReset();
  nav.replace.mockReset();
  nav.params = new URLSearchParams();
  sesionMock.actual = sesion();
});
afterEach(() => vi.unstubAllGlobals());

const filaDe = (nombre: string) => screen.getAllByRole("row").find((r) => r.textContent?.includes(nombre))!;

describe("HU14588 PaginaUsuarios — acceso (A10.1)", () => {
  it("sin gestionarUsuarios lleva a la 403 y no pide usuarios", async () => {
    sesionMock.actual = sesion({ gestionar: false });
    const f = simular({});
    render(<PaginaUsuarios />);
    await waitFor(() => expect(nav.replace).toHaveBeenCalledWith(expect.stringMatching(/^\/403/)));
    expect(f).not.toHaveBeenCalled();
  });

  it("un 403 de la lista lleva a la 403", async () => {
    simular({ "GET /usuarios": () => Response.json({ code: "PRODUCTO_NO_HABILITADO" }, { status: 403 }) });
    render(<PaginaUsuarios />);
    await waitFor(() => expect(nav.replace).toHaveBeenCalledWith("/403?code=PRODUCTO_NO_HABILITADO"));
  });
});

describe("HU14588 PaginaUsuarios — admin de compañía (spec-ui §10.1)", () => {
  it("cabecera «Usuarios» con la compañía e «Invitar usuario»; pide GET /usuarios y nunca las compañías (A10.2)", async () => {
    const f = simular({ "GET /usuarios": () => lista(USUARIOS) });
    render(<PaginaUsuarios />);
    expect(screen.getByRole("heading", { level: 1, name: /^Usuarios/ })).toBeInTheDocument();
    expect(screen.getByText("Mi Compañía")).toBeInTheDocument();
    expect(await screen.findByRole("table")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Invitar usuario" })).toBeInTheDocument();
    expect(pedidos(f, "/backoffice")).toEqual([]);
    expect(screen.getByText(/^Mostrando 3 de 3 usuarios$/)).toBeInTheDocument();
  });

  it("cargando: skeleton con role=status «Cargando usuarios…»", () => {
    simular({ "GET /usuarios": () => new Promise<Response>(() => {}) });
    render(<PaginaUsuarios />);
    expect(screen.getByRole("status", { name: "Cargando usuarios…" })).toBeInTheDocument();
  });

  it("vacío real: «Aún no hay usuarios en Diagnóstico» con CTA «Invitar usuario»", async () => {
    simular({ "GET /usuarios": () => lista([]) });
    render(<PaginaUsuarios />);
    expect(await screen.findByText("Aún no hay usuarios en Diagnóstico")).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Invitar usuario" }).length).toBeGreaterThanOrEqual(2);
  });

  it("vacío por filtro: «Ningún usuario coincide» con «Limpiar filtros», que vuelve a pedir sin filtros", async () => {
    const f = simular({ "GET /usuarios": (url) => (url.searchParams.get("busqueda") ? lista([]) : lista(USUARIOS)) });
    render(<PaginaUsuarios />);
    await screen.findByRole("table");
    fireEvent.change(screen.getByRole("textbox", { name: "Buscar por nombre o correo" }), { target: { value: "zzz" } });
    // Debounce de 300 ms + fetch: con la CPU acotada (maxWorkers=2) puede pasar de 1 s.
    expect(await screen.findByText("Ningún usuario coincide", undefined, { timeout: 4000 })).toBeInTheDocument();
    fireEvent.click(screen.getAllByRole("button", { name: "Limpiar filtros" }).at(-1)!);
    await screen.findByRole("table");
    expect(screen.getByRole("textbox", { name: "Buscar por nombre o correo" })).toHaveValue("");
    const ultima = f.mock.calls.at(-1)![0];
    expect(new URL(String(ultima), "http://x").searchParams.has("busqueda")).toBe(false);
  });

  it("filtro Estado: pide estado=SUSPENDIDO al servidor", async () => {
    const f = simular({ "GET /usuarios": () => lista(USUARIOS) });
    render(<PaginaUsuarios />);
    await screen.findByRole("table");
    fireEvent.change(screen.getByRole("combobox", { name: "Estado" }), { target: { value: "SUSPENDIDO" } });
    await waitFor(() => expect(new URL(String(f.mock.calls.at(-1)![0]), "http://x").searchParams.getAll("estado")).toEqual(["SUSPENDIDO"]));
  });

  it("error: alerta con «Reintentar»; 503 de la plataforma con su texto propio", async () => {
    let n = 0;
    simular({ "GET /usuarios": () => (n++ === 0 ? Response.json({ code: "PLATAFORMA_NO_DISPONIBLE" }, { status: 503 }) : lista(USUARIOS)) });
    render(<PaginaUsuarios />);
    expect(await screen.findByText(/plataforma/i)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("table")).toBeInTheDocument();
  });

  it("invitar: cierra, muestra el aviso uniforme del 202 (no «Invitación enviada a…»), refresca y devuelve el foco (SP-15)", async () => {
    let lecturas = 0;
    simular({
      "GET /usuarios": () => {
        lecturas++;
        return lista(USUARIOS);
      },
      "POST /usuarios/invitaciones": () => new Response(JSON.stringify({ resultado: "ACEPTADA", mensaje: "otro texto" }), { status: 202 }),
    });
    render(<PaginaUsuarios />);
    await screen.findByRole("table");
    const invitar = screen.getByRole("button", { name: "Invitar usuario" });
    fireEvent.click(invitar);
    const dialogo = screen.getByRole("dialog", { name: "Invitar usuario" });
    fireEvent.change(within(dialogo).getByRole("textbox", { name: "Nombre completo" }), { target: { value: "Persona Invitada" } });
    fireEvent.change(within(dialogo).getByRole("textbox", { name: "Correo electrónico" }), { target: { value: "persona@ejemplo.com" } });
    const antes = lecturas;
    fireEvent.click(within(dialogo).getByRole("button", { name: "Enviar invitación" }));
    expect(await screen.findByText(AVISO_INVITACION)).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(screen.queryByText(/Invitación enviada a/)).toBeNull();
    await waitFor(() => expect(lecturas).toBeGreaterThan(antes));
    expect(invitar).toHaveFocus();
  });

  it("suspender: foco inicial en «Cancelar», confirmar llama POST …/suspension y la fila queda «Suspendido» (A10.4)", async () => {
    const id = USUARIOS[1].id;
    const f = simular({
      "GET /usuarios": () => lista(USUARIOS),
      [`POST /usuarios/${id}/suspension`]: () => Response.json({ ...USUARIOS[1], estado: "SUSPENDIDO", acciones: ["REACTIVAR"] }),
    });
    render(<PaginaUsuarios />);
    await screen.findByRole("table");
    const disparador = within(filaDe("Usuario Uno")).getByRole("button", { name: /^Acciones/ });
    fireEvent.click(disparador);
    fireEvent.click(within(screen.getByRole("menu")).getByRole("menuitem", { name: /^Suspender/ }));
    const dialogo = screen.getByRole("dialog", { name: /Suspender/ });
    await waitFor(() => expect(within(dialogo).getByRole("button", { name: "Cancelar" })).toHaveFocus());
    expect(dialogo).toHaveTextContent(/no podrá entrar a Diagnóstico/);
    fireEvent.click(within(dialogo).getByRole("button", { name: "Suspender" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(pedidos(f, "/suspension")).toEqual([`POST /api/v1/diagnostico/usuarios/${id}/suspension`]);
    expect(filaDe("Usuario Uno")).toHaveTextContent("Suspendido");
  });

  it("suspender con 409 ULTIMO_ADMINISTRADOR: el error queda en el diálogo, que no se cierra", async () => {
    const id = USUARIOS[1].id;
    simular({
      "GET /usuarios": () => lista(USUARIOS),
      [`POST /usuarios/${id}/suspension`]: () => Response.json({ code: "ULTIMO_ADMINISTRADOR", error: "texto del servidor" }, { status: 409 }),
    });
    render(<PaginaUsuarios />);
    await screen.findByRole("table");
    fireEvent.click(within(filaDe("Usuario Uno")).getByRole("button", { name: /^Acciones/ }));
    fireEvent.click(within(screen.getByRole("menu")).getByRole("menuitem", { name: /^Suspender/ }));
    const dialogo = screen.getByRole("dialog", { name: /Suspender/ });
    fireEvent.click(within(dialogo).getByRole("button", { name: "Suspender" }));
    expect(await within(dialogo).findByText(/al menos un administrador/i)).toBeInTheDocument();
    expect(within(dialogo).queryByText("texto del servidor")).toBeNull();
  });

  it("reactivar: DELETE …/suspension y la fila vuelve a «Activo»", async () => {
    const id = USUARIOS[2].id;
    const f = simular({
      "GET /usuarios": () => lista(USUARIOS),
      [`DELETE /usuarios/${id}/suspension`]: () => Response.json({ ...USUARIOS[2], estado: "ACTIVO", acciones: ["CAMBIAR_ROL", "SUSPENDER"] }),
    });
    render(<PaginaUsuarios />);
    await screen.findByRole("table");
    fireEvent.click(within(filaDe("Usuario Dos")).getByRole("button", { name: /^Acciones/ }));
    fireEvent.click(within(screen.getByRole("menu")).getByRole("menuitem", { name: /^Reactivar/ }));
    fireEvent.click(within(screen.getByRole("dialog", { name: /Reactivar/ })).getByRole("button", { name: "Reactivar" }));
    await waitFor(() => expect(filaDe("Usuario Dos")).toHaveTextContent("Activo"));
    expect(pedidos(f, "/suspension")).toEqual([`DELETE /api/v1/diagnostico/usuarios/${id}/suspension`]);
  });

  it("cambiar rol: solo los roles asignables, «Guardar rol» deshabilitado sin cambio y PUT …/rol con el código", async () => {
    const id = USUARIOS[1].id;
    const f = simular({
      "GET /usuarios": () => lista(USUARIOS, ["usuario_diagnostico", "admin_diagnostico"]),
      [`PUT /usuarios/${id}/rol`]: () => Response.json({ ...USUARIOS[1], rol: "admin_diagnostico", rol_etiqueta: "Administrador de Diagnóstico" }),
    });
    render(<PaginaUsuarios />);
    await screen.findByRole("table");
    fireEvent.click(within(filaDe("Usuario Uno")).getByRole("button", { name: /^Acciones/ }));
    fireEvent.click(within(screen.getByRole("menu")).getByRole("menuitem", { name: /^Cambiar rol/ }));
    const dialogo = screen.getByRole("dialog", { name: /Cambiar rol de Usuario Uno/ });
    expect(within(dialogo).getAllByRole("radio")).toHaveLength(2);
    expect(within(dialogo).getByRole("radio", { name: /Usuario de Diagnóstico/ })).toBeChecked();
    const guardar = within(dialogo).getByRole("button", { name: "Guardar rol" });
    expect(guardar).toBeDisabled();
    fireEvent.click(within(dialogo).getByRole("radio", { name: /Administrador de Diagnóstico/ }));
    fireEvent.click(guardar);
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    const put = f.mock.calls.find(([, init]) => (init as RequestInit | undefined)?.method === "PUT")!;
    expect(JSON.parse(String((put[1] as RequestInit).body))).toEqual({ rol: "admin_diagnostico" });
    expect(filaDe("Usuario Uno")).toHaveTextContent("Administrador de Diagnóstico");
  });
});

describe("HU14588 PaginaUsuarios — SuperAdmin (spec-ui §10.2)", () => {
  it("sin ?empresa ve la tabla de compañías y no pide /usuarios", async () => {
    sesionMock.actual = sesion({ superAdmin: true });
    const f = simular({
      "GET /backoffice/usuarios/empresas": () =>
        Response.json({ pagina: 1, tamano: 20, total: 1, empresas: [{ ...EMPRESA, estado: "ACTIVA", usuarios_diagnostico: 4, administradores_diagnostico: 1 }] }),
    });
    render(<PaginaUsuarios />);
    expect(await screen.findByRole("link", { name: "Gestionar usuarios de Compañía Demo" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Invitar usuario" })).toBeNull();
    expect(pedidos(f, "/usuarios").filter((c) => !c.includes("/backoffice/usuarios/empresas"))).toEqual([]);
  });

  it("con ?empresa usa el grupo del backoffice, migas «Usuarios › Compañía» y ofrece los dos roles", async () => {
    sesionMock.actual = sesion({ superAdmin: true });
    nav.params = new URLSearchParams({ empresa: EMPRESA.id, busqueda: "demo" });
    const f = simular({
      "GET /backoffice/usuarios/empresas": () =>
        Response.json({ pagina: 1, tamano: 20, total: 1, empresas: [{ ...EMPRESA, estado: "ACTIVA", usuarios_diagnostico: 4, administradores_diagnostico: 1 }] }),
      [`GET /backoffice/empresas/${EMPRESA.id}/usuarios`]: () =>
        Response.json({ pagina: 1, tamano: 25, total: USUARIOS.length, roles_asignables: ["usuario_diagnostico", "admin_diagnostico"], usuarios: USUARIOS, empresa: EMPRESA }),
    });
    render(<PaginaUsuarios />);
    await screen.findByRole("table");
    expect(pedidos(f, `/backoffice/empresas/${EMPRESA.id}/usuarios`).length).toBeGreaterThan(0);
    expect(pedidos(f, "GET /api/v1/diagnostico/usuarios")).toEqual([]);
    const migas = screen.getByRole("navigation", { name: "Migas de pan" });
    const volver = within(migas).getByRole("link", { name: "Usuarios" });
    expect(new URL(volver.getAttribute("href")!, "http://x").searchParams.has("empresa")).toBe(false);
    expect(new URL(volver.getAttribute("href")!, "http://x").searchParams.get("busqueda")).toBe("demo");
    await waitFor(() => expect(migas.querySelector('[aria-current="page"]')).toHaveTextContent("Compañía Demo"));
    fireEvent.click(screen.getByRole("button", { name: "Invitar usuario" }));
    const dialogo = screen.getByRole("dialog", { name: "Invitar usuario" });
    expect(within(dialogo).getAllByRole("radio")).toHaveLength(2);
  });

  it("con ?empresa inexistente (404 EMPRESA_NO_ENCONTRADA) muestra el error con el camino de vuelta", async () => {
    sesionMock.actual = sesion({ superAdmin: true });
    nav.params = new URLSearchParams({ empresa: EMPRESA.id });
    simular({
      "GET /backoffice/usuarios/empresas": () => Response.json({ pagina: 1, tamano: 20, total: 0, empresas: [] }),
      [`GET /backoffice/empresas/${EMPRESA.id}/usuarios`]: () => Response.json({ code: "EMPRESA_NO_ENCONTRADA" }, { status: 404 }),
    });
    render(<PaginaUsuarios />);
    expect(await screen.findByText(/no tiene Diagnóstico activo|no existe/i)).toBeInTheDocument();
    expect(screen.getAllByRole("link", { name: /Usuarios|Volver a compañías/ }).length).toBeGreaterThan(0);
  });
});

// D-6: la cabecera del backoffice sale de `empresa` de GET /backoffice/empresas/{id}/usuarios; la lista pagina en el servidor.
describe("HU14588 PaginaUsuarios — D-6 cabecera de la compañía y paginación", () => {
  const conEmpresa = (empresa: unknown) => () =>
    Response.json({
      pagina: 1,
      tamano: 25,
      total: USUARIOS.length,
      roles_asignables: ["usuario_diagnostico", "admin_diagnostico"],
      usuarios: USUARIOS,
      ...(empresa ? { empresa } : {}),
    });

  it("con ?empresa, nombre y NIT de la cabecera y de las migas salen de `empresa` de la lista, sin pedir las compañías", async () => {
    sesionMock.actual = sesion({ superAdmin: true });
    nav.params = new URLSearchParams({ empresa: EMPRESA.id });
    const f = simular({ [`GET /backoffice/empresas/${EMPRESA.id}/usuarios`]: conEmpresa(EMPRESA) });
    render(<PaginaUsuarios />);
    await screen.findByRole("table");
    expect(screen.getByText(/Compañía Demo · NIT 900000001/)).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Migas de pan" }).querySelector('[aria-current="page"]')).toHaveTextContent("Compañía Demo");
    expect(pedidos(f, "/backoffice/usuarios/empresas")).toEqual([]);
    expect(screen.queryByText(/Compañía seleccionada/)).toBeNull();
  });

  it("sin `empresa` en la respuesta (back anterior): esqueleto en la cabecera, nunca un texto genérico", async () => {
    sesionMock.actual = sesion({ superAdmin: true });
    nav.params = new URLSearchParams({ empresa: EMPRESA.id });
    const f = simular({ [`GET /backoffice/empresas/${EMPRESA.id}/usuarios`]: conEmpresa(null) });
    render(<PaginaUsuarios />);
    await screen.findByRole("table");
    expect(screen.getByRole("status", { name: "Cargando la compañía…" })).toBeInTheDocument();
    expect(screen.queryByText(/Compañía seleccionada/)).toBeNull();
    expect(pedidos(f, "/backoffice/usuarios/empresas")).toEqual([]);
  });

  it("la lista pide tamano=25 por defecto; cambiar de página y de tamaño vuelve a pedir y actualiza «N de M»", async () => {
    const f = simular({
      "GET /usuarios": (url) => {
        const pagina = Number(url.searchParams.get("pagina"));
        const tamano = Number(url.searchParams.get("tamano"));
        const usuarios = [fila(`00000000-0000-4000-8000-0000000009${pagina}1`, `Persona ${pagina}`, { acciones: ["SUSPENDER"] })];
        return Response.json({ pagina, tamano, total: 60, roles_asignables: ["usuario_diagnostico"], usuarios });
      },
    });
    render(<PaginaUsuarios />);
    await screen.findByText("Persona 1");
    const q = () => new URL(String(f.mock.calls.at(-1)![0]), "http://x").searchParams;
    expect([q().get("pagina"), q().get("tamano")]).toEqual(["1", "25"]);
    expect(screen.getByText(/^Mostrando 1 de 60 usuarios$/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Página 2" }));
    await screen.findByText("Persona 2");
    expect([q().get("pagina"), q().get("tamano")]).toEqual(["2", "25"]);
    fireEvent.change(screen.getByRole("combobox", { name: "Filas por página" }), { target: { value: "50" } });
    await screen.findByText("Persona 1");
    expect([q().get("pagina"), q().get("tamano")]).toEqual(["1", "50"]);
    expect(screen.getByText(/^Mostrando 1 de 60 usuarios$/)).toBeInTheDocument();
  });

  it("SuperAdmin sin ?empresa: las compañías piden tamano=25 por defecto", async () => {
    sesionMock.actual = sesion({ superAdmin: true });
    const f = simular({ "GET /backoffice/usuarios/empresas": () => Response.json({ pagina: 1, tamano: 25, total: 0, empresas: [] }) });
    render(<PaginaUsuarios />);
    await screen.findByText("Ninguna compañía tiene Diagnóstico activo");
    expect(new URL(String(f.mock.calls.at(-1)![0]), "http://x").searchParams.get("tamano")).toBe("25");
  });
});

// Code review HU #14588 (CR-2, CR-5, CR-6).
describe("HU14588 PaginaUsuarios — región viva, límites de la URL y foco (CR-2, CR-5, CR-6)", () => {
  const abrirAccion = (nombre: string, accion: RegExp) => {
    const disparador = within(filaDe(nombre)).getByRole("button", { name: /^Acciones/ });
    fireEvent.click(disparador);
    fireEvent.click(within(screen.getByRole("menu")).getByRole("menuitem", { name: accion }));
    return disparador;
  };

  it("CR-2: el aviso vive en una sola región viva (sin otra anidada)", async () => {
    const id = USUARIOS[1].id;
    simular({
      "GET /usuarios": () => lista(USUARIOS),
      [`POST /usuarios/${id}/suspension`]: () => Response.json({ ...USUARIOS[1], estado: "SUSPENDIDO", acciones: ["REACTIVAR"] }),
    });
    render(<PaginaUsuarios />);
    await screen.findByRole("table");
    abrirAccion("Usuario Uno", /^Suspender/);
    fireEvent.click(within(screen.getByRole("dialog", { name: /Suspender/ })).getByRole("button", { name: "Suspender" }));
    const aviso = await screen.findByText(/quedó suspendido/);
    const region = aviso.closest('[aria-live]')!;
    expect(region).toHaveAttribute("aria-atomic", "true");
    expect(region.querySelectorAll('[role="status"], [role="alert"], [aria-live]')).toHaveLength(0);
  });

  it("CR-5: ?tamano=500 se acota a 100 y ?pagina se acota a floor(10000/tamano) al leerlos de la URL", async () => {
    sesionMock.actual = sesion({ superAdmin: true });
    nav.params = new URLSearchParams({ tamano: "500", pagina: "900" });
    const f = simular({ "GET /backoffice/usuarios/empresas": () => Response.json({ pagina: 100, tamano: 100, total: 0, empresas: [] }) });
    render(<PaginaUsuarios />);
    await waitFor(() => expect(f).toHaveBeenCalled());
    const q = new URL(String(f.mock.calls.at(-1)![0]), "http://x").searchParams;
    expect(q.get("tamano")).toBe("100");
    expect(q.get("pagina")).toBe("100");
  });
});

import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { Usuarios } from "@/components/usuarios/Usuarios";
import type { EstadoSesion } from "@/components/ImpuestosShell";
import type { SesionImpuestos } from "@/lib/sesion";

// Uso de ejemplo: <Usuarios /> en /usuarios. El Administrador Impuestos ve los usuarios de su compañía (GET /usuarios);
// el Super Admin sin ?empresa= ve la tabla de compañías y, con ?empresa=<id>, los usuarios de esa compañía (la API recibe
// `empresa`). Invitar, cambiar rol, suspender y reactivar contra impuestos.v1.yaml; los errores con x-flit-literales.

const replace = vi.fn();
let busqueda = new URLSearchParams();
vi.mock("next/navigation", () => ({
  usePathname: () => "/usuarios",
  useRouter: () => ({ replace, push: vi.fn(), refresh: vi.fn() }),
  useSearchParams: () => busqueda,
}));

let estadoSesion: EstadoSesion;
vi.mock("@/components/ImpuestosShell", () => ({ useSesion: () => estadoSesion }));

const YO = "00000000-0000-7000-8000-000000000001";
const PERMISOS_ADMIN = ["impuestos.usuarios.gestionar", "impuestos.configuracion.ver", "impuestos.configuracion.editar", "impuestos.liquidaciones.ver"];

function sesion(rol: "admin" | "gestor" | "superadmin"): SesionImpuestos {
  return {
    usuario: { id: YO, email: "nombre@empresa.com", nombre: "Persona Demo" },
    empresa: { id: "00000000-0000-7000-8000-0000000000e1", nombre: "Compañía Demo" },
    roles: rol === "admin" ? ["admin_impuestos"] : rol === "gestor" ? ["gestor_impuestos"] : [],
    permisos: rol === "admin" ? PERMISOS_ADMIN : rol === "gestor" ? ["impuestos.liquidaciones.ver"] : [],
    esSuperAdmin: rol === "superadmin",
    aprovisionamiento: { estado: "LISTO", firmaConfirmada: true },
  };
}

function conSesion(rol: "admin" | "gestor" | "superadmin") {
  estadoSesion = { sesion: sesion(rol), cargando: false, error: null, recargar: vi.fn() };
}

const ANA = { id: "u-ana", nombre: "Ana Demo", email: "ana@empresa.com", rol: "gestor_impuestos", estado: "ACTIVO" };
const LUIS = { id: "u-luis", nombre: "Luis Demo", email: "luis@empresa.com", rol: "admin_impuestos", estado: "SUSPENDIDO" };
const EVA = { id: "u-eva", nombre: "Eva Demo", email: "eva@empresa.com", rol: "gestor_impuestos", estado: "INVITADO" };
const PROPIO = { id: YO, nombre: "Persona Demo", email: "nombre@empresa.com", rol: "admin_impuestos", estado: "ACTIVO" };

const pagina = (items: unknown[]) => ({ items, total: items.length, page: 1, pageSize: 50 });
const EMPRESAS = { items: [{ id: "e-1", nombre: "Compañía Uno", nit: "900000001", aprovisionamiento: { estado: "LISTO", motivo: null, accion: null, firmaConfirmada: true }, suspendida: false }], total: 1, page: 1, pageSize: 50 };

type Llamada = { url: URL; method: string; body: unknown };
let llamadas: Llamada[];

/** fetch simulado: la lista de usuarios, las compañías del backoffice y las respuestas de cada acción. */
function api(respuestas: { usuarios?: () => Response; accion?: (l: Llamada) => Response } = {}) {
  return vi.fn(async (u: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(u), "http://app");
    const llamada = { url, method: init?.method ?? "GET", body: init?.body ? JSON.parse(String(init.body)) : undefined };
    llamadas.push(llamada);
    if (url.pathname === "/api/v1/impuestos/backoffice/empresas") return Response.json(EMPRESAS);
    if (url.pathname === "/api/v1/impuestos/usuarios" && llamada.method === "GET") return respuestas.usuarios ? respuestas.usuarios() : Response.json(pagina([ANA, LUIS, EVA, PROPIO]));
    return respuestas.accion ? respuestas.accion(llamada) : new Response(null, { status: 204 });
  });
}

const fila = (nombre: string) => screen.getByText(nombre).closest("tr") as HTMLElement;
const gets = () => llamadas.filter((l) => l.method === "GET" && l.url.pathname === "/api/v1/impuestos/usuarios");

async function elegirAccion(nombre: string, accion: string) {
  fireEvent.click(within(fila(nombre)).getByRole("button", { name: "Acciones del usuario" }));
  fireEvent.click(await screen.findByRole("menuitem", { name: accion }));
}

beforeEach(() => {
  replace.mockReset();
  busqueda = new URLSearchParams();
  llamadas = [];
  conSesion("admin");
});
afterEach(() => vi.unstubAllGlobals());

describe("Usuarios — Administrador Impuestos (su compañía)", () => {
  it("pide GET /usuarios sin empresa y pinta Nombre · Correo · Rol · Estado con las etiquetas del contrato", async () => {
    vi.stubGlobal("fetch", api());

    render(<Usuarios />);

    expect(await screen.findByText("Ana Demo")).toBeInTheDocument();
    const [primera] = gets();
    expect(primera.url.searchParams.get("empresa")).toBeNull();
    expect(primera.url.searchParams.get("page")).toBe("1");
    expect(screen.getByRole("heading", { name: "Usuarios" })).toBeInTheDocument();
    expect(screen.getByText("Compañía Demo")).toBeInTheDocument();
    const ana = fila("Ana Demo");
    expect(within(ana).getByText("ana@empresa.com")).toBeInTheDocument();
    expect(within(ana).getByText("Gestor Impuestos")).toBeInTheDocument();
    expect(within(ana).getByText("Activo")).toBeInTheDocument();
    expect(within(fila("Luis Demo")).getByText("Administrador Impuestos")).toBeInTheDocument();
    expect(within(fila("Luis Demo")).getByText("Suspendido")).toBeInTheDocument();
    expect(within(fila("Eva Demo")).getByText("Invitado")).toBeInTheDocument();
    expect(screen.queryByText("gestor_impuestos")).toBeNull();
    expect(screen.getByRole("button", { name: "Invitar usuario" })).toBeEnabled();
  });

  it("la fila propia dice «Eres tú» y la invitación pendiente su literal, ambas sin menú", async () => {
    vi.stubGlobal("fetch", api());

    render(<Usuarios />);
    await screen.findByText("Ana Demo");

    const propia = screen.getAllByText("Persona Demo").map((el) => el.closest("tr")).find(Boolean) as HTMLElement;
    expect(within(propia).getByText("Eres tú")).toBeInTheDocument();
    expect(within(propia).queryByRole("button", { name: "Acciones del usuario" })).toBeNull();
    expect(within(fila("Eva Demo")).getByText("El usuario todavía no acepta su invitación.")).toBeInTheDocument();
    expect(within(fila("Eva Demo")).queryByRole("button", { name: "Acciones del usuario" })).toBeNull();
  });

  it("activo: Cambiar rol y Suspender; suspendido: Reactivar", async () => {
    vi.stubGlobal("fetch", api());

    render(<Usuarios />);
    await screen.findByText("Ana Demo");

    fireEvent.click(within(fila("Luis Demo")).getByRole("button", { name: "Acciones del usuario" }));
    expect((await screen.findAllByRole("menuitem")).map((m) => m.textContent)).toEqual(["Reactivar"]);
  });

  it("cargando: esqueleto «Cargando usuarios…»", () => {
    vi.stubGlobal("fetch", vi.fn(() => new Promise<Response>(() => {})));

    render(<Usuarios />);

    expect(screen.getByRole("status", { name: "Cargando usuarios…" })).toBeInTheDocument();
  });

  it("error: «No pudimos cargar los usuarios» con Reintentar (sin el mensaje del servidor) y, al reintentar, la tabla", async () => {
    let primera = true;
    vi.stubGlobal(
      "fetch",
      api({
        usuarios: () => {
          if (!primera) return Response.json(pagina([ANA]));
          primera = false;
          return Response.json({ error: "detalle interno del servidor" }, { status: 503 });
        },
      }),
    );

    render(<Usuarios />);

    const alerta = await screen.findByText("No pudimos cargar los usuarios");
    expect(document.body).not.toHaveTextContent("detalle interno del servidor");
    fireEvent.click(within(alerta.closest("div[style]") as HTMLElement).getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByText("Ana Demo")).toBeInTheDocument();
  });

  it("vacío: «Aún no hay usuarios en Impuestos» con Invitar; con búsqueda, «Ningún usuario coincide»", async () => {
    vi.stubGlobal("fetch", api({ usuarios: () => Response.json(pagina([])) }));

    render(<Usuarios />);

    expect(await screen.findByText("Aún no hay usuarios en Impuestos")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Buscar nombre o correo"), { target: { value: "zz" } });
    fireEvent.submit(screen.getByRole("search"));
    expect(await screen.findByText("Ningún usuario coincide")).toBeInTheDocument();
    expect(screen.getByText("Prueba con otro nombre o correo, o quita algún filtro.")).toBeInTheDocument();
    expect(gets().at(-1)?.url.searchParams.get("q")).toBe("zz");
  });

  it("el Gestor (sin impuestos.usuarios.gestionar) va a la 403 y no pide la lista", async () => {
    conSesion("gestor");
    vi.stubGlobal("fetch", api());

    render(<Usuarios />);

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/403?code=SIN_PERMISO"));
    expect(gets()).toHaveLength(0);
  });
});

describe("Invitar usuario", () => {
  it("el Administrador solo ofrece Gestor Impuestos; valida por campo con el resumen y envía POST /usuarios", async () => {
    vi.stubGlobal("fetch", api({ accion: () => Response.json({ ...ANA, id: "u-nuevo" }, { status: 201 }) }));

    render(<Usuarios />);
    await screen.findByText("Ana Demo");
    fireEvent.click(screen.getByRole("button", { name: "Invitar usuario" }));

    const dialogo = screen.getByRole("dialog", { name: "Invitar usuario" });
    expect(within(dialogo).getByText("Le llegará un correo para entrar a Impuestos con el rol que elijas.")).toBeInTheDocument();
    expect(within(dialogo).getAllByRole("radio").map((r) => r.getAttribute("value"))).toEqual(["gestor_impuestos"]);
    expect(within(dialogo).getByText("Ve toda la compañía y liquida por placa o masivo. No gestiona usuarios ni configuración.")).toBeInTheDocument();

    fireEvent.click(within(dialogo).getByRole("button", { name: "Enviar invitación" }));
    expect(await within(dialogo).findByText("Revisa los datos de la invitación")).toBeInTheDocument();
    const nombre = within(dialogo).getByLabelText("Nombre completo");
    expect(nombre).toHaveAttribute("aria-invalid", "true");
    expect(document.getElementById(nombre.getAttribute("aria-describedby")!.split(" ").at(-1)!)).toHaveTextContent("Escribe el nombre completo.");
    expect(llamadas.filter((l) => l.method === "POST")).toHaveLength(0);

    fireEvent.change(nombre, { target: { value: "Nueva Demo" } });
    fireEvent.change(within(dialogo).getByLabelText("Correo electrónico"), { target: { value: "nueva@empresa.com" } });
    fireEvent.click(within(dialogo).getByRole("button", { name: "Enviar invitación" }));

    await waitFor(() => expect(screen.queryByRole("dialog", { name: "Invitar usuario" })).toBeNull());
    const post = llamadas.find((l) => l.method === "POST")!;
    expect(post.url.pathname).toBe("/api/v1/impuestos/usuarios");
    expect(post.url.searchParams.get("empresa")).toBeNull();
    expect(post.body).toEqual({ email: "nueva@empresa.com", nombre: "Nueva Demo", rol: "gestor_impuestos" });
    await waitFor(() => expect(gets().length).toBeGreaterThan(1));
  });

  it("un 409 ROL_YA_VIGENTE se muestra dentro del diálogo con el literal del contrato y el diálogo no se cierra", async () => {
    vi.stubGlobal("fetch", api({ accion: () => Response.json({ code: "ROL_YA_VIGENTE", error: "El usuario ya tiene un rol vigente en Impuestos." }, { status: 409 }) }));

    render(<Usuarios />);
    await screen.findByText("Ana Demo");
    fireEvent.click(screen.getByRole("button", { name: "Invitar usuario" }));
    const dialogo = screen.getByRole("dialog", { name: "Invitar usuario" });
    fireEvent.change(within(dialogo).getByLabelText("Nombre completo"), { target: { value: "Ana Demo" } });
    fireEvent.change(within(dialogo).getByLabelText("Correo electrónico"), { target: { value: "ana@empresa.com" } });
    fireEvent.click(within(dialogo).getByRole("button", { name: "Enviar invitación" }));

    expect(await within(dialogo).findByText("El usuario ya tiene un rol vigente en Impuestos.")).toBeInTheDocument();
    expect(screen.getByRole("dialog", { name: "Invitar usuario" })).toBeInTheDocument();
  });
});

describe("Cambiar rol, suspender y reactivar", () => {
  it("Cambiar rol: «Guardar rol» deshabilitado si no cambia; el Super Admin puede dar Administrador (PUT /rol con empresa)", async () => {
    conSesion("superadmin");
    busqueda = new URLSearchParams("empresa=e-1");
    vi.stubGlobal("fetch", api());

    render(<Usuarios />);
    await screen.findByText("Ana Demo");
    expect(gets()[0].url.searchParams.get("empresa")).toBe("e-1");
    await elegirAccion("Ana Demo", "Cambiar rol");

    const dialogo = screen.getByRole("dialog", { name: "Cambiar rol de Ana Demo" });
    const guardar = within(dialogo).getByRole("button", { name: "Guardar rol" });
    expect(guardar).toBeDisabled();
    fireEvent.click(within(dialogo).getByRole("radio", { name: /Administrador Impuestos/ }));
    expect(guardar).toBeEnabled();
    fireEvent.click(guardar);

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    const put = llamadas.find((l) => l.method === "PUT")!;
    expect(put.url.pathname).toBe("/api/v1/impuestos/usuarios/u-ana/rol");
    expect(put.url.searchParams.get("empresa")).toBe("e-1");
    expect(put.body).toEqual({ rol: "admin_impuestos" });
  });

  it("Suspender: título, cuerpo del spec, foco inicial en Cancelar, acción de alerta y POST /suspender", async () => {
    vi.stubGlobal("fetch", api());

    render(<Usuarios />);
    await screen.findByText("Ana Demo");
    await elegirAccion("Ana Demo", "Suspender");

    const dialogo = screen.getByRole("dialog", { name: "Suspender usuario" });
    expect(within(dialogo).getByText("Pierde el acceso a Impuestos. Su cuenta y los demás productos no cambian.")).toBeInTheDocument();
    await waitFor(() => expect(within(dialogo).getByRole("button", { name: "Cancelar" })).toHaveFocus());
    fireEvent.click(within(dialogo).getByRole("button", { name: "Suspender" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    const post = llamadas.find((l) => l.method === "POST")!;
    expect(post.url.pathname).toBe("/api/v1/impuestos/usuarios/u-ana/suspender");
  });

  it("un 409 ULTIMO_ADMINISTRADOR queda dentro del diálogo con el literal del contrato", async () => {
    vi.stubGlobal("fetch", api({ accion: () => Response.json({ code: "ULTIMO_ADMINISTRADOR", error: "x" }, { status: 409 }) }));

    render(<Usuarios />);
    await screen.findByText("Ana Demo");
    await elegirAccion("Ana Demo", "Suspender");
    const dialogo = screen.getByRole("dialog", { name: "Suspender usuario" });
    fireEvent.click(within(dialogo).getByRole("button", { name: "Suspender" }));

    expect(await within(dialogo).findByText("La compañía debe conservar al menos un Administrador Impuestos.")).toBeInTheDocument();
  });

  it("Reactivar: POST /reactivar y relee la lista", async () => {
    vi.stubGlobal("fetch", api());

    render(<Usuarios />);
    await screen.findByText("Luis Demo");
    await elegirAccion("Luis Demo", "Reactivar");
    fireEvent.click(within(screen.getByRole("dialog", { name: "Reactivar usuario" })).getByRole("button", { name: "Reactivar" }));

    await waitFor(() => expect(llamadas.some((l) => l.method === "POST" && l.url.pathname === "/api/v1/impuestos/usuarios/u-luis/reactivar")).toBe(true));
    await waitFor(() => expect(gets().length).toBeGreaterThan(1));
  });
});

describe("Super Admin", () => {
  it("sin ?empresa= ve la tabla de compañías con «Ver usuarios», que fija ?empresa= (y el selector)", async () => {
    conSesion("superadmin");
    vi.stubGlobal("fetch", api());

    render(<Usuarios />);

    expect(await screen.findByText("Compañía Uno")).toBeInTheDocument();
    expect(gets()).toHaveLength(0);
    expect(within(screen.getByText("Compañía Uno").closest("tr") as HTMLElement).getByText("900000001")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Ver usuarios de Compañía Uno" }));

    expect(replace).toHaveBeenCalledWith("/usuarios?empresa=e-1");
  });

  it("con ?empresa= la miga dice «Usuarios › {Compañía}» y ofrece los dos roles al invitar", async () => {
    conSesion("superadmin");
    busqueda = new URLSearchParams("empresa=e-1");
    vi.stubGlobal("fetch", api());

    render(<Usuarios />);
    await screen.findByText("Ana Demo");

    const miga = await screen.findByRole("navigation", { name: /miga/i });
    expect(miga).toHaveTextContent("Usuarios");
    expect(await within(miga).findByText("Compañía Uno")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Invitar usuario" }));
    expect(within(screen.getByRole("dialog", { name: "Invitar usuario" })).getAllByRole("radio").map((r) => r.getAttribute("value"))).toEqual([
      "admin_impuestos",
      "gestor_impuestos",
    ]);
  });
});

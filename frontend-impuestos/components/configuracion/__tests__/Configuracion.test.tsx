import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { Configuracion } from "@/components/configuracion/Configuracion";
import type { EstadoSesion } from "@/components/ImpuestosShell";
import type { SesionImpuestos } from "@/lib/sesion";

// Uso de ejemplo: <Configuracion /> en /configuracion. Pestaña «Operativa» (Administrador Impuestos y Super Admin:
// máximo por lote ≤ contractual y correos del aviso) y «Contractual» (solo lectura para el Administrador, editable por el
// Super Admin con la compañía de ?empresa=). GET/PUT /configuracion y PUT /backoffice/empresas/{id}/configuracion-contractual.

const replace = vi.fn();
let busqueda = new URLSearchParams();
vi.mock("next/navigation", () => ({
  usePathname: () => "/configuracion",
  useRouter: () => ({ replace, push: vi.fn(), refresh: vi.fn() }),
  useSearchParams: () => busqueda,
}));

let estadoSesion: EstadoSesion;
vi.mock("@/components/ImpuestosShell", () => ({ useSesion: () => estadoSesion }));

function conSesion(rol: "admin" | "admin-lectura" | "gestor" | "superadmin") {
  const permisos =
    rol === "admin"
      ? ["impuestos.configuracion.ver", "impuestos.configuracion.editar"]
      : rol === "admin-lectura"
        ? ["impuestos.configuracion.ver"]
        : rol === "gestor"
          ? ["impuestos.liquidaciones.ver"]
          : [];
  const sesion: SesionImpuestos = {
    usuario: { id: "u-1", email: "nombre@empresa.com", nombre: "Persona Demo" },
    empresa: { id: "e-propia", nombre: "Compañía Demo" },
    roles: [],
    permisos,
    esSuperAdmin: rol === "superadmin",
    aprovisionamiento: { estado: "LISTO", firmaConfirmada: true },
  };
  estadoSesion = { sesion, cargando: false, error: null, recargar: vi.fn() };
}

const DEPARTAMENTOS = [
  { codigo: "25", nombre: "Cundinamarca", activa: true },
  { codigo: "05", nombre: "Antioquia", activa: true },
];

function configuracion(over: { maxContractual?: number; departamentos?: string[] | null; maxOperativo?: number | null; correos?: string[] } = {}) {
  return {
    contractual: { maxRegistrosLote: over.maxContractual ?? 300, departamentosHabilitados: over.departamentos === undefined ? null : over.departamentos },
    operativa: { maxRegistrosLote: over.maxOperativo === undefined ? 100 : over.maxOperativo, correosAviso: over.correos ?? ["aviso@empresa.com"] },
    departamentosDisponibles: DEPARTAMENTOS,
  };
}

type Llamada = { url: URL; method: string; body: unknown };
let llamadas: Llamada[];

function api(opciones: { get?: () => Response; put?: (l: Llamada) => Response } = {}) {
  return vi.fn(async (u: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(u), "http://app");
    const llamada = { url, method: init?.method ?? "GET", body: init?.body ? JSON.parse(String(init.body)) : undefined };
    llamadas.push(llamada);
    if (llamada.method === "GET") return opciones.get ? opciones.get() : Response.json(configuracion());
    return opciones.put ? opciones.put(llamada) : Response.json(configuracion({ maxOperativo: (llamada.body as { maxRegistrosLote?: number }).maxRegistrosLote ?? null }));
  });
}

const campoMaximo = () => screen.getByLabelText("Máximo de placas por lote");
const campoCorreos = () => screen.getByLabelText("Correos para el aviso de lote terminado");
const errorDe = (campo: HTMLElement) =>
  (campo.getAttribute("aria-describedby") ?? "")
    .split(" ")
    .map((id) => document.getElementById(id)?.textContent ?? "")
    .join(" | ");

beforeEach(() => {
  replace.mockReset();
  busqueda = new URLSearchParams();
  llamadas = [];
  conSesion("admin");
});
afterEach(() => vi.unstubAllGlobals());

describe("Configuración — Operativa (Administrador Impuestos)", () => {
  it("pide GET /configuracion sin empresa y abre «Operativa» con el máximo acotado al contractual", async () => {
    vi.stubGlobal("fetch", api());

    render(<Configuracion />);

    const campo = await screen.findByLabelText("Máximo de placas por lote");
    expect(llamadas[0].url.pathname).toBe("/api/v1/impuestos/configuracion");
    expect(llamadas[0].url.searchParams.get("empresa")).toBeNull();
    expect(screen.getByRole("heading", { name: "Configuración" })).toBeInTheDocument();
    expect(screen.getByRole("tablist", { name: "Configuración de Impuestos" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Operativa" })).toHaveAttribute("aria-selected", "true");
    expect(campo).toHaveAttribute("type", "number");
    expect(campo).toHaveAttribute("min", "1");
    expect(campo).toHaveAttribute("max", "300");
    expect(campo).toHaveValue(100);
    expect(errorDe(campo)).toContain("Hasta 300, lo contratado.");
    expect(campoCorreos()).toHaveValue("aviso@empresa.com");
    expect(screen.getByText("Los cambios aplican a los lotes nuevos.")).toBeInTheDocument();
  });

  it("tope contractual: un máximo por encima no se envía y el campo dice el literal con el tope", async () => {
    vi.stubGlobal("fetch", api());

    render(<Configuracion />);
    const campo = await screen.findByLabelText("Máximo de placas por lote");
    fireEvent.change(campo, { target: { value: "301" } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar configuración" }));

    await waitFor(() => expect(campo).toHaveAttribute("aria-invalid", "true"));
    expect(errorDe(campo)).toContain("El máximo por lote no puede superar el tope contractual de 300.");
    expect(llamadas.filter((l) => l.method === "PUT")).toHaveLength(0);
  });

  it("tope contractual: un 422 TOPE_SUPERA_CONTRACTUAL va junto al campo con el `tope` de la respuesta y conserva lo digitado", async () => {
    vi.stubGlobal("fetch", api({ put: () => Response.json({ code: "TOPE_SUPERA_CONTRACTUAL", error: "x", campo: "maxRegistrosLote", tope: 250 }, { status: 422 }) }));

    render(<Configuracion />);
    const campo = await screen.findByLabelText("Máximo de placas por lote");
    fireEvent.change(campo, { target: { value: "280" } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar configuración" }));

    await waitFor(() => expect(errorDe(campo)).toContain("El máximo por lote no puede superar el tope contractual de 250."));
    expect(campo).toHaveAttribute("aria-invalid", "true");
    expect(campo).toHaveValue(280);
  });

  it("guardar: PUT /configuracion con el máximo y un correo por línea, y «Configuración guardada.»", async () => {
    vi.stubGlobal("fetch", api());

    render(<Configuracion />);
    const campo = await screen.findByLabelText("Máximo de placas por lote");
    fireEvent.change(campo, { target: { value: "200" } });
    fireEvent.change(campoCorreos(), { target: { value: "uno@empresa.com\n\n dos@empresa.com " } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar configuración" }));

    expect(await screen.findByText("Configuración guardada.")).toBeInTheDocument();
    const put = llamadas.find((l) => l.method === "PUT")!;
    expect(put.url.pathname).toBe("/api/v1/impuestos/configuracion");
    expect(put.body).toEqual({ maxRegistrosLote: 200, correosAviso: ["uno@empresa.com", "dos@empresa.com"] });
  });

  it("máximo vacío viaja como null (usa el contractual)", async () => {
    vi.stubGlobal("fetch", api());

    render(<Configuracion />);
    fireEvent.change(await screen.findByLabelText("Máximo de placas por lote"), { target: { value: "" } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar configuración" }));

    await waitFor(() => expect(llamadas.find((l) => l.method === "PUT")?.body).toEqual({ maxRegistrosLote: null, correosAviso: ["aviso@empresa.com"] }));
  });

  it("un correo inválido se señala por línea y no se envía", async () => {
    vi.stubGlobal("fetch", api());

    render(<Configuracion />);
    await screen.findByLabelText("Máximo de placas por lote");
    fireEvent.change(campoCorreos(), { target: { value: "uno@empresa.com\nno-es-correo" } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar configuración" }));

    await waitFor(() => expect(campoCorreos()).toHaveAttribute("aria-invalid", "true"));
    expect(errorDe(campoCorreos())).toContain("La línea 2 no es un correo válido.");
    expect(llamadas.filter((l) => l.method === "PUT")).toHaveLength(0);
  });

  it("«Descartar» vuelve a lo guardado", async () => {
    vi.stubGlobal("fetch", api());

    render(<Configuracion />);
    const campo = await screen.findByLabelText("Máximo de placas por lote");
    fireEvent.change(campo, { target: { value: "5" } });
    fireEvent.click(screen.getByRole("button", { name: "Descartar" }));

    expect(campo).toHaveValue(100);
  });

  it("sin correos, la ayuda del estado vacío", async () => {
    vi.stubGlobal("fetch", api({ get: () => Response.json(configuracion({ correos: [] })) }));

    render(<Configuracion />);

    expect(await screen.findByText("Nadie recibe el aviso por correo; el aviso en pantalla sigue llegando.")).toBeInTheDocument();
  });

  it("guardar falla: «No se guardó la configuración» sin el mensaje del servidor y conserva lo digitado", async () => {
    vi.stubGlobal("fetch", api({ put: () => Response.json({ error: "detalle interno" }, { status: 500 }) }));

    render(<Configuracion />);
    const campo = await screen.findByLabelText("Máximo de placas por lote");
    fireEvent.change(campo, { target: { value: "150" } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar configuración" }));

    expect(await screen.findByText("No se guardó la configuración")).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent("detalle interno");
    expect(campo).toHaveValue(150);
  });

  it("sin impuestos.configuracion.editar: «Solo lectura» y campos deshabilitados, sin botón de guardar", async () => {
    conSesion("admin-lectura");
    vi.stubGlobal("fetch", api());

    render(<Configuracion />);

    expect(await screen.findByText("Solo lectura")).toBeInTheDocument();
    expect(screen.getByText("Puedes ver la configuración, pero no cambiarla.")).toBeInTheDocument();
    expect(campoMaximo()).toBeDisabled();
    expect(campoCorreos()).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Guardar configuración" })).toBeNull();
  });
});

describe("Configuración — Contractual", () => {
  it("el Administrador la ve en solo lectura: máximo contratado y «Todos los departamentos», sin «Guardar contrato»", async () => {
    vi.stubGlobal("fetch", api());

    render(<Configuracion />);
    fireEvent.click(await screen.findByRole("tab", { name: "Contractual" }));

    const panel = screen.getByRole("tabpanel");
    expect(within(panel).getByText("Máximo de placas por lote contratado")).toBeInTheDocument();
    expect(within(panel).getByText("300")).toBeInTheDocument();
    expect(within(panel).getByText("Todos los departamentos")).toBeInTheDocument();
    expect(within(panel).queryByRole("button", { name: "Guardar contrato" })).toBeNull();
    expect(within(panel).queryByRole("spinbutton")).toBeNull();
  });

  it("con departamentos habilitados pinta sus nombres como chips", async () => {
    vi.stubGlobal("fetch", api({ get: () => Response.json(configuracion({ departamentos: ["25"] })) }));

    render(<Configuracion />);
    fireEvent.click(await screen.findByRole("tab", { name: "Contractual" }));

    expect(within(screen.getByRole("tabpanel")).getByText("Cundinamarca")).toBeInTheDocument();
    expect(within(screen.getByRole("tabpanel")).queryByText("Antioquia")).toBeNull();
  });

  it("?pestana=contractual abre esa pestaña", async () => {
    busqueda = new URLSearchParams("pestana=contractual");
    vi.stubGlobal("fetch", api());

    render(<Configuracion />);

    expect(await screen.findByRole("tab", { name: "Contractual" })).toHaveAttribute("aria-selected", "true");
  });

  it("Super Admin con ?empresa=: GET con empresa y guarda el contrato en PUT /backoffice/empresas/{id}/configuracion-contractual", async () => {
    conSesion("superadmin");
    busqueda = new URLSearchParams("empresa=e-1&pestana=contractual");
    vi.stubGlobal("fetch", api());

    render(<Configuracion />);

    const maximo = await screen.findByLabelText("Máximo de placas por lote contratado");
    expect(llamadas[0].url.searchParams.get("empresa")).toBe("e-1");
    const todos = screen.getByRole("switch", { name: "Todos los departamentos" });
    expect(todos).toHaveAttribute("aria-checked", "true");
    fireEvent.click(todos);
    fireEvent.click(screen.getByRole("checkbox", { name: "Antioquia" }));
    fireEvent.change(maximo, { target: { value: "800" } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar contrato" }));

    await waitFor(() => expect(llamadas.some((l) => l.method === "PUT")).toBe(true));
    const put = llamadas.find((l) => l.method === "PUT")!;
    expect(put.url.pathname).toBe("/api/v1/impuestos/backoffice/empresas/e-1/configuracion-contractual");
    expect(put.body).toEqual({ maxRegistrosLote: 800, departamentosHabilitados: ["05"] });
  });

  it("Super Admin: «Todos los departamentos» encendido envía null; un 422 DEPARTAMENTO_NO_DISPONIBLE muestra su literal", async () => {
    conSesion("superadmin");
    busqueda = new URLSearchParams("empresa=e-1&pestana=contractual");
    vi.stubGlobal("fetch", api({ put: () => Response.json({ code: "DEPARTAMENTO_NO_DISPONIBLE", error: "x" }, { status: 422 }) }));

    render(<Configuracion />);
    await screen.findByLabelText("Máximo de placas por lote contratado");
    fireEvent.click(screen.getByRole("button", { name: "Guardar contrato" }));

    expect(await screen.findByText("Un departamento no está en la lista del proveedor.")).toBeInTheDocument();
    expect(llamadas.find((l) => l.method === "PUT")?.body).toEqual({ maxRegistrosLote: 300, departamentosHabilitados: null });
  });

  it("Super Admin: un 409 APROVISIONAMIENTO_NO_LISTO al guardar el contrato muestra el literal del contrato 1.1.0", async () => {
    conSesion("superadmin");
    busqueda = new URLSearchParams("empresa=e-1&pestana=contractual");
    vi.stubGlobal("fetch", api({ put: () => Response.json({ code: "APROVISIONAMIENTO_NO_LISTO", error: "detalle interno" }, { status: 409 }) }));

    render(<Configuracion />);
    await screen.findByLabelText("Máximo de placas por lote contratado");
    fireEvent.click(screen.getByRole("button", { name: "Guardar contrato" }));

    expect(
      await screen.findByText("La compañía todavía no puede liquidar porque su configuración con el proveedor no está lista."),
    ).toBeInTheDocument();
    expect(screen.queryByText("detalle interno")).not.toBeInTheDocument();
  });

  it("Super Admin sin compañía elegida: «Elige una compañía en la barra para editar su contrato» y no pide nada", async () => {
    conSesion("superadmin");
    vi.stubGlobal("fetch", api());

    render(<Configuracion />);

    expect(await screen.findByText("Elige una compañía en la barra para editar su contrato")).toBeInTheDocument();
    expect(llamadas).toHaveLength(0);
  });
});

describe("Configuración — estados y permisos", () => {
  it("cargando: «Cargando la configuración…»", () => {
    vi.stubGlobal("fetch", vi.fn(() => new Promise<Response>(() => {})));

    render(<Configuracion />);

    expect(screen.getByRole("status", { name: "Cargando la configuración…" })).toBeInTheDocument();
  });

  it("error: «No se pudo cargar la configuración» con Reintentar", async () => {
    let primera = true;
    vi.stubGlobal(
      "fetch",
      api({
        get: () => {
          if (!primera) return Response.json(configuracion());
          primera = false;
          return Response.json({ error: "detalle interno" }, { status: 503 });
        },
      }),
    );

    render(<Configuracion />);

    expect(await screen.findByText("No se pudo cargar la configuración")).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent("detalle interno");
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByLabelText("Máximo de placas por lote")).toBeInTheDocument();
  });

  it("un 403 EMPRESA_SOLO_SUPER_ADMIN al cargar muestra el literal del contrato 1.1.0, no el texto genérico", async () => {
    vi.stubGlobal("fetch", api({ get: () => Response.json({ code: "EMPRESA_SOLO_SUPER_ADMIN", error: "detalle interno" }, { status: 403 }) }));

    render(<Configuracion />);

    expect(await screen.findByText("Solo el Super Admin puede elegir la compañía.")).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent("No tienes permiso para esta acción.");
    expect(document.body).not.toHaveTextContent("detalle interno");
  });

  it("el alias retirado EMPRESA_NO_PERMITIDA recibe el mismo literal", async () => {
    vi.stubGlobal("fetch", api({ get: () => Response.json({ code: "EMPRESA_NO_PERMITIDA", error: "x" }, { status: 400 }) }));

    render(<Configuracion />);

    expect(await screen.findByText("Solo el Super Admin puede elegir la compañía.")).toBeInTheDocument();
  });

  it("un 400 EMPRESA_REQUERIDA al guardar la operativa muestra «Elige una compañía.»", async () => {
    vi.stubGlobal("fetch", api({ put: () => Response.json({ code: "EMPRESA_REQUERIDA", error: "x" }, { status: 400 }) }));

    render(<Configuracion />);
    fireEvent.change(await screen.findByLabelText("Máximo de placas por lote"), { target: { value: "120" } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar configuración" }));

    expect(await screen.findByText("Elige una compañía.")).toBeInTheDocument();
  });

  it("Super Admin: GET /configuracion?empresa= con 404 EMPRESA_NO_ENCONTRADA muestra el literal en línea, sin ir a la 403", async () => {
    conSesion("superadmin");
    busqueda = new URLSearchParams("empresa=e-inexistente");
    vi.stubGlobal(
      "fetch",
      vi.fn(async (u: RequestInfo | URL, init?: RequestInit) => {
        const url = new URL(String(u), "http://app");
        llamadas.push({ url, method: init?.method ?? "GET", body: undefined });
        if (url.pathname === "/api/v1/impuestos/backoffice/empresas") return Response.json({ items: [], total: 0, page: 1, pageSize: 200 });
        return Response.json({ code: "EMPRESA_NO_ENCONTRADA", error: "detalle interno" }, { status: 404 });
      }),
    );

    render(<Configuracion />);

    expect(await screen.findByText("No se pudo cargar la configuración")).toBeInTheDocument();
    expect(screen.getByText("La compañía no existe o no tiene Impuestos.")).toBeInTheDocument();
    expect(llamadas.find((l) => l.url.pathname === "/api/v1/impuestos/configuracion")?.url.searchParams.get("empresa")).toBe("e-inexistente");
    expect(replace).not.toHaveBeenCalled();
    expect(document.body).not.toHaveTextContent("detalle interno");
  });

  it("PUT /configuracion con 404 EMPRESA_NO_ENCONTRADA: «No se guardó la configuración» con el literal y conserva lo digitado", async () => {
    vi.stubGlobal("fetch", api({ put: () => Response.json({ code: "EMPRESA_NO_ENCONTRADA", error: "x" }, { status: 404 }) }));

    render(<Configuracion />);
    fireEvent.change(await screen.findByLabelText("Máximo de placas por lote"), { target: { value: "120" } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar configuración" }));

    expect(await screen.findByText("La compañía no existe o no tiene Impuestos.")).toBeInTheDocument();
    expect(screen.getByText("No se guardó la configuración")).toBeInTheDocument();
    expect(campoMaximo()).toHaveValue(120);
    expect(replace).not.toHaveBeenCalled();
  });

  it("PUT /configuracion con 403 EMPRESA_SOLO_SUPER_ADMIN: literal en línea, nunca la 403 ni el texto genérico", async () => {
    vi.stubGlobal("fetch", api({ put: () => Response.json({ code: "EMPRESA_SOLO_SUPER_ADMIN", error: "x" }, { status: 403 }) }));

    render(<Configuracion />);
    fireEvent.change(await screen.findByLabelText("Máximo de placas por lote"), { target: { value: "120" } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar configuración" }));

    expect(await screen.findByText("Solo el Super Admin puede elegir la compañía.")).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent("No tienes permiso para esta acción.");
    expect(replace).not.toHaveBeenCalled();
  });

  it("el Gestor (sin impuestos.configuracion.ver) va a la 403", async () => {
    conSesion("gestor");
    vi.stubGlobal("fetch", api());

    render(<Configuracion />);

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/403?code=SIN_PERMISO"));
    expect(llamadas).toHaveLength(0);
  });
});

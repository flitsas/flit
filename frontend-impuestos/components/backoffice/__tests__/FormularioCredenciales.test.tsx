import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { DetalleAprovisionamiento } from "@/components/backoffice/DetalleAprovisionamiento";
import { EmpresasAprovisionamiento } from "@/components/backoffice/EmpresasAprovisionamiento";
import { FormularioCredenciales } from "@/components/backoffice/FormularioCredenciales";
import type { EstadoSesion } from "@/components/ImpuestosShell";

// Uso de ejemplo: <FormularioCredenciales tenantId="…" estado="REQUIERE_CREDENCIALES" onGuardado={releer} /> en el detalle
// del backoffice del Super Admin. Lee GET /backoffice/empresas/{id}/credenciales (nunca los valores, solo si están) y carga
// api_key y webhook_secret con PUT …/credenciales en campos ocultos que se vacían al guardar o cancelar.

const replace = vi.fn();
// En Next notFound() lanza; aquí solo se registra la llamada (la pantalla no pinta nada después).
const notFound = vi.fn();
let busqueda = new URLSearchParams();
vi.mock("next/navigation", () => ({
  usePathname: () => "/backoffice/empresas",
  useRouter: () => ({ replace, push: vi.fn(), refresh: vi.fn() }),
  useSearchParams: () => busqueda,
  notFound: () => notFound(),
}));

let estadoSesion: EstadoSesion;
vi.mock("@/components/ImpuestosShell", () => ({ useSesion: () => estadoSesion }));

function conSesion(superAdmin: boolean) {
  estadoSesion = {
    sesion: {
      usuario: { id: "u-1", email: "nombre@empresa.com", nombre: "Persona Demo" },
      empresa: { id: "e-flit", nombre: "FLIT" },
      roles: [],
      permisos: superAdmin ? [] : ["impuestos.usuarios.gestionar"],
      esSuperAdmin: superAdmin,
      aprovisionamiento: null,
    },
    cargando: false,
    error: null,
    recargar: vi.fn(),
  };
}

const TENANT = "00000000-0000-7000-8000-0000000000e1";
const API_KEY = "fk_live_valor_secreto_de_prueba_0001"; // gitleaks:allow
const SECRETO = "whsec_valor_secreto_de_prueba_0002"; // gitleaks:allow
const CONFIGURADA = { tieneApiKey: true, tieneWebhookSecret: false, origen: "COPIA_DIAGNOSTICO", apiKeyPrefijo: "fk_live_", actualizadoEn: "2026-11-20T15:00:00Z" };
const SIN_NADA = { tieneApiKey: false, tieneWebhookSecret: false, origen: null, apiKeyPrefijo: null, actualizadoEn: null };

type Llamada = { url: URL; method: string; body: unknown };
let llamadas: Llamada[];

function api(opciones: { credenciales?: () => Response; put?: (l: Llamada) => Response; detalle?: () => Response; empresas?: () => Response; reintentar?: () => Response } = {}) {
  return vi.fn(async (u: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(u), "http://app");
    const llamada = { url, method: init?.method ?? "GET", body: init?.body ? JSON.parse(String(init.body)) : undefined };
    llamadas.push(llamada);
    const p = url.pathname;
    if (p.endsWith("/credenciales") && llamada.method === "PUT") return opciones.put ? opciones.put(llamada) : new Response(null, { status: 204 });
    if (p.endsWith("/credenciales")) return opciones.credenciales ? opciones.credenciales() : Response.json(CONFIGURADA);
    if (p.endsWith("/reintentar")) return opciones.reintentar ? opciones.reintentar() : new Response(null, { status: 202 });
    if (p.endsWith("/aprovisionamiento")) return opciones.detalle ? opciones.detalle() : Response.json(DETALLE);
    if (p === "/api/v1/impuestos/backoffice/empresas") return opciones.empresas ? opciones.empresas() : Response.json(EMPRESAS);
    return Response.json({});
  });
}

const montar = (estado: string | null = "LISTO_SIN_WEBHOOK", onGuardado = vi.fn()) =>
  render(<FormularioCredenciales tenantId={TENANT} estado={estado} onGuardado={onGuardado} />);

beforeEach(() => {
  replace.mockReset();
  notFound.mockClear();
  busqueda = new URLSearchParams();
  llamadas = [];
  conSesion(true);
});
afterEach(() => vi.unstubAllGlobals());

describe("FormularioCredenciales — estado (nunca los valores)", () => {
  it("lee GET …/credenciales y muestra «Configurada» con fecha y prefijo, el secreto «Sin configurar» y el origen del contrato", async () => {
    vi.stubGlobal("fetch", api());

    montar();

    expect(await screen.findByText(/empieza por fk_live_/)).toBeInTheDocument();
    expect(llamadas[0].url.pathname).toBe(`/api/v1/impuestos/backoffice/empresas/${TENANT}/credenciales`);
    const tarjeta = screen.getByRole("group", { name: "Credenciales" });
    expect(tarjeta).toHaveTextContent("API key: Configurada el 20/11/2026 10:00 · empieza por fk_live_");
    expect(tarjeta).toHaveTextContent("Secreto del webhook: Sin configurar");
    expect(tarjeta).toHaveTextContent("Origen: Copiada de Diagnóstico");
    expect(screen.getByRole("button", { name: "Reemplazar" })).toBeInTheDocument();
    expect(screen.queryByLabelText("API key")).toBeNull();
  });

  it("sin credenciales: «Sin configurar» y «Cargar credenciales»", async () => {
    vi.stubGlobal("fetch", api({ credenciales: () => Response.json(SIN_NADA) }));

    montar("REQUIERE_CREDENCIALES");

    expect(await screen.findByRole("button", { name: "Cargar credenciales" })).toBeInTheDocument();
    expect(screen.getByRole("group", { name: "Credenciales" })).toHaveTextContent("API key: Sin configurar");
  });

  it("en REQUIERE_CREDENCIALES avisa que la API key guardada fue rechazada (literal del contrato 1.2.1)", async () => {
    vi.stubGlobal("fetch", api({ credenciales: () => Response.json(SIN_NADA) }));

    montar("REQUIERE_CREDENCIALES");

    expect(
      await screen.findByText(
        "El proveedor rechazó la API key guardada: reintenta el aprovisionamiento o carga credenciales nuevas.",
      ),
    ).toBeInTheDocument();
  });

  it("cargando y error de la lectura (con Reintentar)", async () => {
    let primera = true;
    vi.stubGlobal(
      "fetch",
      api({
        credenciales: () => {
          if (!primera) return Response.json(CONFIGURADA);
          primera = false;
          return Response.json({ error: "detalle interno" }, { status: 503 });
        },
      }),
    );

    montar();

    expect(screen.getByRole("status", { name: "Cargando las credenciales…" })).toBeInTheDocument();
    expect(await screen.findByText("No se pudo leer el estado de las credenciales")).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent("detalle interno");
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByText(/empieza por fk_live_/)).toBeInTheDocument();
  });
});

describe("FormularioCredenciales — carga protegida", () => {
  async function abrir() {
    fireEvent.click(await screen.findByRole("button", { name: "Reemplazar" }));
    return { apiKey: screen.getByLabelText("API key") as HTMLInputElement, secreto: screen.getByLabelText("Secreto del webhook (opcional)") as HTMLInputElement };
  }

  it("campos tipo contraseña sin autocompletar ni corrector, con «Mostrar API key» (aria-pressed) y la ayuda del spec", async () => {
    vi.stubGlobal("fetch", api());

    montar();
    const { apiKey, secreto } = await abrir();

    for (const campo of [apiKey, secreto]) {
      expect(campo).toHaveAttribute("type", "password");
      expect(campo).toHaveAttribute("autocomplete", "off");
      expect(campo).toHaveAttribute("spellcheck", "false");
      expect(campo).toHaveAttribute("autocapitalize", "none");
      expect(campo).toHaveValue("");
    }
    const mostrar = screen.getByRole("button", { name: "Mostrar API key" });
    expect(mostrar).toHaveAttribute("aria-pressed", "false");
    fireEvent.click(mostrar);
    expect(apiKey).toHaveAttribute("type", "text");
    expect(screen.getByRole("button", { name: "Ocultar API key" })).toHaveAttribute("aria-pressed", "true");
    expect(
      screen.getByText("Rota las credenciales en la consola del proveedor antes de cargarlas. Al guardar se validan con el proveedor y no se vuelven a mostrar."),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Validar y guardar" })).toBeInTheDocument();
  });

  it("guardar: PUT con los valores, 204 ⇒ se limpian, se cierra, «Credenciales guardadas y validadas.» y relee el estado; los valores no quedan en el DOM", async () => {
    const onGuardado = vi.fn();
    vi.stubGlobal("fetch", api());

    const { container } = montar("LISTO_SIN_WEBHOOK", onGuardado);
    const { apiKey, secreto } = await abrir();
    fireEvent.change(apiKey, { target: { value: API_KEY } });
    fireEvent.change(secreto, { target: { value: SECRETO } });
    fireEvent.click(screen.getByRole("button", { name: "Validar y guardar" }));

    expect(await screen.findByText("Credenciales guardadas y validadas.")).toBeInTheDocument();
    const put = llamadas.find((l) => l.method === "PUT")!;
    expect(put.url.pathname).toBe(`/api/v1/impuestos/backoffice/empresas/${TENANT}/credenciales`);
    expect(put.body).toEqual({ apiKey: API_KEY, webhookSecret: SECRETO });
    expect(screen.queryByLabelText("API key")).toBeNull();
    expect(container.innerHTML).not.toContain(API_KEY);
    expect(container.innerHTML).not.toContain(SECRETO);
    expect(onGuardado).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(llamadas.filter((l) => l.method === "GET" && l.url.pathname.endsWith("/credenciales"))).toHaveLength(2));

    // Al volver a abrir, los campos siguen vacíos: nada de lo escrito se conservó.
    const otra = await abrir();
    expect(otra.apiKey).toHaveValue("");
    expect(otra.secreto).toHaveValue("");
  });

  it("solo el secreto: webhookSecret con apiKey null", async () => {
    vi.stubGlobal("fetch", api());

    montar();
    const { secreto } = await abrir();
    fireEvent.change(secreto, { target: { value: SECRETO } });
    fireEvent.click(screen.getByRole("button", { name: "Validar y guardar" }));

    await waitFor(() => expect(llamadas.find((l) => l.method === "PUT")?.body).toEqual({ apiKey: null, webhookSecret: SECRETO }));
  });

  it("vacío: el literal CREDENCIAL_REQUERIDA y no se envía nada", async () => {
    vi.stubGlobal("fetch", api());

    montar();
    const { apiKey } = await abrir();
    fireEvent.click(screen.getByRole("button", { name: "Validar y guardar" }));

    expect(await screen.findByText("Escribe la API key o el secreto del webhook.")).toBeInTheDocument();
    expect(apiKey).toHaveAttribute("aria-invalid", "true");
    expect(llamadas.filter((l) => l.method === "PUT")).toHaveLength(0);
  });

  it("422 CREDENCIAL_INVALIDA: literal del contrato junto a la API key, el formulario sigue abierto", async () => {
    vi.stubGlobal("fetch", api({ put: () => Response.json({ code: "CREDENCIAL_INVALIDA", error: "x", campo: "apiKey" }, { status: 422 }) }));

    montar();
    const { apiKey } = await abrir();
    fireEvent.change(apiKey, { target: { value: API_KEY } });
    fireEvent.click(screen.getByRole("button", { name: "Validar y guardar" }));

    expect(await screen.findByText("El proveedor rechazó la API key: revisa el valor e intenta de nuevo.")).toBeInTheDocument();
    expect(screen.getByLabelText("API key")).toHaveAttribute("aria-invalid", "true");
    expect(screen.queryByText("Credenciales guardadas y validadas.")).toBeNull();
  });

  it("409 ESTADO_NO_ADMITE_CARGA: literal del contrato", async () => {
    vi.stubGlobal("fetch", api({ put: () => Response.json({ code: "ESTADO_NO_ADMITE_CARGA", error: "x" }, { status: 409 }) }));

    montar("PENDIENTE");
    const { apiKey } = await abrir();
    fireEvent.change(apiKey, { target: { value: API_KEY } });
    fireEvent.click(screen.getByRole("button", { name: "Validar y guardar" }));

    expect(await screen.findByText("En el estado actual de la compañía no se pueden cargar credenciales.")).toBeInTheDocument();
  });

  it.each([400, 422])("%s CREDENCIAL_REQUERIDA del servidor: literal del contrato en la tarjeta, sin el texto del servidor", async (status) => {
    vi.stubGlobal("fetch", api({ put: () => Response.json({ code: "CREDENCIAL_REQUERIDA", error: "detalle interno" }, { status }) }));

    montar();
    const { secreto } = await abrir();
    fireEvent.change(secreto, { target: { value: SECRETO } });
    fireEvent.click(screen.getByRole("button", { name: "Validar y guardar" }));

    const tarjeta = screen.getByRole("group", { name: "Credenciales" });
    expect(await within(tarjeta).findByText("Escribe la API key o el secreto del webhook.")).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent("detalle interno");
    expect(screen.getByLabelText("Secreto del webhook (opcional)")).toBeInTheDocument();
    expect(replace).not.toHaveBeenCalled();
  });

  it("409 ESTADO_NO_ADMITE_CARGA queda dentro de la tarjeta «Credenciales» y el formulario sigue abierto", async () => {
    vi.stubGlobal("fetch", api({ put: () => Response.json({ code: "ESTADO_NO_ADMITE_CARGA", error: "detalle interno" }, { status: 409 }) }));

    montar("PENDIENTE");
    const { apiKey } = await abrir();
    fireEvent.change(apiKey, { target: { value: API_KEY } });
    fireEvent.click(screen.getByRole("button", { name: "Validar y guardar" }));

    const tarjeta = screen.getByRole("group", { name: "Credenciales" });
    expect(await within(tarjeta).findByText("En el estado actual de la compañía no se pueden cargar credenciales.")).toBeInTheDocument();
    expect(screen.getByLabelText("API key")).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent("detalle interno");
  });

  it("503 PROVEEDOR_NO_DISPONIBLE: literal del contrato en la tarjeta, sin «guardadas» y sin el texto del servidor", async () => {
    vi.stubGlobal("fetch", api({ put: () => Response.json({ code: "PROVEEDOR_NO_DISPONIBLE", error: "detalle interno" }, { status: 503 }) }));

    montar();
    const { apiKey } = await abrir();
    fireEvent.change(apiKey, { target: { value: API_KEY } });
    fireEvent.click(screen.getByRole("button", { name: "Validar y guardar" }));

    const tarjeta = screen.getByRole("group", { name: "Credenciales" });
    expect(await within(tarjeta).findByText("El proveedor no responde. Intenta más tarde.")).toBeInTheDocument();
    expect(screen.queryByText("Credenciales guardadas y validadas.")).toBeNull();
    expect(document.body).not.toHaveTextContent("detalle interno");
  });

  it("404 EMPRESA_NO_ENCONTRADA al guardar: literal de ERRORES_EMPRESA en línea, nunca la 403", async () => {
    vi.stubGlobal("fetch", api({ put: () => Response.json({ code: "EMPRESA_NO_ENCONTRADA", error: "x" }, { status: 404 }) }));

    montar();
    const { apiKey } = await abrir();
    fireEvent.change(apiKey, { target: { value: API_KEY } });
    fireEvent.click(screen.getByRole("button", { name: "Validar y guardar" }));

    expect(await screen.findByText("La compañía no existe o no tiene Impuestos.")).toBeInTheDocument();
    expect(replace).not.toHaveBeenCalled();
  });

  it("«Cancelar» cierra y vacía lo escrito", async () => {
    vi.stubGlobal("fetch", api());

    montar();
    const { apiKey } = await abrir();
    fireEvent.change(apiKey, { target: { value: API_KEY } });
    fireEvent.click(screen.getByRole("button", { name: "Cancelar" }));

    expect(screen.queryByLabelText("API key")).toBeNull();
    expect((await abrir()).apiKey).toHaveValue("");
  });
});

const EMPRESAS = {
  items: [
    { id: TENANT, nombre: "Compañía Uno", nit: "900000001", aprovisionamiento: { estado: "LISTO", motivo: null, accion: null, firmaConfirmada: false }, suspendida: false },
    { id: "e-2", nombre: "Compañía Dos", nit: null, aprovisionamiento: { estado: "REQUIERE_CREDENCIALES", motivo: "EMPRESA_YA_REGISTRADA", accion: "CARGAR_CREDENCIALES", firmaConfirmada: false }, suspendida: false },
  ],
  total: 2,
  page: 1,
  pageSize: 50,
};

const DETALLE = {
  empresa: { id: TENANT, nombre: "Compañía Uno", nit: "900000001" },
  estado: "ERROR",
  motivo: "ALTA_DATOS_RECHAZADOS",
  accion: "REINTENTAR",
  firmaConfirmada: false,
  modoRespaldoFirma: false,
  actualizadoEn: "2026-11-20T15:00:00Z",
  historial: [{ desde: "PENDIENTE", hacia: "ERROR", motivo: "ALTA_DATOS_RECHAZADOS", actorTipo: "SISTEMA", en: "2026-11-20T15:00:00Z" }],
};

describe("Backoffice — compañías con Impuestos (lista)", () => {
  it("lista con estado (etiqueta del contrato), «Firma sin confirmar», motivo legible y «Ver detalle»", async () => {
    vi.stubGlobal("fetch", api());

    render(<EmpresasAprovisionamiento />);

    expect(await screen.findByText("Compañía Uno")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Compañías con Impuestos" })).toBeInTheDocument();
    expect(screen.getByText("Estás en el backoffice de FLIT: ves todas las compañías, incluidas las suspendidas.")).toBeInTheDocument();
    const uno = screen.getByText("Compañía Uno").closest("tr") as HTMLElement;
    expect(within(uno).getByText("Listo")).toBeInTheDocument();
    expect(within(uno).getByText("Firma sin confirmar")).toBeInTheDocument();
    const dos = screen.getByText("Compañía Dos").closest("tr") as HTMLElement;
    expect(within(dos).getByText("Requiere credenciales")).toBeInTheDocument();
    expect(within(dos).getByText("La empresa ya existe en el proveedor: rota las credenciales en la consola del API y cárgalas en Impuestos.")).toBeInTheDocument();
    expect(within(uno).getByRole("link", { name: /Ver detalle/ })).toHaveAttribute("href", `/backoffice/empresas/${TENANT}`);
    expect(screen.queryByText("REQUIERE_CREDENCIALES")).toBeNull();
  });

  it("vacío, búsqueda sin resultados (q fuera de la URL) y error con Reintentar", async () => {
    let n = 0;
    vi.stubGlobal(
      "fetch",
      api({
        empresas: () => {
          n++;
          if (n === 1) return Response.json({ items: [], total: 0, page: 1, pageSize: 50 });
          if (n === 2) return Response.json({ items: [], total: 0, page: 1, pageSize: 50 });
          return Response.json({ error: "detalle interno" }, { status: 503 });
        },
      }),
    );

    render(<EmpresasAprovisionamiento />);

    expect(await screen.findByText("Ninguna compañía tiene Impuestos activo todavía")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Buscar por nombre o NIT"), { target: { value: "900" } });
    fireEvent.submit(screen.getByRole("search"));
    expect(await screen.findByText("Ninguna compañía coincide")).toBeInTheDocument();
    expect(llamadas.at(-1)?.url.searchParams.get("q")).toBe("900");
    expect(replace).not.toHaveBeenCalled();
    fireEvent.submit(screen.getByRole("search"));
    expect(await screen.findByText("No se pudieron cargar las compañías")).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent("detalle interno");
  });

  it("sin ser Super Admin va a la 403", async () => {
    conSesion(false);
    vi.stubGlobal("fetch", api());

    render(<EmpresasAprovisionamiento />);

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/403?code=SIN_PERMISO"));
  });
});

describe("Backoffice — detalle de la compañía", () => {
  it("estado, motivo legible, Reintentar (202 ⇒ aviso), credenciales, enlace al contrato e historial rotulado", async () => {
    vi.stubGlobal("fetch", api());

    render(<DetalleAprovisionamiento tenantId={TENANT} />);

    expect(await screen.findByRole("heading", { name: "Compañía Uno" })).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Miga de pan" })).toHaveTextContent("Backoffice");
    const estado = screen.getByRole("group", { name: "Estado" });
    expect(within(estado).getByText("Error")).toBeInTheDocument();
    expect(within(estado).getByText("El proveedor rechazó los datos del alta de la compañía.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Editar contrato" })).toHaveAttribute("href", `/configuracion?pestana=contractual&empresa=${TENANT}`);
    const historial = screen.getByRole("table", { name: "Historial del aprovisionamiento" });
    expect(within(historial).getByText("Pendiente")).toBeInTheDocument();
    expect(within(historial).getByText("Sistema")).toBeInTheDocument();
    expect(await screen.findByRole("group", { name: "Credenciales" })).toBeInTheDocument();

    fireEvent.click(within(estado).getByRole("button", { name: "Reintentar aprovisionamiento" }));
    expect(await screen.findByText("Reintento en cola. El estado se actualiza en unos segundos.")).toBeInTheDocument();
    expect(llamadas.some((l) => l.method === "POST" && l.url.pathname === `/api/v1/impuestos/backoffice/empresas/${TENANT}/aprovisionamiento/reintentar`)).toBe(true);
  });

  it("409 ESTADO_NO_REINTENTABLE al reintentar: literal del contrato", async () => {
    vi.stubGlobal("fetch", api({ reintentar: () => Response.json({ code: "ESTADO_NO_REINTENTABLE", error: "x" }, { status: 409 }) }));

    render(<DetalleAprovisionamiento tenantId={TENANT} />);
    fireEvent.click(await screen.findByRole("button", { name: "Reintentar aprovisionamiento" }));

    expect(await screen.findByText("En el estado actual de la compañía no se puede reintentar.")).toBeInTheDocument();
  });

  it("sin Reintentar en LISTO; historial vacío «Sin cambios registrados.»", async () => {
    vi.stubGlobal("fetch", api({ detalle: () => Response.json({ ...DETALLE, estado: "LISTO", motivo: null, accion: null, historial: [] }) }));

    render(<DetalleAprovisionamiento tenantId={TENANT} />);

    expect(await screen.findByText("Sin cambios registrados.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reintentar aprovisionamiento" })).toBeNull();
  });

  it("cargando «Cargando la compañía…», error con Reintentar y 404 ⇒ notFound()", async () => {
    vi.stubGlobal("fetch", api({ detalle: () => Response.json({ error: "detalle interno" }, { status: 503 }) }));

    const { unmount } = render(<DetalleAprovisionamiento tenantId={TENANT} />);

    expect(screen.getByRole("status", { name: "Cargando la compañía…" })).toBeInTheDocument();
    expect(await screen.findByText("No se pudieron cargar las compañías")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reintentar" })).toBeInTheDocument();
    unmount();

    vi.stubGlobal("fetch", api({ detalle: () => Response.json({ code: "EMPRESA_NO_ENCONTRADA", error: "x" }, { status: 404 }) }));
    render(<DetalleAprovisionamiento tenantId={TENANT} />);
    await waitFor(() => expect(notFound).toHaveBeenCalled());
  });
});

describe("FormularioCredenciales — respaldo manual con el contrato 1.2.0 (plan §5.8 punto 6)", () => {
  it("en LISTO_SIN_WEBHOOK (la acción es Reintentar) la carga manual sigue disponible y guarda con PUT", async () => {
    vi.stubGlobal("fetch", api());
    const onGuardado = vi.fn();
    montar("LISTO_SIN_WEBHOOK", onGuardado);

    fireEvent.click(await screen.findByRole("button", { name: "Reemplazar" }));
    fireEvent.change(screen.getByLabelText("Secreto del webhook (opcional)"), { target: { value: SECRETO } });
    fireEvent.click(screen.getByRole("button", { name: "Validar y guardar" }));

    expect(await screen.findByText("Credenciales guardadas y validadas.")).toBeInTheDocument();
    expect(llamadas.find((l) => l.method === "PUT")?.body).toEqual({ apiKey: null, webhookSecret: SECRETO });
    expect(onGuardado).toHaveBeenCalled();
    expect(screen.queryByText(LITERAL_AVISO_ROTACION)).toBeNull();
    expect(screen.queryByText("Cargar secreto")).toBeNull();
  });

  it("en ERROR por EMPRESA_INACTIVA_EN_PROVEEDOR también se puede cargar a mano, sin el aviso de rotación", async () => {
    vi.stubGlobal("fetch", api({ credenciales: () => Response.json(SIN_NADA) }));
    montar("ERROR");

    expect(await screen.findByRole("button", { name: "Cargar credenciales" })).toBeInTheDocument();
    expect(screen.queryByText(LITERAL_AVISO_ROTACION)).toBeNull();
  });
});

const LITERAL_AVISO_ROTACION =
  "El proveedor rechazó la API key guardada: reintenta el aprovisionamiento o carga credenciales nuevas.";

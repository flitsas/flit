import { afterEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { PestanaContrato } from "@/components/configuracion/PestanaContrato";
import {
  DISPONIBLES_CON_IMPUESTO,
  DISPONIBLES_SIN_IMPUESTO,
  EMPRESAS_BACKOFFICE,
  EMPRESA_CLIENTE,
  EMPRESA_SIN_NIT,
  URL_EMPRESAS,
  configuracion,
  contratoBackoffice,
  cuerpo,
} from "./datos";

// Uso de ejemplo: <PestanaContrato contrato={configuracion.contrato} puedeEditar={sesion.capacidades.editarContrato} />
// muestra lo contratado en solo lectura y, para el backoffice FLIT, el editor del contrato de la empresa elegida en el
// selector de empresas (GET /backoffice/empresas, HU #13741).

afterEach(() => vi.unstubAllGlobals());

const URL_CONTRATO = `/api/v1/diagnostico/backoffice/empresas/${EMPRESA_CLIENTE}/configuracion/contrato`;

/**
 * El listado de empresas responde aparte: `fetchMock` solo ve las llamadas del contrato, así sus índices no cambian.
 * `listado` permite simular el 503 o la lista vacía (HU #13744, escenario 8).
 */
function conEmpresas(fetchMock: ReturnType<typeof vi.fn>, listado: () => Promise<Response> = async () => Response.json(EMPRESAS_BACKOFFICE)) {
  const empresas = vi.fn(listado);
  const contrato = fetchMock as unknown as (url: string, init?: RequestInit) => Promise<Response>;
  vi.stubGlobal("fetch", (url: string, init?: RequestInit) => (url === URL_EMPRESAS ? empresas() : contrato(url, init)));
  return empresas;
}

/** Busca la empresa en el selector y la elige: el contrato se carga sin escribir su identificador (escenario 7). */
async function elegirEmpresa(busqueda = "Cliente", opcion: RegExp = /Transportes Cliente SAS/) {
  const selector = await screen.findByRole("combobox", { name: "Empresa" });
  fireEvent.change(selector, { target: { value: busqueda } });
  fireEvent.click(screen.getByRole("option", { name: opcion }));
}

async function cargarEmpresa() {
  await elegirEmpresa();
  return screen.findByRole("spinbutton", { name: "Límite de vehículos por lote" });
}

describe("pestaña Contrato", () => {
  it("solo lectura para admin de la compañía: fuentes habilitadas y límite sin controles de edición", () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    render(<PestanaContrato contrato={configuracion().contrato} puedeEditar={false} />);

    expect(screen.getByText("RUNT vehículo")).toBeInTheDocument();
    expect(screen.getByText("SIMIT vehículo")).toBeInTheDocument();
    expect(screen.getByText("Avalúo comercial")).toBeInTheDocument();
    expect(screen.getByText("500")).toBeInTheDocument();
    expect(screen.queryAllByRole("checkbox")).toHaveLength(0);
    expect(screen.queryAllByRole("spinbutton")).toHaveLength(0);
    expect(screen.queryAllByRole("switch")).toHaveLength(0);
    expect(screen.queryAllByRole("textbox")).toHaveLength(0);
    expect(screen.queryByRole("button", { name: /guardar/i })).toBeNull();
    expect(screen.queryByText(/redacción/i)).toBeNull();
    expect(screen.queryByRole("combobox")).toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("contrato sin fuentes habilitadas muestra el estado vacío", () => {
    render(<PestanaContrato contrato={configuracion({ contrato: { fuentes_habilitadas: [] } }).contrato} puedeEditar={false} />);

    expect(screen.getByText(/no tiene fuentes habilitadas/i)).toBeInTheDocument();
  });

  it("editable para backoffice FLIT: carga el contrato de la empresa elegida y guarda fuentes, límite e interruptor", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(Response.json(contratoBackoffice()))
      .mockResolvedValueOnce(
        Response.json(contratoBackoffice({ fuentes_habilitadas: ["RUNT", "SIMIT_VEHICULO", "AVALUO"], limite_vehiculos_lote: 200, redaccion_ia_suspendida: true })),
      );
    conEmpresas(fetchMock);

    render(<PestanaContrato contrato={configuracion().contrato} puedeEditar />);

    const limite = await cargarEmpresa();
    expect(fetchMock.mock.calls[0][0]).toBe(URL_CONTRATO);
    expect(screen.getByRole("checkbox", { name: "RUNT vehículo" })).toBeChecked();
    const avaluo = screen.getByRole("checkbox", { name: "Avalúo comercial" });
    expect(avaluo).not.toBeChecked();
    expect(limite).toHaveValue(300);
    const interruptor = screen.getByRole("switch", { name: "Suspender la redacción con IA" });
    expect(interruptor).not.toBeChecked();

    fireEvent.click(avaluo);
    fireEvent.change(limite, { target: { value: "200" } });
    fireEvent.click(interruptor);
    fireEvent.click(screen.getByRole("button", { name: "Guardar contrato" }));

    expect(await screen.findByText("Contrato guardado.")).toBeInTheDocument();
    const [url, init] = fetchMock.mock.calls[1] as [string, RequestInit];
    expect(url).toBe(URL_CONTRATO);
    expect(init.method).toBe("PUT");
    expect(cuerpo(fetchMock, 1)).toEqual({
      fuentes_habilitadas: ["RUNT", "SIMIT_VEHICULO", "AVALUO"],
      limite_vehiculos_lote: 200,
      redaccion_ia_suspendida: true,
    });
  });

  it("no guarda un límite que no es mayor que cero", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(Response.json(contratoBackoffice()));
    conEmpresas(fetchMock);

    render(<PestanaContrato contrato={null} puedeEditar />);
    const limite = await cargarEmpresa();

    fireEvent.change(limite, { target: { value: "0" } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar contrato" }));

    expect(screen.getByText(/mayor que cero/i)).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("si falla la carga del contrato muestra el error con Reintentar", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(Response.json({}, { status: 500 })).mockResolvedValueOnce(Response.json(contratoBackoffice()));
    conEmpresas(fetchMock);

    render(<PestanaContrato contrato={null} puedeEditar />);
    await elegirEmpresa();

    expect(await screen.findByText(/Diagnóstico no respondió/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    await waitFor(() => expect(screen.getByRole("checkbox", { name: "RUNT vehículo" })).toBeChecked());
  });
});

// HU #13865: la disponibilidad de cada fuente sale de fuentes_disponibles del contrato del backoffice (gate Q7 = a).
describe("pestaña Contrato — fuentes según fuentes_disponibles (HU #13865)", () => {
  const AYUDA = /Disponible cuando la consulta automatizada esté en producción/;

  it("AC3: una fuente no disponible y no habilitada queda desmarcada, bloqueada y enfocable, con la ayuda accesible y sin marcarse", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(Response.json(contratoBackoffice({ fuentes_disponibles: DISPONIBLES_SIN_IMPUESTO })))
      .mockResolvedValueOnce(Response.json(contratoBackoffice()));
    conEmpresas(fetchMock);

    render(<PestanaContrato contrato={null} puedeEditar />);
    await cargarEmpresa();

    const impuesto = screen.getByRole("checkbox", { name: "Impuesto departamental" });
    expect(impuesto).not.toBeChecked();
    expect(impuesto).toHaveAttribute("aria-disabled", "true");
    expect(impuesto).not.toBeDisabled();
    expect(impuesto).toHaveAccessibleDescription(AYUDA);
    expect(screen.getAllByRole("tooltip").some((t) => AYUDA.test(t.textContent ?? ""))).toBe(true);
    impuesto.focus();
    expect(impuesto).toHaveFocus();

    fireEvent.click(impuesto);
    expect(impuesto).not.toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Avalúo comercial" })).not.toHaveAttribute("aria-disabled");

    fireEvent.click(screen.getByRole("button", { name: "Guardar contrato" }));
    expect(await screen.findByText("Contrato guardado.")).toBeInTheDocument();
    expect((cuerpo(fetchMock, 1) as { fuentes_habilitadas: string[] }).fuentes_habilitadas).toEqual(["RUNT", "SIMIT_VEHICULO"]);
  });

  it("con el impuesto en fuentes_disponibles la casilla queda libre y se puede habilitar", async () => {
    conEmpresas(vi.fn().mockResolvedValueOnce(Response.json(contratoBackoffice({ fuentes_disponibles: DISPONIBLES_CON_IMPUESTO }))));

    render(<PestanaContrato contrato={null} puedeEditar />);
    await cargarEmpresa();

    const impuesto = screen.getByRole("checkbox", { name: "Impuesto departamental" });
    expect(impuesto).not.toHaveAttribute("aria-disabled");
    expect(impuesto).not.toHaveAccessibleDescription(AYUDA);
    fireEvent.click(impuesto);
    expect(impuesto).toBeChecked();
  });

  it("una fuente habilitada que dejó de estar disponible se ve marcada, con aviso, y se puede desmarcar", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(
        Response.json(contratoBackoffice({ fuentes_habilitadas: ["RUNT", "SIMIT_VEHICULO", "IMPUESTO_DEPARTAMENTAL"], fuentes_disponibles: DISPONIBLES_SIN_IMPUESTO })),
      )
      .mockResolvedValueOnce(Response.json(contratoBackoffice()));
    conEmpresas(fetchMock);

    render(<PestanaContrato contrato={null} puedeEditar />);
    await cargarEmpresa();

    const impuesto = screen.getByRole("checkbox", { name: "Impuesto departamental" });
    expect(impuesto).toBeChecked();
    expect(impuesto).not.toHaveAttribute("aria-disabled");
    expect(impuesto).toHaveAccessibleDescription(/ya no está disponible/i);

    fireEvent.click(impuesto);
    expect(impuesto).not.toBeChecked();
    fireEvent.click(screen.getByRole("button", { name: "Guardar contrato" }));
    expect(await screen.findByText("Contrato guardado.")).toBeInTheDocument();
    expect((cuerpo(fetchMock, 1) as { fuentes_habilitadas: string[] }).fuentes_habilitadas).toEqual(["RUNT", "SIMIT_VEHICULO"]);
  });

  it("si nadie la toca, la habilitada que dejó de estar disponible se conserva al guardar (no se borra en silencio)", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(
        Response.json(contratoBackoffice({ fuentes_habilitadas: ["RUNT", "IMPUESTO_DEPARTAMENTAL"], fuentes_disponibles: DISPONIBLES_SIN_IMPUESTO })),
      )
      .mockResolvedValueOnce(Response.json(contratoBackoffice({ fuentes_habilitadas: ["RUNT", "IMPUESTO_DEPARTAMENTAL"] })));
    conEmpresas(fetchMock);

    render(<PestanaContrato contrato={null} puedeEditar />);
    const limite = await cargarEmpresa();
    fireEvent.change(limite, { target: { value: "100" } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar contrato" }));

    expect(await screen.findByText("Contrato guardado.")).toBeInTheDocument();
    expect((cuerpo(fetchMock, 1) as { fuentes_habilitadas: string[] }).fuentes_habilitadas).toEqual(["RUNT", "IMPUESTO_DEPARTAMENTAL"]);
  });

  it("AC3: el 422 FUENTE_NO_DISPONIBLE se muestra sobre el campo Fuentes habilitadas, sin perder lo digitado", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(Response.json(contratoBackoffice({ fuentes_disponibles: DISPONIBLES_CON_IMPUESTO })))
      .mockResolvedValueOnce(
        Response.json({ code: "FUENTE_NO_DISPONIBLE", error: "La fuente IMPUESTO_DEPARTAMENTAL todavía no se puede habilitar.", campo: null }, { status: 422 }),
      );
    conEmpresas(fetchMock);

    render(<PestanaContrato contrato={null} puedeEditar />);
    await cargarEmpresa();
    fireEvent.click(screen.getByRole("checkbox", { name: "Impuesto departamental" }));
    fireEvent.click(screen.getByRole("button", { name: "Guardar contrato" }));

    const campo = screen.getByRole("group", { name: "Fuentes habilitadas" });
    const alerta = await within(campo).findByRole("alert");
    expect(alerta).toHaveTextContent("La fuente IMPUESTO_DEPARTAMENTAL todavía no se puede habilitar.");
    expect(campo).toHaveAccessibleDescription(/todavía no se puede habilitar/);
    expect(screen.queryByText("No se guardó el contrato")).toBeNull();
    expect(screen.getByRole("checkbox", { name: "Impuesto departamental" })).toBeChecked();
  });
});

// HU #13744, escenario 7: selector de empresas (GET /backoffice/empresas, HU #13741) en el editor del contrato.
describe("pestaña Contrato — escenario 7: selector de empresas", () => {
  it("lista las empresas con nombre y NIT, filtra por nombre sin tildes ni mayúsculas y carga el contrato de la elegida", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(Response.json(contratoBackoffice({ empresa: EMPRESA_SIN_NIT })));
    const empresas = conEmpresas(fetchMock);

    render(<PestanaContrato contrato={null} puedeEditar />);

    const selector = await screen.findByRole("combobox", { name: "Empresa" });
    expect(empresas).toHaveBeenCalledTimes(1);
    fireEvent.click(selector);
    const opciones = within(screen.getByRole("listbox")).getAllByRole("option");
    expect(opciones.map((o) => o.textContent)).toEqual([
      "Logística Andina SASNIT 900000001",
      "Óptima Carga SASsin NIT",
      "Transportes Cliente SASNIT 900000002",
    ]);

    fireEvent.change(selector, { target: { value: "OPTIMA" } });
    expect(screen.getAllByRole("option").map((o) => o.textContent)).toEqual(["Óptima Carga SASsin NIT"]);
    expect(screen.getByText("1 opciones disponibles")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("option", { name: /Óptima Carga SAS/ }));

    await screen.findByRole("spinbutton", { name: "Límite de vehículos por lote" });
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toBe(`/api/v1/diagnostico/backoffice/empresas/${EMPRESA_SIN_NIT}/configuracion/contrato`);
    expect(screen.getByRole("form", { name: "Editar contrato" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Óptima Carga SAS" })).toBeInTheDocument();
    expect(screen.getByText("sin NIT")).toBeInTheDocument();
    expect(screen.queryByText(EMPRESA_SIN_NIT)).toBeNull();
    expect(screen.queryByRole("textbox", { name: /identificador/i })).toBeNull();
  });

  it("busca por NIT y elige con el teclado (flechas y Enter); el encabezado muestra nombre y NIT, no el uuid", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(Response.json(contratoBackoffice()));
    conEmpresas(fetchMock);

    render(<PestanaContrato contrato={null} puedeEditar />);

    const selector = await screen.findByRole("combobox", { name: "Empresa" });
    fireEvent.change(selector, { target: { value: "900000002" } });
    expect(selector).toHaveAttribute("aria-expanded", "true");
    expect(screen.getAllByRole("option")).toHaveLength(1);
    fireEvent.keyDown(selector, { key: "ArrowDown" });
    fireEvent.keyDown(selector, { key: "Enter" });

    await screen.findByRole("spinbutton", { name: "Límite de vehículos por lote" });
    expect(fetchMock.mock.calls[0][0]).toBe(URL_CONTRATO);
    expect(screen.getByRole("heading", { name: "Transportes Cliente SAS" })).toBeInTheDocument();
    expect(screen.getByText("NIT 900000002")).toBeInTheDocument();
    expect(screen.queryByText(EMPRESA_CLIENTE)).toBeNull();
  });

  it("Escape cierra la lista sin elegir y no consulta ningún contrato", async () => {
    const fetchMock = vi.fn();
    conEmpresas(fetchMock);

    render(<PestanaContrato contrato={null} puedeEditar />);

    const selector = await screen.findByRole("combobox", { name: "Empresa" });
    fireEvent.change(selector, { target: { value: "Andina" } });
    fireEvent.keyDown(selector, { key: "Escape" });

    expect(selector).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("listbox")).toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.queryByRole("form", { name: "Editar contrato" })).toBeNull();
  });

  it("una búsqueda sin resultados muestra «Ninguna empresa coincide»", async () => {
    conEmpresas(vi.fn());

    render(<PestanaContrato contrato={null} puedeEditar />);

    const selector = await screen.findByRole("combobox", { name: "Empresa" });
    fireEvent.change(selector, { target: { value: "Empresa inexistente" } });

    expect(screen.getByText("Ninguna empresa coincide")).toBeInTheDocument();
    expect(screen.queryAllByRole("option")).toHaveLength(0);
  });
});

// HU #13744, escenario 8: listado de empresas no disponible (503, error o vacío); el editor no se abre sin empresa.
describe("pestaña Contrato — escenario 8: listado de empresas no disponible", () => {
  it("mientras carga el listado muestra el estado cargando y no el editor", async () => {
    let responder: (r: Response) => void = () => {};
    conEmpresas(vi.fn(), () => new Promise<Response>((r) => (responder = r)));

    render(<PestanaContrato contrato={null} puedeEditar />);

    expect(screen.getByRole("status", { name: "Cargando empresas…" })).toBeInTheDocument();
    expect(screen.queryByRole("combobox")).toBeNull();
    expect(screen.queryByRole("form", { name: "Editar contrato" })).toBeNull();
    await act(async () => responder(Response.json(EMPRESAS_BACKOFFICE)));
    expect(await screen.findByRole("combobox", { name: "Empresa" })).toBeInTheDocument();
  });

  it("con el listado cargado y sin empresa elegida, el editor no se abre", async () => {
    const fetchMock = vi.fn();
    conEmpresas(fetchMock);

    render(<PestanaContrato contrato={null} puedeEditar />);

    await screen.findByRole("combobox", { name: "Empresa" });
    expect(screen.getByText("Todavía no has elegido una empresa.")).toBeInTheDocument();
    expect(screen.queryByRole("form", { name: "Editar contrato" })).toBeNull();
    expect(screen.queryByRole("spinbutton")).toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("503 IDENTIDAD_NO_CONFIGURADA: «El listado de empresas no está configurado», sin selector ni editor", async () => {
    const fetchMock = vi.fn();
    conEmpresas(fetchMock, async () => Response.json({ code: "IDENTIDAD_NO_CONFIGURADA", error: "Identidad no está configurada." }, { status: 503 }));

    render(<PestanaContrato contrato={null} puedeEditar />);

    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("El listado de empresas no está configurado");
    expect(screen.queryByRole("combobox")).toBeNull();
    expect(screen.queryByRole("form", { name: "Editar contrato" })).toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("503 IDENTIDAD_NO_DISPONIBLE: «No se pudo cargar el listado de empresas» con Reintentar, que vuelve a pedirlo", async () => {
    const fetchMock = vi.fn();
    let intento = 0;
    const empresas = conEmpresas(fetchMock, async () =>
      ++intento === 1
        ? Response.json({ code: "IDENTIDAD_NO_DISPONIBLE", error: "Identidad no respondió." }, { status: 503, headers: { "Retry-After": "30" } })
        : Response.json(EMPRESAS_BACKOFFICE),
    );

    render(<PestanaContrato contrato={null} puedeEditar />);

    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("No se pudo cargar el listado de empresas");
    expect(screen.queryByRole("combobox")).toBeNull();
    expect(screen.queryByRole("form", { name: "Editar contrato" })).toBeNull();

    fireEvent.click(within(alerta).getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("combobox", { name: "Empresa" })).toBeInTheDocument();
    expect(empresas).toHaveBeenCalledTimes(2);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("lista vacía: «No hay empresas con Diagnóstico habilitado», sin selector ni editor", async () => {
    const fetchMock = vi.fn();
    conEmpresas(fetchMock, async () => Response.json([]));

    render(<PestanaContrato contrato={null} puedeEditar />);

    expect(await screen.findByText("No hay empresas con Diagnóstico habilitado")).toBeInTheDocument();
    expect(screen.queryByRole("combobox")).toBeNull();
    expect(screen.queryByRole("form", { name: "Editar contrato" })).toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { PestanaContrato } from "@/components/configuracion/PestanaContrato";
import { DISPONIBLES_CON_IMPUESTO, DISPONIBLES_SIN_IMPUESTO, EMPRESA_CLIENTE, configuracion, contratoBackoffice, cuerpo } from "./datos";

// Uso de ejemplo: <PestanaContrato contrato={configuracion.contrato} puedeEditar={sesion.capacidades.editarContrato} />
// muestra lo contratado en solo lectura y, para el backoffice FLIT, el editor del contrato de la empresa indicada.

afterEach(() => vi.unstubAllGlobals());

const URL_CONTRATO = `/api/v1/diagnostico/backoffice/empresas/${EMPRESA_CLIENTE}/configuracion/contrato`;

async function cargarEmpresa() {
  fireEvent.change(screen.getByLabelText("Identificador de la empresa"), { target: { value: EMPRESA_CLIENTE } });
  fireEvent.click(screen.getByRole("button", { name: "Cargar contrato" }));
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
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("contrato sin fuentes habilitadas muestra el estado vacío", () => {
    render(<PestanaContrato contrato={configuracion({ contrato: { fuentes_habilitadas: [] } }).contrato} puedeEditar={false} />);

    expect(screen.getByText(/no tiene fuentes habilitadas/i)).toBeInTheDocument();
  });

  it("editable para backoffice FLIT: carga el contrato de la empresa indicada y guarda fuentes, límite e interruptor", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(Response.json(contratoBackoffice()))
      .mockResolvedValueOnce(
        Response.json(contratoBackoffice({ fuentes_habilitadas: ["RUNT", "SIMIT_VEHICULO", "AVALUO"], limite_vehiculos_lote: 200, redaccion_ia_suspendida: true })),
      );
    vi.stubGlobal("fetch", fetchMock);

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

  it("no consulta con un identificador de empresa que no es un uuid", () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    render(<PestanaContrato contrato={null} puedeEditar />);

    fireEvent.change(screen.getByLabelText("Identificador de la empresa"), { target: { value: "empresa-x" } });
    fireEvent.click(screen.getByRole("button", { name: "Cargar contrato" }));

    expect(screen.getByText(/identificador válido/i)).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("no guarda un límite que no es mayor que cero", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(Response.json(contratoBackoffice()));
    vi.stubGlobal("fetch", fetchMock);

    render(<PestanaContrato contrato={null} puedeEditar />);
    const limite = await cargarEmpresa();

    fireEvent.change(limite, { target: { value: "0" } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar contrato" }));

    expect(screen.getByText(/mayor que cero/i)).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("si falla la carga del contrato muestra el error con Reintentar", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(Response.json({}, { status: 500 })).mockResolvedValueOnce(Response.json(contratoBackoffice()));
    vi.stubGlobal("fetch", fetchMock);

    render(<PestanaContrato contrato={null} puedeEditar />);
    fireEvent.change(screen.getByLabelText("Identificador de la empresa"), { target: { value: EMPRESA_CLIENTE } });
    fireEvent.click(screen.getByRole("button", { name: "Cargar contrato" }));

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
    vi.stubGlobal("fetch", fetchMock);

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
    vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(Response.json(contratoBackoffice({ fuentes_disponibles: DISPONIBLES_CON_IMPUESTO }))));

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
    vi.stubGlobal("fetch", fetchMock);

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
    vi.stubGlobal("fetch", fetchMock);

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
    vi.stubGlobal("fetch", fetchMock);

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

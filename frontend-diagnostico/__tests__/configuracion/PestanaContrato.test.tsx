import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { PestanaContrato } from "@/components/configuracion/PestanaContrato";
import { EMPRESA_CLIENTE, configuracion, contratoBackoffice, cuerpo } from "./datos";

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

  it("muestra el 422 de la API al habilitar el impuesto con la fuente no disponible, sin perder lo digitado", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(Response.json(contratoBackoffice()))
      .mockResolvedValueOnce(
        Response.json({ code: "FUENTE_NO_DISPONIBLE", error: "La fuente Impuesto departamental todavía no está disponible." }, { status: 422 }),
      );
    vi.stubGlobal("fetch", fetchMock);

    render(<PestanaContrato contrato={null} puedeEditar />);
    await cargarEmpresa();
    fireEvent.click(screen.getByRole("checkbox", { name: "Impuesto departamental" }));
    fireEvent.click(screen.getByRole("button", { name: "Guardar contrato" }));

    expect(await screen.findByText("La fuente Impuesto departamental todavía no está disponible.")).toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: "Impuesto departamental" })).toBeChecked();
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

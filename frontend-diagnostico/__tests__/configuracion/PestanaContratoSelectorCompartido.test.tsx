import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { PestanaContrato } from "@/components/configuracion/PestanaContrato";
import { EMPRESA_CLIENTE, contratoBackoffice } from "./datos";

// Uso de ejemplo: <PestanaContrato contrato={null} puedeEditar /> elige la empresa con <SelectorEmpresaBackoffice />, el
// mismo selector del listado de diagnósticos del backoffice (Punto 6, ola 2026-11-11). Aquí se sustituye el selector por un
// doble para comprobar que el editor lo usa y carga el contrato de la empresa que entrega.

const h = vi.hoisted(() => ({ props: [] as Array<Record<string, unknown>> }));
vi.mock("@/components/backoffice/SelectorEmpresaBackoffice", () => ({
  SelectorEmpresaBackoffice: (props: { value: string; onChange: (e: { id: string; nombre: string; nit: string | null }) => void }) => {
    h.props.push(props);
    return (
      <button type="button" onClick={() => props.onChange({ id: EMPRESA_CLIENTE, nombre: "Transportes Cliente SAS", nit: "900000002" })}>
        doble del selector
      </button>
    );
  },
}));

afterEach(() => {
  vi.unstubAllGlobals();
  h.props.length = 0;
});

describe("PestanaContrato — AC5: usa el selector compartido del backoffice", () => {
  it("el editor elige la empresa con SelectorEmpresaBackoffice y carga su contrato", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(Response.json(contratoBackoffice()));
    vi.stubGlobal("fetch", fetchMock);
    render(<PestanaContrato contrato={null} puedeEditar />);

    expect(h.props.at(-1)).toMatchObject({ value: "" });
    fireEvent.click(screen.getByRole("button", { name: "doble del selector" }));

    expect(await screen.findByRole("spinbutton", { name: "Límite de vehículos por lote" })).toBeInTheDocument();
    expect(fetchMock.mock.calls[0][0]).toBe(`/api/v1/diagnostico/backoffice/empresas/${EMPRESA_CLIENTE}/configuracion/contrato`);
    expect(h.props.at(-1)).toMatchObject({ value: EMPRESA_CLIENTE });
    expect(screen.getByRole("heading", { name: "Transportes Cliente SAS" })).toBeInTheDocument();
  });
});

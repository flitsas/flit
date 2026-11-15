import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { SelectorCompania } from "@/components/estadisticas/SelectorCompania";
import { llamadas, simularApi } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: <SelectorCompania value={empresa} onChange={setEmpresa} /> lista GET /backoffice/empresas en un
// SearchableSelect con «Todas las compañías» (valor "") por defecto. Si la lista falla queda deshabilitado con aviso y
// Reintentar; la vista global sigue.

afterEach(() => vi.unstubAllGlobals());

const E1 = "01920000-0000-7000-8000-0000000000a1";
const E2 = "01920000-0000-7000-8000-0000000000a2";
const empresas = () =>
  Response.json([
    { id: E1, nombre: "Compañía Demo", nit: "900000001" },
    { id: E2, nombre: "Compañía Ejemplo", nit: null },
  ]);

describe("SelectorCompania — selector de la vista global", () => {
  it("ofrece «Todas las compañías» y las empresas del backoffice; elegir una llama onChange con su id", async () => {
    simularApi({ "GET /backoffice/empresas": empresas });
    const onChange = vi.fn();
    render(<SelectorCompania value="" onChange={onChange} />);
    const combo = screen.getByRole("combobox", { name: "Compañía" });
    await waitFor(() => expect(combo).not.toBeDisabled());
    expect(combo).toHaveValue("Todas las compañías");
    fireEvent.focus(combo);
    fireEvent.click(await screen.findByRole("option", { name: /Compañía Ejemplo/ }));
    expect(onChange).toHaveBeenCalledWith(E2);
  });

  it("si la lista falla (503) queda deshabilitado con aviso y Reintentar vuelve a pedirla", async () => {
    let intentos = 0;
    const fetchMock = simularApi({
      "GET /backoffice/empresas": () =>
        ++intentos === 1 ? Response.json({ code: "IDENTIDAD_NO_DISPONIBLE", error: "x" }, { status: 503 }) : empresas(),
    });
    render(<SelectorCompania value="" onChange={() => {}} />);
    expect(await screen.findByText("No se pudo cargar la lista de compañías")).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Compañía" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    await waitFor(() => expect(screen.getByRole("combobox", { name: "Compañía" })).not.toBeDisabled());
    expect(llamadas(fetchMock, "GET", "/backoffice/empresas")).toHaveLength(2);
  });
});

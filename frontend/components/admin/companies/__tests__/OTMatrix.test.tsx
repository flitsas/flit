// AC4 — Matriz OT con buscador en tiempo real (client-side, insensible a tildes) y
// checkboxes que reflejan los grants; al alternar dispara POST/DELETE.
//
// Uso de ejemplo:
//   render(<OTMatrix offices={offices} grantedIds={["o1"]} onToggle={spy} />);
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { OTMatrix, type OtOperationalInfo } from "../OTMatrix";
import type { TransitOffice } from "@/lib/api/types";
import { ApiValidationError } from "@/lib/api/types";

const offices: TransitOffice[] = [
  { id: "o1", code: "11001", name: "Secretaría de Movilidad Bogotá", departmentCode: "11", cityCode: "11001" },
  { id: "o2", code: "05001", name: "Medellín — Secretaría de Movilidad", departmentCode: "05", cityCode: "05001" },
  { id: "o3", code: "76001", name: "Cali — STTMP", departmentCode: "76", cityCode: "76001" },
];

describe("OTMatrix (AC4)", () => {
  it("filtra en tiempo real ignorando mayúsculas y tildes", async () => {
    const user = userEvent.setup();
    render(<OTMatrix offices={offices} grantedIds={[]} onToggle={vi.fn()} />);

    expect(within(screen.getByTestId("ot-list")).getAllByRole("listitem")).toHaveLength(3);

    await user.type(screen.getByLabelText(/buscar organismo/i), "bogota");

    const list = screen.getByTestId("ot-list");
    expect(within(list).getAllByRole("listitem")).toHaveLength(1);
    expect(within(list).getByText(/Secretaría de Movilidad Bogotá/i)).toBeInTheDocument();
  });

  it("filtra por código", async () => {
    const user = userEvent.setup();
    render(<OTMatrix offices={offices} grantedIds={[]} onToggle={vi.fn()} />);

    await user.type(screen.getByLabelText(/buscar organismo/i), "05001");
    const list = screen.getByTestId("ot-list");
    expect(within(list).getAllByRole("listitem")).toHaveLength(1);
    expect(within(list).getByText(/Medellín/i)).toBeInTheDocument();
  });

  it("refleja los grants existentes en los checkboxes", () => {
    render(<OTMatrix offices={offices} grantedIds={["o1"]} onToggle={vi.fn()} />);
    expect(screen.getByLabelText(/Secretaría de Movilidad Bogotá/i)).toBeChecked();
    expect(screen.getByLabelText(/Cali/i)).not.toBeChecked();
  });

  it("dispara onToggle al habilitar (POST) y al deshabilitar (DELETE)", async () => {
    const user = userEvent.setup();
    const onToggle = vi.fn().mockResolvedValue(undefined);
    render(<OTMatrix offices={offices} grantedIds={["o1"]} onToggle={onToggle} />);

    await user.click(screen.getByLabelText(/Cali/i));
    await waitFor(() => expect(onToggle).toHaveBeenCalledWith("o3", true));

    await user.click(screen.getByLabelText(/Secretaría de Movilidad Bogotá/i));
    await waitFor(() => expect(onToggle).toHaveBeenCalledWith("o1", false));
  });

  it("revierte el checkbox (rollback) si la persistencia falla", async () => {
    const user = userEvent.setup();
    const onToggle = vi.fn().mockRejectedValue(new Error("boom"));
    const onError = vi.fn();
    render(<OTMatrix offices={offices} grantedIds={[]} onToggle={onToggle} onError={onError} />);

    const cali = screen.getByLabelText(/Cali/i);
    await user.click(cali);

    await waitFor(() => expect(cali).not.toBeChecked());
    expect(onError).toHaveBeenCalled();
  });

  it("muestra mensaje cuando ningún organismo coincide", async () => {
    const user = userEvent.setup();
    render(<OTMatrix offices={offices} grantedIds={[]} onToggle={vi.fn()} />);
    await user.type(screen.getByLabelText(/buscar organismo/i), "zzz");
    expect(screen.getByText(/ningún organismo coincide/i)).toBeInTheDocument();
  });

  // ── HU #10518: bloqueo de habilitación para OT no operativos ─────────────────
  const operational: Record<string, OtOperationalInfo> = {
    o1: { hasTenant: true, estadoActivo: true }, // operable
    o2: { hasTenant: false, estadoActivo: null }, // sin alta
    o3: { hasTenant: true, estadoActivo: false }, // inactivo
  };

  it("deshabilita el checkbox y muestra badge para OT sin alta o inactivo", () => {
    render(
      <OTMatrix offices={offices} grantedIds={[]} operationalById={operational} onToggle={vi.fn()} />,
    );

    expect(screen.getByLabelText(/Secretaría de Movilidad Bogotá/i)).not.toBeDisabled();
    expect(screen.getByLabelText(/Medellín/i)).toBeDisabled(); // sin alta
    expect(screen.getByLabelText(/Cali/i)).toBeDisabled(); // inactivo

    expect(screen.getByText(/Sin alta en FLIT/i)).toBeInTheDocument();
    expect(screen.getByText(/Inactivo en FLIT/i)).toBeInTheDocument();
  });

  it("no dispara onToggle al intentar habilitar un OT no operable", async () => {
    const user = userEvent.setup();
    const onToggle = vi.fn().mockResolvedValue(undefined);
    render(
      <OTMatrix offices={offices} grantedIds={[]} operationalById={operational} onToggle={onToggle} />,
    );

    await user.click(screen.getByLabelText(/Medellín/i));
    expect(onToggle).not.toHaveBeenCalled();
  });

  it("permite DESMARCAR (quitar grant) un OT que quedó inactivo pero ya tenía grant", async () => {
    const user = userEvent.setup();
    const onToggle = vi.fn().mockResolvedValue(undefined);
    // o3 está inactivo pero ya estaba habilitado → debe poder quitarse.
    render(
      <OTMatrix offices={offices} grantedIds={["o3"]} operationalById={operational} onToggle={onToggle} />,
    );

    const cali = screen.getByLabelText(/Cali/i);
    expect(cali).not.toBeDisabled();
    expect(cali).toBeChecked();
    await user.click(cali);
    await waitFor(() => expect(onToggle).toHaveBeenCalledWith("o3", false));
  });

  it("muestra el mensaje del backend (422) cuando falla el POST del grant", async () => {
    const user = userEvent.setup();
    const serverMessage = "Este organismo está inactivo en FLIT. Actívelo antes de habilitarlo para la compañía.";
    const onToggle = vi
      .fn()
      .mockRejectedValue(new ApiValidationError([{ field: "transitOfficeId", message: serverMessage }], 422));
    const onError = vi.fn();
    // Sin operationalById → checkbox habilitado, el rechazo llega del backend.
    render(<OTMatrix offices={offices} grantedIds={[]} onToggle={onToggle} onError={onError} />);

    await user.click(screen.getByLabelText(/Cali/i));
    await waitFor(() => expect(onError).toHaveBeenCalledWith(serverMessage));
  });
});

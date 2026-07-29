// Ajustes HU #10929 — El formulario de escritura ya NO tiene el multiselect "Compañías a las que
// aplica": la escritura se crea SIEMPRE para UNA compañía fija que llega por contexto (desde la
// pantalla del representante). El nombre se muestra como dato de solo lectura y al guardar se envía
// como el único elemento de `companyIds`.
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DeedsFormPanel, type DeedFormCompany } from "../DeedsFormPanel";
import type { DeedFormInput, DeedSaved } from "@/lib/api/admin-deeds";

const COMPANY: DeedFormCompany = {
  id: "co-1",
  name: "Comercializadora XYZ",
  nit: "900123456-7",
};

function renderPanel(overrides: Partial<React.ComponentProps<typeof DeedsFormPanel>> = {}) {
  const onSubmit = vi.fn((input: DeedFormInput): Promise<DeedSaved> => {
    void input; // el argumento se inspecciona vía onSubmit.mock.calls, no dentro del cuerpo
    return Promise.resolve({ id: "deed-new" });
  });
  const onSaved = vi.fn();
  const onError = vi.fn();
  render(
    <DeedsFormPanel
      open
      editing={null}
      company={COMPANY}
      onClose={() => {}}
      onSubmit={onSubmit}
      onSaved={onSaved}
      onError={onError}
      {...overrides}
    />,
  );
  return { onSubmit, onSaved, onError };
}

describe("DeedsFormPanel (HU #10929)", () => {
  it("muestra la compañía fija como dato de solo lectura y sin selector de compañías", () => {
    renderPanel();
    // La compañía se muestra (nombre + NIT), pero NO hay multiselect ni el título del antiguo fieldset.
    expect(screen.getByText("Comercializadora XYZ")).toBeInTheDocument();
    expect(screen.getByText("900123456-7")).toBeInTheDocument();
    expect(screen.queryByText(/compañías a las que aplica/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
  });

  it("al registrar envía companyIds con la única compañía fija", async () => {
    const { onSubmit, onSaved } = renderPanel();

    await userEvent.type(screen.getByLabelText(/descripción/i), "Escritura de constitución");
    const file = new File(["%PDF-1.4"], "escritura.pdf", { type: "application/pdf" });
    await userEvent.upload(
      screen.getByLabelText(/selecciona el documento pdf de la escritura/i),
      file,
    );
    await userEvent.type(screen.getByLabelText(/vigencia desde/i), "2026-01-01");
    await userEvent.type(screen.getByLabelText(/vigencia hasta/i), "2027-01-01");

    await userEvent.click(screen.getByRole("button", { name: /registrar escritura/i }));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    const input = onSubmit.mock.calls[0][0];
    expect(input.companyIds).toEqual(["co-1"]);
    expect(input.description).toBe("Escritura de constitución");
    expect(input.file).toBe(file);
    expect(onSaved).toHaveBeenCalledWith({ id: "deed-new" });
  });

  it("en edición no exige un PDF nuevo (conserva el custodiado)", async () => {
    const { onSubmit } = renderPanel({
      editing: {
        id: "deed-1",
        description: "Poder general",
        vigenciaDesde: "2025-01-01",
        vigenciaHasta: "2026-01-01",
      },
    });

    // El formulario precarga los datos de la escritura; el botón de guardar queda habilitado sin PDF.
    expect(screen.getByDisplayValue("Poder general")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /guardar cambios/i }));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    const input = onSubmit.mock.calls[0][0];
    expect(input.file).toBeNull();
    expect(input.companyIds).toEqual(["co-1"]);
  });
});

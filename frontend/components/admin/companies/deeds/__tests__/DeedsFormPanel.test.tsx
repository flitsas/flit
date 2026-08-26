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

// Novedad nov.10 — la obligatoriedad de los campos existía pero era invisible: ningún input
// llevaba asterisco ni `aria-required`, y no había mensaje de error por campo.
describe("DeedsFormPanel — indicación de campos obligatorios (novedad nov.10)", () => {
  it("marca descripción, vigencia desde/hasta y PDF (en alta) como obligatorios vía aria-required", () => {
    renderPanel();

    expect(screen.getByLabelText(/descripción/i)).toHaveAttribute("aria-required", "true");
    expect(screen.getByLabelText(/vigencia desde/i)).toHaveAttribute("aria-required", "true");
    expect(screen.getByLabelText(/vigencia hasta/i)).toHaveAttribute("aria-required", "true");
    // El input de archivo también queda marcado como obligatorio en alta.
    const fileInput = document.getElementById("deed-file");
    expect(fileInput).toHaveAttribute("aria-required", "true");
  });

  it("en edición el PDF NO se marca como obligatorio (se conserva el custodiado si no se reemplaza)", () => {
    renderPanel({
      editing: {
        id: "deed-1",
        description: "Poder general",
        vigenciaDesde: "2025-01-01",
        vigenciaHasta: "2026-01-01",
      },
    });

    const fileInput = document.getElementById("deed-file");
    expect(fileInput).not.toHaveAttribute("aria-required");
    expect(screen.getByText(/opcional: reemplaza el actual/i)).toBeInTheDocument();
  });

  it("al intentar registrar con campos vacíos, muestra un mensaje de error por cada campo obligatorio y NO envía", async () => {
    const { onSubmit } = renderPanel();

    // El botón no está deshabilitado: al hacer click revela los errores en vez de bloquear
    // silenciosamente la interacción.
    const submitBtn = screen.getByRole("button", { name: /registrar escritura/i });
    expect(submitBtn).not.toBeDisabled();
    await userEvent.click(submitBtn);

    expect(await screen.findByText("La descripción es obligatoria.")).toBeInTheDocument();
    expect(screen.getByText("La vigencia desde es obligatoria.")).toBeInTheDocument();
    expect(screen.getByText("La vigencia hasta es obligatoria.")).toBeInTheDocument();
    expect(screen.getByText("El documento PDF es obligatorio.")).toBeInTheDocument();

    const description = screen.getByLabelText(/descripción/i);
    expect(description).toHaveAttribute("aria-invalid", "true");
    expect(description).toHaveAttribute("aria-describedby", "deed-description-error");

    expect(onSubmit).not.toHaveBeenCalled();
  });
});

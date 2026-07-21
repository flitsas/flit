// HU #10644 — Panel de alta de firma: validación en cliente, captura por carga de PNG
// y mapeo de errores 422 (incl. firma_activa_existente como mensaje amistoso).
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SignatureVaultFormPanel } from "../SignatureVaultFormPanel";
import { ApiValidationError } from "@/lib/api/types";

const PNG = new File([new Uint8Array([137, 80, 78, 71])], "firma.png", { type: "image/png" });

async function fillValidForm() {
  await userEvent.type(screen.getByLabelText(/número de documento/i), "1098765432");
  await userEvent.type(screen.getByLabelText(/nit de la empresa/i), "900123456-7");
  await userEvent.type(screen.getByLabelText(/nombre del apoderado/i), "Ana Gómez");
  fireEvent.change(screen.getByLabelText(/vigencia desde/i), { target: { value: "2026-01-01" } });
  fireEvent.change(screen.getByLabelText(/vigencia hasta/i), { target: { value: "2026-12-31" } });
  // Cargar el artefacto por PNG (evita el canvas, no soportado en jsdom).
  await userEvent.click(screen.getByRole("radio", { name: /cargar png/i }));
  await userEvent.upload(screen.getByLabelText(/selecciona un archivo png/i), PNG);
  await screen.findByAltText(/vista previa de la firma/i);
}

describe("SignatureVaultFormPanel (HU #10644)", () => {
  it("deshabilita el envío hasta que el formulario es válido y hay artefacto", async () => {
    render(
      <SignatureVaultFormPanel open onClose={vi.fn()} onSubmit={vi.fn()} onSaved={vi.fn()} onError={vi.fn()} />,
    );
    expect(screen.getByRole("button", { name: /registrar firma/i })).toBeDisabled();
  });

  it("envía el payload con el artefacto capturado", async () => {
    const onSubmit = vi.fn().mockResolvedValue({ id: "sig-9" });
    const onSaved = vi.fn();
    render(
      <SignatureVaultFormPanel open onClose={vi.fn()} onSubmit={onSubmit} onSaved={onSaved} onError={vi.fn()} />,
    );
    await fillValidForm();

    const submit = screen.getByRole("button", { name: /registrar firma/i });
    await waitFor(() => expect(submit).toBeEnabled());
    await userEvent.click(submit);

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    const payload = onSubmit.mock.calls[0][0];
    expect(payload).toMatchObject({
      documentType: "CC",
      documentNumber: "1098765432",
      nitEmpresa: "900123456-7",
      fullName: "Ana Gómez",
      vigenciaDesde: "2026-01-01",
      vigenciaHasta: "2026-12-31",
    });
    expect(payload.artefactoFirmaBase64).toMatch(/^data:image\/png/);
    expect(onSaved).toHaveBeenCalledWith({ id: "sig-9" });
  });

  it("muestra un mensaje amistoso ante firma_activa_existente (422)", async () => {
    const onSubmit = vi.fn().mockRejectedValue(
      new ApiValidationError(
        [{ field: "documentNumber", code: "firma_activa_existente", message: "existe" } as never],
        422,
      ),
    );
    render(
      <SignatureVaultFormPanel open onClose={vi.fn()} onSubmit={onSubmit} onSaved={vi.fn()} onError={vi.fn()} />,
    );
    await fillValidForm();
    await userEvent.click(screen.getByRole("button", { name: /registrar firma/i }));

    expect(await screen.findByText(/ya existe una firma activa/i)).toBeInTheDocument();
  });
});

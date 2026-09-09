// Feature #12201 — captura del NIT, revisión previa y generación del Certificado RUES (CF-04).
//
// Estos casos cubren el hueco que dejó la descomposición: la HU #12203 es `[BACKEND]` y ninguna
// HU pidió el formulario, así que la pestaña mostraba encabezado y ningún control.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RuesGeneracionForm } from "../RuesGeneracionForm";
import { previewRuesCompany, generateRuesDocument } from "@/lib/api/admin-generacion-documental";
import { ApiValidationError } from "@/lib/api/types";

vi.mock("@/lib/api/admin-generacion-documental", () => ({
  previewRuesCompany: vi.fn(),
  generateRuesDocument: vi.fn(),
}));

const previewMock = vi.mocked(previewRuesCompany);
const generateMock = vi.mocked(generateRuesDocument);

beforeEach(() => {
  vi.clearAllMocks();
});

describe("RuesGeneracionForm — la pestaña tiene por fin dónde escribir y qué pulsar", () => {
  it("expone el campo de NIT y las dos acciones", () => {
    render(<RuesGeneracionForm />);

    expect(screen.getByLabelText(/NIT de la compañía/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /revisar antes de generar/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /generar certificado/i })).toBeInTheDocument();
  });

  it("sin NIT ambas acciones están deshabilitadas", () => {
    render(<RuesGeneracionForm />);

    expect(screen.getByRole("button", { name: /revisar antes de generar/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /generar certificado/i })).toBeDisabled();
  });
});

describe("RuesGeneracionForm — revisión previa (no persiste nada)", () => {
  it("consulta con el NIT sin espacios y pinta los campos devueltos", async () => {
    previewMock.mockResolvedValue({
      found: true,
      nit: "900123456",
      fields: [
        { key: "razonSocial", label: "Razón social", value: "TRANSPORTES ACME S.A.S." },
        { key: "estadoMatricula", label: "Estado de la matrícula", value: null },
      ],
    });

    render(<RuesGeneracionForm />);
    await userEvent.type(screen.getByLabelText(/NIT de la compañía/i), "  900123456  ");
    await userEvent.click(screen.getByRole("button", { name: /revisar antes de generar/i }));

    await waitFor(() => expect(previewMock).toHaveBeenCalledWith("900123456"));
    expect(await screen.findByText("TRANSPORTES ACME S.A.S.")).toBeInTheDocument();
    // Un campo sin valor se pinta como guion, no como "null" ni como hueco mudo.
    expect(screen.getByText("—")).toBeInTheDocument();
    // La revisión previa no genera: el endpoint de generación no se toca.
    expect(generateMock).not.toHaveBeenCalled();
  });

  it("un NIT sin coincidencia lo dice, y no finge una tabla vacía", async () => {
    previewMock.mockResolvedValue({ found: false, nit: "999999999", fields: [] });

    render(<RuesGeneracionForm />);
    await userEvent.type(screen.getByLabelText(/NIT de la compañía/i), "999999999");
    await userEvent.click(screen.getByRole("button", { name: /revisar antes de generar/i }));

    expect(await screen.findByText(/no tiene coincidencia en el RUES/i)).toBeInTheDocument();
    expect(screen.queryByText(/datos que quedarán congelados/i)).not.toBeInTheDocument();
  });

  it("editar el NIT invalida la revisión previa en pantalla", async () => {
    previewMock.mockResolvedValue({
      found: true,
      nit: "900123456",
      fields: [{ key: "razonSocial", label: "Razón social", value: "TRANSPORTES ACME S.A.S." }],
    });

    render(<RuesGeneracionForm />);
    const input = screen.getByLabelText(/NIT de la compañía/i);
    await userEvent.type(input, "900123456");
    await userEvent.click(screen.getByRole("button", { name: /revisar antes de generar/i }));
    expect(await screen.findByText("TRANSPORTES ACME S.A.S.")).toBeInTheDocument();

    await userEvent.type(input, "7");

    expect(screen.queryByText("TRANSPORTES ACME S.A.S.")).not.toBeInTheDocument();
  });
});

describe("RuesGeneracionForm — generación", () => {
  it("genera y remite al historial, dejando claro que no devuelve el PDF", async () => {
    generateMock.mockResolvedValue({ id: "0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b", status: "generated" });

    render(<RuesGeneracionForm />);
    await userEvent.type(screen.getByLabelText(/NIT de la compañía/i), "900123456");
    await userEvent.click(screen.getByRole("button", { name: /generar certificado/i }));

    await waitFor(() => expect(generateMock).toHaveBeenCalledWith("900123456"));

    const aviso = await screen.findByRole("status");
    expect(aviso).toHaveTextContent(/nunca el PDF/i);
    expect(screen.getByRole("link", { name: /ir al historial/i })).toHaveAttribute(
      "href",
      "/admin/generacion-documental/historial",
    );
  });

  it("un 422 se muestra por campo y NUNCA repite el valor capturado", async () => {
    generateMock.mockRejectedValue(
      new ApiValidationError(
        [{ field: "nit", message: "El NIT no tiene coincidencia en RUES.", value: "900123456" }],
        422,
      ),
    );

    render(<RuesGeneracionForm />);
    await userEvent.type(screen.getByLabelText(/NIT de la compañía/i), "900123456");
    await userEvent.click(screen.getByRole("button", { name: /generar certificado/i }));

    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("El NIT no tiene coincidencia en RUES.");

    // Control positivo: el extractor sí ve el contenido de la alerta, así que la ausencia del
    // valor capturado significa algo. El NIT aparece en el input —eso es lo que el usuario
    // escribió—, pero no debe reaparecer dentro del mensaje de error.
    expect(alerta).toHaveTextContent("nit");
    expect(alerta.textContent ?? "").not.toContain("900123456");
  });

  it("un fallo inesperado no deja la pantalla muda", async () => {
    generateMock.mockRejectedValue(new Error("boom"));

    render(<RuesGeneracionForm />);
    await userEvent.type(screen.getByLabelText(/NIT de la compañía/i), "900123456");
    await userEvent.click(screen.getByRole("button", { name: /generar certificado/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/no se pudo generar el certificado/i);
  });
});

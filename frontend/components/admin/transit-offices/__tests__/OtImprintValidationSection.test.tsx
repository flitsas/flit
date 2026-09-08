import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { OtImprintValidationSection } from "@/components/admin/transit-offices/OtImprintValidationSection";
import { ToastProvider } from "@/components/admin/Toast";
import type { ImprintSignatureDto } from "@/lib/api/admin-ot-imprint-signatures";

const fetchListImprintSignatures = vi.fn();
const validateImprintSignature = vi.fn();

vi.mock("@/lib/api/admin-ot-imprint-signatures", () => ({
  fetchListImprintSignatures: (...args: unknown[]) => fetchListImprintSignatures(...args),
  validateImprintSignature: (...args: unknown[]) => validateImprintSignature(...args),
}));

function imprintRow(overrides: Partial<ImprintSignatureDto> = {}): ImprintSignatureDto {
  return {
    id: "imp-1",
    tenantId: "tenant-1",
    procedureInstanceId: "proc-1",
    placa: "ABC123",
    moduleCode: "impronta_manual",
    attachmentId: null,
    publicKey: "-----BEGIN PUBLIC KEY-----",
    documentHash: "aabbccddeeff00112233445566778899",
    signature: "sig-base64",
    signedAt: "2026-09-08T12:00:00Z",
    wasSignedWithoutOwnerSignature: false,
    signedStoragePath: null,
    signedSha256: null,
    signedSizeBytes: null,
    signedFilename: null,
    deletedAt: null,
    ...overrides,
  };
}

function renderSection() {
  return render(
    <ToastProvider>
      <OtImprintValidationSection transitOfficeId="ot-1" />
    </ToastProvider>,
  );
}

describe("OtImprintValidationSection", () => {
  beforeEach(() => {
    fetchListImprintSignatures.mockReset();
    validateImprintSignature.mockReset();
  });

  it("muestra estado vacío inicial antes de consultar", () => {
    renderSection();
    expect(screen.getByTestId("ot-imprint-validation-idle")).toBeInTheDocument();
    expect(
      screen.getByText("Ingrese una placa y pulse Consultar para ver las improntas firmadas."),
    ).toBeInTheDocument();
  });

  it("muestra tabla al obtener resultados tras consultar", async () => {
    fetchListImprintSignatures.mockResolvedValue([imprintRow()]);
    renderSection();

    await userEvent.type(screen.getByTestId("ot-imprint-validation-placa-input"), "ABC123");
    await userEvent.click(screen.getByTestId("ot-imprint-validation-search-btn"));

    await waitFor(() => {
      expect(screen.getByTestId("ot-imprint-validation-table")).toBeInTheDocument();
    });
    expect(screen.getByText("ABC123")).toBeInTheDocument();
    expect(fetchListImprintSignatures).toHaveBeenCalledWith("ABC123", undefined, {
      transitOfficeId: "ot-1",
    });
  });

  it("muestra estado vacío tras búsqueda sin resultados", async () => {
    fetchListImprintSignatures.mockResolvedValue([]);
    renderSection();

    await userEvent.type(screen.getByTestId("ot-imprint-validation-placa-input"), "ZZZ999");
    await userEvent.click(screen.getByTestId("ot-imprint-validation-search-btn"));

    await waitFor(() => {
      expect(screen.getByText("No hay improntas firmadas para la placa ZZZ999.")).toBeInTheDocument();
    });
  });

  it("muestra error y permite reintentar", async () => {
    fetchListImprintSignatures.mockRejectedValueOnce(new Error("fallo")).mockResolvedValueOnce([]);
    renderSection();

    await userEvent.type(screen.getByTestId("ot-imprint-validation-placa-input"), "ERR001");
    await userEvent.click(screen.getByTestId("ot-imprint-validation-search-btn"));

    await waitFor(() => {
      expect(screen.getByTestId("ui-error")).toBeInTheDocument();
    });

    await userEvent.click(screen.getByRole("button", { name: /reintentar/i }));

    await waitFor(() => {
      expect(fetchListImprintSignatures).toHaveBeenCalledTimes(2);
    });
  });

  it("valida firma y muestra badge válida", async () => {
    fetchListImprintSignatures.mockResolvedValue([imprintRow()]);
    validateImprintSignature.mockResolvedValue({
      validationId: "val-1",
      vehicleSignatureImprintId: "imp-1",
      result: "valid",
      failureReason: null,
      validatedAt: "2026-09-08T13:00:00Z",
    });

    renderSection();
    await userEvent.type(screen.getByTestId("ot-imprint-validation-placa-input"), "ABC123");
    await userEvent.click(screen.getByTestId("ot-imprint-validation-search-btn"));

    await waitFor(() => {
      expect(screen.getByRole("button", { name: /validar firma de impronta abc123/i })).toBeInTheDocument();
    });

    await userEvent.click(screen.getByRole("button", { name: /validar firma de impronta abc123/i }));

    await waitFor(() => {
      expect(screen.getByLabelText("Estado: Válida")).toBeInTheDocument();
    });
    expect(validateImprintSignature).toHaveBeenCalledWith("imp-1", undefined, {
      transitOfficeId: "ot-1",
    });
  });
});

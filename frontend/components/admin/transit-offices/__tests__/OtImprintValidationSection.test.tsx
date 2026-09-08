import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { OtImprintValidationSection } from "@/components/admin/transit-offices/OtImprintValidationSection";
import { ToastProvider } from "@/components/admin/Toast";
import type { ImprintSignatureDto } from "@/lib/api/admin-ot-imprint-signatures";

const fetchListImprintSignatures = vi.fn();
const validateImprintSignature = vi.fn();
const fetchImprintSignaturePreviewUrl = vi.fn();
const fetchImprintSignatureValidations = vi.fn();
const openPdfBlobInNewTab = vi.fn();

vi.mock("@/lib/api/admin-ot-imprint-signatures", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/admin-ot-imprint-signatures")>(
    "@/lib/api/admin-ot-imprint-signatures",
  );
  return {
    ...actual,
    fetchListImprintSignatures: (...args: unknown[]) => fetchListImprintSignatures(...args),
    validateImprintSignature: (...args: unknown[]) => validateImprintSignature(...args),
    fetchImprintSignaturePreviewUrl: (...args: unknown[]) => fetchImprintSignaturePreviewUrl(...args),
    fetchImprintSignatureValidations: (...args: unknown[]) =>
      fetchImprintSignatureValidations(...args),
  };
});

vi.mock("@/lib/documents/open-document-tab", () => ({
  openPdfBlobInNewTab: (...args: unknown[]) => openPdfBlobInNewTab(...args),
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
    lastValidation: null,
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
    fetchImprintSignaturePreviewUrl.mockReset();
    fetchImprintSignatureValidations.mockReset();
    openPdfBlobInNewTab.mockReset();
    openPdfBlobInNewTab.mockImplementation(async (fn: () => Promise<Blob>) => {
      await fn();
    });
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

  it("abre modal, acepta firma pegada y muestra badge válida", async () => {
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
      expect(screen.getByTestId("ot-imprint-validation-modal")).toBeInTheDocument();
    });

    await userEvent.type(
      screen.getByTestId("ot-imprint-validation-signature-input"),
      "UgMgkJ+Ay1OwexM8",
    );
    await userEvent.click(screen.getByTestId("ot-imprint-validation-accept-btn"));

    await waitFor(() => {
      expect(screen.queryByTestId("ot-imprint-validation-modal")).not.toBeInTheDocument();
    });
    expect(screen.getByLabelText("Estado: Válida")).toBeInTheDocument();
    expect(validateImprintSignature).toHaveBeenCalledWith(
      "imp-1",
      "UgMgkJ+Ay1OwexM8",
      undefined,
      { transitOfficeId: "ot-1" },
    );
  });

  it("cierra el modal y muestra badge inválida cuando la firma no corresponde", async () => {
    fetchListImprintSignatures.mockResolvedValue([imprintRow()]);
    validateImprintSignature.mockResolvedValue({
      validationId: "val-2",
      vehicleSignatureImprintId: "imp-1",
      result: "invalid",
      failureReason: "La firma ingresada no corresponde a esta impronta.",
      validatedAt: "2026-09-08T13:00:00Z",
    });

    renderSection();
    await userEvent.type(screen.getByTestId("ot-imprint-validation-placa-input"), "ABC123");
    await userEvent.click(screen.getByTestId("ot-imprint-validation-search-btn"));
    await userEvent.click(
      await screen.findByRole("button", { name: /validar firma de impronta abc123/i }),
    );
    await userEvent.type(screen.getByTestId("ot-imprint-validation-signature-input"), "firma-mala");
    await userEvent.click(screen.getByTestId("ot-imprint-validation-accept-btn"));

    await waitFor(() => {
      expect(screen.queryByTestId("ot-imprint-validation-modal")).not.toBeInTheDocument();
    });
    expect(
      screen.getByLabelText(/Firma inválida: La firma ingresada no corresponde/),
    ).toBeInTheDocument();
    expect(
      screen.getByText("La firma ingresada no corresponde a esta impronta."),
    ).toBeInTheDocument();
  });

  it("deshabilita Ver PDF cuando no hay archivo disponible", async () => {
    fetchListImprintSignatures.mockResolvedValue([imprintRow()]);
    renderSection();
    await userEvent.type(screen.getByTestId("ot-imprint-validation-placa-input"), "ABC123");
    await userEvent.click(screen.getByTestId("ot-imprint-validation-search-btn"));

    const viewBtn = await screen.findByTestId("ot-imprint-view-pdf-imp-1");
    expect(viewBtn).toBeDisabled();
    expect(fetchImprintSignaturePreviewUrl).not.toHaveBeenCalled();
  });

  it("abre el PDF firmado cuando hay signedStoragePath", async () => {
    fetchListImprintSignatures.mockResolvedValue([
      imprintRow({ signedStoragePath: "snap/impronta.pdf", signedFilename: "impronta.pdf" }),
    ]);
    fetchImprintSignaturePreviewUrl.mockResolvedValue({
      url: "https://s3.test/view/snap/impronta.pdf",
      expiresAt: "2026-09-08T14:00:00Z",
    });
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      blob: async () => new Blob(["%PDF"], { type: "application/octet-stream" }),
    }) as unknown as typeof fetch;

    renderSection();
    await userEvent.type(screen.getByTestId("ot-imprint-validation-placa-input"), "ABC123");
    await userEvent.click(screen.getByTestId("ot-imprint-validation-search-btn"));

    const viewBtn = await screen.findByRole("button", { name: /ver pdf de impronta abc123/i });
    expect(viewBtn).toBeEnabled();
    await userEvent.click(viewBtn);

    await waitFor(() => {
      expect(fetchImprintSignaturePreviewUrl).toHaveBeenCalledWith("imp-1", undefined, {
        transitOfficeId: "ot-1",
      });
    });
    expect(openPdfBlobInNewTab).toHaveBeenCalled();
    expect(global.fetch).toHaveBeenCalledWith("https://s3.test/view/snap/impronta.pdf");
  });

  it("muestra badge desde lastValidation del listado (persistido)", async () => {
    fetchListImprintSignatures.mockResolvedValue([
      imprintRow({
        lastValidation: {
          id: "val-persist",
          result: "valid",
          failureReason: null,
          validatedAt: "2026-09-08T11:00:00Z",
          validatedBy: "11111111-1111-1111-1111-111111111111",
          placa: "ABC123",
        },
      }),
    ]);
    renderSection();
    await userEvent.type(screen.getByTestId("ot-imprint-validation-placa-input"), "ABC123");
    await userEvent.click(screen.getByTestId("ot-imprint-validation-search-btn"));

    await waitFor(() => {
      expect(screen.getByLabelText("Estado: Válida")).toBeInTheDocument();
    });
  });

  it("abre historial de bitácora", async () => {
    fetchListImprintSignatures.mockResolvedValue([imprintRow()]);
    fetchImprintSignatureValidations.mockResolvedValue([
      {
        id: "val-1",
        result: "invalid",
        failureReason: "no coincide",
        validatedAt: "2026-09-08T10:00:00Z",
        validatedBy: "22222222-2222-2222-2222-222222222222",
        placa: "ABC123",
      },
    ]);

    renderSection();
    await userEvent.type(screen.getByTestId("ot-imprint-validation-placa-input"), "ABC123");
    await userEvent.click(screen.getByTestId("ot-imprint-validation-search-btn"));
    await userEvent.click(await screen.findByTestId("ot-imprint-history-imp-1"));

    await waitFor(() => {
      expect(screen.getByTestId("ot-imprint-history-modal")).toBeInTheDocument();
    });
    expect(fetchImprintSignatureValidations).toHaveBeenCalledWith("imp-1", undefined, {
      transitOfficeId: "ot-1",
    });
    expect(screen.getByLabelText("Estado: Inválida")).toBeInTheDocument();
    expect(screen.getByText("no coincide")).toBeInTheDocument();
  });
});

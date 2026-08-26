// HU #11193 — capturar la firma del baúl desde el formulario del representante.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SignatureVaultSelector } from "../SignatureVaultSelector";

vi.mock("@/lib/api/admin-signature-vault", () => ({
  fetchSignatureVaultByDocument: vi.fn(),
  createSignatureVaultEntry: vi.fn(),
}));

// El capturador real dibuja sobre un <canvas>, que jsdom no rasteriza. Se sustituye por un doble
// que produce el mismo contrato: un data URL PNG a través de onChange.
vi.mock("@/components/admin/companies/signature-vault/SignatureCapture", () => ({
  SignatureCapture: ({ onChange }: { onChange: (v: string | null) => void }) => (
    <button type="button" onClick={() => onChange("data:image/png;base64,QUJD")}>
      Dibujar firma
    </button>
  ),
}));

import {
  createSignatureVaultEntry,
  fetchSignatureVaultByDocument,
} from "@/lib/api/admin-signature-vault";

const TENANT = "tenant-1";

/** Firma vigente de la persona, SIN código hash: el caso que el punto 1 dejaba invisible. */
const VIGENTE_SIN_HASH = {
  id: "firma-1",
  documentType: "CC",
  documentNumber: "1038409485",
  fullName: "Juan Felipe Montoya",
  estado: "activa",
  vigenciaDesde: "2026-01-01",
  vigenciaHasta: "2027-01-01",
} as never;
const PROPS = {
  tenantId: TENANT,
  documentType: "CC",
  documentNumber: "1038409485",
  value: null,
  readOnly: false,
  fullName: "Juan Felipe Montoya",
  nitEmpresa: "900123456",
};

describe("SignatureVaultSelector — HU #11193 (captura desde el formulario)", () => {
  beforeEach(() => vi.clearAllMocks());

  it("AC1 sin firmas vigentes ofrece capturar y abre el bloque en el mismo formulario", async () => {
    vi.mocked(fetchSignatureVaultByDocument).mockResolvedValue([]);
    const user = userEvent.setup();
    render(<SignatureVaultSelector {...PROPS} onChange={vi.fn()} />);

    const abrir = await screen.findByTestId("sig-capture-open");
    await user.click(abrir);

    expect(await screen.findByTestId("sig-capture-block")).toBeInTheDocument();
    // No se abre otro formulario: no hay diálogo modal.
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("AC2 el bloque toma el nombre y el documento del representante", async () => {
    vi.mocked(fetchSignatureVaultByDocument).mockResolvedValue([]);
    const user = userEvent.setup();
    render(<SignatureVaultSelector {...PROPS} onChange={vi.fn()} />);

    await user.click(await screen.findByTestId("sig-capture-open"));
    const bloque = await screen.findByTestId("sig-capture-block");

    expect(bloque).toHaveTextContent("Juan Felipe Montoya");
    expect(bloque).toHaveTextContent("CC");
    expect(bloque).toHaveTextContent("1038409485");
  });

  it("AC3 al guardar registra la firma con los datos del representante y la deja elegida", async () => {
    vi.mocked(fetchSignatureVaultByDocument).mockResolvedValue([]);
    vi.mocked(createSignatureVaultEntry).mockResolvedValue({ id: "firma-nueva" });
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<SignatureVaultSelector {...PROPS} onChange={onChange} />);

    await user.click(await screen.findByTestId("sig-capture-open"));
    await user.click(screen.getByRole("button", { name: /Dibujar firma/i }));
    await user.type(screen.getByLabelText("Código hash"), "A1B2C3D4");
    await user.type(screen.getByLabelText("Vigencia desde"), "2026-08-01");
    await user.type(screen.getByLabelText("Vigencia hasta"), "2027-08-01");
    await user.click(screen.getByRole("button", { name: /Guardar firma/i }));

    await waitFor(() =>
      expect(createSignatureVaultEntry).toHaveBeenCalledWith(TENANT, {
        documentType: "CC",
        documentNumber: "1038409485",
        nitEmpresa: "900123456",
        fullName: "Juan Felipe Montoya",
        codigoHash: "A1B2C3D4",
        vigenciaDesde: "2026-08-01",
        vigenciaHasta: "2027-08-01",
        artefactoFirmaBase64: "data:image/png;base64,QUJD",
      }),
    );
    expect(onChange).toHaveBeenCalledWith("firma-nueva");
  });

  // El código hash es lo que se estampa como "Hash:" en el sello de la firma del baúl de todos los
  // documentos. Sin este campo, una firma capturada desde el formulario del representante salía sin
  // hash mientras la capturada desde el baúl sí lo llevaba.
  it("el código hash digitado viaja al baúl y sin digitarlo no se manda cadena vacía", async () => {
    vi.mocked(fetchSignatureVaultByDocument).mockResolvedValue([]);
    vi.mocked(createSignatureVaultEntry).mockResolvedValue({ id: "firma-nueva" });
    const user = userEvent.setup();
    render(<SignatureVaultSelector {...PROPS} onChange={vi.fn()} />);

    await user.click(await screen.findByTestId("sig-capture-open"));
    await user.click(screen.getByRole("button", { name: /Dibujar firma/i }));
    await user.type(screen.getByLabelText("Vigencia desde"), "2026-08-01");
    await user.type(screen.getByLabelText("Vigencia hasta"), "2027-08-01");
    await user.click(screen.getByRole("button", { name: /Guardar firma/i }));

    // `undefined`, no "": una cadena vacía persistiría un hash en blanco que el sello trataría como
    // presente y ensuciaría el documento con una línea "Hash:" sin valor.
    await waitFor(() =>
      expect(vi.mocked(createSignatureVaultEntry).mock.calls[0][1].codigoHash).toBeUndefined(),
    );
  });

  it("AC3 si el registro falla se informa y el bloque sigue abierto", async () => {
    vi.mocked(fetchSignatureVaultByDocument).mockResolvedValue([]);
    vi.mocked(createSignatureVaultEntry).mockRejectedValue(new Error("422"));
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<SignatureVaultSelector {...PROPS} onChange={onChange} />);

    await user.click(await screen.findByTestId("sig-capture-open"));
    await user.click(screen.getByRole("button", { name: /Dibujar firma/i }));
    await user.type(screen.getByLabelText("Vigencia desde"), "2026-08-01");
    await user.type(screen.getByLabelText("Vigencia hasta"), "2027-08-01");
    await user.click(screen.getByRole("button", { name: /Guardar firma/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/No se pudo registrar la firma/i);
    expect(onChange).not.toHaveBeenCalled();
    expect(screen.getByTestId("sig-capture-block")).toBeInTheDocument();
  });

  it("muestra el mensaje del backend cuando el alta falla por validación", async () => {
    const { ApiValidationError } = await import("@/lib/api/types");
    vi.mocked(fetchSignatureVaultByDocument).mockResolvedValue([]);
    vi.mocked(createSignatureVaultEntry).mockRejectedValue(
      new ApiValidationError(
        [{ field: "vigenciaHasta", message: "La vigencia hasta no puede ser anterior a la vigencia desde." }],
        422,
      ),
    );
    const user = userEvent.setup();
    render(<SignatureVaultSelector {...PROPS} onChange={vi.fn()} />);

    await user.click(await screen.findByTestId("sig-capture-open"));
    await user.click(screen.getByRole("button", { name: /Dibujar firma/i }));
    await user.type(screen.getByLabelText("Vigencia desde"), "2026-08-01");
    await user.type(screen.getByLabelText("Vigencia hasta"), "2027-08-01");
    await user.click(screen.getByRole("button", { name: /Guardar firma/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/vigencia hasta no puede ser anterior/i);
  });

  it("AC5 con firmas vigentes se elige de la lista, no se pide capturar una primera", async () => {
    vi.mocked(fetchSignatureVaultByDocument).mockResolvedValue([VIGENTE_SIN_HASH]);
    render(<SignatureVaultSelector {...PROPS} onChange={vi.fn()} />);

    expect(await screen.findByTestId("sig-selector-select")).toBeInTheDocument();
    expect(screen.queryByTestId("sig-capture-open")).not.toBeInTheDocument();
  });

  it("en modo consulta no se ofrece capturar", async () => {
    vi.mocked(fetchSignatureVaultByDocument).mockResolvedValue([]);
    render(<SignatureVaultSelector {...PROPS} readOnly onChange={vi.fn()} />);

    await screen.findByTestId("sig-selector-empty");
    expect(screen.queryByTestId("sig-capture-open")).not.toBeInTheDocument();
  });

  // ── Corregir una firma ya capturada (hallado al auditar el código hash) ──

  it("con una firma vigente SI se puede capturar otra: es la unica forma de corregir el hash", async () => {
    // La captura solo se ofrecía cuando la persona no tenía firmas, así que un código hash mal
    // digitado no se podía corregir desde el panel del representante: había que salir al Baúl y
    // anular la firma primero. El backend ya sustituye la activa (D7); faltaba ofrecerlo.
    vi.mocked(fetchSignatureVaultByDocument).mockResolvedValue([VIGENTE_SIN_HASH]);
    const user = userEvent.setup();
    render(<SignatureVaultSelector {...PROPS} onChange={vi.fn()} />);

    await user.click(await screen.findByTestId("sig-capture-replace"));

    expect(await screen.findByTestId("sig-capture-block")).toBeInTheDocument();
    // Sustituir la firma de una persona no debe descubrirse después de guardar.
    expect(screen.getByText(/sustituirá a la que la persona tiene vigente/i)).toBeInTheDocument();
  });

  it("una firma sin codigo hash se distingue de una completa en la lista", async () => {
    // Sin mostrarlo, una firma capturada sin código era indistinguible de una completa hasta que
    // alguien generaba un PDF y notaba que faltaba la línea "Hash:".
    vi.mocked(fetchSignatureVaultByDocument).mockResolvedValue([
      VIGENTE_SIN_HASH,
      { ...(VIGENTE_SIN_HASH as object), id: "firma-2", codigoHash: "A1B2C3" } as never,
    ]);
    render(<SignatureVaultSelector {...PROPS} onChange={vi.fn()} />);

    await screen.findByTestId("sig-selector-select");
    expect(screen.getByRole("option", { name: /sin código hash/i })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: /Hash A1B2C3/ })).toBeInTheDocument();
  });

  it("en modo consulta no se ofrece recapturar aunque haya firma", async () => {
    vi.mocked(fetchSignatureVaultByDocument).mockResolvedValue([VIGENTE_SIN_HASH]);
    render(<SignatureVaultSelector {...PROPS} readOnly onChange={vi.fn()} />);

    await screen.findByTestId("sig-selector-readonly");
    expect(screen.queryByTestId("sig-capture-replace")).not.toBeInTheDocument();
  });
});

// HU #13248 (F9 #13245) — formulario del mandatario: correo obligatorio con validación de identidad,
// avisos antes de guardar y errores del servidor sin perder lo escrito.

import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApiError, ApiValidationError } from "@/lib/api/types";
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";

vi.mock("@/lib/api/admin-mandate-signers", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/admin-mandate-signers")>()),
  fetchCompanyAssociableCompanies: vi
    .fn()
    .mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 100, aplicaSoloASuCompania: true }),
}));
vi.mock("@/lib/api/admin-signature-vault", () => ({
  fetchSignatureVaultByDocument: vi.fn().mockResolvedValue([]),
  createSignatureVaultEntry: vi.fn(),
}));

import { CompanyMandatarioForm } from "../CompanyMandatarioForm";

const OFICINAS = [{ transitOfficeId: "ot-1", code: "05001000", name: "Tránsito de Medellín" }];

function signer(overrides: Partial<MandateSigner> = {}): MandateSigner {
  return {
    id: "ms-1",
    transitOfficeId: "ot-1",
    fullName: "Ana Restrepo",
    documentType: "CC",
    documentNumber: "1020304050",
    integrityHash: "a".repeat(64),
    email: "ana@ejemplo.com",
    userId: null,
    identityStatus: "none",
    signatureVaultId: null,
    registeredAt: "2026-08-01T10:00:00Z",
    isActive: true,
    companyTenantIds: ["t-1"],
    transitOfficeIds: ["ot-1"],
    signerModel: "natural",
    signatureMethod: "biometria",
    ...overrides,
  };
}

const onSubmit = vi.fn();
function renderForm(props: Partial<React.ComponentProps<typeof CompanyMandatarioForm>> = {}) {
  return render(
    <CompanyMandatarioForm
      tenantId="t-1"
      offices={OFICINAS}
      editing={null}
      initialOfficeIds={["ot-1"]}
      onCancel={vi.fn()}
      onSubmit={onSubmit}
      {...props}
    />,
  );
}
const radio = (name: string) => screen.getByRole("radio", { name });
const guardar = () => screen.getByRole("button", { name: "Guardar" });
const correo = () => screen.getByLabelText(/^Correo/);

beforeEach(() => {
  vi.clearAllMocks();
  onSubmit.mockResolvedValue({ id: "ms-2", integrityHash: "b".repeat(64) });
});

async function llenar(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText("Nombre completo"), "Ana Restrepo");
  await user.type(screen.getByLabelText("Número de documento"), "1020304050");
}

describe("HU #13248 AC3 — correo obligatorio en Persona natural", () => {
  it("el correo es obligatorio desde el inicio, con Validación de identidad y con Baúl", async () => {
    const user = userEvent.setup();
    renderForm();
    expect(screen.getByLabelText("Correo (obligatorio)")).toBeRequired();
    await user.click(radio("Validación de identidad"));
    expect(correo()).toBeRequired();
    await user.click(radio("Baúl de firmas"));
    expect(correo()).toBeRequired();
  });

  it("con Baúl de firmas tampoco guarda sin correo", async () => {
    const user = userEvent.setup();
    renderForm();
    await llenar(user);
    await user.click(radio("Baúl de firmas"));
    await user.click(guardar());
    expect(onSubmit).not.toHaveBeenCalled();
    expect(screen.getByText(/Escribe el correo/)).toBeInTheDocument();
  });

  it("sin correo no guarda y lo dice junto al campo", async () => {
    const user = userEvent.setup();
    renderForm();
    await llenar(user);
    await user.click(radio("Validación de identidad"));
    await user.click(guardar());
    expect(onSubmit).not.toHaveBeenCalled();
    expect(screen.getByText(/Escribe el correo/)).toBeInTheDocument();
    expect(correo()).toHaveAttribute("aria-invalid", "true");
  });

  it("con correo guarda y lo envía", async () => {
    const user = userEvent.setup();
    renderForm();
    await llenar(user);
    await user.type(correo(), "ana@ejemplo.com");
    await user.click(radio("Validación de identidad"));
    await user.click(guardar());
    expect(onSubmit).toHaveBeenCalledWith(
      expect.objectContaining({ signatureMethod: "biometria", email: "ana@ejemplo.com" }),
    );
  });
});

describe("HU #13248 AC4 — avisos antes de guardar", () => {
  const aviso = () => screen.queryByTestId("mandatario-aviso-validacion");

  it("alta con validación de identidad avisa que se enviará el enlace", async () => {
    const user = userEvent.setup();
    renderForm();
    expect(aviso()).not.toBeInTheDocument();
    await user.click(radio("Validación de identidad"));
    expect(aviso()).toHaveTextContent(/enviaremos el enlace/i);
  });

  it("pasar de Baúl a Validación de identidad avisa de la nueva validación", async () => {
    const user = userEvent.setup();
    renderForm({ editing: signer({ signatureMethod: "baul", signatureVaultId: "sig-1" }) });
    expect(aviso()).not.toBeInTheDocument();
    await user.click(radio("Validación de identidad"));
    expect(aviso()).toHaveTextContent(/nueva validación/i);
  });

  it("cambiar el número de documento avisa que la anterior deja de contar", async () => {
    const user = userEvent.setup();
    renderForm({ editing: signer() });
    expect(aviso()).not.toBeInTheDocument();
    const doc = screen.getByLabelText("Número de documento");
    await user.clear(doc);
    await user.type(doc, "99999");
    expect(aviso()).toHaveTextContent(/nueva validación.*La anterior deja de contar/i);
  });

  it("cambiar el tipo de documento también avisa", async () => {
    const user = userEvent.setup();
    renderForm({ editing: signer() });
    await user.selectOptions(screen.getByLabelText("Tipo de documento"), "CE");
    expect(aviso()).toHaveTextContent(/La anterior deja de contar/);
  });

  it("editar sin cambiar documento ni forma no avisa; con Baúl tampoco", async () => {
    const user = userEvent.setup();
    renderForm({ editing: signer() });
    expect(aviso()).not.toBeInTheDocument();
    await user.click(radio("Baúl de firmas"));
    expect(aviso()).not.toBeInTheDocument();
  });
});

describe("HU #13248 AC5 — errores del servidor", () => {
  it("422 por correo se muestra junto al campo y el formulario conserva lo escrito", async () => {
    const user = userEvent.setup();
    onSubmit.mockRejectedValueOnce(
      new ApiValidationError(
        [{ field: "email", message: "El correo es obligatorio con validación.", value: null }],
        422,
      ),
    );
    renderForm();
    await llenar(user);
    await user.type(correo(), "ana@ejemplo.com");
    await user.click(radio("Validación de identidad"));
    await user.click(guardar());
    expect(await screen.findByText("El correo es obligatorio con validación.")).toBeInTheDocument();
    expect(screen.getByLabelText("Nombre completo")).toHaveValue("Ana Restrepo");
    expect(correo()).toHaveValue("ana@ejemplo.com");
    expect(radio("Validación de identidad")).toBeChecked();
  });

  it("409 por modelo sin validación y 502 del envío muestran un mensaje y conservan lo escrito", async () => {
    const user = userEvent.setup();
    onSubmit
      .mockRejectedValueOnce(new ApiError(409, "x", { code: "mandatario_no_requiere_validacion" }))
      .mockRejectedValueOnce(new ApiError(502, "x", { code: "proveedor_error" }));
    renderForm();
    await llenar(user);
    await user.click(radio("Validación de identidad"));
    await user.type(correo(), "ana@ejemplo.com");
    await user.click(guardar());
    expect(await screen.findByRole("alert")).toHaveTextContent(/no requiere validación/i);
    await user.click(guardar());
    expect(await screen.findByText(/No pudimos enviar la validación/)).toBeInTheDocument();
    expect(screen.getByLabelText("Nombre completo")).toHaveValue("Ana Restrepo");
  });
});

describe("HU #13248 AC1/AC6/AC7 — ficha dentro del formulario", () => {
  it("al editar con validación de identidad se ve el estado y «Reenviar validación» con onResend", () => {
    renderForm({ editing: signer(), onResend: vi.fn() });
    expect(screen.getByTestId("mandatario-validacion-estado")).toHaveTextContent("Pendiente de validación");
    expect(screen.getByRole("button", { name: "Reenviar validación" })).toBeInTheDocument();
  });

  it("al editar con Baúl no hay bloque ni botón", () => {
    renderForm({ editing: signer({ signatureMethod: "baul", signatureVaultId: "s" }), onResend: vi.fn() });
    expect(screen.queryByTestId("mandatario-identidad")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /reenviar/i })).not.toBeInTheDocument();
  });

  it("en el alta no hay bloque (aún no existe la ficha)", () => {
    renderForm({ onResend: vi.fn() });
    expect(screen.queryByTestId("mandatario-identidad")).not.toBeInTheDocument();
  });
});

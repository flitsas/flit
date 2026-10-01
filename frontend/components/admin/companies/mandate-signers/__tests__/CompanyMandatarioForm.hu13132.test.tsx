// HU #13132 (Feature F2 #13114, épica #13090) — formulario de mandatario con modelo, forma de firma y
// vigencia. Un bloque por criterio de aceptación.

import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApiValidationError } from "@/lib/api/types";
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";

// HU #13181 — el formulario consulta las compañías asociables; aquí el Admin de Compañía no tiene red.
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
    email: null,
    userId: null,
    identityStatus: "none",
    signatureVaultId: null,
    registeredAt: "2026-08-01T10:00:00Z",
    isActive: true,
    companyTenantIds: ["t-1"],
    transitOfficeIds: ["ot-1"],
    ...overrides,
  };
}

const onSubmit = vi.fn();
const onCancel = vi.fn();

function renderForm(props: Partial<React.ComponentProps<typeof CompanyMandatarioForm>> = {}) {
  return render(
    <CompanyMandatarioForm
      tenantId="t-1"
      offices={OFICINAS}
      editing={null}
      initialOfficeIds={["ot-1"]}
      onCancel={onCancel}
      onSubmit={onSubmit}
      {...props}
    />,
  );
}

const radio = (name: string) => screen.getByRole("radio", { name });
const guardar = () => screen.getByRole("button", { name: "Guardar" });

beforeEach(() => {
  vi.clearAllMocks();
  onSubmit.mockResolvedValue({ id: "ms-2", integrityHash: "b".repeat(64) });
});

async function llenarNatural(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText("Nombre completo"), "Ana Restrepo");
  await user.type(screen.getByLabelText("Número de documento"), "1020304050");
  // HU #13248 - con validación de identidad el correo es obligatorio: se escribe siempre.
  await user.type(screen.getByLabelText(/^Correo/), "ana@ejemplo.com");
}

describe("HU #13132 AC1 — Persona natural", () => {
  it("por defecto es Persona natural y muestra forma de firma y vigencia", () => {
    renderForm();
    expect(radio("Persona natural")).toBeChecked();
    expect(radio("Baúl de firmas")).not.toBeChecked();
    expect(radio("Validación de identidad")).not.toBeChecked();
    expect(radio("Fija")).toBeChecked();
    expect(radio("Rango de fechas")).toBeInTheDocument();
  });

  it("con Baúl aparece el selector de firma del baúl; con Validación de identidad no", async () => {
    const user = userEvent.setup();
    renderForm();
    expect(screen.queryByText("Firma del baúl")).not.toBeInTheDocument();
    await user.click(radio("Baúl de firmas"));
    expect(screen.getByText("Firma del baúl")).toBeInTheDocument();
    await user.click(radio("Validación de identidad"));
    expect(screen.queryByText("Firma del baúl")).not.toBeInTheDocument();
  });

  it("con Rango aparecen fecha de inicio y de fin; con Fija no", async () => {
    const user = userEvent.setup();
    renderForm();
    expect(screen.queryByLabelText("Fecha de inicio")).not.toBeInTheDocument();
    await user.click(radio("Rango de fechas"));
    expect(screen.getByLabelText("Fecha de inicio")).toBeInTheDocument();
    expect(screen.getByLabelText("Fecha de fin")).toBeInTheDocument();
  });

  it("envía modelo, forma de firma y rango con fechas yyyy-MM-dd", async () => {
    const user = userEvent.setup();
    renderForm();
    await llenarNatural(user);
    await user.click(radio("Validación de identidad"));
    await user.click(radio("Rango de fechas"));
    await user.type(screen.getByLabelText("Fecha de inicio"), "2026-10-01");
    await user.type(screen.getByLabelText("Fecha de fin"), "2026-12-31");
    await user.click(guardar());
    expect(onSubmit).toHaveBeenCalledWith(
      expect.objectContaining({
        signerModel: "natural",
        signatureMethod: "biometria",
        validityKind: "range",
        validFrom: "2026-10-01",
        validTo: "2026-12-31",
        signatureVaultId: null,
      }),
    );
  });

  it("con vigencia fija no envía fechas", async () => {
    const user = userEvent.setup();
    renderForm();
    await llenarNatural(user);
    await user.click(radio("Validación de identidad"));
    await user.click(guardar());
    const enviado = onSubmit.mock.calls[0][0];
    expect(enviado.validityKind).toBe("fixed");
    expect(enviado.validFrom).toBeUndefined();
    expect(enviado.validTo).toBeUndefined();
  });
});

describe("HU #13132 AC2 — Persona jurídica y Formato en blanco", () => {
  it.each(["Persona jurídica", "Formato en blanco"])(
    "%s oculta forma de firma, vigencia, correo e identidad",
    async (modelo) => {
      const user = userEvent.setup();
      renderForm({ editing: signer() });
      expect(screen.getByTestId("mandatario-identidad")).toBeInTheDocument();
      await user.click(radio(modelo));
      expect(screen.queryByRole("radio", { name: "Baúl de firmas" })).not.toBeInTheDocument();
      expect(screen.queryByRole("radio", { name: "Fija" })).not.toBeInTheDocument();
      expect(screen.queryByLabelText("Correo")).not.toBeInTheDocument();
      expect(screen.queryByTestId("mandatario-identidad")).not.toBeInTheDocument();
    },
  );

  it("Persona jurídica pide el nombre y el NIT de la entidad y los envía sin datos de firma", async () => {
    const user = userEvent.setup();
    renderForm();
    await user.click(radio("Persona jurídica"));
    await user.type(screen.getByLabelText("Nombre de la entidad"), "Gestora SAS");
    await user.type(screen.getByLabelText("NIT"), "900123456");
    await user.click(guardar());
    const enviado = onSubmit.mock.calls[0][0];
    expect(enviado).toMatchObject({
      fullName: "Gestora SAS",
      documentType: "NIT",
      documentNumber: "900123456",
      email: null,
      signerModel: "juridica",
      signatureVaultId: null,
    });
    expect(enviado.signatureMethod).toBeUndefined();
    expect(enviado.validityKind).toBeUndefined();
  });

  it("Persona jurídica sin nombre ni NIT no se guarda", async () => {
    const user = userEvent.setup();
    renderForm();
    await user.click(radio("Persona jurídica"));
    await user.click(guardar());
    expect(screen.getByText("Escribe el nombre de la entidad.")).toBeInTheDocument();
    expect(screen.getByText("Escribe el NIT.")).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("Formato en blanco no pide datos: envía el nombre fijo y documento nulo", async () => {
    const user = userEvent.setup();
    renderForm();
    await user.click(radio("Formato en blanco"));
    expect(screen.queryByLabelText("Nombre completo")).not.toBeInTheDocument();
    expect(screen.getByTestId("mandatario-formato-blanco-nota")).toHaveTextContent(/sin firma/i);
    await user.click(guardar());
    expect(onSubmit).toHaveBeenCalledWith(
      expect.objectContaining({
        fullName: "Formato en blanco",
        documentNumber: null,
        email: null,
        signerModel: "formato_blanco",
      }),
    );
  });
});

describe("HU #13132 AC3 — validaciones locales", () => {
  it("sin forma de firma muestra el error junto al campo y no envía", async () => {
    const user = userEvent.setup();
    renderForm();
    await llenarNatural(user);
    await user.click(guardar());
    expect(screen.getByRole("alert")).toHaveTextContent("Elige la forma de firma.");
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("con baúl sin firma elegida no envía", async () => {
    const user = userEvent.setup();
    renderForm();
    await llenarNatural(user);
    await user.click(radio("Baúl de firmas"));
    await user.click(guardar());
    expect(screen.getByText("Elige una firma del baúl.")).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("con rango sin fechas no envía", async () => {
    const user = userEvent.setup();
    renderForm();
    await llenarNatural(user);
    await user.click(radio("Validación de identidad"));
    await user.click(radio("Rango de fechas"));
    await user.click(guardar());
    expect(screen.getByText("Indica la fecha de inicio.")).toBeInTheDocument();
    expect(screen.getByText("Indica la fecha de fin.")).toBeInTheDocument();
    expect(screen.getByLabelText("Fecha de inicio")).toHaveAttribute("aria-invalid", "true");
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("con fin anterior al inicio no envía", async () => {
    const user = userEvent.setup();
    renderForm();
    await llenarNatural(user);
    await user.click(radio("Validación de identidad"));
    await user.click(radio("Rango de fechas"));
    await user.type(screen.getByLabelText("Fecha de inicio"), "2026-12-31");
    await user.type(screen.getByLabelText("Fecha de fin"), "2026-10-01");
    await user.click(guardar());
    expect(screen.getByText("La fecha de fin no puede ser anterior a la de inicio.")).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });
});

describe("HU #13132 AC4 — error 422 del servidor", () => {
  it("muestra el mensaje junto al campo y conserva lo escrito", async () => {
    onSubmit.mockRejectedValue(
      new ApiValidationError(
        [{ field: "validTo", message: "La fecha de fin no puede ser anterior a la fecha de inicio." }],
        422,
      ),
    );
    const user = userEvent.setup();
    renderForm();
    await llenarNatural(user);
    await user.click(radio("Validación de identidad"));
    await user.click(radio("Rango de fechas"));
    await user.type(screen.getByLabelText("Fecha de inicio"), "2026-10-01");
    await user.type(screen.getByLabelText("Fecha de fin"), "2026-12-31");
    await user.click(guardar());

    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("La fecha de fin no puede ser anterior a la fecha de inicio.");
    expect(screen.getByLabelText("Fecha de fin")).toHaveAccessibleDescription(
      "La fecha de fin no puede ser anterior a la fecha de inicio.",
    );
    expect(screen.getByLabelText("Nombre completo")).toHaveValue("Ana Restrepo");
    expect(screen.getByLabelText("Fecha de fin")).toHaveValue("2026-12-31");
    expect(guardar()).toBeEnabled();
  });

  it("un 422 sin campo conocido queda como mensaje general", async () => {
    onSubmit.mockRejectedValue(
      new ApiValidationError([{ field: "otro", message: "Ya existe un mandatario así." }], 422),
    );
    const user = userEvent.setup();
    renderForm();
    await llenarNatural(user);
    await user.click(radio("Validación de identidad"));
    await user.click(guardar());
    expect(await screen.findByRole("alert")).toHaveTextContent("Ya existe un mandatario así.");
  });
});

describe("HU #13132 AC5 — edición y cambio de modelo", () => {
  it("carga el modelo, la forma de firma y la vigencia del mandatario", () => {
    renderForm({
      editing: signer({
        signerModel: "natural",
        signatureMethod: "biometria",
        validityKind: "range",
        validFrom: "2026-10-01",
        validTo: "2026-12-31",
      }),
    });
    expect(radio("Persona natural")).toBeChecked();
    expect(radio("Validación de identidad")).toBeChecked();
    expect(radio("Rango de fechas")).toBeChecked();
    expect(screen.getByLabelText("Fecha de inicio")).toHaveValue("2026-10-01");
    expect(screen.getByLabelText("Fecha de fin")).toHaveValue("2026-12-31");
  });

  it("avisa antes de guardar que cambiar de Persona natural descarta forma de firma y vigencia", async () => {
    const user = userEvent.setup();
    renderForm({ editing: signer({ signerModel: "natural", signatureMethod: "biometria" }) });
    expect(screen.queryByTestId("mandatario-cambio-modelo-aviso")).not.toBeInTheDocument();
    await user.click(radio("Formato en blanco"));
    expect(screen.getByTestId("mandatario-cambio-modelo-aviso")).toHaveTextContent(
      /se descartan la forma de firma y la vigencia/i,
    );
    expect(onSubmit).not.toHaveBeenCalled();
    await user.click(guardar());
    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ signerModel: "formato_blanco" }));
  });

  it("al crear no muestra el aviso de descarte", async () => {
    const user = userEvent.setup();
    renderForm();
    await user.click(radio("Persona jurídica"));
    expect(screen.queryByTestId("mandatario-cambio-modelo-aviso")).not.toBeInTheDocument();
  });
});

describe("HU #13132 AC6 — mandatario anterior sin modelo explícito", () => {
  it("aparece como Persona natural con su firma del baúl y no la pierde al guardar", async () => {
    const user = userEvent.setup();
    renderForm({ editing: signer({ signatureVaultId: "vault-9" }) });
    expect(radio("Persona natural")).toBeChecked();
    expect(radio("Baúl de firmas")).toBeChecked();
    expect(screen.getByLabelText("Nombre completo")).toHaveValue("Ana Restrepo");
    await user.click(guardar());
    expect(onSubmit).toHaveBeenCalledWith(
      expect.objectContaining({
        signerModel: "natural",
        signatureMethod: "baul",
        signatureVaultId: "vault-9",
        validityKind: "fixed",
      }),
    );
  });

  it("uno con identidad vigente y sin baúl arranca con Validación de identidad", () => {
    renderForm({ editing: signer({ identityStatus: "valid" }) });
    expect(radio("Validación de identidad")).toBeChecked();
  });
});

describe("HU #13132 — variante hub del organismo", () => {
  it("ofrece forma de firma y vigencia pero no el selector del baúl", async () => {
    const user = userEvent.setup();
    renderForm({ variant: "hub", restrictToOfficeIds: ["ot-1"] });
    await user.click(radio("Baúl de firmas"));
    expect(screen.queryByText("Firma del baúl")).not.toBeInTheDocument();
    expect(screen.getByTestId("mandatario-hub-firma-nota")).toBeInTheDocument();
    await llenarNatural(user);
    await user.click(guardar());
    const enviado = onSubmit.mock.calls[0][0];
    expect(enviado.signatureMethod).toBe("baul");
    expect(enviado.signatureVaultId).toBeUndefined();
  });
});

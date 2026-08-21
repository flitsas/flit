// HU #11180 — Representantes legales — Firma del baúl e identidad dentro del formulario.
// HU #11755 (ADR-0050) actualizó AC5/AC6: el bloque de identidad pasa a SOLO CONSULTA — ya no ofrece
// Enviar / Reenviar / Renovar / Asociar validación en ningún estado. Se conservan aquí como regresión.
// Tests que cubren los AC de la HU:
//   AC1 — selector de firma: solo firmas vigentes de la persona
//   AC2 — la firma elegida se envía en el payload de guardado
//   AC3 — sin firmas vigentes: aviso con remisión al baúl
//   AC4 — modo create: aviso de identidad automática al guardar (sin botones)
//   AC5/AC6 (HU #11755) — solo consulta: nunca se renderizan los 3 controles de escritura

import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  LegalRepresentativesFormPanel,
  type LegalRepresentativesFormPanelProps,
} from "../LegalRepresentativesFormPanel";
import type {
  AssignableProcedureType,
  LegalRepresentativeItem,
} from "@/lib/api/admin-legal-representatives";
import type { SignatureVaultItem } from "@/lib/api/admin-signature-vault";

// ── Mocks ────────────────────────────────────────────────────────────────────

vi.mock("@/lib/api/admin-legal-representatives", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-legal-representatives")>();
  return {
    ...actual,
    fetchLegalRepresentative: vi.fn(),
    sendLegalRepresentativeIdentity: vi.fn(),
  };
});

vi.mock("@/lib/api/admin-signature-vault", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-signature-vault")>();
  return {
    ...actual,
    fetchSignatureVaultByDocument: vi.fn(),
  };
});

vi.mock("@/lib/api/admin-deeds", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-deeds")>();
  return {
    ...actual,
    saveDeed: vi.fn(),
    fetchDeedDetail: vi.fn(),
  };
});

import {
  fetchLegalRepresentative,
} from "@/lib/api/admin-legal-representatives";
import { fetchSignatureVaultByDocument } from "@/lib/api/admin-signature-vault";

// ── Fixtures ─────────────────────────────────────────────────────────────────

const TENANT = "22222222-2222-2222-2222-222222222222";

const PROC_TYPES: AssignableProcedureType[] = [
  { id: "019f8195-fed1-770a-98ae-295ed59b53d4", code: "TRASPASO_STANDARD", name: "Traspaso" },
];

const SIG_VIGENTE: SignatureVaultItem = {
  id: "sig-1",
  documentType: "CC",
  documentNumber: "1098765432",
  fullName: "Ana Gómez",
  vigenciaDesde: "2025-01-01",
  vigenciaHasta: "2027-12-31",
  estado: "activa",
};

const SIG_VIGENTE_2: SignatureVaultItem = {
  id: "sig-2",
  documentType: "CC",
  documentNumber: "1098765432",
  fullName: "Ana Gómez (2da firma)",
  vigenciaDesde: "2026-01-01",
  vigenciaHasta: "2028-06-30",
  estado: "activa",
};

/** Representante sin identidad ni firma asociada. */
const ITEM_NONE: LegalRepresentativeItem = {
  id: "rep-2",
  representedCompanyId: "co-1",
  companyDocumentNumber: "900123456-7",
  companyName: "Comercializadora XYZ",
  documentType: "CC",
  documentNumber: "1098765432",
  firstLastName: "Gómez",
  secondLastName: null,
  name: "Ana",
  email: "ana@xyz.co",
  address: null,
  city: null,
  phone: null,
  signatureVaultId: null,
  identityValidationRef: null,
  hasSignatureOrIdentity: false,
  identityStatus: "none",
  identityValidUntil: null,
  firmaBaulVigente: false,
  firmaBaulVigenteHasta: null,
  procedureTypeIds: [],
  companies: [
    {
      id: "co-1",
      nit: "900123456-7",
      name: "Comercializadora XYZ",
      isPrimary: true,
      deeds: [],
    },
  ],
  isActive: true,
  createdAt: "2026-01-01T00:00:00Z",
};

/** Representante con identidad validada y vigente. */
const ITEM_VALID_IDENTITY: LegalRepresentativeItem = {
  ...ITEM_NONE,
  identityStatus: "valid",
  identityValidUntil: "2027-06-30",
  firmaBaulVigente: true,
  firmaBaulVigenteHasta: "2027-12-31",
  signatureVaultId: "sig-1",
};

/** Representante con identidad en proceso (pending). */
const ITEM_PENDING: LegalRepresentativeItem = {
  ...ITEM_NONE,
  identityStatus: "pending",
  identityValidUntil: null,
};

type SubmitFn = LegalRepresentativesFormPanelProps["onSubmit"];

function renderPanel(
  mode: "view" | "create" | "edit" | "companies",
  opts?: {
    representativeId?: string | null;
    onSubmit?: SubmitFn;
  },
) {
  const submitMock = vi.fn().mockResolvedValue({ id: "rep-2", signals: [] }) as unknown as SubmitFn;

  render(
    <LegalRepresentativesFormPanel
      open
      mode={mode}
      representativeId={opts?.representativeId ?? (mode !== "create" ? "rep-2" : null)}
      tenantId={TENANT}
      procedureTypes={PROC_TYPES}
      onClose={vi.fn()}
      onSubmit={opts?.onSubmit ?? submitMock}
      onSaved={vi.fn()}
      onError={vi.fn()}
      onSwitchToEdit={vi.fn()}
      onSwitchToCompanies={vi.fn()}
    />,
  );

  return {
    onSubmit: (opts?.onSubmit ?? submitMock) as unknown as ReturnType<typeof vi.fn>,
  };
}

// ── AC1 — Selector de firma: solo firmas vigentes de la persona ───────────────

describe("HU #11180 — AC1: selector de firma lista solo las firmas vigentes de la persona", () => {
  // Uso de ejemplo: fetchSignatureVaultByDocument(tenantId, "CC", "1098765432", true) →
  // devuelve [SIG_VIGENTE, SIG_VIGENTE_2] y el selector las muestra.

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchLegalRepresentative).mockResolvedValue(ITEM_NONE);
    vi.mocked(fetchSignatureVaultByDocument).mockResolvedValue([SIG_VIGENTE, SIG_VIGENTE_2]);
  });

  it("AC1 — happy path: muestra las firmas vigentes en el selector cuando el documento está completo", async () => {
    renderPanel("edit");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());

    // El fetch de firmas se dispara con el documento del representante.
    await waitFor(() =>
      expect(fetchSignatureVaultByDocument).toHaveBeenCalledWith(
        TENANT,
        "CC",
        "1098765432",
        true,
        expect.anything(),
      ),
    );

    // Ambas firmas aparecen en el selector.
    expect(await screen.findByText(/Ana Gómez — vigente hasta/i)).toBeInTheDocument();
    expect(screen.getByText(/Ana Gómez \(2da firma\) — vigente hasta/i)).toBeInTheDocument();
  });

  it("AC1 — edge case: el selector tiene el aria-label «Firma del baúl» accesible", async () => {
    renderPanel("edit");
    await waitFor(() => expect(fetchSignatureVaultByDocument).toHaveBeenCalled());

    const select = await screen.findByRole("combobox", { name: /firma del baúl/i });
    expect(select).toBeInTheDocument();
  });

  it("AC1 — contrato: fetchSignatureVaultByDocument se llama con soloVigentes=true", async () => {
    renderPanel("edit");
    await waitFor(() => expect(fetchSignatureVaultByDocument).toHaveBeenCalled());

    const [, , , soloVigentes] = vi.mocked(fetchSignatureVaultByDocument).mock.calls[0];
    expect(soloVigentes).toBe(true);
  });

  it("AC1 — edge case: sin documento diligenciado muestra mensaje orientativo (estado vacío)", async () => {
    // En create no hay documento precargado.
    vi.mocked(fetchSignatureVaultByDocument).mockResolvedValue([]);
    renderPanel("create", { representativeId: null });

    // El mensaje de "ingresa el documento" aparece inmediatamente.
    expect(
      screen.getByText(/ingresa el tipo y número de documento/i),
    ).toBeInTheDocument();
  });
});

// ── AC2 — La firma elegida se guarda con el representante ────────────────────

describe("HU #11180 — AC2: la firma elegida se envía en el payload de guardado", () => {
  // Uso de ejemplo: usuario elige "sig-1" en el selector → onSubmit recibe { signatureVaultId: "sig-1" }

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchLegalRepresentative).mockResolvedValue(ITEM_NONE);
    vi.mocked(fetchSignatureVaultByDocument).mockResolvedValue([SIG_VIGENTE]);
  });

  it("AC2 — happy path: la firma seleccionada se incluye en el payload al guardar", async () => {
    const { onSubmit } = renderPanel("edit");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    await waitFor(() => expect(fetchSignatureVaultByDocument).toHaveBeenCalled());

    // Seleccionar la firma.
    const select = await screen.findByRole("combobox", { name: /firma del baúl/i });
    await userEvent.selectOptions(select, "sig-1");

    await userEvent.click(screen.getByRole("button", { name: /guardar cambios/i }));
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));

    expect(onSubmit.mock.calls[0][0]).toMatchObject({ signatureVaultId: "sig-1" });
  });

  it("AC2 — edge case: sin firma seleccionada el payload envía signatureVaultId=null", async () => {
    const { onSubmit } = renderPanel("edit");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());

    await userEvent.click(await screen.findByRole("button", { name: /guardar cambios/i }));
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));

    expect(onSubmit.mock.calls[0][0].signatureVaultId).toBeNull();
  });

  it("AC2 — contrato: la firma precargada desde el detalle aparece seleccionada al abrir en edit", async () => {
    vi.mocked(fetchLegalRepresentative).mockResolvedValue(ITEM_VALID_IDENTITY);
    vi.mocked(fetchSignatureVaultByDocument).mockResolvedValue([SIG_VIGENTE]);

    renderPanel("edit");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    await waitFor(() => expect(fetchSignatureVaultByDocument).toHaveBeenCalled());

    const select = (await screen.findByRole("combobox", { name: /firma del baúl/i })) as HTMLSelectElement;
    expect(select.value).toBe("sig-1");
  });
});

// ── AC3 — Sin firmas vigentes: aviso ─────────────────────────────────────────

describe("HU #11180 — AC3: sin firmas vigentes se muestra aviso que remite al baúl", () => {
  // Uso de ejemplo: fetchSignatureVaultByDocument → [] → se muestra el aviso AC3.

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchLegalRepresentative).mockResolvedValue(ITEM_NONE);
    vi.mocked(fetchSignatureVaultByDocument).mockResolvedValue([]);
  });

  it("AC3 — happy path: muestra el aviso cuando no hay firmas vigentes", async () => {
    renderPanel("edit");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    await waitFor(() => expect(fetchSignatureVaultByDocument).toHaveBeenCalled());

    expect(
      await screen.findByText(/esta persona no tiene firmas vigentes en el baúl/i),
    ).toBeInTheDocument();
  });

  // HU #11193 — el aviso YA NO remite al baúl: la salida pasó a ser capturar la firma aquí mismo.
  // El AC3 de esta HU (avisar cuando no hay firmas vigentes) sigue cubierto por el test anterior;
  // lo que cambia es qué se le ofrece al usuario a continuación.
  it("AC3 — contrato: el aviso ofrece capturar la firma sin salir del formulario", async () => {
    renderPanel("edit");
    await waitFor(() => expect(fetchSignatureVaultByDocument).toHaveBeenCalled());

    const aviso = await screen.findByTestId("sig-selector-empty");
    expect(aviso).toHaveTextContent(/no tiene firmas vigentes/i);
    expect(await screen.findByTestId("sig-capture-open")).toBeInTheDocument();
  });

  it("AC3 — edge case: cuando el fetch falla se muestra error (no el aviso de AC3)", async () => {
    vi.mocked(fetchSignatureVaultByDocument).mockRejectedValue(new Error("red"));
    renderPanel("edit");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());

    // El error de red no es lo mismo que "sin firmas" — se muestra el mensaje de error.
    expect(
      await screen.findByText(/no se pudo consultar el baúl de firmas/i),
    ).toBeInTheDocument();
    expect(screen.queryByText(/esta persona no tiene firmas vigentes/i)).not.toBeInTheDocument();
  });
});

// ── AC4 — Identidad ya aprobada al crear ─────────────────────────────────────

describe("HU #11180 — AC4: en modo create se informa de la identidad automática", () => {
  // Uso de ejemplo: renderPanel("create") → se muestra la nota de identidad automática.

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchSignatureVaultByDocument).mockResolvedValue([]);
  });

  it("AC4 — happy path: el modo create muestra nota de que la identidad se asociará automáticamente", () => {
    renderPanel("create", { representativeId: null });

    expect(
      screen.getByText(/la identidad se asociará automáticamente/i),
    ).toBeInTheDocument();
  });

  it("AC4 — edge case: el bloque de identidad en create no ofrece botones de acción", () => {
    renderPanel("create", { representativeId: null });

    expect(screen.queryByTestId("rl-identity-send")).not.toBeInTheDocument();
    expect(screen.queryByTestId("rl-identity-resend")).not.toBeInTheDocument();
    expect(screen.queryByTestId("rl-identity-link")).not.toBeInTheDocument();
  });

  it("AC4 — contrato: fetchLegalRepresentative no se llama en modo create (sin representativeId)", () => {
    renderPanel("create", { representativeId: null });
    expect(fetchLegalRepresentative).not.toHaveBeenCalled();
  });
});

// ── AC5/AC6 (HU #11755, ADR-0050) — solo consulta: sin controles de escritura ─

describe("HU #11755 — la ficha del RL retira los controles de escritura de identidad", () => {
  // Uso de ejemplo: cualquier estado de identidad (none/pending/valid/expired), en view o edit,
  // renderiza el badge y la vigencia pero NUNCA los botones Enviar/Reenviar/Asociar.

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchSignatureVaultByDocument).mockResolvedValue([]);
  });

  it("happy path: en modo view con identidad «none» no aparece ningún control de escritura", async () => {
    vi.mocked(fetchLegalRepresentative).mockResolvedValue(ITEM_NONE);
    renderPanel("view");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    await screen.findByTestId("rl-identity-block");

    expect(screen.queryByTestId("rl-identity-send")).not.toBeInTheDocument();
    expect(screen.queryByTestId("rl-identity-resend")).not.toBeInTheDocument();
    expect(screen.queryByTestId("rl-identity-link")).not.toBeInTheDocument();
  });

  it("edge case: en modo view con identidad «pending» tampoco aparece ningún control", async () => {
    vi.mocked(fetchLegalRepresentative).mockResolvedValue(ITEM_PENDING);
    renderPanel("view");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    await screen.findByTestId("rl-identity-block");

    expect(screen.queryByTestId("rl-identity-send")).not.toBeInTheDocument();
    expect(screen.queryByTestId("rl-identity-resend")).not.toBeInTheDocument();
    expect(screen.queryByTestId("rl-identity-link")).not.toBeInTheDocument();
  });

  it("edge case: en modo view con identidad «valid» tampoco aparece ningún control", async () => {
    vi.mocked(fetchLegalRepresentative).mockResolvedValue(ITEM_VALID_IDENTITY);
    renderPanel("view");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    await screen.findByTestId("rl-identity-block");

    expect(screen.queryByTestId("rl-identity-send")).not.toBeInTheDocument();
    expect(screen.queryByTestId("rl-identity-resend")).not.toBeInTheDocument();
    expect(screen.queryByTestId("rl-identity-link")).not.toBeInTheDocument();
  });

  it("contrato: en modo edit (representante persistido) no aparece ningún control de escritura", async () => {
    vi.mocked(fetchLegalRepresentative).mockResolvedValue(ITEM_NONE);
    renderPanel("edit");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    await screen.findByTestId("rl-identity-block");

    expect(screen.queryByTestId("rl-identity-send")).not.toBeInTheDocument();
    expect(screen.queryByTestId("rl-identity-resend")).not.toBeInTheDocument();
    expect(screen.queryByTestId("rl-identity-link")).not.toBeInTheDocument();
  });

  it("contrato: en modo create no aparece ningún control de escritura", () => {
    renderPanel("create", { representativeId: null });

    expect(screen.queryByTestId("rl-identity-send")).not.toBeInTheDocument();
    expect(screen.queryByTestId("rl-identity-resend")).not.toBeInTheDocument();
    expect(screen.queryByTestId("rl-identity-link")).not.toBeInTheDocument();
  });

  it("contrato: el panel en modo view sigue mostrando los dos rótulos (solo consulta, HU #11756)", async () => {
    vi.mocked(fetchLegalRepresentative).mockResolvedValue(ITEM_VALID_IDENTITY);
    renderPanel("view");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());

    expect(await screen.findByTestId("rl-identity-status-badge")).toHaveTextContent(
      /identidad: aprobada y vigente/i,
    );
    expect(screen.getByTestId("rl-identity-firma-baul-badge")).toHaveTextContent(
      /firma del baúl: vigente hasta/i,
    );
  });
});

// ── HU #11756 (ADR-0050) — copy por estado + matriz de rótulos ───────────────

describe("HU #11756 — la ficha del RL muestra los dos rótulos y el copy por estado", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchSignatureVaultByDocument).mockResolvedValue([]);
  });

  it("happy path: sin validación y sin firma vigente muestra el enlace al módulo Identidad", async () => {
    vi.mocked(fetchLegalRepresentative).mockResolvedValue(ITEM_NONE);
    renderPanel("view");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());

    expect(await screen.findByTestId("rl-identity-status-badge")).toHaveTextContent(
      /identidad: sin validación/i,
    );
    expect(screen.getByTestId("rl-identity-firma-baul-badge")).toHaveTextContent(
      /firma del baúl: sin firma vigente/i,
    );
    expect(screen.getByTestId("rl-identity-module-link")).toBeInTheDocument();
  });

  it("edge case: con firma de baúl vigente NO se invita a prevalidar (D8 ADR-0025 manda)", async () => {
    vi.mocked(fetchLegalRepresentative).mockResolvedValue(ITEM_VALID_IDENTITY);
    renderPanel("view");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());

    await screen.findByTestId("rl-identity-firma-baul-badge");
    expect(screen.queryByTestId("rl-identity-copy")).not.toBeInTheDocument();
    expect(screen.queryByTestId("rl-identity-module-link")).not.toBeInTheDocument();
  });

  it("contrato: identidad vencida sin firma vigente invita a renovar (copy distinto de «sin validación»)", async () => {
    vi.mocked(fetchLegalRepresentative).mockResolvedValue({
      ...ITEM_NONE,
      identityStatus: "expired",
    });
    renderPanel("view");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());

    expect(await screen.findByTestId("rl-identity-status-badge")).toHaveTextContent(
      /identidad: vencida/i,
    );
    expect(screen.getByTestId("rl-identity-copy")).toHaveTextContent(/venció/i);
    expect(screen.getByTestId("rl-identity-module-link")).toBeInTheDocument();
  });

  it("contrato: persona jurídica (NIT) sin firma vigente ve «no aplica», no el enlace", async () => {
    vi.mocked(fetchLegalRepresentative).mockResolvedValue({
      ...ITEM_NONE,
      documentType: "NIT",
    });
    renderPanel("view");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());

    expect(await screen.findByTestId("rl-identity-copy")).toHaveTextContent(
      /no aplica prevalidación/i,
    );
    expect(screen.queryByTestId("rl-identity-module-link")).not.toBeInTheDocument();
  });
});

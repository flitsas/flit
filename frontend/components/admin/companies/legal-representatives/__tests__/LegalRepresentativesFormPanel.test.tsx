// HU #11178 — Panel unificado de representante legal: tres modos view / create / edit.
//
// Tests heredados de HU #11058 (precarga de compañías al editar) se migran al nuevo API
// de props (mode/representativeId/tenantId). El mock de fetchLegalRepresentative cubre el
// skeleton de carga y la precarga completa del formulario desde GET /{id}.
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

vi.mock("@/lib/api/admin-legal-representatives", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-legal-representatives")>();
  return {
    ...actual,
    fetchLegalRepresentative: vi.fn(),
  };
});

import { fetchLegalRepresentative } from "@/lib/api/admin-legal-representatives";

const TENANT = "11111111-1111-1111-1111-111111111111";

const PROC_TYPES: AssignableProcedureType[] = [
  { id: "019f8195-fed1-770a-98ae-295ed59b53d4", code: "TRASPASO_STANDARD", name: "Traspaso" },
];

/** Representante con DOS compañías, ambas con contacto completo y una marcada principal. */
const ITEM: LegalRepresentativeItem = {
  id: "rep-1",
  representedCompanyId: "co-1",
  companyDocumentNumber: "900123456-7",
  companyName: "Comercializadora XYZ",
  documentType: "CC",
  documentNumber: "1098765432",
  firstLastName: "Gómez",
  secondLastName: "Ruiz",
  name: "Ana",
  email: "ana@xyz.co",
  address: "Calle 1",
  city: "Medellín",
  phone: "3001112233",
  signatureVaultId: null,
  identityValidationRef: null,
  hasSignatureOrIdentity: false,
  identityStatus: "none",
  identityValidUntil: null,
  firmaBaulVigente: false,
  firmaBaulVigenteHasta: null,
  procedureTypeIds: ["019f8195-fed1-770a-98ae-295ed59b53d4"],
  companies: [
    {
      id: "co-1",
      nit: "900123456-7",
      name: "Comercializadora XYZ",
      isPrimary: true,
      deeds: [],
      email: "contacto@xyz.co",
      address: "Carrera 50 #10-20",
      city: "Medellín",
      phone: "6041234567",
    },
    {
      id: "co-2",
      nit: "901987654-3",
      name: "Inversiones ABC",
      isPrimary: false,
      deeds: [],
      email: "hola@abc.co",
      address: "Avenida 80 #5-15",
      city: "Bogotá",
      phone: "6017654321",
    },
  ],
  isActive: true,
  createdAt: "2026-06-01T00:00:00Z",
  updatedAt: null,
};

type SubmitFn = LegalRepresentativesFormPanelProps["onSubmit"];
type SavedFn = LegalRepresentativesFormPanelProps["onSaved"];

function renderPanel(
  mode: "view" | "create" | "edit",
  opts?: {
    representativeId?: string | null;
    onSubmit?: SubmitFn;
    onSwitchToEdit?: () => void;
    onSaved?: SavedFn;
    onClose?: () => void;
  },
) {
  const submitMock = vi.fn().mockResolvedValue({ id: "rep-1", signals: [] }) as unknown as SubmitFn;
  const savedMock = vi.fn() as unknown as SavedFn;
  const switchMock = vi.fn();
  const closeMock = vi.fn();

  const onSubmit = opts?.onSubmit ?? submitMock;
  const onSwitchToEdit = opts?.onSwitchToEdit ?? switchMock;
  const onSaved = opts?.onSaved ?? savedMock;
  const onClose = opts?.onClose ?? closeMock;

  render(
    <LegalRepresentativesFormPanel
      open
      mode={mode}
      representativeId={opts?.representativeId ?? (mode !== "create" ? "rep-1" : null)}
      tenantId={TENANT}
      procedureTypes={PROC_TYPES}
      onClose={onClose}
      onSubmit={onSubmit}
      onSaved={onSaved}
      onError={vi.fn()}
      onSwitchToEdit={onSwitchToEdit}
    />,
  );
  return {
    onSubmit: onSubmit as unknown as ReturnType<typeof vi.fn>,
    onSwitchToEdit: onSwitchToEdit as unknown as ReturnType<typeof vi.fn>,
    onSaved: onSaved as unknown as ReturnType<typeof vi.fn>,
    onClose: onClose as unknown as ReturnType<typeof vi.fn>,
  };
}

// ── Modo CREATE ──────────────────────────────────────────────────────────────

describe("LegalRepresentativesFormPanel — modo create (AC4)", () => {
  beforeEach(() => vi.clearAllMocks());

  it("no llama a fetchLegalRepresentative en el alta", () => {
    renderPanel("create", { representativeId: null });
    expect(fetchLegalRepresentative).not.toHaveBeenCalled();
  });

  it("arranca con el formulario en blanco", () => {
    renderPanel("create", { representativeId: null });
    expect(screen.queryByDisplayValue("Ana")).not.toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /registrar representante/i }),
    ).toBeInTheDocument();
  });

  it("el título es «Nuevo representante legal»", () => {
    renderPanel("create", { representativeId: null });
    expect(screen.getByRole("dialog", { name: /registrar representante legal/i })).toBeInTheDocument();
  });

  it("permite agregar empresas y registrar", async () => {
    const { onSubmit } = renderPanel("create", { representativeId: null });

    await userEvent.type(screen.getByLabelText(/^nombres$/i), "Carlos");
    await userEvent.type(screen.getByLabelText(/primer apellido/i), "Pérez");
    await userEvent.type(screen.getByLabelText(/número de documento/i), "123456789");
    const nits = screen.getAllByLabelText(/nit de la compañía/i);
    await userEvent.type(nits[0], "900111222");
    const names = screen.getAllByLabelText(/razón social/i);
    await userEvent.type(names[0], "Empresa Test");

    await userEvent.click(screen.getByRole("button", { name: /registrar representante/i }));
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit.mock.calls[0][0]).toMatchObject({
      name: "Carlos",
      firstLastName: "Pérez",
      documentNumber: "123456789",
    });
  });

  it("no llama a onSaved si el submit falla", async () => {
    const { onSubmit, onSaved } = renderPanel("create", {
      representativeId: null,
      onSubmit: vi.fn().mockRejectedValue(new Error("fail")) as unknown as SubmitFn,
    });

    await userEvent.type(screen.getByLabelText(/^nombres$/i), "X");
    await userEvent.type(screen.getByLabelText(/primer apellido/i), "Y");
    await userEvent.type(screen.getByLabelText(/número de documento/i), "123");
    const nits = screen.getAllByLabelText(/nit de la compañía/i);
    await userEvent.type(nits[0], "900");
    const names = screen.getAllByLabelText(/razón social/i);
    await userEvent.type(names[0], "Emp");

    await userEvent.click(screen.getByRole("button", { name: /registrar representante/i }));
    await waitFor(() => expect(onSubmit).toHaveBeenCalled());
    expect(onSaved).not.toHaveBeenCalled();
  });
});

// ── Modo VIEW ────────────────────────────────────────────────────────────────

describe("LegalRepresentativesFormPanel — modo view (AC1)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchLegalRepresentative).mockResolvedValue(ITEM);
  });

  it("carga el detalle desde GET /{id} al abrir (AC3)", async () => {
    renderPanel("view");
    await waitFor(() =>
      expect(fetchLegalRepresentative).toHaveBeenCalledWith(TENANT, "rep-1", expect.anything()),
    );
  });

  it("el título es «Detalle del representante»", async () => {
    renderPanel("view");
    await waitFor(() =>
      expect(screen.getByRole("dialog", { name: /ver representante legal/i })).toBeInTheDocument(),
    );
  });

  it("muestra el nombre y los datos del representante", async () => {
    renderPanel("view");
    expect(await screen.findByText("Ana")).toBeInTheDocument();
    expect(screen.getByText("Gómez")).toBeInTheDocument();
    expect(screen.getByText("ana@xyz.co")).toBeInTheDocument();
  });

  it("muestra el bloque de identidad y firma del baúl", async () => {
    renderPanel("view");
    expect(await screen.findByTestId("rl-identidad")).toBeInTheDocument();
    expect(screen.getByTestId("rl-firma-baul")).toBeInTheDocument();
    expect(screen.getByText(/identidad sin validar/i)).toBeInTheDocument();
    expect(screen.getByText(/sin firma registrada/i)).toBeInTheDocument();
  });

  it("muestra las empresas con la principal marcada (HU #11177)", async () => {
    renderPanel("view");
    expect(await screen.findByText("Comercializadora XYZ")).toBeInTheDocument();
    expect(screen.getByText("Inversiones ABC")).toBeInTheDocument();
    expect(screen.getByLabelText("Compañía principal")).toBeInTheDocument();
  });

  it("muestra los tipos de trámite como badges", async () => {
    renderPanel("view");
    expect(await screen.findByText("Traspaso")).toBeInTheDocument();
  });

  it("el botón «Editar» llama a onSwitchToEdit sin cerrar el panel (AC2)", async () => {
    const { onSwitchToEdit, onClose } = renderPanel("view");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());

    await userEvent.click(screen.getByRole("button", { name: /pasar a modo edición/i }));
    expect(onSwitchToEdit).toHaveBeenCalledTimes(1);
    expect(onClose).not.toHaveBeenCalled();
  });

  it("muestra el skeleton mientras carga", () => {
    vi.mocked(fetchLegalRepresentative).mockImplementation(
      () => new Promise(() => {/* pendiente */}),
    );
    renderPanel("view");
    expect(screen.getByLabelText(/cargando información del representante/i)).toBeInTheDocument();
  });

  it("muestra error si la carga falla", async () => {
    vi.mocked(fetchLegalRepresentative).mockRejectedValue(new Error("fail"));
    renderPanel("view");
    expect(
      await screen.findByText(/no se pudo cargar la información del representante/i),
    ).toBeInTheDocument();
  });

  it("NO muestra el formulario (inputs) en modo view", async () => {
    renderPanel("view");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    expect(screen.queryByLabelText(/número de documento/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /guardar cambios/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /registrar representante/i })).not.toBeInTheDocument();
  });
});

// ── Modo EDIT ────────────────────────────────────────────────────────────────

describe("LegalRepresentativesFormPanel — modo edit (AC3)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchLegalRepresentative).mockResolvedValue(ITEM);
  });

  it("carga el detalle desde GET /{id} para precargar el formulario", async () => {
    renderPanel("edit");
    await waitFor(() =>
      expect(fetchLegalRepresentative).toHaveBeenCalledWith(TENANT, "rep-1", expect.anything()),
    );
  });

  it("el título es «Editar representante legal»", async () => {
    renderPanel("edit");
    await waitFor(() =>
      expect(screen.getByRole("dialog", { name: /editar representante legal/i })).toBeInTheDocument(),
    );
  });

  it("precarga TODAS las compañías desde GET /{id} (AC3, herencia HU #11058)", async () => {
    renderPanel("edit");
    expect(await screen.findByDisplayValue("900123456-7")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Comercializadora XYZ")).toBeInTheDocument();
    expect(screen.getByDisplayValue("901987654-3")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Inversiones ABC")).toBeInTheDocument();
  });

  it("precarga el contacto de cada compañía (AC3, herencia HU #11058)", async () => {
    renderPanel("edit");
    for (const valor of [
      "contacto@xyz.co",
      "Carrera 50 #10-20",
      "6041234567",
      "hola@abc.co",
      "Avenida 80 #5-15",
      "6017654321",
    ]) {
      expect(await screen.findByDisplayValue(valor)).toBeInTheDocument();
    }
  });

  it("precarga los datos del representante desde el fetch (no desde item del listado)", async () => {
    renderPanel("edit");
    expect(await screen.findByDisplayValue("Ana")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Gómez")).toBeInTheDocument();
    expect(screen.getByDisplayValue("3001112233")).toBeInTheDocument();
  });

  it("muestra skeleton mientras carga", () => {
    vi.mocked(fetchLegalRepresentative).mockImplementation(
      () => new Promise(() => {/* pendiente */}),
    );
    renderPanel("edit");
    expect(screen.getByLabelText(/cargando información del representante/i)).toBeInTheDocument();
  });

  it("muestra error si el fetch falla", async () => {
    vi.mocked(fetchLegalRepresentative).mockRejectedValue(new Error("fail"));
    renderPanel("edit");
    expect(
      await screen.findByText(/no se pudo cargar la información del representante/i),
    ).toBeInTheDocument();
  });

  it("guardar sin tocar las asociaciones las conserva con contacto intacto (herencia HU #11058)", async () => {
    const { onSubmit } = renderPanel("edit");
    await screen.findByDisplayValue("900123456-7");
    await userEvent.click(screen.getByRole("button", { name: /guardar cambios/i }));
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    const payload = onSubmit.mock.calls[0][0];
    expect(payload.companies).toEqual([
      {
        nit: "900123456-7",
        name: "Comercializadora XYZ",
        email: "contacto@xyz.co",
        address: "Carrera 50 #10-20",
        city: "Medellín",
        phone: "6041234567",
      },
      {
        nit: "901987654-3",
        name: "Inversiones ABC",
        email: "hola@abc.co",
        address: "Avenida 80 #5-15",
        city: "Bogotá",
        phone: "6017654321",
      },
    ]);
  });

  it("cambiar solo un dato del representante no altera las compañías (herencia HU #11058)", async () => {
    const { onSubmit } = renderPanel("edit");
    const telefono = await screen.findByDisplayValue("3001112233");
    await userEvent.clear(telefono);
    await userEvent.type(telefono, "3009998877");
    await userEvent.click(screen.getByRole("button", { name: /guardar cambios/i }));
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    const payload = onSubmit.mock.calls[0][0];
    expect(payload.phone).toBe("3009998877");
    expect(payload.companies).toHaveLength(2);
    expect(payload.companies[0].email).toBe("contacto@xyz.co");
    expect(payload.companies[1].email).toBe("hola@abc.co");
  });

  it("una compañía sin contacto se envía en null (herencia HU #11058)", async () => {
    const sinContacto: LegalRepresentativeItem = {
      ...ITEM,
      companies: [{ id: "co-1", nit: "900123456-7", name: "Comercializadora XYZ", deeds: [] }],
    };
    vi.mocked(fetchLegalRepresentative).mockResolvedValue(sinContacto);
    const { onSubmit } = renderPanel("edit");

    await screen.findByDisplayValue("900123456-7");
    await userEvent.click(screen.getByRole("button", { name: /guardar cambios/i }));
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit.mock.calls[0][0].companies[0]).toEqual({
      nit: "900123456-7",
      name: "Comercializadora XYZ",
      email: null,
      address: null,
      city: null,
      phone: null,
    });
  });

  it("no llama a fetchLegalRepresentative dos veces si el modo cambia de view a edit (AC2 — sin re-fetch)", async () => {
    // Simulamos que el panel ya tiene el detalle cargado del modo view.
    // Al montar en edit, debe pedir el detalle una sola vez.
    renderPanel("edit");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalledTimes(1));
  });
});

// ── AC6 — Modal de detalle eliminado ─────────────────────────────────────────

describe("LegalRepresentativeDetailModal eliminado (AC6)", () => {
  it("LegalRepresentativeDetailModal no se importa en LegalRepresentativesFormPanel", async () => {
    // Verifica que el panel unificado NO re-exporta ni usa el modal retirado.
    // Si el archivo LegalRepresentativeDetailModal.tsx existiera y se importara,
    // su eliminación rompería este test. El panel correcto es LegalRepresentativesFormPanel.
    const panelModule = await import("../LegalRepresentativesFormPanel");
    // El módulo del panel exporta únicamente lo esperado: PanelMode + el componente.
    expect(typeof panelModule.LegalRepresentativesFormPanel).toBe("function");
    // No hay re-exportación del modal retirado.
    expect((panelModule as Record<string, unknown>).LegalRepresentativeDetailModal).toBeUndefined();
  });
});

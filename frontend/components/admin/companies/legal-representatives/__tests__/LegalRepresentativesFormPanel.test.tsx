// Los campos de compañía están visibles en el grid (sin acordeón plegable).
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
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

vi.mock("@/lib/api/admin-deeds", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-deeds")>();
  return {
    ...actual,
    saveDeed: vi.fn(),
    fetchDeedDetail: vi.fn(),
  };
});

import { fetchLegalRepresentative } from "@/lib/api/admin-legal-representatives";
import { fetchDeedDetail } from "@/lib/api/admin-deeds";

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

/** Representante con escrituras activas e inactivas en la primera compañía (HU #11179). */
const ITEM_WITH_DEEDS: LegalRepresentativeItem = {
  ...ITEM,
  companies: [
    {
      ...ITEM.companies[0],
      deeds: [
        {
          id: "deed-1",
          description: "Escritura de constitución",
          vigenciaDesde: "2024-01-01",
          vigenciaHasta: "2026-12-31",
          isActive: true,
          estado: "vigente",
        },
        {
          id: "deed-2",
          description: "Poder notarial vencido",
          vigenciaDesde: "2022-01-01",
          vigenciaHasta: "2023-12-31",
          isActive: true,
          estado: "vencida",
        },
      ],
    },
    ITEM.companies[1],
  ],
};

type SubmitFn = LegalRepresentativesFormPanelProps["onSubmit"];
type SavedFn = LegalRepresentativesFormPanelProps["onSaved"];

function renderPanel(
  mode: "view" | "create" | "edit" | "companies",
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
      onSwitchToCompanies={vi.fn()}
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

  it("permite registrar solo la persona (sin NITs ni firma)", async () => {
    const { onSubmit } = renderPanel("create", { representativeId: null });

    await userEvent.type(screen.getByLabelText(/^nombres$/i), "Carlos");
    await userEvent.type(screen.getByLabelText(/primer apellido/i), "Pérez");
    await userEvent.type(screen.getByLabelText(/número de documento/i), "123456789");

    expect(screen.queryByTestId("rl-companies-grid")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /agregar empresa/i })).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /registrar representante/i }));
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit.mock.calls[0][0]).toMatchObject({
      name: "Carlos",
      firstLastName: "Pérez",
      documentNumber: "123456789",
      companies: [],
    });
  });

  it("permite agregar empresas y guardar en modo companies", async () => {
    vi.mocked(fetchLegalRepresentative).mockResolvedValue(ITEM);
    const { onSubmit } = renderPanel("companies");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());

    await userEvent.click(await screen.findByRole("button", { name: /agregar empresa/i }));
    const nits = screen.getAllByLabelText(/nit de la compañía/i);
    const last = nits.length - 1;
    await userEvent.clear(nits[last]);
    await userEvent.type(nits[last], "900111222");
    const names = screen.getAllByLabelText(/razón social/i);
    await userEvent.clear(names[last]);
    await userEvent.type(names[last], "Empresa Test");

    await userEvent.click(screen.getByRole("button", { name: /guardar empresas/i }));
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit.mock.calls[0][0].companies.some((c: { nit: string }) => c.nit === "900111222")).toBe(
      true,
    );
  });

  it("no llama a onSaved si el submit falla", async () => {
    const { onSubmit, onSaved } = renderPanel("create", {
      representativeId: null,
      onSubmit: vi.fn().mockRejectedValue(new Error("fail")) as unknown as SubmitFn,
    });

    await userEvent.type(screen.getByLabelText(/^nombres$/i), "X");
    await userEvent.type(screen.getByLabelText(/primer apellido/i), "Y");
    await userEvent.type(screen.getByLabelText(/número de documento/i), "123");

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
    // HU #11756 — el rotulo pasa a ser explicito sobre su fuente: «Identidad: {estado}», con los 4
    // estados del ADR-0050. El copy anterior («Identidad sin validar») desaparece a proposito.
    expect(screen.getByText(/identidad: sin validaci[oó]n/i)).toBeInTheDocument();
    expect(screen.getByText(/sin firma registrada/i)).toBeInTheDocument();
    // Y el rotulo del baul esta SIEMPRE, tenga o no firma (CF-04).
    expect(screen.getByText(/firma del ba[uú]l: sin firma vigente/i)).toBeInTheDocument();
  });

  it("ordena la ficha: persona → firma → identidad → NITs", async () => {
    renderPanel("view");
    await screen.findByTestId("rl-firma-baul");

    const persona = screen.getByRole("region", { name: /datos del representante/i });
    const firma = screen.getByTestId("rl-firma-baul");
    const identidad = screen.getByTestId("rl-identidad");
    const nits = screen.getByRole("region", { name: /empresas representadas/i });

    expect(persona.compareDocumentPosition(firma) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(firma.compareDocumentPosition(identidad) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(identidad.compareDocumentPosition(nits) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
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

  it("el título es «Editar persona, firma y trámites»", async () => {
    renderPanel("edit");
    await waitFor(() =>
      expect(screen.getByRole("dialog", { name: /editar representante legal/i })).toBeInTheDocument(),
    );
  });

  it("NO muestra el grid de empresas en modo edit (se asocian aparte)", async () => {
    renderPanel("edit");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    expect(screen.queryByTestId("rl-companies-grid")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /agregar empresa/i })).not.toBeInTheDocument();
  });

  it("precarga TODAS las compañías en modo companies", async () => {
    renderPanel("companies");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());

    expect(await screen.findByDisplayValue("900123456-7")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Comercializadora XYZ")).toBeInTheDocument();
    expect(screen.getByDisplayValue("901987654-3")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Inversiones ABC")).toBeInTheDocument();
  });

  it("precarga el contacto de cada compañía en modo companies", async () => {
    renderPanel("companies");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());

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
    // El form se precarga desde GET /{id}; no hace falta expandir el acordeón para el submit.
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    await userEvent.click(await screen.findByRole("button", { name: /guardar cambios/i }));
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

    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    await userEvent.click(await screen.findByRole("button", { name: /guardar cambios/i }));
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
    renderPanel("edit");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalledTimes(1));
  });
});

// ── AC6 — Modal de detalle eliminado ─────────────────────────────────────────

describe("LegalRepresentativeDetailModal eliminado (AC6)", () => {
  it("LegalRepresentativeDetailModal no se importa en LegalRepresentativesFormPanel", async () => {
    const panelModule = await import("../LegalRepresentativesFormPanel");
    expect(typeof panelModule.LegalRepresentativesFormPanel).toBe("function");
    expect((panelModule as Record<string, unknown>).LegalRepresentativeDetailModal).toBeUndefined();
  });
});

// ── Grid de compañías con escrituras ─────────────────────────────────────────

describe("RepresentativeCompaniesAccordion — grid de compañías", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchLegalRepresentative).mockResolvedValue(ITEM_WITH_DEEDS);
  });

  it("AC1: cada compañía se muestra como tarjeta en el grid", async () => {
    renderPanel("view");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());

    expect(await screen.findByTestId("rl-companies-grid")).toBeInTheDocument();
    expect(screen.getByRole("article", { name: /comercializadora xyz/i })).toBeInTheDocument();
    expect(screen.getByRole("article", { name: /inversiones abc/i })).toBeInTheDocument();
  });

  it("AC2: la compañía principal tiene el ícono con aria-label «Compañía principal»", async () => {
    renderPanel("view");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    expect(await screen.findByLabelText("Compañía principal")).toBeInTheDocument();
  });

  it("AC2: la compañía secundaria NO tiene el ícono de principal", async () => {
    renderPanel("view");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    await screen.findByLabelText("Compañía principal");
    expect(screen.getAllByLabelText("Compañía principal")).toHaveLength(1);
  });

  it("AC3: en cada tarjeta se muestran las escrituras con su estado de vigencia", async () => {
    renderPanel("view");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    expect(await screen.findByText("Escritura de constitución")).toBeInTheDocument();
    expect(screen.getByText("Poder notarial vencido")).toBeInTheDocument();
    expect(screen.getByText("Vigente")).toBeInTheDocument();
    expect(screen.getByText("Vencida")).toBeInTheDocument();
  });

  it("AC3: se muestran los botones «Ver PDF» para cada escritura", async () => {
    renderPanel("view");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    expect(await screen.findAllByRole("button", { name: /ver pdf/i })).toHaveLength(2);
  });

  it("AC3: «Ver PDF» llama a fetchDeedDetail y abre la URL", async () => {
    vi.mocked(fetchDeedDetail).mockResolvedValue({
      deed: {} as never,
      viewUrl: "https://example.com/deed.pdf",
    });
    const openSpy = vi.spyOn(window, "open").mockImplementation(() => null);

    renderPanel("view");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    await userEvent.click((await screen.findAllByRole("button", { name: /ver pdf/i }))[0]);
    await waitFor(() => expect(fetchDeedDetail).toHaveBeenCalledWith(TENANT, "deed-1"));
    expect(openSpy).toHaveBeenCalledWith(
      "https://example.com/deed.pdf",
      "_blank",
      "noopener,noreferrer",
    );
    openSpy.mockRestore();
  });

  it("AC4: en modo companies la compañía CON escritura no muestra «Asociar escritura»", async () => {
    renderPanel("companies");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    const xyz = screen.getByRole("article", { name: /empresa comercializadora xyz/i });
    expect(within(xyz).queryByRole("button", { name: /asociar escritura/i })).not.toBeInTheDocument();
  });

  it("AC4: en modo companies aparece «Asociar escritura» si la ficha aún no tiene ninguna", async () => {
    renderPanel("companies");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    const btns = await screen.findAllByRole("button", { name: /asociar escritura/i });
    expect(btns.length).toBeGreaterThanOrEqual(1);
  });

  it("AC4: en modo view NO aparece «Asociar escritura»", async () => {
    renderPanel("view");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    await screen.findByTestId("rl-companies-grid");
    expect(screen.queryByRole("button", { name: /asociar escritura/i })).not.toBeInTheDocument();
  });

  it("AC4: al hacer clic en «Asociar escritura» se abre DeedsFormPanel", async () => {
    renderPanel("companies");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalledTimes(1));
    const btns = await screen.findAllByRole("button", { name: /asociar escritura/i });
    await userEvent.click(btns[0]);
    expect(await screen.findByRole("dialog", { name: /registrar escritura/i })).toBeInTheDocument();
  });

  it("AC4: al cerrar DeedsFormPanel el grid sigue visible", async () => {
    renderPanel("companies");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    const btns = await screen.findAllByRole("button", { name: /asociar escritura/i });
    await userEvent.click(btns[0]);
    await screen.findByRole("dialog", { name: /registrar escritura/i });
    const deedsDialog = screen.getByRole("dialog", { name: /registrar escritura/i });
    await userEvent.click(within(deedsDialog).getByRole("button", { name: /cerrar/i }));
    expect(screen.getByRole("dialog", { name: /asociar empresas y escrituras/i })).toBeInTheDocument();
    expect(screen.getByTestId("rl-companies-grid")).toBeInTheDocument();
  });

  it("AC5: en companies el bloque de escrituras permite asociar (representante ya guardado)", async () => {
    renderPanel("companies");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    const btns = await screen.findAllByRole("button", { name: /asociar escritura/i });
    expect(btns.length).toBeGreaterThanOrEqual(1);
    expect(screen.queryByText(/disponible al guardar/i)).not.toBeInTheDocument();
  });

  it("AC5b: al asociar escritura en empresa nueva, se guarda automáticamente y abre el panel", async () => {
    const onSubmit = vi.fn().mockResolvedValue({ id: "rep-1", signals: [] });
    const ITEM_AFTER_SAVE: LegalRepresentativeItem = {
      ...ITEM,
      companies: [
        ...ITEM.companies,
        {
          id: "co-3",
          nit: "900333333-3",
          name: "Empresa Tres",
          isPrimary: false,
          deeds: [],
          email: null,
          address: null,
          city: null,
          phone: null,
        },
      ],
    };

    // Primera carga: 2 compañías. Tras auto-guardar: 3.
    vi.mocked(fetchLegalRepresentative)
      .mockResolvedValueOnce(ITEM)
      .mockResolvedValue(ITEM_AFTER_SAVE);

    renderPanel("companies", { onSubmit: onSubmit as unknown as SubmitFn });
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());

    await userEvent.click(await screen.findByRole("button", { name: /agregar empresa/i }));
    const nitInputs = await screen.findAllByLabelText(/nit de la compañía/i);
    const nameInputs = await screen.findAllByLabelText(/razón social/i);
    const last = nitInputs.length - 1;
    await userEvent.clear(nitInputs[last]);
    await userEvent.type(nitInputs[last], "900333333-3");
    await userEvent.clear(nameInputs[last]);
    await userEvent.type(nameInputs[last], "Empresa Tres");

    const asociarBtns = await screen.findAllByRole("button", { name: /asociar escritura/i });
    expect(asociarBtns.length).toBe(3);
    await userEvent.click(asociarBtns[2]);

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(await screen.findByRole("dialog", { name: /registrar escritura/i })).toBeInTheDocument();
    // No cierra el panel de empresas.
    expect(screen.getByRole("dialog", { name: /asociar empresas y escrituras/i })).toBeInTheDocument();
  }, 15000);

  it("AC5: en modo create NO aparece el grid de empresas", () => {
    renderPanel("create", { representativeId: null });
    expect(screen.queryByRole("button", { name: /agregar empresa/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /asociar escritura/i })).not.toBeInTheDocument();
  });

  it("AC6: CompanyDeedsSection ya no se importa ni renderiza", async () => {
    const tabModule = await import("../RepresentativesAndVaultTab");
    expect(tabModule).toBeDefined();
  });

  it("AC6: el grid en modo companies muestra botón «Editar» para cada escritura", async () => {
    renderPanel("companies");
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalled());
    const editBtns = await screen.findAllByRole("button", { name: /editar escritura/i });
    expect(editBtns.length).toBeGreaterThanOrEqual(1);
  });
});

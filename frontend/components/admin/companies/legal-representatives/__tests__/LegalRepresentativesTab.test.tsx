// HU #10904 — Pestaña Representantes legales: lista paginada (4 estados), enmascarado de documento,
// estado de firma/identidad con StatusBadge, eliminación con confirmación y envío de validación de
// identidad.
// HU #11178 — Panel unificado view/create/edit: «Ver» abre el panel en modo consulta, «Editar» en
// modo edición directamente desde la tabla. El LegalRepresentativeDetailModal ha sido retirado.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { LegalRepresentativesTab } from "../LegalRepresentativesTab";
import type {
  LegalRepresentativeItem,
  LegalRepresentativePage,
} from "@/lib/api/admin-legal-representatives";

vi.mock("@/lib/api/admin-legal-representatives", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-legal-representatives")>();
  return {
    ...actual,
    fetchLegalRepresentatives: vi.fn(),
    fetchLegalRepresentative: vi.fn(),
    fetchAssignableProcedureTypes: vi.fn(),
    createLegalRepresentative: vi.fn(),
    updateLegalRepresentative: vi.fn(),
    deleteLegalRepresentative: vi.fn(),
    sendLegalRepresentativeIdentity: vi.fn(),
  };
});

import {
  createLegalRepresentative,
  deleteLegalRepresentative,
  fetchAssignableProcedureTypes,
  fetchLegalRepresentative,
  fetchLegalRepresentatives,
  sendLegalRepresentativeIdentity,
  type AssignableProcedureType,
} from "@/lib/api/admin-legal-representatives";

const TENANT = "11111111-1111-1111-1111-111111111111";

const PROC_TYPES: AssignableProcedureType[] = [
  { id: "019f8195-fed1-770a-98ae-295ed59b53d4", code: "TRASPASO_STANDARD", name: "Traspaso" },
  { id: "019f8195-bbdf-72ea-b226-e026826cbfa6", code: "MATRICULA_NUEVA", name: "Matrícula inicial" },
];

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
  address: null,
  city: "Medellín",
  phone: null,
  signatureVaultId: null,
  identityValidationRef: null,
  hasSignatureOrIdentity: false,
  identityStatus: "none",
  identityValidUntil: null,
  firmaBaulVigente: false,
  firmaBaulVigenteHasta: null,
  procedureTypeIds: ["019ef140-f24e-78e4-8e6d-97faa44ed7a8"],
  companies: [
    { id: "co-1", nit: "900123456-7", name: "Comercializadora XYZ", isPrimary: true, deeds: [] },
  ],
  isActive: true,
  createdAt: "2026-06-01T00:00:00Z",
  updatedAt: null,
};

function page(data: LegalRepresentativeItem[]): LegalRepresentativePage {
  return { data, totalCount: data.length, page: 1, pageSize: 20 };
}

function renderTab() {
  return render(
    <ToastProvider>
      <LegalRepresentativesTab tenantId={TENANT} />
    </ToastProvider>,
  );
}

describe("LegalRepresentativesTab (HU #10904)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchAssignableProcedureTypes).mockResolvedValue(PROC_TYPES);
    // Por defecto el fetch del detalle no resuelve (solo se llama cuando se abre el panel).
    vi.mocked(fetchLegalRepresentative).mockResolvedValue(ITEM);
  });

  it("muestra el estado vacío cuando no hay representantes", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([]));
    renderTab();
    expect(
      await screen.findByText(/aún no tiene representantes legales registrados/i),
    ).toBeInTheDocument();
  });

  it("muestra el estado de error y permite reintentar", async () => {
    vi.mocked(fetchLegalRepresentatives).mockRejectedValueOnce(new Error("boom"));
    renderTab();
    expect(await screen.findByText(/no se pudieron cargar los representantes/i)).toBeInTheDocument();

    vi.mocked(fetchLegalRepresentatives).mockResolvedValueOnce(page([ITEM]));
    await userEvent.click(screen.getByRole("button", { name: /reintentar/i }));
    expect(await screen.findByText("Ana Gómez Ruiz")).toBeInTheDocument();
  });

  it("lista enmascarando el documento y marca el estado sin firma ni identidad", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([ITEM]));
    renderTab();
    expect(await screen.findByText("Ana Gómez Ruiz")).toBeInTheDocument();
    expect(screen.getByText(/••••5432/)).toBeInTheDocument();
    expect(screen.getByText(/Sin firma ni identidad/i)).toBeInTheDocument();
    // El número completo del documento no debe renderizarse.
    expect(screen.queryByText(/1098765432/)).not.toBeInTheDocument();
  });

  it("no muestra la columna Compañía en la tabla (razón social / NIT ocultos)", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([ITEM]));
    renderTab();
    await screen.findByText("Ana Gómez Ruiz");
    expect(screen.queryByRole("columnheader", { name: /^compañía$/i })).not.toBeInTheDocument();
    expect(screen.queryByText("Comercializadora XYZ")).not.toBeInTheDocument();
    expect(screen.queryByText("900123456-7")).not.toBeInTheDocument();
  });

  it("envía el correo de validación de identidad", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([ITEM]));
    vi.mocked(sendLegalRepresentativeIdentity).mockResolvedValue({
      id: "val-1",
      status: "PENDING",
      reused: false,
    });
    renderTab();
    await screen.findByText("Ana Gómez Ruiz");

    await userEvent.click(screen.getByRole("button", { name: /^validar identidad$/i }));
    await waitFor(() =>
      expect(sendLegalRepresentativeIdentity).toHaveBeenCalledWith(TENANT, "rep-1"),
    );
  });

  it("alimenta el multiselect con los tipos del catálogo del backend (no una lista estática)", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([]));
    renderTab();
    await screen.findByText(/aún no tiene representantes legales registrados/i);

    await waitFor(() =>
      expect(fetchAssignableProcedureTypes).toHaveBeenCalledWith(TENANT, expect.anything()),
    );

    await userEvent.click(screen.getByRole("button", { name: /^nuevo representante$/i }));

    expect(await screen.findByText("Traspaso")).toBeInTheDocument();
    expect(screen.getByText("Matrícula inicial")).toBeInTheDocument();
  });

  it("muestra el aviso cuando no hay tipos de trámite habilitados", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([]));
    vi.mocked(fetchAssignableProcedureTypes).mockResolvedValue([]);
    renderTab();
    await screen.findByText(/aún no tiene representantes legales registrados/i);

    await userEvent.click(screen.getByRole("button", { name: /^nuevo representante$/i }));
    expect(
      await screen.findByText(/no hay tipos de trámite habilitados en el módulo de trámites/i),
    ).toBeInTheDocument();
  });

  it("elimina un representante tras confirmar en el diálogo", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([ITEM]));
    vi.mocked(deleteLegalRepresentative).mockResolvedValue(undefined);
    renderTab();
    await screen.findByText("Ana Gómez Ruiz");

    await userEvent.click(screen.getByRole("button", { name: /eliminar ana gómez ruiz/i }));
    const dialog = await screen.findByRole("dialog");
    await userEvent.click(within(dialog).getByRole("button", { name: /^eliminar$/i }));

    await waitFor(() => expect(deleteLegalRepresentative).toHaveBeenCalledWith(TENANT, "rep-1"));
  });

  it("permite agregar/quitar empresas y envía companies[] al registrar (HU #10934)", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([]));
    vi.mocked(createLegalRepresentative).mockResolvedValue({ id: "rep-new", signals: [] });
    // Después del alta el panel pasa a edit → fetchLegalRepresentative se llama con el nuevo id.
    vi.mocked(fetchLegalRepresentative).mockResolvedValue({ ...ITEM, id: "rep-new" });
    renderTab();
    await screen.findByText(/aún no tiene representantes legales registrados/i);

    await userEvent.click(screen.getByRole("button", { name: /^nuevo representante$/i }));

    await userEvent.type(screen.getByLabelText(/^nombres$/i), "Ana");
    await userEvent.type(screen.getByLabelText(/número de documento/i), "1098765432");
    await userEvent.type(screen.getByLabelText(/primer apellido/i), "Gómez");

    // En modo create el primer acordeón empieza abierto; los inputs están en el DOM.
    const nits = await screen.findAllByLabelText(/nit de la compañía/i);
    const names = await screen.findAllByLabelText(/razón social/i);
    await userEvent.type(nits[0], "900111111-1");
    await userEvent.type(names[0], "Empresa Uno");

    // Al agregar empresa, el nuevo acordeón se abre automáticamente vía useEffect.
    await userEvent.click(screen.getByRole("button", { name: /agregar empresa/i }));
    await userEvent.click(screen.getByRole("button", { name: /agregar empresa/i }));
    // Usar findAllByLabelText para esperar a que los nuevos acordeones estén visibles.
    let nitInputs = await screen.findAllByLabelText(/nit de la compañía/i);
    const nameInputs = await screen.findAllByLabelText(/razón social/i);
    expect(nitInputs).toHaveLength(3);
    await userEvent.type(nitInputs[1], "900222222-2");
    await userEvent.type(nameInputs[1], "Empresa Dos");

    await userEvent.click(screen.getByRole("button", { name: /quitar empresa 3/i }));
    nitInputs = await screen.findAllByLabelText(/nit de la compañía/i);
    expect(nitInputs).toHaveLength(2);

    await userEvent.click(screen.getByRole("button", { name: /registrar representante/i }));

    await waitFor(() => expect(createLegalRepresentative).toHaveBeenCalled());
    const [, payload] = vi.mocked(createLegalRepresentative).mock.calls[0];
    expect(payload.companies).toHaveLength(2);
    // digitsOnly strips hyphens → "900111111-1" → "9001111111", "900222222-2" → "9002222222"
    expect(payload.companies[0]).toMatchObject({ nit: "9001111111", name: "Empresa Uno" });
    expect(payload.companies[1]).toMatchObject({ nit: "9002222222", name: "Empresa Dos" });
    expect(payload.companyNit).toBe("9001111111");
  }, 15000); // Plazo extendido: el test tipea 65+ caracteres en acordeones auto-expandibles.

  // ── HU #11178: panel unificado ───────────────────────────────────────────────

  it("AC1: «Ver» abre el panel en modo consulta (no un modal separado)", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([ITEM]));
    renderTab();
    await screen.findByText("Ana Gómez Ruiz");

    await userEvent.click(screen.getByRole("button", { name: /ver detalle de ana gómez ruiz/i }));

    await waitFor(() =>
      expect(fetchLegalRepresentative).toHaveBeenCalledWith(TENANT, "rep-1", expect.anything()),
    );

    // El panel tiene role=dialog con aria-label "Ver representante legal".
    expect(
      await screen.findByRole("dialog", { name: /ver representante legal/i }),
    ).toBeInTheDocument();
  });

  it("AC2: «Editar» dentro del panel de consulta cambia al modo edición sin cerrar", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([ITEM]));
    renderTab();
    await screen.findByText("Ana Gómez Ruiz");

    // Abre en consulta.
    await userEvent.click(screen.getByRole("button", { name: /ver detalle de ana gómez ruiz/i }));
    await screen.findByRole("dialog", { name: /ver representante legal/i });
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalledTimes(1));

    // Pulsa «Editar» → cambia al panel de edición sin cerrarse.
    const panelEdit = screen.getByRole("button", { name: /pasar a modo edición/i });
    await userEvent.click(panelEdit);

    // Ahora el panel es de edición (mismo dialog, aria-label cambia).
    expect(
      await screen.findByRole("dialog", { name: /editar representante legal/i }),
    ).toBeInTheDocument();

    // El panel sigue abierto (no se cerró).
    expect(screen.queryByRole("button", { name: /ver detalle de ana gómez ruiz/i })).toBeInTheDocument();
  });

  it("«Editar» desde la tabla abre directamente en modo edición (sin pasar por consulta)", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([ITEM]));
    renderTab();
    await screen.findByText("Ana Gómez Ruiz");

    await userEvent.click(screen.getByRole("button", { name: /^editar ana gómez ruiz$/i }));

    // El panel abre directamente en modo edición.
    expect(
      await screen.findByRole("dialog", { name: /editar representante legal/i }),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(fetchLegalRepresentative).toHaveBeenCalledWith(TENANT, "rep-1", expect.anything()),
    );
  });

  it("AC5: después de registrar, el panel permanece abierto en modo edición sobre el recién creado", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([]));
    vi.mocked(createLegalRepresentative).mockResolvedValue({ id: "rep-new", signals: [] });
    vi.mocked(fetchLegalRepresentative).mockResolvedValue({ ...ITEM, id: "rep-new" });
    renderTab();
    await screen.findByText(/aún no tiene representantes legales registrados/i);

    await userEvent.click(screen.getByRole("button", { name: /^nuevo representante$/i }));

    // Rellena el mínimo requerido (acordeón[0] abierto automáticamente en modo create).
    await userEvent.type(screen.getByLabelText(/^nombres$/i), "Pedro");
    await userEvent.type(screen.getByLabelText(/primer apellido/i), "López");
    await userEvent.type(screen.getByLabelText(/número de documento/i), "9876543");
    const nits = await screen.findAllByLabelText(/nit de la compañía/i);
    await userEvent.type(nits[0], "900000001");
    const names = await screen.findAllByLabelText(/razón social/i);
    await userEvent.type(names[0], "Empresa Alta");

    await userEvent.click(screen.getByRole("button", { name: /registrar representante/i }));

    // Tras el alta el panel pasa a EDICIÓN (no se cierra).
    expect(
      await screen.findByRole("dialog", { name: /editar representante legal/i }),
    ).toBeInTheDocument();
    // El createLegalRepresentative se llamó con el nuevo representante.
    expect(createLegalRepresentative).toHaveBeenCalledTimes(1);
    // El fetchLegalRepresentative se llamó con el ID del recién creado.
    await waitFor(() =>
      expect(fetchLegalRepresentative).toHaveBeenCalledWith(TENANT, "rep-new", expect.anything()),
    );
  });

  it("AC6: LegalRepresentativeDetailModal ya no se usa (no hay dialog de tipo modal tras Ver)", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([ITEM]));
    renderTab();
    await screen.findByText("Ana Gómez Ruiz");

    await userEvent.click(screen.getByRole("button", { name: /ver detalle de ana gómez ruiz/i }));
    await screen.findByRole("dialog", { name: /ver representante legal/i });

    // No debe haber un role=dialog con título "Detalle del representante" que venga de un Modal
    // (<dialog> de atom/Modal), solo el <aside role="dialog"> del OtSidePanel.
    const dialogs = screen.getAllByRole("dialog");
    // Exactamente un dialog visible: el panel lateral.
    expect(dialogs).toHaveLength(1);
    expect(dialogs[0].tagName.toLowerCase()).toBe("aside");
  });
});

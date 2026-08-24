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
import { IDENTITY_MODULE_HREF } from "@/lib/admin/identity-vigencia";
import {
  SIGNAL_SIN_FIRMA_NI_IDENTIDAD,
  type LegalRepresentativeItem,
  type LegalRepresentativePage,
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
  };
});

import {
  createLegalRepresentative,
  deleteLegalRepresentative,
  fetchAssignableProcedureTypes,
  fetchLegalRepresentative,
  fetchLegalRepresentatives,
  updateLegalRepresentative,
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

  it("lista el documento completo y marca el estado sin firma ni identidad", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([ITEM]));
    renderTab();
    expect(await screen.findByText("Ana Gómez Ruiz")).toBeInTheDocument();
    // El listado muestra el documento completo: los últimos cuatro dígitos no bastan para
    // identificar a la persona durante la operación.
    expect(screen.getByText(/1098765432/)).toBeInTheDocument();
    expect(screen.queryByText(/••••/)).not.toBeInTheDocument();
    expect(screen.getByText(/Sin firma ni identidad/i)).toBeInTheDocument();
  });

  it("no muestra la columna Compañía en la tabla (razón social / NIT ocultos)", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([ITEM]));
    renderTab();
    await screen.findByText("Ana Gómez Ruiz");
    expect(screen.queryByRole("columnheader", { name: /^compañía$/i })).not.toBeInTheDocument();
    expect(screen.queryByText("Comercializadora XYZ")).not.toBeInTheDocument();
    expect(screen.queryByText("900123456-7")).not.toBeInTheDocument();
  });

  // HU #11758 (ADR-0050) — el aviso de "quedó guardado sin firma ni validación" ya no dispara el
  // correo de identidad (esa ruta responde 410 Gone): remite al módulo Identidad.
  it("tras registrar sin firma ni identidad, el aviso enlaza al módulo Identidad (no dispara correo)", async () => {
    // El aviso solo aparece si el representante recien creado esta EN LA LISTA: `pendingItem` se
    // resuelve buscando el id devuelto por el alta dentro de `items`. Por eso la primera carga va
    // vacia y la recarga posterior al alta ya trae la fila.
    vi.mocked(fetchLegalRepresentatives)
      .mockResolvedValueOnce(page([]))
      .mockResolvedValue(page([{ ...ITEM, id: "rep-new", name: "Pedro", firstLastName: "López" }]));
    vi.mocked(createLegalRepresentative).mockResolvedValue({
      id: "rep-new",
      signals: [SIGNAL_SIN_FIRMA_NI_IDENTIDAD],
    });
    renderTab();
    await screen.findByText(/aún no tiene representantes legales registrados/i);

    await userEvent.click(screen.getByRole("button", { name: /^nuevo representante$/i }));
    await userEvent.type(screen.getByLabelText(/^nombres$/i), "Pedro");
    await userEvent.type(screen.getByLabelText(/primer apellido/i), "López");
    await userEvent.type(screen.getByLabelText(/número de documento/i), "9876543");
    await userEvent.click(screen.getByRole("button", { name: /^registrar representante$/i }));

    await waitFor(() => expect(createLegalRepresentative).toHaveBeenCalledTimes(1));

    const link = await screen.findByRole("link", { name: /ir al módulo identidad/i });
    expect(link).toHaveAttribute("href", IDENTITY_MODULE_HREF);
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

  it("permite agregar/quitar empresas desde el botón Empresas del listado", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([ITEM]));
    vi.mocked(updateLegalRepresentative).mockResolvedValue({ id: "rep-1", signals: [] });
    vi.mocked(fetchLegalRepresentative).mockResolvedValue(ITEM);
    renderTab();
    await screen.findByText("Ana Gómez Ruiz");

    await userEvent.click(screen.getByRole("button", { name: /asociar empresas de ana gómez ruiz/i }));
    expect(
      await screen.findByRole("dialog", { name: /asociar empresas y escrituras/i }),
    ).toBeInTheDocument();

    await userEvent.click(await screen.findByRole("button", { name: /agregar empresa/i }));
    const nitInputs = await screen.findAllByLabelText(/nit de la compañía/i);
    const nameInputs = await screen.findAllByLabelText(/razón social/i);
    const last = nitInputs.length - 1;
    await userEvent.clear(nitInputs[last]);
    await userEvent.type(nitInputs[last], "900333333-3");
    await userEvent.clear(nameInputs[last]);
    await userEvent.type(nameInputs[last], "Empresa Tres");

    await userEvent.click(screen.getByRole("button", { name: /guardar empresas/i }));
    await waitFor(() => expect(updateLegalRepresentative).toHaveBeenCalled());
  }, 15000);

  // ── Panel unificado ──────────────────────────────────────────────────────────

  it("AC1: el grid no muestra el botón «Ver»", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([ITEM]));
    renderTab();
    await screen.findByText("Ana Gómez Ruiz");

    expect(
      screen.queryByRole("button", { name: /ver ficha completa de ana gómez ruiz/i }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /editar persona y firma de ana gómez ruiz/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /asociar empresas de ana gómez ruiz/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /eliminar ana gómez ruiz/i })).toBeInTheDocument();
  });

  it("«Editar» desde la tabla abre directamente en modo edición", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([ITEM]));
    renderTab();
    await screen.findByText("Ana Gómez Ruiz");

    await userEvent.click(screen.getByRole("button", { name: /editar persona y firma de ana gómez ruiz/i }));

    expect(
      await screen.findByRole("dialog", { name: /editar representante legal/i }),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(fetchLegalRepresentative).toHaveBeenCalledWith(TENANT, "rep-1", expect.anything()),
    );
  });

  it("AC5: después de registrar, el panel se cierra y el listado se refresca", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([]));
    vi.mocked(createLegalRepresentative).mockResolvedValue({ id: "rep-new", signals: [] });
    renderTab();
    await screen.findByText(/aún no tiene representantes legales registrados/i);

    await userEvent.click(screen.getByRole("button", { name: /^nuevo representante$/i }));

    await userEvent.type(screen.getByLabelText(/^nombres$/i), "Pedro");
    await userEvent.type(screen.getByLabelText(/primer apellido/i), "López");
    await userEvent.type(screen.getByLabelText(/número de documento/i), "9876543");

    await userEvent.click(screen.getByRole("button", { name: /^registrar representante$/i }));

    await waitFor(() => expect(createLegalRepresentative).toHaveBeenCalledTimes(1), {
      timeout: 10000,
    });
    expect(vi.mocked(createLegalRepresentative).mock.calls[0][1].companies).toEqual([]);
    await waitFor(() =>
      expect(screen.queryByRole("dialog", { name: /registrar representante legal/i })).not.toBeInTheDocument(),
    );
  }, 15000);
});

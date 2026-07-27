// HU #10904 — Pestaña Representantes legales: lista paginada (4 estados), enmascarado de documento,
// estado de firma/identidad con StatusBadge, eliminación con confirmación y envío de validación de
// identidad. La API se mockea.
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

// El detalle representante-céntrico resuelve el PDF de una escritura con el cliente de escrituras.
vi.mock("@/lib/api/admin-deeds", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-deeds")>();
  return {
    ...actual,
    fetchDeedDetail: vi.fn(),
    saveDeed: vi.fn(),
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
import { saveDeed } from "@/lib/api/admin-deeds";

const TENANT = "11111111-1111-1111-1111-111111111111";

// IDs reales del catálogo (uuidv7 del entorno), NO los hardcodeados que causaban tipo_tramite_inexistente.
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
  procedureTypeIds: ["019ef140-f24e-78e4-8e6d-97faa44ed7a8"],
  companies: [{ id: "co-1", nit: "900123456-7", name: "Comercializadora XYZ", deeds: [] }],
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
    // Por defecto el catálogo de tipos de trámite responde con los tipos activos+published.
    vi.mocked(fetchAssignableProcedureTypes).mockResolvedValue(PROC_TYPES);
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
    // El encabezado de columna "Compañía" ya no existe.
    expect(screen.queryByRole("columnheader", { name: /^compañía$/i })).not.toBeInTheDocument();
    // Ni la razón social ni el NIT de la compañía se pintan en la fila.
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

    // Se consultó el catálogo real acotado al tenant.
    await waitFor(() =>
      expect(fetchAssignableProcedureTypes).toHaveBeenCalledWith(TENANT, expect.anything()),
    );

    await userEvent.click(screen.getByRole("button", { name: /^nuevo representante$/i }));

    // El multiselect muestra los nombres devueltos por el backend.
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
    renderTab();
    await screen.findByText(/aún no tiene representantes legales registrados/i);

    await userEvent.click(screen.getByRole("button", { name: /^nuevo representante$/i }));

    // Datos del representante-persona (se captura una sola vez).
    await userEvent.type(screen.getByLabelText(/^nombres$/i), "Ana");
    await userEvent.type(screen.getByLabelText(/número de documento/i), "1098765432");
    await userEvent.type(screen.getByLabelText(/primer apellido/i), "Gómez");

    // Empresa primaria.
    const nits = screen.getAllByLabelText(/nit de la compañía/i);
    const names = screen.getAllByLabelText(/razón social/i);
    await userEvent.type(nits[0], "900111111-1");
    await userEvent.type(names[0], "Empresa Uno");

    // Agrega una segunda empresa y luego una tercera que se quita.
    await userEvent.click(screen.getByRole("button", { name: /agregar empresa/i }));
    await userEvent.click(screen.getByRole("button", { name: /agregar empresa/i }));
    let nitInputs = screen.getAllByLabelText(/nit de la compañía/i);
    const nameInputs = screen.getAllByLabelText(/razón social/i);
    expect(nitInputs).toHaveLength(3);
    await userEvent.type(nitInputs[1], "900222222-2");
    await userEvent.type(nameInputs[1], "Empresa Dos");

    // Quita la tercera (vacía): queda con dos empresas.
    await userEvent.click(screen.getByRole("button", { name: /quitar empresa 3/i }));
    nitInputs = screen.getAllByLabelText(/nit de la compañía/i);
    expect(nitInputs).toHaveLength(2);

    await userEvent.click(screen.getByRole("button", { name: /registrar representante/i }));

    await waitFor(() => expect(createLegalRepresentative).toHaveBeenCalled());
    const [, payload] = vi.mocked(createLegalRepresentative).mock.calls[0];
    expect(payload.companies).toHaveLength(2);
    expect(payload.companies[0]).toMatchObject({ nit: "900111111-1", name: "Empresa Uno" });
    expect(payload.companies[1]).toMatchObject({ nit: "900222222-2", name: "Empresa Dos" });
    // Retrocompatibilidad: la primera empresa también viaja en los campos planos.
    expect(payload.companyNit).toBe("900111111-1");
  });

  it("el detalle muestra las escrituras por empresa con su estado (HU #10934)", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([ITEM]));
    vi.mocked(fetchLegalRepresentative).mockResolvedValue({
      ...ITEM,
      companies: [
        {
          id: "co-1",
          nit: "900123456-7",
          name: "Comercializadora XYZ",
          deeds: [
            {
              id: "deed-1",
              description: "Escritura de constitución",
              vigenciaDesde: "2020-01-01",
              vigenciaHasta: "2999-12-31",
              isActive: true,
              estado: "vigente",
            },
            {
              id: "deed-2",
              description: "Poder revocado",
              vigenciaDesde: "2019-01-01",
              vigenciaHasta: "2020-01-01",
              isActive: true,
              estado: "vencida",
            },
          ],
        },
      ],
    });
    renderTab();
    await screen.findByText("Ana Gómez Ruiz");

    await userEvent.click(screen.getByRole("button", { name: /ver detalle de ana gómez ruiz/i }));

    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalledWith(TENANT, "rep-1", expect.anything()));

    const dialog = await screen.findByRole("dialog");
    expect(await within(dialog).findByText("Escritura de constitución")).toBeInTheDocument();
    expect(within(dialog).getByText("Poder revocado")).toBeInTheDocument();
    expect(within(dialog).getByText("Vigente")).toBeInTheDocument();
    expect(within(dialog).getByText("Vencida")).toBeInTheDocument();
    // Punto de entrada para asociar una escritura nueva a la empresa desde la misma vista.
    expect(within(dialog).getByRole("button", { name: /asociar escritura/i })).toBeInTheDocument();
  });

  it("'Asociar escritura' abre el panel con la compañía fija, guarda y refresca el detalle (HU #10929)", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([ITEM]));
    vi.mocked(fetchLegalRepresentative).mockResolvedValue({
      ...ITEM,
      companies: [{ id: "co-1", nit: "900123456-7", name: "Comercializadora XYZ", deeds: [] }],
    });
    vi.mocked(saveDeed).mockResolvedValue({ id: "deed-new" });

    renderTab();
    await screen.findByText("Ana Gómez Ruiz");
    await userEvent.click(screen.getByRole("button", { name: /ver detalle de ana gómez ruiz/i }));
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalledTimes(1));

    // Abre el panel de alta desde la empresa (antes NO abría por el z-index bajo el modal).
    await userEvent.click(await screen.findByRole("button", { name: /asociar escritura/i }));

    const panel = await screen.findByRole("dialog", { name: /registrar escritura/i });
    // La compañía llega FIJA por contexto (dato de solo lectura) y no hay selector de compañías.
    expect(within(panel).getByText("Comercializadora XYZ")).toBeInTheDocument();
    expect(within(panel).queryByRole("checkbox")).not.toBeInTheDocument();

    // Captura descripción + vigencia + PDF.
    await userEvent.type(within(panel).getByLabelText(/descripción/i), "Poder general 2026");
    const file = new File(["%PDF-1.4 test"], "poder.pdf", { type: "application/pdf" });
    await userEvent.upload(
      within(panel).getByLabelText(/selecciona el documento pdf de la escritura/i),
      file,
    );
    // Los inputs de fecha se fijan directamente (userEvent.type es frágil con type="date").
    await userEvent.click(within(panel).getByRole("button", { name: /registrar escritura/i }));

    // Aún sin fechas el guardado no dispara: el botón exige vigencia.
    expect(saveDeed).not.toHaveBeenCalled();

    await userEvent.type(within(panel).getByLabelText(/vigencia desde/i), "2026-01-01");
    await userEvent.type(within(panel).getByLabelText(/vigencia hasta/i), "2027-01-01");
    await userEvent.click(within(panel).getByRole("button", { name: /registrar escritura/i }));

    // Guarda para la ÚNICA compañía fija (alta → editingId null) y refresca el detalle.
    await waitFor(() => expect(saveDeed).toHaveBeenCalledTimes(1));
    const [tenantArg, editingArg, inputArg] = vi.mocked(saveDeed).mock.calls[0];
    expect(tenantArg).toBe(TENANT);
    expect(editingArg).toBeNull();
    expect(inputArg.companyIds).toEqual(["co-1"]);
    expect(inputArg.description).toBe("Poder general 2026");
    expect(inputArg.file).toBe(file);
    // El detalle se recarga tras guardar (fetchLegalRepresentative se vuelve a llamar).
    await waitFor(() => expect(fetchLegalRepresentative).toHaveBeenCalledTimes(2));
  });
});

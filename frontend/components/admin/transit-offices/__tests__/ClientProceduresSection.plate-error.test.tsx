// El OT reportó que, al escribir una placa ya asignada, el sistema no informaba nada: el modal
// simplemente no avanzaba. El backend devolvía un 422 genérico y la UI descartaba el error y
// mostraba "No se pudo asignar la placa". Ahora cada causa llega nombrada y se muestra tal cual,
// dejando el modal abierto para corregir la placa.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { ClientProceduresSection } from "../ClientProceduresSection";
import { ApiError } from "@/lib/api/types";
import type { OtClientProcedure } from "@/lib/api/types-ot";

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtClientProcedures: vi.fn(),
  fetchOtBandejaHealth: vi.fn(),
  fetchOtProfile: vi.fn(),
  approveOtClientProcedure: vi.fn(),
  rejectOtClientProcedure: vi.fn(),
  generarOtConsolidadoMaestro: vi.fn(),
  fetchOtDocuments: vi.fn(),
  fetchOtAttachmentPreviewUrl: vi.fn(),
  adjuntarOtLicenciaTransito: vi.fn(),
}));

const plateMocks = vi.hoisted(() => ({
  listPlateDetails: vi.fn(),
  assignPlateToProcedure: vi.fn(),
}));
vi.mock("@/lib/api/admin-plate-ranges", () => ({
  listPlateDetails: plateMocks.listPlateDetails,
  assignPlateToProcedure: plateMocks.assignPlateToProcedure,
  revokeProcedurePlate: vi.fn(),
}));

vi.mock("@/lib/auth/jwt", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/auth/jwt")>();
  return { ...actual, isSuperAdmin: () => false };
});

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: { listPublishedProcedureTypes: vi.fn().mockResolvedValue([]) },
}));

import {
  fetchOtBandejaHealth,
  fetchOtClientProcedures,
  fetchOtProfile,
} from "@/lib/api/admin-ot";

const preasignado: OtClientProcedure = {
  id: "proc-7",
  clientTenantId: "client-tenant-aaaa",
  clientTenantName: "Flota Andina S.A.S.",
  procedureTypeId: "matricula_inicial-type-id",
  procedureTypeName: "Matrícula inicial",
  referenceNumber: "RAD-2026-777",
  status: "entregado",
  plateFlowStatus: "preasignado",
  platePreferredLastDigit: "5",
  createdAt: "2026-06-23T09:00:00Z",
};

const YA_ASIGNADA =
  "La placa ABC105 ya está asignada a otro trámite de este organismo de tránsito. Elija una placa diferente.";

function renderSection() {
  return render(
    <ToastProvider>
      <ClientProceduresSection />
    </ToastProvider>,
  );
}

/** Abre el modal y confirma la asignación de la primera placa del rango. */
async function intentarAsignar(user: ReturnType<typeof userEvent.setup>) {
  await screen.findByText("RAD-2026-777");
  await user.click(await screen.findByRole("button", { name: /Asignar placa/i }));
  const select = await screen.findByLabelText(/Placa del rango/i);
  await user.selectOptions(select, "ABC105");
  const confirmar = await screen.findByRole("button", { name: /^Asignar$/i });
  await user.click(confirmar);
}

describe("ClientProceduresSection — error al asignar placa", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchOtProfile).mockResolvedValue({
      operationMode: "dashboard",
      quipuxReadOnly: false,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      featureFlags: [],
    });
    vi.mocked(fetchOtClientProcedures).mockResolvedValue({
      data: [preasignado],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    });
    vi.mocked(fetchOtBandejaHealth).mockResolvedValue({
      transitOfficeResolved: true,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      deliveredTotal: 1,
      deliveredWithGrant: 1,
      deliveredWithoutGrant: 0,
      hasDeliveredWithoutGrant: false,
    });
    plateMocks.listPlateDetails.mockResolvedValue([
      {
        id: "2",
        plateRangeId: "r",
        tenantId: "t",
        transitOfficeId: "o",
        plate: "ABC105",
        state: "disponible" as const,
        procedureInstanceId: null,
      },
    ]);
  });

  it("muestra el motivo que devuelve el backend cuando la placa ya está asignada", async () => {
    const user = userEvent.setup();
    plateMocks.assignPlateToProcedure.mockRejectedValue(
      new ApiError(409, "Error 409", { detail: YA_ASIGNADA }),
    );

    renderSection();
    await intentarAsignar(user);

    expect(await screen.findByText(YA_ASIGNADA)).toBeInTheDocument();
  });

  it("no se queda con el mensaje genérico cuando hay un motivo concreto", async () => {
    const user = userEvent.setup();
    plateMocks.assignPlateToProcedure.mockRejectedValue(
      new ApiError(409, "Error 409", { detail: YA_ASIGNADA }),
    );

    renderSection();
    await intentarAsignar(user);

    await screen.findByText(YA_ASIGNADA);
    expect(screen.queryByText("No se pudo asignar la placa.")).not.toBeInTheDocument();
  });

  it("cae al mensaje genérico si el error no trae detalle", async () => {
    const user = userEvent.setup();
    plateMocks.assignPlateToProcedure.mockRejectedValue(new Error("network"));

    renderSection();
    await intentarAsignar(user);

    expect(await screen.findByText("No se pudo asignar la placa.")).toBeInTheDocument();
  });

  it("muestra el detalle de un 422 ProblemDetails (antes se perdía en ApiValidationError)", async () => {
    const user = userEvent.setup();
    const motivo =
      "El trámite no está en preasignado: no admite asignación de placa en su estado actual.";
    plateMocks.assignPlateToProcedure.mockRejectedValue(
      new ApiError(422, motivo, { detail: motivo, title: "Unprocessable" }),
    );

    renderSection();
    await intentarAsignar(user);

    expect(await screen.findByText(motivo)).toBeInTheDocument();
    expect(screen.queryByText("No se pudo asignar la placa.")).not.toBeInTheDocument();
  });

  it("muestra el motivo de placa fuera de rango ya registrada", async () => {
    const user = userEvent.setup();
    const motivo =
      "La placa QXU030 ya está registrada para este organismo de tránsito.";
    plateMocks.assignPlateToProcedure.mockRejectedValue(
      new ApiError(409, motivo, { detail: motivo, title: "Conflict" }),
    );
    plateMocks.listPlateDetails.mockResolvedValue([]);

    renderSection();
    await screen.findByText("RAD-2026-777");
    await user.click(await screen.findByRole("button", { name: /Asignar placa/i }));
    const input = await screen.findByLabelText(/Placa fuera de rango/i);
    await user.clear(input);
    await user.type(input, "QXU030");
    await user.click(screen.getByRole("button", { name: /^Asignar$/i }));

    expect(await screen.findByText(motivo)).toBeInTheDocument();
  });
});

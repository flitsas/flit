// HU #10218 — Súper-sección Trámites OT: paneles Dashboard/QX, switch modo, 4 estados UI.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { TramitesSuperSection } from "../TramitesSuperSection";
import type { OtClientProcedure, OtProfile } from "@/lib/api/types-ot";

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtProfile: vi.fn(),
  updateOtProfile: vi.fn(),
  updateOtFeatureFlag: vi.fn(),
  fetchOtClientProcedures: vi.fn(),
  approveOtClientProcedure: vi.fn(),
  rejectOtClientProcedure: vi.fn(),
}));

// El panel «Cola QX» monta <QuipuxQueueList/>, que llama fetchQuipuxCola al montar (aunque el
// panel esté oculto). Se mockea aquí para no disparar una llamada real; los helpers puros
// (canRetry/canCancel) se reexportan tal cual.
vi.mock("@/lib/api/admin-transit-office-tenants", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/admin-transit-office-tenants")>()),
  fetchQuipuxCola: vi.fn(),
  retryQuipuxSubmission: vi.fn(),
  cancelQuipuxSubmission: vi.fn(),
}));

import {
  approveOtClientProcedure,
  fetchOtClientProcedures,
  fetchOtProfile,
  rejectOtClientProcedure,
  updateOtProfile,
} from "@/lib/api/admin-ot";
import { fetchQuipuxCola } from "@/lib/api/admin-transit-office-tenants";

const OT_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const PROC_ID = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

const dashboardProfile: OtProfile = {
  operationMode: "dashboard",
  quipuxReadOnly: false,
  transitOfficeId: OT_ID,
  featureFlags: [],
};

const quipuxReadOnlyProfile: OtProfile = {
  operationMode: "quipux",
  quipuxReadOnly: true,
  transitOfficeId: OT_ID,
  featureFlags: [],
};

const sampleProcedure: OtClientProcedure = {
  id: PROC_ID,
  clientTenantId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
  procedureTypeId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
  referenceNumber: "REF-001",
  status: "entregado",
  createdAt: "2026-06-23T12:00:00Z",
};

function renderSection() {
  return render(
    <ToastProvider>
      <TramitesSuperSection transitOfficeId={OT_ID} />
    </ToastProvider>,
  );
}

describe("TramitesSuperSection — HU #10218", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchOtProfile).mockResolvedValue(dashboardProfile);
    vi.mocked(fetchOtClientProcedures).mockResolvedValue({
      data: [sampleProcedure],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    });
    vi.mocked(updateOtProfile).mockResolvedValue(quipuxReadOnlyProfile);
    vi.mocked(approveOtClientProcedure).mockResolvedValue({
      ...sampleProcedure,
      status: "aprobado",
    });
    vi.mocked(rejectOtClientProcedure).mockResolvedValue({
      ...sampleProcedure,
      status: "rechazado",
    });
    vi.mocked(fetchQuipuxCola).mockResolvedValue({
      data: [],
      totalCount: 0,
      page: 1,
      pageSize: 20,
    });
  });

  it("AC1 muestra dos paneles con roles ARIA de pestañas", async () => {
    renderSection();

    expect(await screen.findByRole("tab", { name: /Dashboard FLIT/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /Cola QX/i })).toBeInTheDocument();
    expect(screen.getByRole("tablist", { name: /Paneles de trámites/i })).toBeInTheDocument();
  });

  it("AC2 al activar «Consola en solo lectura» llama PATCH profile con operation_mode quipux", async () => {
    const user = userEvent.setup();
    renderSection();

    await screen.findByRole("switch", { name: /Consola en solo lectura/i });
    await user.click(screen.getByRole("switch", { name: /Consola en solo lectura/i }));

    await waitFor(() => {
      expect(updateOtProfile).toHaveBeenCalledWith({ operationMode: "quipux" });
    });
    expect(await screen.findByRole("tab", { name: /Cola QX/i })).toHaveAttribute(
      "aria-selected",
      "true",
    );
  });

  it("AC3 con la consola en solo lectura no renderiza botones Aprobar/Rechazar", async () => {
    vi.mocked(fetchOtProfile).mockResolvedValue(quipuxReadOnlyProfile);
    renderSection();

    await waitFor(() => {
      expect(screen.getAllByText("REF-001").length).toBeGreaterThan(0);
    });
    expect(screen.queryByRole("button", { name: /Aprobar/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Rechazar/i })).not.toBeInTheDocument();
    expect(screen.getByText(/^Solo lectura$/i)).toBeInTheDocument();
  });

  // HU #10710 — el toggle se llamaba «Modo QX / Integración con cola Quipux», lo que hacía
  // creer que decidía si a la secretaría se le radica por Quipux. Solo pone la consola de
  // este OT-cliente en solo lectura; la radicación se parametriza en el catálogo (DIVIPO).
  it("el toggle nombra su efecto real y no se confunde con la radicación Quipux", async () => {
    renderSection();

    const toggle = await screen.findByRole("switch", { name: /Consola en solo lectura/i });
    expect(toggle).toBeInTheDocument();
    expect(screen.queryByRole("switch", { name: /Modo QX/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/Integración con cola Quipux/i)).not.toBeInTheDocument();
  });

  it("AC4 muestra skeleton mientras carga el perfil", () => {
    vi.mocked(fetchOtProfile).mockImplementation(() => new Promise(() => {}));
    renderSection();
    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();
  });

  it("AC5 inicializa el switch desde GET profile (quipux persistido)", async () => {
    vi.mocked(fetchOtProfile).mockResolvedValue(quipuxReadOnlyProfile);
    renderSection();

    const toggle = await screen.findByRole("switch", { name: /Consola en solo lectura/i });
    expect(toggle).toBeChecked();
    expect(fetchOtProfile).toHaveBeenCalled();
    expect(localStorage.getItem("ot_operation_mode")).toBeNull();
  });

  it("en panel Dashboard, aprobar y confirmar llama approveOtClientProcedure", async () => {
    const user = userEvent.setup();
    renderSection();

    await waitFor(() => {
      expect(screen.getAllByText("REF-001").length).toBeGreaterThan(0);
    });
    await user.click(screen.getByRole("button", { name: /Aprobar/i }));

    const dialog = await screen.findByRole("dialog", { name: /Confirmar aprobación/i });
    await user.click(within(dialog).getByRole("button", { name: /Confirmar/i }));

    await waitFor(() => {
      expect(approveOtClientProcedure).toHaveBeenCalledWith(PROC_ID);
    });
    // UX: tras aprobar, el trámite sale de la cola de pendientes.
    await waitFor(() => {
      expect(screen.queryByText("REF-001")).not.toBeInTheDocument();
    });
  });

  it("en panel Dashboard, rechazar exige motivo antes de confirmar", async () => {
    const user = userEvent.setup();
    renderSection();

    await waitFor(() => {
      expect(screen.getAllByText("REF-001").length).toBeGreaterThan(0);
    });
    await user.click(screen.getByRole("button", { name: /Rechazar/i }));

    const dialog = await screen.findByRole("dialog", { name: /Rechazar trámite/i });
    const confirmBtn = within(dialog).getByRole("button", { name: /Confirmar rechazo/i });
    expect(confirmBtn).toBeDisabled();

    await user.type(dialog.querySelector("textarea")!, "Documentos incompletos");
    expect(confirmBtn).toBeEnabled();
    await user.click(confirmBtn);

    await waitFor(() => {
      expect(rejectOtClientProcedure).toHaveBeenCalledWith(PROC_ID, {
        reason: "Documentos incompletos",
      });
    });
    // UX: tras rechazar, el trámite sale de la cola de pendientes.
    await waitFor(() => {
      expect(screen.queryByText("REF-001")).not.toBeInTheDocument();
    });
  });
});

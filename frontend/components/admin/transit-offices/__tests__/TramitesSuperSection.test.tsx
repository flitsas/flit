// HU #10218 — Súper-sección Trámites OT: paneles Dashboard/QX, switch modo, 4 estados UI.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { TramitesSuperSection } from "../TramitesSuperSection";
import type { OtClientProcedure, OtProfile } from "@/lib/api/types-ot";

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtProfile: vi.fn(),
  updateOtProfile: vi.fn(),
  fetchOtClientProcedures: vi.fn(),
}));

import {
  fetchOtClientProcedures,
  fetchOtProfile,
  updateOtProfile,
} from "@/lib/api/admin-ot";

const OT_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const PROC_ID = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

const dashboardProfile: OtProfile = {
  operationMode: "dashboard",
  quipuxReadOnly: false,
  featureFlags: [],
};

const quipuxReadOnlyProfile: OtProfile = {
  operationMode: "quipux",
  quipuxReadOnly: true,
  featureFlags: [],
};

const sampleProcedure: OtClientProcedure = {
  id: PROC_ID,
  clientTenantId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
  procedureTypeId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
  referenceNumber: "REF-001",
  status: "pending_ot",
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
  });

  it("AC1 muestra dos paneles con roles ARIA de pestañas", async () => {
    renderSection();

    expect(await screen.findByRole("tab", { name: /Dashboard FLIT/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /Cola QX/i })).toBeInTheDocument();
    expect(screen.getByRole("tablist", { name: /Paneles de trámites/i })).toBeInTheDocument();
  });

  it("AC2 al activar Modo QX llama PATCH profile con operation_mode quipux", async () => {
    const user = userEvent.setup();
    renderSection();

    await screen.findByRole("switch", { name: /Modo QX/i });
    await user.click(screen.getByRole("switch", { name: /Modo QX/i }));

    await waitFor(() => {
      expect(updateOtProfile).toHaveBeenCalledWith({ operationMode: "quipux" });
    });
    expect(await screen.findByRole("tab", { name: /Cola QX/i })).toHaveAttribute(
      "aria-selected",
      "true",
    );
  });

  it("AC3 en modo QX solo lectura no renderiza botones Aprobar/Rechazar", async () => {
    vi.mocked(fetchOtProfile).mockResolvedValue(quipuxReadOnlyProfile);
    renderSection();

    await waitFor(() => {
      expect(screen.getAllByText("REF-001").length).toBeGreaterThan(0);
    });
    expect(screen.queryByRole("button", { name: /Aprobar/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Rechazar/i })).not.toBeInTheDocument();
    expect(screen.getByText(/^Solo lectura$/i)).toBeInTheDocument();
  });

  it("AC4 muestra skeleton mientras carga el perfil", () => {
    vi.mocked(fetchOtProfile).mockImplementation(() => new Promise(() => {}));
    renderSection();
    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();
  });

  it("AC5 inicializa el switch desde GET profile (quipux persistido)", async () => {
    vi.mocked(fetchOtProfile).mockResolvedValue(quipuxReadOnlyProfile);
    renderSection();

    const toggle = await screen.findByRole("switch", { name: /Modo QX/i });
    expect(toggle).toBeChecked();
    expect(fetchOtProfile).toHaveBeenCalled();
    expect(localStorage.getItem("ot_operation_mode")).toBeNull();
  });
});

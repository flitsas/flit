// HU #10774 — Cola Quipux real: listado de radicaciones, estados y acciones retry/cancel.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { QuipuxQueueList } from "../QuipuxQueueList";
import type { QuipuxColaItem } from "@/lib/api/admin-transit-office-tenants";

vi.mock("@/lib/api/admin-transit-office-tenants", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/admin-transit-office-tenants")>()),
  fetchQuipuxCola: vi.fn(),
  retryQuipuxSubmission: vi.fn(),
  cancelQuipuxSubmission: vi.fn(),
}));

import {
  cancelQuipuxSubmission,
  fetchQuipuxCola,
  retryQuipuxSubmission,
} from "@/lib/api/admin-transit-office-tenants";

const OT_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

function item(overrides: Partial<QuipuxColaItem>): QuipuxColaItem {
  return {
    id: "s-1",
    procedureInstanceId: "p-1",
    referenceNumber: "TRM-001",
    procedureTypeName: "Traspaso",
    clientTenantName: "Renting S.A.S.",
    documentName: "FLIT_TRM-001",
    status: "pendiente",
    attempts: 0,
    pollCount: 0,
    qxRegisterCode: null,
    qxProcedureCode: null,
    rejectionReason: null,
    createdAt: "2026-07-01T12:00:00Z",
    registeredAt: null,
    lastPolledAt: null,
    completedAt: null,
    updatedAt: null,
    ...overrides,
  };
}

function renderList() {
  return render(
    <ToastProvider>
      <QuipuxQueueList transitOfficeId={OT_ID} />
    </ToastProvider>,
  );
}

function page(items: QuipuxColaItem[]) {
  return { data: items, totalCount: items.length, page: 1, pageSize: 20 };
}

describe("QuipuxQueueList — HU #10774", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(retryQuipuxSubmission).mockResolvedValue({ code: "OK" });
    vi.mocked(cancelQuipuxSubmission).mockResolvedValue({ code: "OK" });
  });

  it("lista las radicaciones con su referencia y estado", async () => {
    vi.mocked(fetchQuipuxCola).mockResolvedValue(
      page([item({ id: "s-1", referenceNumber: "TRM-001", status: "registrado" })]),
    );
    renderList();

    expect(await screen.findByText("TRM-001")).toBeInTheDocument();
    expect(screen.getByText("Registrado")).toBeInTheDocument();
    expect(fetchQuipuxCola).toHaveBeenCalledWith(OT_ID, { page: 1, pageSize: 20 }, expect.anything());
  });

  it("muestra estado vacío cuando no hay radicaciones", async () => {
    vi.mocked(fetchQuipuxCola).mockResolvedValue(page([]));
    renderList();

    expect(await screen.findByText(/no tiene radicaciones/i)).toBeInTheDocument();
  });

  it("solo un `fallido` ofrece Re-encolar; confirmar llama retry y refresca", async () => {
    const user = userEvent.setup();
    vi.mocked(fetchQuipuxCola).mockResolvedValue(
      page([item({ id: "s-fail", status: "fallido", attempts: 5, referenceNumber: "TRM-FAIL" })]),
    );
    renderList();

    await screen.findByText("TRM-FAIL");
    expect(screen.queryByRole("button", { name: /Cancelar/i })).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Re-encolar/i }));

    const dialog = await screen.findByRole("dialog", { name: /re-encolado/i });
    await user.click(within(dialog).getByRole("button", { name: /Confirmar/i }));

    await waitFor(() => {
      expect(retryQuipuxSubmission).toHaveBeenCalledWith(OT_ID, "s-fail");
    });
    // Refresca la cola tras la acción.
    expect(fetchQuipuxCola).toHaveBeenCalledTimes(2);
  });

  it("solo un `pendiente` ofrece Cancelar; confirmar llama cancel", async () => {
    const user = userEvent.setup();
    vi.mocked(fetchQuipuxCola).mockResolvedValue(
      page([item({ id: "s-pend", status: "pendiente", referenceNumber: "TRM-PEND" })]),
    );
    renderList();

    await screen.findByText("TRM-PEND");
    expect(screen.queryByRole("button", { name: /Re-encolar/i })).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Cancelar/i }));

    const dialog = await screen.findByRole("dialog", { name: /cancelación/i });
    await user.click(within(dialog).getByRole("button", { name: /Confirmar/i }));

    await waitFor(() => {
      expect(cancelQuipuxSubmission).toHaveBeenCalledWith(OT_ID, "s-pend");
    });
  });

  it("un desenlace (aprobado) no ofrece acciones", async () => {
    vi.mocked(fetchQuipuxCola).mockResolvedValue(
      page([item({ id: "s-ok", status: "aprobado", referenceNumber: "TRM-OK" })]),
    );
    renderList();

    await screen.findByText("TRM-OK");
    expect(screen.queryByRole("button", { name: /Re-encolar/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Cancelar/i })).not.toBeInTheDocument();
  });
});

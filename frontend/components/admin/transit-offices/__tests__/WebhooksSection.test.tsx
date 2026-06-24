// HU #10219 — Webhooks OT: listado, creación, edición y bitácora.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { WebhooksSection } from "../WebhooksSection";
import type { OtApiCallLog, OtWebhook } from "@/lib/api/types-ot";

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtWebhooks: vi.fn(),
  createOtWebhook: vi.fn(),
  updateOtWebhook: vi.fn(),
  fetchOtApiLogs: vi.fn(),
}));

import {
  createOtWebhook,
  fetchOtApiLogs,
  fetchOtWebhooks,
  updateOtWebhook,
} from "@/lib/api/admin-ot";

const webhook: OtWebhook = {
  id: "wh-1",
  eventType: "vehicle_state_changed",
  targetUrl: "https://hooks.example.com/ot-endpoint",
  isActive: true,
  createdAt: "2026-06-23T10:00:00Z",
};

const log5xx: OtApiCallLog = {
  endpoint: "https://hooks.example.com/ot",
  httpMethod: "POST",
  responseCode: 502,
  durationMs: 120,
  calledAt: "2026-06-23T11:00:00Z",
  payloadHash: "abc123hash",
};

function renderSection() {
  return render(
    <ToastProvider>
      <WebhooksSection />
    </ToastProvider>,
  );
}

describe("WebhooksSection — HU #10219", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchOtWebhooks).mockResolvedValue({ data: [webhook] });
    vi.mocked(fetchOtApiLogs).mockResolvedValue({
      data: [log5xx],
      totalCount: 1,
      page: 1,
      pageSize: 50,
    });
    vi.mocked(createOtWebhook).mockResolvedValue({
      ...webhook,
      id: "wh-new",
      targetUrl: "https://hooks.example.com/new",
    });
    vi.mocked(updateOtWebhook).mockResolvedValue({
      ...webhook,
      targetUrl: "https://hooks.example.com/updated",
    });
  });

  it("AC1 lista webhooks con URL enmascarada", async () => {
    renderSection();
    expect(await screen.findByText("vehicle_state_changed")).toBeInTheDocument();
    expect(screen.getByText("…dpoint")).toBeInTheDocument();
    expect(screen.getByText("Activo")).toBeInTheDocument();
  });

  it("AC2 crear webhook llama POST y muestra en tabla", async () => {
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("vehicle_state_changed");
    await user.click(screen.getByRole("button", { name: /Nuevo webhook/i }));
    await user.type(screen.getByLabelText(/URL destino/i), "https://hooks.example.com/new");
    await user.type(screen.getByLabelText(/Secreto/i), "secret");
    await user.click(screen.getByRole("button", { name: /^Guardar$/i }));
    await waitFor(() => expect(createOtWebhook).toHaveBeenCalled());
  });

  it("AC3 editar webhook llama PATCH sin recargar página", async () => {
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("Editar");
    await user.click(screen.getByRole("button", { name: /Editar/i }));
    const urlInput = screen.getByLabelText(/URL destino/i);
    await user.clear(urlInput);
    await user.type(urlInput, "https://hooks.example.com/updated");
    await user.click(screen.getByRole("button", { name: /^Guardar$/i }));
    await waitFor(() =>
      expect(updateOtWebhook).toHaveBeenCalledWith("wh-1", {
        targetUrl: "https://hooks.example.com/updated",
      }),
    );
  });

  it("AC5 detalle de log muestra payload_hash y nota Ley 1581", async () => {
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("vehicle_state_changed");
    await user.click(screen.getByRole("tab", { name: /Bitácora/i }));
    await screen.findByText("502");
    await user.click(screen.getByText("502"));
    expect(screen.getByText(/abc123hash/)).toBeInTheDocument();
    expect(screen.getByText(/Ley 1581 de 2012/)).toBeInTheDocument();
  });
});

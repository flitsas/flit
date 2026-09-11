import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobsCatalog } from "../JobsCatalog";

const push = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push }),
}));

vi.mock("@/lib/api/admin-ict-job-catalog", () => ({
  fetchIctJobCatalog: vi.fn(),
}));
vi.mock("@/lib/api/admin-ict-job-settings", () => ({
  fetchIctJobSettings: vi.fn(),
}));
vi.mock("@/lib/api/admin-quipux-settings", () => ({
  fetchQuipuxSettings: vi.fn(),
}));

import { fetchIctJobCatalog } from "@/lib/api/admin-ict-job-catalog";
import { fetchIctJobSettings } from "@/lib/api/admin-ict-job-settings";
import { fetchQuipuxSettings } from "@/lib/api/admin-quipux-settings";

describe("JobsCatalog — HU #12514", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchIctJobSettings).mockResolvedValue({
      windowStartHour: 8,
      windowEndHour: 20,
      businessPollSeconds: 45,
      externalPollSeconds: 45,
      orchestratorPollSeconds: 20,
      orchestratorConcurrency: 10,
      orchestratorBatchSize: 50,
      sendPollSeconds: 20,
      sendConcurrency: 5,
      sendBatchSize: 50,
      webhookPollSeconds: 10,
      webhookBatchSize: 50,
      businessBatchSize: 500,
      externalBatchSize: 500,
      updatedAt: null,
      updatedBy: null,
    });
    vi.mocked(fetchQuipuxSettings).mockResolvedValue({
      enabled: false,
      urlLogin: "",
      urlRegisterDocument: "",
      urlValidateStatus: "",
      username: "",
      hasPassword: false,
      consumerCode: "",
      bucket: "",
      s3Prefix: "FLIT/",
      awsRegion: "us-east-1",
      awsAccessKeyId: "",
      hasAwsSecretAccessKey: false,
      officerDocumentType: 3,
      officerDocumentNumber: "",
      registerIntervalMinutes: 15,
      pollIntervalMinutes: 15,
      batchSize: 20,
      maxAttempts: 5,
      maxPolls: 500,
      timeoutSeconds: 60,
      estaCompleta: false,
      updatedAt: null,
    });
  });

  it("lista ICT y Quipux y aclara RUNT vs Confirmación RUNT", async () => {
    vi.mocked(fetchIctJobCatalog).mockResolvedValue([
      {
        key: "orchestrator",
        displayName: "Orchestrator",
        owner: "core-ict",
        types: ["BD", "ENDPOINT_INTERNO", "ENDPOINT_EXTERNO"],
        hasPipelineRuns: true,
        notes: "RUNT",
        lastRun: null,
      },
    ]);
    render(<JobsCatalog />);
    expect(await screen.findByRole("button", { name: "Orchestrator" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "RegisterProcessor" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "StatusPollProcessor" })).toBeInTheDocument();
    expect(screen.getByText(/consultas runt del pre-trámite/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /confirmacion-runt/i })).toHaveAttribute(
      "href",
      "/admin/plataforma/confirmacion-runt",
    );
  });

  it("ICT navega al formulario y Quipux a /admin/quipux", async () => {
    vi.mocked(fetchIctJobCatalog).mockResolvedValue([
      {
        key: "orchestrator",
        displayName: "Orchestrator",
        owner: "core-ict",
        types: ["BD"],
        hasPipelineRuns: true,
        notes: null,
        lastRun: null,
      },
    ]);
    const user = userEvent.setup();
    render(<JobsCatalog />);
    await user.click(await screen.findByRole("button", { name: "Orchestrator" }));
    expect(push).toHaveBeenCalledWith("/admin/jobs/ict");
    await user.click(screen.getByRole("button", { name: "RegisterProcessor" }));
    expect(push).toHaveBeenCalledWith("/admin/quipux");
  });

  it("muestra estado de error sin listar parámetros", async () => {
    vi.mocked(fetchIctJobCatalog).mockRejectedValue(new Error("net"));
    render(<JobsCatalog />);
    expect(await screen.findByRole("alert")).toHaveTextContent(/no se pudo cargar/i);
    expect(screen.queryByRole("button", { name: "Orchestrator" })).not.toBeInTheDocument();
  });
});

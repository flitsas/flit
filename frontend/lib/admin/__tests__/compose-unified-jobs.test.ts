import { describe, expect, it } from "vitest";
import { composeUnifiedJobs } from "../compose-unified-jobs";
import type { IctJobCatalogItem } from "@/lib/api/admin-ict-job-catalog";
import type { IctJobSettings } from "@/lib/api/admin-ict-job-settings";
import type { QuipuxSettings } from "@/lib/api/admin-quipux-settings";

const orch: IctJobCatalogItem = {
  key: "orchestrator",
  displayName: "Orchestrator",
  owner: "core-ict",
  types: ["BD", "ENDPOINT_INTERNO", "ENDPOINT_EXTERNO"],
  hasPipelineRuns: true,
  notes: "Consultas RUNT/familia. No es Lambda ni confirmacion-runt.",
  lastRun: { outcome: "ok", startedAt: "2026-09-11T15:00:00Z", durationMs: 12 },
};

const settings = {
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
} satisfies IctJobSettings;

const quipux = {
  enabled: true,
  urlLogin: "https://qx",
  urlRegisterDocument: "https://qx/r",
  urlValidateStatus: "https://qx/s",
  username: "flit",
  hasPassword: true,
  consumerCode: "1",
  bucket: "b",
  s3Prefix: "FLIT/",
  awsRegion: "us-east-1",
  awsAccessKeyId: "A",
  hasAwsSecretAccessKey: true,
  officerDocumentType: 3,
  officerDocumentNumber: "9",
  registerIntervalMinutes: 15,
  pollIntervalMinutes: 10,
  batchSize: 20,
  maxAttempts: 5,
  maxPolls: 500,
  timeoutSeconds: 60,
  estaCompleta: true,
  updatedAt: null,
} satisfies QuipuxSettings;

describe("composeUnifiedJobs — HU #12514", () => {
  it("une ICT y Quipux sin reimplementar secretos", () => {
    const rows = composeUnifiedJobs([orch], settings, quipux);
    expect(rows.map((r) => r.displayName)).toEqual([
      "Consultas RUNT",
      "Radicar en Quipux",
      "Consultar estado Quipux",
    ]);
    expect(rows[0].technicalName).toBe("Orchestrator");
    expect(rows[0].owner).toBe("core-ict");
    expect(rows[0].module).toBe("ICT");
    expect(rows[0].intervalLabel).toBe("Cada 20 s");
    expect(rows[0].lastRunPrimary).toBe("Completado");
    expect(rows[1].intervalLabel).toBe("Cada 15 min");
    expect(rows[1].enabledLabel).toBe("Encendido");
    expect(JSON.stringify(rows)).not.toContain("password");
    expect(JSON.stringify(rows)).not.toContain("awsSecret");
  });
});

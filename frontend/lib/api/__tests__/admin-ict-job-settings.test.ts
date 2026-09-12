import { describe, expect, it } from "vitest";
import {
  toIctJobSettingsWrite,
  validateIctJobSettings,
  type IctJobSettings,
} from "../admin-ict-job-settings";

function settings(overrides: Partial<IctJobSettings> = {}): IctJobSettings {
  return {
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
    updatedAt: "2026-09-11T15:00:00Z",
    updatedBy: "11111111-1111-1111-1111-111111111111",
    ...overrides,
  };
}

describe("validateIctJobSettings — HU #12123 AC2", () => {
  it("acepta el contrato vigente del backend", () => {
    expect(validateIctJobSettings(toIctJobSettingsWrite(settings()))).toEqual({});
  });

  it("rechaza polls menores a 1 y ventana incoherente", () => {
    const errors = validateIctJobSettings(
      toIctJobSettingsWrite(
        settings({ businessPollSeconds: 0, windowStartHour: 8, windowEndHour: 8 }),
      ),
    );
    expect(errors.businessPollSeconds).toBeTruthy();
    expect(errors.windowEndHour).toBeTruthy();
  });

  it("no incluye secretos en el payload de escritura", () => {
    const body = toIctJobSettingsWrite(settings());
    expect(body).not.toHaveProperty("password");
    expect(body).not.toHaveProperty("connectionString");
    expect(Object.keys(body).sort()).toEqual(
      [
        "businessBatchSize",
        "businessPollSeconds",
        "externalBatchSize",
        "externalPollSeconds",
        "orchestratorBatchSize",
        "orchestratorConcurrency",
        "orchestratorPollSeconds",
        "sendBatchSize",
        "sendConcurrency",
        "sendPollSeconds",
        "webhookBatchSize",
        "webhookPollSeconds",
        "windowEndHour",
        "windowStartHour",
      ].sort(),
    );
  });
});

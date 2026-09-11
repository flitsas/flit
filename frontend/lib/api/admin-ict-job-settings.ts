// Cliente de ict.job_settings (HU #12123 / API HU #12512). SuperAdmin, sin secretos.
import { apiFetch } from "./client";

export interface IctJobSettings {
  windowStartHour: number;
  windowEndHour: number;
  businessPollSeconds: number;
  externalPollSeconds: number;
  orchestratorPollSeconds: number;
  orchestratorConcurrency: number;
  orchestratorBatchSize: number;
  sendPollSeconds: number;
  sendConcurrency: number;
  sendBatchSize: number;
  webhookPollSeconds: number;
  webhookBatchSize: number;
  businessBatchSize: number;
  externalBatchSize: number;
  updatedAt: string | null;
  updatedBy: string | null;
}

export type IctJobSettingsWrite = Omit<IctJobSettings, "updatedAt" | "updatedBy">;

export type IctJobSettingsFieldErrors = Partial<Record<keyof IctJobSettingsWrite, string>>;

const base = "/api/v1/admin/ict/job-settings";

export function fetchIctJobSettings(signal?: AbortSignal): Promise<IctJobSettings> {
  return apiFetch<IctJobSettings>(base, { signal });
}

export function saveIctJobSettings(body: IctJobSettingsWrite): Promise<IctJobSettings> {
  return apiFetch<IctJobSettings>(base, { method: "PUT", body });
}

export function toIctJobSettingsWrite(s: IctJobSettings): IctJobSettingsWrite {
  return {
    windowStartHour: s.windowStartHour,
    windowEndHour: s.windowEndHour,
    businessPollSeconds: s.businessPollSeconds,
    externalPollSeconds: s.externalPollSeconds,
    orchestratorPollSeconds: s.orchestratorPollSeconds,
    orchestratorConcurrency: s.orchestratorConcurrency,
    orchestratorBatchSize: s.orchestratorBatchSize,
    sendPollSeconds: s.sendPollSeconds,
    sendConcurrency: s.sendConcurrency,
    sendBatchSize: s.sendBatchSize,
    webhookPollSeconds: s.webhookPollSeconds,
    webhookBatchSize: s.webhookBatchSize,
    businessBatchSize: s.businessBatchSize,
    externalBatchSize: s.externalBatchSize,
  };
}

/** Clamps alineados a IctJobSettingsRules (core-api). Si hay errores, el PUT no debe viajar. */
export function validateIctJobSettings(form: IctJobSettingsWrite): IctJobSettingsFieldErrors {
  const errors: IctJobSettingsFieldErrors = {};

  hour(form.windowStartHour, "windowStartHour", errors);
  hour(form.windowEndHour, "windowEndHour", errors);
  if (
    !errors.windowStartHour &&
    !errors.windowEndHour &&
    form.windowStartHour >= form.windowEndHour
  ) {
    errors.windowEndHour = "La ventana debe cumplir inicio < fin (hora fin exclusiva, sin overnight).";
  }

  poll(form.businessPollSeconds, "businessPollSeconds", errors);
  poll(form.externalPollSeconds, "externalPollSeconds", errors);
  poll(form.orchestratorPollSeconds, "orchestratorPollSeconds", errors);
  poll(form.sendPollSeconds, "sendPollSeconds", errors);
  poll(form.webhookPollSeconds, "webhookPollSeconds", errors);

  concurrency(form.orchestratorConcurrency, "orchestratorConcurrency", errors);
  concurrency(form.sendConcurrency, "sendConcurrency", errors);

  batch(form.orchestratorBatchSize, "orchestratorBatchSize", errors);
  batch(form.sendBatchSize, "sendBatchSize", errors);
  batch(form.webhookBatchSize, "webhookBatchSize", errors);
  batch(form.businessBatchSize, "businessBatchSize", errors);
  batch(form.externalBatchSize, "externalBatchSize", errors);

  return errors;
}

function hour(
  value: number,
  field: keyof IctJobSettingsWrite,
  errors: IctJobSettingsFieldErrors,
): void {
  if (!Number.isInteger(value) || value < 0 || value > 23) {
    errors[field] = "La hora debe estar entre 0 y 23.";
  }
}

function poll(
  value: number,
  field: keyof IctJobSettingsWrite,
  errors: IctJobSettingsFieldErrors,
): void {
  if (!Number.isInteger(value) || value < 1 || value > 3600) {
    errors[field] = "El intervalo debe estar entre 1 y 3600 segundos.";
  }
}

function concurrency(
  value: number,
  field: keyof IctJobSettingsWrite,
  errors: IctJobSettingsFieldErrors,
): void {
  if (!Number.isInteger(value) || value < 1 || value > 100) {
    errors[field] = "La concurrencia debe estar entre 1 y 100.";
  }
}

function batch(
  value: number,
  field: keyof IctJobSettingsWrite,
  errors: IctJobSettingsFieldErrors,
): void {
  if (!Number.isInteger(value) || value < 1 || value > 5000) {
    errors[field] = "El lote debe estar entre 1 y 5000.";
  }
}

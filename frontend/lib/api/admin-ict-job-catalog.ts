import { apiFetch } from "./client";

export interface IctJobLastRun {
  outcome: string;
  startedAt: string;
  durationMs: number;
}

export interface IctJobCatalogItem {
  key: string;
  displayName: string;
  owner: string;
  types: string[];
  hasPipelineRuns: boolean;
  notes: string | null;
  lastRun: IctJobLastRun | null;
}

export function fetchIctJobCatalog(signal?: AbortSignal): Promise<IctJobCatalogItem[]> {
  return apiFetch<IctJobCatalogItem[]>("/api/v1/admin/ict/jobs", { signal });
}

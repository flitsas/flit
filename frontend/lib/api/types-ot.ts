/** Tipos del módulo Administración OT (HU #10215 / #10217 / #10218). */

export type OtOperationMode = "dashboard" | "quipux";

export interface OtFeatureFlag {
  id: string;
  flagKey: string;
  isEnabled: boolean;
  config: string;
}

export interface OtProfile {
  operationMode: OtOperationMode;
  quipuxReadOnly: boolean;
  featureFlags: OtFeatureFlag[];
}

export interface UpdateOtProfileRequest {
  operationMode?: OtOperationMode;
}

export interface OtClientProcedure {
  id: string;
  clientTenantId: string;
  procedureTypeId: string;
  referenceNumber: string;
  status: string;
  transitOfficeId?: string | null;
  createdAt: string;
  submittedAt?: string | null;
}

export interface OtClientProcedurePagedResult {
  data: OtClientProcedure[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface OtClientProceduresParams {
  status?: string;
  procedureTypeId?: string;
  page?: number;
  pageSize?: number;
}

export interface OtWebhook {
  id: string;
  eventType: string;
  targetUrl: string;
  isActive: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

export interface CreateOtWebhookRequest {
  eventType: string;
  targetUrl: string;
  secret: string;
}

export interface UpdateOtWebhookRequest {
  targetUrl?: string;
  isActive?: boolean;
}

export interface OtWebhooksListResult {
  data: OtWebhook[];
}

export interface OtApiCallLog {
  endpoint: string;
  httpMethod: string;
  responseCode?: number | null;
  durationMs?: number | null;
  calledAt: string;
  correlationId?: string | null;
  payloadHash: string;
}

export interface OtApiLogsPagedResult {
  data: OtApiCallLog[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface OtApiLogsParams {
  direction?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

export interface RejectOtClientProcedureRequest {
  reason: string;
}

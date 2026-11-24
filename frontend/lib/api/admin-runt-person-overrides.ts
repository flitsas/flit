// Cliente tipado — SuperAdmin → Overrides de persona RUNT por tenant (HU #14642).
// Contrato: GET/POST/PATCH /api/v1/superadmin/companies/{tenantId}/runt-person-overrides
// Requiere SuperAdminPolicy (token con rol superadmin).
import { apiBaseForPageRoot, sessionAwareBase } from "@/lib/api/base-url";
import { getToken } from "./client";

/** Tipos de documento que acepta el override (DDL HU #14642). */
export type OverrideDocumentType = "CC" | "CE" | "TI" | "PAS" | "PPT";

export interface RuntPersonOverride {
  id: string;
  tenantId: string;
  documentType: OverrideDocumentType;
  documentNumber: string;
  firstName: string;
  secondName: string | null;
  firstLastName: string;
  secondLastName: string | null;
  citizenStatus: string | null;
  driverStatus: string | null;
  hasActiveLicense: boolean | null;
  licenseCategories: string | null;
  hasPendingFines: boolean | null;
  nroPazYSalvo: string | null;
  reason: string;
  isActive: boolean;
  expiresAt: string | null;
  createdAt: string;
  updatedAt: string | null;
  createdBy: string | null;
}

export interface CreateRuntPersonOverrideInput {
  documentType: OverrideDocumentType;
  documentNumber: string;
  firstName: string;
  secondName?: string;
  firstLastName: string;
  secondLastName?: string;
  citizenStatus?: string;
  driverStatus?: string;
  hasActiveLicense?: boolean;
  licenseCategories?: string;
  hasPendingFines?: boolean;
  nroPazYSalvo?: string;
  reason: string;
  expiresAt?: string;
}

export interface UpdateRuntPersonOverrideInput {
  firstName?: string;
  secondName?: string;
  firstLastName?: string;
  secondLastName?: string;
  citizenStatus?: string;
  driverStatus?: string;
  hasActiveLicense?: boolean;
  licenseCategories?: string;
  hasPendingFines?: boolean;
  nroPazYSalvo?: string;
  reason?: string;
  isActive?: boolean;
  expiresAt?: string | null;
}

// ── HTTP helper (mismo patrón que superadmin-client.ts) ───────────────────────

const BASE_URL = apiBaseForPageRoot(
  process.env.NEXT_PUBLIC_API_BASE_URL ?? process.env.NEXT_PUBLIC_API_URL ?? "",
);

function resolveBase(): string {
  return (
    sessionAwareBase(BASE_URL) ||
    (typeof window !== "undefined" ? window.location.origin : "http://localhost:3000")
  );
}

class OverridesApiError extends Error {
  constructor(
    readonly status: number,
    readonly body: unknown,
    msg: string,
  ) {
    super(msg);
    this.name = "OverridesApiError";
  }
}

async function req<T>(path: string, init?: RequestInit): Promise<T> {
  const token = typeof window !== "undefined" ? getToken() : null;
  const res = await fetch(new URL(path, resolveBase()).toString(), {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...init?.headers,
    },
  });
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    let body: unknown = null;
    try {
      body = text ? JSON.parse(text) : null;
    } catch {
      body = text || null;
    }
    const detail = (body as { detail?: unknown })?.detail;
    throw new OverridesApiError(
      res.status,
      body,
      typeof detail === "string" && detail
        ? detail
        : `${res.status} ${res.statusText}`,
    );
  }
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  if (!text.trim()) return undefined as T;
  return JSON.parse(text) as T;
}

// ── Endpoints ─────────────────────────────────────────────────────────────────

function base(tenantId: string): string {
  return `/api/v1/superadmin/companies/${encodeURIComponent(tenantId)}/runt-person-overrides`;
}

/** GET — lista de overrides del tenant, con filtro opcional por tipo de documento. */
export function listRuntPersonOverrides(
  tenantId: string,
  documentType?: OverrideDocumentType | "",
  signal?: AbortSignal,
): Promise<RuntPersonOverride[]> {
  const url = new URL(base(tenantId), resolveBase());
  if (documentType) url.searchParams.set("documentType", documentType);
  return req<RuntPersonOverride[]>(url.pathname + url.search, { signal });
}

/** POST — crea un override para el tenant. */
export function createRuntPersonOverride(
  tenantId: string,
  input: CreateRuntPersonOverrideInput,
): Promise<RuntPersonOverride> {
  return req<RuntPersonOverride>(base(tenantId), {
    method: "POST",
    body: JSON.stringify(input),
  });
}

/** PATCH — actualiza un override existente. */
export function updateRuntPersonOverride(
  tenantId: string,
  id: string,
  input: UpdateRuntPersonOverrideInput,
): Promise<RuntPersonOverride> {
  return req<RuntPersonOverride>(`${base(tenantId)}/${encodeURIComponent(id)}`, {
    method: "PATCH",
    body: JSON.stringify(input),
  });
}

/** Desactiva un override (PATCH isActive=false). */
export function deactivateRuntPersonOverride(
  tenantId: string,
  id: string,
): Promise<RuntPersonOverride> {
  return updateRuntPersonOverride(tenantId, id, { isActive: false });
}

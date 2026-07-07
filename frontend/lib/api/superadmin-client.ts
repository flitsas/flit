import type {
  ApplyTemplateFieldsRequest,
  ProcedureTypeSummary,
  CreateProcedureTypeRequest,
  ConformationRuleItem,
  ProcedureStep,
  ProcedureStepInput,
  ValidationResult,
  ProcedureEntity,
  ExternalDataSource,
  ConsultationTemplate,
} from './types/procedure-parametrization';
import { getToken } from './client';

export interface RbacModule {
  id: string;
  code: string;
  name: string;
  description: string | null;
  sortOrder: number;
  isActive: boolean;
  permissionCount: number;
  createdAt: string;
}

export interface RbacPermission {
  id: string;
  moduleId: string;
  slug: string;
  name: string;
  action: string;
  description: string | null;
  isActive: boolean;
}

/** Fila del catálogo GLOBAL de roles (HU #10505 / #10509) — GET /superadmin/roles?targetEntityType=. */
export interface RbacRole {
  id: string;
  code: string;
  name: string;
  description: string | null;
  isSystem: boolean;
  permissionCount: number;
  createdAt: string;
}

/** Tipo de entidad a la que aplica un rol del catálogo global. */
export type RoleTargetEntityType = "COMPANY" | "TRANSIT_OFFICE";

/** Detalle completo de un rol (respuesta de PUT .../permissions) — incluye permisos otorgados. */
export interface RbacRoleDetail {
  id: string;
  targetEntityType: RoleTargetEntityType;
  code: string;
  name: string;
  description: string | null;
  isSystem: boolean;
  isActive: boolean;
  permissions: { id: string; slug: string; name: string }[];
}

export interface TenantModuleGrantItem {
  tenantId: string;
  tenantName: string;
}

export interface CompanyItem {
  id: string;
  nit: string;
  razonSocial: string;
  estadoActivo: boolean;
}

// Misma resolución de base que lib/api/client.ts: sin env en dev local → origen del
// frontend (rewrites Next → core-api :4003).
const BASE_URL =
  process.env.NEXT_PUBLIC_API_BASE_URL ?? process.env.NEXT_PUBLIC_API_URL ?? '';

function resolveBaseUrl(): string {
  return (
    BASE_URL ||
    (typeof window !== 'undefined' ? window.location.origin : 'http://localhost:3000')
  );
}

const JSON_HEADERS: HeadersInit = {
  'Content-Type': 'application/json',
};

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const token = typeof window !== 'undefined' ? getToken() : null;
  // Path absoluto → toma el ORIGEN del base e ignora su path (evita duplicar /api/v1).
  // HU #10508: la policy real es el JWT SuperAdmin (Authorization: Bearer); el header
  // X-Flit-SuperAdmin quedó obsoleto y el backend ya no lo lee.
  const res = await fetch(new URL(path, resolveBaseUrl()).toString(), {
    ...init,
    headers: {
      ...JSON_HEADERS,
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...init?.headers,
    },
  });
  if (!res.ok) {
    const body = await res.text().catch(() => '');
    throw new Error(`${res.status} ${res.statusText}${body ? ': ' + body : ''}`);
  }

  if (res.status === 204) {
    return undefined as T;
  }

  const contentLength = res.headers.get('content-length');
  if (contentLength === '0') {
    return undefined as T;
  }

  const text = await res.text();
  if (!text.trim()) {
    return undefined as T;
  }

  return JSON.parse(text) as T;
}

export const superadminClient = {
  listProcedureTypes: () =>
    request<ProcedureTypeSummary[]>('/api/v1/superadmin/procedure-types'),

  createProcedureType: (body: CreateProcedureTypeRequest) =>
    request<ProcedureTypeSummary>('/api/v1/superadmin/procedure-types', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  getProcedureType: (id: string) =>
    request<ProcedureTypeSummary>(`/api/v1/superadmin/procedure-types/${id}`),

  getConformationRules: (id: string) =>
    request<ConformationRuleItem[]>(
      `/api/v1/superadmin/procedure-types/${id}/conformation-rules`,
    ),

  updateConformationRules: (id: string, rules: ConformationRuleItem[]) =>
    request<ConformationRuleItem[]>(
      `/api/v1/superadmin/procedure-types/${id}/conformation-rules`,
      { method: 'PUT', body: JSON.stringify(rules) },
    ),

  getSteps: (id: string) =>
    request<ProcedureStep[]>(`/api/v1/superadmin/procedure-types/${id}/steps`),

  updateSteps: (id: string, body: ProcedureStepInput[]) =>
    request<ProcedureStep[]>(`/api/v1/superadmin/procedure-types/${id}/steps`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),

  validate: (id: string) =>
    request<ValidationResult>(`/api/v1/superadmin/procedure-types/${id}/validate`, {
      method: 'POST',
    }),

  publish: (id: string) =>
    request<ProcedureTypeSummary>(`/api/v1/superadmin/procedure-types/${id}/publish`, {
      method: 'POST',
    }),

  listProcedureEntities: () =>
    request<ProcedureEntity[]>('/api/v1/superadmin/procedure-entities'),

  listExternalDataSources: () =>
    request<ExternalDataSource[]>('/api/v1/superadmin/external-data-sources'),

  listConsultationTemplates: () =>
    request<ConsultationTemplate[]>('/api/v1/superadmin/consultation-templates'),

  applyTemplateFields: (templateId: string, payload: ApplyTemplateFieldsRequest) =>
    request<void>(
      `/api/v1/superadmin/consultation-templates/${templateId}/apply-fields`,
      { method: 'POST', body: JSON.stringify(payload) },
    ),

  // Módulos RBAC
  listModules: () =>
    request<RbacModule[]>('/api/v1/superadmin/modules'),
  createModule: (body: { code: string; name: string; description?: string; sortOrder?: number }) =>
    request<RbacModule>('/api/v1/superadmin/modules', { method: 'POST', body: JSON.stringify(body) }),
  activateModule: (id: string) =>
    request<void>(`/api/v1/superadmin/modules/${id}/activate`, { method: 'PATCH' }),
  deactivateModule: (id: string) =>
    request<void>(`/api/v1/superadmin/modules/${id}/deactivate`, { method: 'PATCH' }),
  deleteModule: (id: string) =>
    request<void>(`/api/v1/superadmin/modules/${id}`, { method: 'DELETE' }),
  listModuleGrants: (moduleId: string) =>
    request<TenantModuleGrantItem[]>(`/api/v1/superadmin/modules/${moduleId}/grants`),
  grantModuleToTenant: (moduleId: string, tenantId: string) =>
    request<void>(`/api/v1/superadmin/modules/${moduleId}/grants/${tenantId}`, { method: 'POST' }),
  revokeModuleFromTenant: (moduleId: string, tenantId: string) =>
    request<void>(`/api/v1/superadmin/modules/${moduleId}/grants/${tenantId}`, { method: 'DELETE' }),

  // Permisos RBAC
  listPermissions: (moduleId: string) =>
    request<RbacPermission[]>(`/api/v1/superadmin/permissions?moduleId=${moduleId}`),
  createPermission: (body: { moduleId: string; slug: string; name: string; action?: string; description?: string }) =>
    request<RbacPermission>('/api/v1/superadmin/permissions', { method: 'POST', body: JSON.stringify(body) }),
  deactivatePermission: (id: string) =>
    request<void>(`/api/v1/superadmin/permissions/${id}/deactivate`, { method: 'PATCH' }),
  deletePermission: (id: string) =>
    request<void>(`/api/v1/superadmin/permissions/${id}`, { method: 'DELETE' }),

  // Roles RBAC — catálogo GLOBAL por tipo de entidad (HU #10505 gobernanza HU #10508/#10509).
  listRoles: (targetEntityType: RoleTargetEntityType) =>
    request<RbacRole[]>(`/api/v1/superadmin/roles?targetEntityType=${targetEntityType}`),
  createRole: (body: { targetEntityType: RoleTargetEntityType; code: string; name: string; description?: string }) =>
    request<{ id: string }>('/api/v1/superadmin/roles', { method: 'POST', body: JSON.stringify(body) }),
  deleteRole: (id: string) =>
    request<void>(`/api/v1/superadmin/roles/${id}`, { method: 'DELETE' }),
  setRolePermissions: (id: string, permissionIds: string[]) =>
    request<RbacRoleDetail>(`/api/v1/superadmin/roles/${id}/permissions`, {
      method: 'PUT',
      body: JSON.stringify({ permissionIds }),
    }),
  activateRole: (id: string) =>
    request<void>(`/api/v1/superadmin/roles/${id}/activate`, { method: 'PATCH' }),
  deactivateRole: (id: string) =>
    request<void>(`/api/v1/superadmin/roles/${id}/deactivate`, { method: 'PATCH' }),

  // Compañías (para el picker de tenant en gestión de roles)
  listCompanies: () =>
    request<{ data: CompanyItem[] }>('/api/v1/admin/companies/index'),
};

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

export interface RbacRole {
  id: string;
  tenantId: string;
  code: string;
  name: string;
  description: string | null;
  isSystem: boolean;
  permissionCount: number;
  createdAt: string;
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

// Misma resolución de base que lib/api/client.ts: el CD inyecta NEXT_PUBLIC_API_BASE_URL
// (api.<env>.flitsas.online). Compat con NEXT_PUBLIC_API_URL (local) y localhost. ANTES leía
// solo NEXT_PUBLIC_API_URL → vacío en DEV → caía al mismo origen (Next.js) → 500.
const BASE_URL =
  process.env.NEXT_PUBLIC_API_BASE_URL ?? process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:4002';

const SUPERADMIN_HEADERS: HeadersInit = {
  'Content-Type': 'application/json',
  'X-Flit-SuperAdmin': 'true',
};

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const token = typeof window !== 'undefined' ? getToken() : null;
  // Path absoluto → toma el ORIGEN de BASE_URL e ignora su path (evita duplicar /api/v1).
  const res = await fetch(new URL(path, BASE_URL).toString(), {
    ...init,
    headers: {
      ...SUPERADMIN_HEADERS,
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

  // Roles RBAC (SuperAdmin scope — tenantId requerido)
  listRoles: (tenantId: string) =>
    request<RbacRole[]>(`/api/v1/superadmin/roles?tenantId=${tenantId}`),
  createRole: (body: { tenantId: string; code: string; name: string; description?: string }) =>
    request<{ id: string }>('/api/v1/superadmin/roles', { method: 'POST', body: JSON.stringify(body) }),
  deleteRole: (id: string) =>
    request<void>(`/api/v1/superadmin/roles/${id}`, { method: 'DELETE' }),
  setRolePermissions: (id: string, tenantId: string, permissionIds: string[]) =>
    request<unknown>(`/api/v1/superadmin/roles/${id}/permissions`, {
      method: 'PUT',
      body: JSON.stringify({ tenantId, permissionIds }),
    }),

  // Compañías (para el picker de tenant en gestión de roles)
  listCompanies: () =>
    request<{ data: CompanyItem[] }>('/api/v1/admin/companies/index'),
};

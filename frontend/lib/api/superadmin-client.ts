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
  ConformationProfile,
  UpdateConformationProfileRequest,
  UpdateProcedureTypeRequest,
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

/** Tipo de entidad a la que aplica un rol del catálogo global. */
export type RoleTargetEntityType = "COMPANY" | "TRANSIT_OFFICE";

/** Fila del catálogo GLOBAL de roles (HU #10505 / #10509) — GET /superadmin/roles?targetEntityType=. */
export interface RbacRole {
  id: string;
  targetEntityType: RoleTargetEntityType;
  code: string;
  name: string;
  description: string | null;
  isSystem: boolean;
  isActive: boolean;
  permissionCount: number;
  createdAt: string;
}

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

/**
 * Error de la API de superadmin con el CUERPO ya interpretado.
 *
 * Antes se lanzaba un `Error` plano con el JSON del servidor concatenado al mensaje, así que un
 * llamador que necesitara un dato de la respuesta —la lista de impedimentos de la barrera, por
 * ejemplo— tenía que volver a parsear un texto que ya venía estructurado.
 */
export class SuperadminApiError extends Error {
  constructor(
    readonly status: number,
    readonly body: unknown,
    mensaje: string,
  ) {
    super(mensaje);
    this.name = 'SuperadminApiError';
  }
}

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
    const texto = await res.text().catch(() => '');
    let cuerpo: unknown = null;
    try {
      cuerpo = texto ? JSON.parse(texto) : null;
    } catch {
      cuerpo = texto || null;
    }
    // `detail` es el campo de ProblemDetails, que es lo que el backend devuelve en los errores
    // explicados; sin él se cae al texto crudo, que al menos dice el código de estado.
    const detalle = (cuerpo as { detail?: unknown })?.detail;
    throw new SuperadminApiError(
      res.status,
      cuerpo,
      typeof detalle === 'string' && detalle
        ? detalle
        : `${res.status} ${res.statusText}${texto ? ': ' + texto : ''}`,
    );
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

  // ── Configurador de tipos de trámite (ADR-0050) ────────────────────────────

  /**
   * Corrige la identidad del tipo: nombre, descripción, familia y si está activo.
   *
   * Funciona sobre tipos PUBLICADOS y sube su versión. El nombre es el rótulo legal del mandato y
   * de la portada del expediente, así que corregirlo no es cosmético; la familia gobierna
   * clasificación, filtros y causales de rechazo.
   */
  updateProcedureType: (id: string, body: UpdateProcedureTypeRequest) =>
    request<ProcedureTypeSummary>(`/api/v1/superadmin/procedure-types/${id}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),

  /**
   * Retira el tipo del catálogo. NO borra: lo archiva, y se niega con 409 si tiene trámites — de
   * otro modo quedarían apuntando a un tipo archivado. Al archivar se apaga su barrera.
   */
  retirar: (id: string) =>
    request<void>(`/api/v1/superadmin/procedure-types/${id}`, { method: 'DELETE' }),

  archive: (id: string) =>
    request<ProcedureTypeSummary>(`/api/v1/superadmin/procedure-types/${id}/archive`, {
      method: 'POST',
    }),

  /**
   * Mueve la barrera de operación: si el gestor puede elegir este tipo al crear un trámite.
   *
   * Encender exige que el tipo esté listo (publicado, activo, con pasos y sin errores de
   * validación); si no lo está responde 422 con la lista de lo que falta. Apagar no exige nada.
   */
  setWizardEnabled: (id: string, enabled: boolean) =>
    request<ProcedureTypeSummary>(
      `/api/v1/superadmin/procedure-types/${id}/wizard-enabled`,
      { method: 'PUT', body: JSON.stringify({ enabled }) },
    ),

  /** Perfil completo: capacidades, actores, fuentes externas y matriz documental. */
  getConformationProfile: (id: string) =>
    request<ConformationProfile>(
      `/api/v1/superadmin/procedure-types/${id}/conformation-profile`,
    ),

  /** Guarda el perfil. Las listas ausentes no se tocan; sobre un publicado sube la versión. */
  updateConformationProfile: (id: string, body: UpdateConformationProfileRequest) =>
    request<ConformationProfile>(
      `/api/v1/superadmin/procedure-types/${id}/conformation-profile`,
      { method: 'PUT', body: JSON.stringify(body) },
    ),

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
  getRole: (id: string) =>
    request<RbacRoleDetail>(`/api/v1/superadmin/roles/${id}`),
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

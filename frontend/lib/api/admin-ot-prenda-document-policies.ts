import { apiFetch } from "@/lib/api/client";
import type { OtPrendaDocumentPolicyCompany } from "@/lib/api/types";

const otBase = (transitOfficeId: string) =>
  `/api/v1/admin/transit-offices/${transitOfficeId}/prenda-document-policies`;

/** GET — compañías con grant al OT y si la prenda es opcional. */
export function fetchOtPrendaDocumentPoliciesForOffice(
  transitOfficeId: string,
  signal?: AbortSignal,
): Promise<OtPrendaDocumentPolicyCompany[]> {
  return apiFetch<OtPrendaDocumentPolicyCompany[]>(otBase(transitOfficeId), { signal });
}

/** PUT — opt-out de prenda obligatoria para una compañía en este OT. */
export function setOtPrendaDocumentPolicyForOffice(
  transitOfficeId: string,
  tenantId: string,
  documentOptional: boolean,
): Promise<void> {
  return apiFetch<void>(`${otBase(transitOfficeId)}/${tenantId}`, {
    method: "PUT",
    body: { documentOptional },
  });
}

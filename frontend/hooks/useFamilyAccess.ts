"use client";

import { useMemo } from "react";
import { getToken } from "@/lib/api/client";
import { decodeJwtPayload } from "@/lib/auth/jwt";
import type { ProcedureFamily } from "@/lib/api/types/procedure-parametrization";
import {
  PROCEDURE_FAMILIES,
  creatableFamilies,
  readableFamilies,
} from "@/lib/tramites/familyPermissions";

export interface FamilyAccess {
  readable: readonly ProcedureFamily[];
  creatable: readonly ProcedureFamily[];
  canRead: (family: ProcedureFamily) => boolean;
  canCreate: (family: ProcedureFamily) => boolean;
}

/**
 * Ver / Crear por familia desde el claim `permissions` (HU #13665). Sin token legible no se restringe nada: sin
 * sesión el shell ya envía al login y la API decide de todos modos (`family_permission_denied`); el filtro de la UI
 * solo evita ofrecer lo que la API va a rechazar.
 */
export function useFamilyAccess(): FamilyAccess {
  return useMemo(() => {
    const claims = decodeJwtPayload(getToken());
    const readable = claims ? readableFamilies(claims) : PROCEDURE_FAMILIES;
    const creatable = claims ? creatableFamilies(claims) : PROCEDURE_FAMILIES;
    return {
      readable,
      creatable,
      canRead: (f) => readable.includes(f),
      canCreate: (f) => creatable.includes(f),
    };
  }, []);
}

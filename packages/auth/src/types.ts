// Tipos públicos de @flit/auth (contrato de plataforma v1, §8).

/** Usuario de la sesión. Sale de los claims del token del producto; el token nunca sale del servidor. */
export interface SessionUser {
  id: string;
  email: string;
  /** `aud` del token de la sesión. */
  product: string;
  /** `dom`: "flit" o el host de la red. */
  domain: string;
  tenant: {
    id: string;
    name: string;
    nit: string;
    type: string;
    entityType: "COMPANY" | "TRANSIT_OFFICE";
    parentId: string | null;
    isGroupParent: boolean;
  };
  roles: { id: string; code: string }[];
  permissions: string[];
  isSuperAdmin: boolean;
  /** `exp` del access token, en segundos. */
  expiresAt: number;
}

/** Lo que guarda la cookie cifrada de la sesión. */
export interface StoredSession {
  accessToken: string;
  refreshToken: string | null;
  /** Vencimiento del access token, en segundos. */
  expiresAt: number;
}

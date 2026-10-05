// Canje y renovación de tokens contra el servidor OIDC del hub (A-05), de servidor a servidor.
import type { AuthConfig } from "./config";
import type { StoredSession } from "./types";

export class TokenError extends Error {
  constructor(
    readonly status: number,
    readonly error: string,
    readonly description: string | undefined,
  ) {
    super(`${error}${description ? `: ${description}` : ""}`);
  }
}

interface TokenResponse {
  access_token: string;
  refresh_token?: string;
  expires_in?: number;
  error?: string;
  error_description?: string;
}

export function exchangeCode(config: AuthConfig, code: string, redirectUri: string, verifier: string): Promise<StoredSession> {
  return tokenRequest(config, {
    grant_type: "authorization_code",
    code,
    redirect_uri: redirectUri,
    client_id: config.productCode,
    code_verifier: verifier,
  });
}

/** El servidor rota el refresh en cada uso: la sesión guarda el nuevo. */
export function refreshSession(config: AuthConfig, session: StoredSession): Promise<StoredSession> {
  if (!session.refreshToken) return Promise.reject(new TokenError(400, "invalid_grant", "SESSION_WITHOUT_REFRESH"));
  return tokenRequest(config, { grant_type: "refresh_token", refresh_token: session.refreshToken, client_id: config.productCode });
}

async function tokenRequest(config: AuthConfig, form: Record<string, string>): Promise<StoredSession> {
  // Por la red interna el gateway no ve el host del hub: se sella con él y la clave interna, así el emisor del token
  // es el mismo del authorize (A-05, OidcIssuer).
  const headers: Record<string, string> = {
    "content-type": "application/x-www-form-urlencoded",
    accept: "application/json",
    "x-flit-domain": new URL(config.hubUrl).hostname,
  };
  if (config.internalApiKey) headers["x-internal-key"] = config.internalApiKey;

  const response = await fetch(`${config.oidcInternalUrl}/connect/token`, {
    method: "POST",
    headers,
    body: new URLSearchParams(form).toString(),
    cache: "no-store",
  });
  const body = (await response.json().catch(() => ({}))) as Partial<TokenResponse>;
  if (!response.ok || !body.access_token) {
    throw new TokenError(response.status, body.error ?? "server_error", body.error_description);
  }

  return {
    accessToken: body.access_token,
    refreshToken: body.refresh_token ?? form.refresh_token ?? null,
    expiresAt: Math.floor(Date.now() / 1000) + (body.expires_in ?? 900),
  };
}

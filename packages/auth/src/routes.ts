// Rutas de sesión de una app (contrato §8): /auth/login, /auth/callback, /auth/logout, /auth/refresh y
// /auth/session. Authorization code con PKCE contra el hub; el token nunca llega al navegador.
import { sessionUser } from "./claims";
import { appOrigin, authConfig, type AuthConfig } from "./config";
import { parseCookies, serializeCookie, txCookie } from "./cookies";
import { pkceChallenge, randomToken, seal, unseal } from "./crypto";
import { clearSessionCookies, freshSession, isSecure, sessionCookies } from "./store";
import { exchangeCode, TokenError } from "./tokens";

export type RouteHandler = (request: Request) => Promise<Response>;

interface Transaction {
  state: string;
  verifier: string;
  returnTo: string;
}

export interface AuthRoutesOptions {
  productCode: string;
  /** Ruta de retorno registrada en el cliente OIDC (A-05). */
  callbackPath?: string;
  /** Adónde va el usuario cuando el hub niega el acceso (PRODUCT_NOT_ENABLED, …); recibe ?code=. */
  errorPath?: string;
  /** Para pruebas: configuración fija en lugar de las variables de entorno. */
  config?: () => AuthConfig;
}

export function createAuthRoutes(options: AuthRoutesOptions): Record<"login" | "callback" | "logout" | "refresh" | "session" | "claims", RouteHandler> {
  const callbackPath = options.callbackPath ?? "/auth/callback";
  const errorPath = options.errorPath ?? "/403";
  const config = options.config ?? (() => authConfig(options.productCode));

  return {
    /** GET /auth/login?returnTo=/ruta → authorize del hub. */
    async login(request) {
      const cfg = config();
      const url = new URL(request.url);
      const tx: Transaction = { state: randomToken(), verifier: randomToken(), returnTo: safeReturnTo(url.searchParams.get("returnTo")) };
      const authorize = new URL(`${cfg.hubUrl}/connect/authorize`);
      authorize.search = new URLSearchParams({
        client_id: cfg.productCode,
        response_type: "code",
        scope: "openid offline_access",
        redirect_uri: appOrigin(request, cfg) + callbackPath,
        code_challenge: await pkceChallenge(tx.verifier),
        code_challenge_method: "S256",
        state: tx.state,
      }).toString();

      return redirect(authorize.toString(), [
        serializeCookie(txCookie(cfg.productCode), await seal(tx, cfg.sessionSecret), { secure: isSecure(request, cfg), maxAgeSeconds: 600 }),
      ]);
    },

    /** GET /auth/callback?code&state → canje del código, sesión y vuelta a donde estaba. */
    async callback(request) {
      const cfg = config();
      const url = new URL(request.url);
      const secure = isSecure(request, cfg);
      const clearTx = serializeCookie(txCookie(cfg.productCode), "", { secure, maxAgeSeconds: 0 });
      const rawTx = parseCookies(request.headers.get("cookie")).get(txCookie(cfg.productCode));
      const tx = rawTx ? await unseal<Transaction>(rawTx, cfg.sessionSecret) : null;

      if (!tx || url.searchParams.get("state") !== tx.state) {
        // Sin transacción propia (otra pestaña, cookie vencida o un callback que no pedimos): se empieza de nuevo.
        return redirect("/auth/login", [clearTx]);
      }

      const denied = url.searchParams.get("error");
      if (denied) {
        const code = url.searchParams.get("error_description") || denied;
        return redirect(`${errorPath}?code=${encodeURIComponent(code)}`, [clearTx]);
      }

      try {
        const session = await exchangeCode(cfg, url.searchParams.get("code") ?? "", appOrigin(request, cfg) + callbackPath, tx.verifier);
        return redirect(tx.returnTo, [clearTx, ...(await sessionCookies(session, request, cfg))]);
      } catch (error) {
        const code = error instanceof TokenError ? error.description ?? error.error : "LOGIN_FAILED";
        return redirect(`${errorPath}?code=${encodeURIComponent(code)}`, [clearTx]);
      }
    },

    /** GET|POST /auth/logout → borra la sesión y cierra la del hub (A-13). */
    async logout(request) {
      const cfg = config();
      const endSession = new URL(`${cfg.hubUrl}/connect/logout`);
      endSession.search = new URLSearchParams({
        client_id: cfg.productCode,
        post_logout_redirect_uri: appOrigin(request, cfg) + "/",
      }).toString();
      return redirect(endSession.toString(), clearSessionCookies(request, cfg));
    },

    /** POST /auth/refresh → renueva si hace falta; 401 si la sesión ya no sirve. */
    async refresh(request) {
      const { session, setCookies } = await freshSession(request, config());
      return json(session ? { expiresAt: session.expiresAt } : { code: "SESSION_EXPIRED" }, session ? 200 : 401, setCookies);
    },

    /**
     * GET /auth/claims → `{ claimsToken }`: el token SIN firma (`header.payload.`), solo para apps que ya leen los
     * claims del JWT en el navegador (Trámites, A-10). No sirve como credencial: la API lo rechaza y el proxy lo
     * descarta; el token real nunca sale del servidor.
     */
    async claims(request) {
      const { session, setCookies } = await freshSession(request, config());
      return session
        ? json({ claimsToken: claimsToken(session.accessToken) }, 200, setCookies)
        : json({ code: "SESSION_EXPIRED" }, 401, setCookies);
    },

    /** GET /auth/session → SessionUser para useSession(); 401 sin sesión. */
    async session(request) {
      const { session, setCookies } = await freshSession(request, config());
      return session
        ? json(sessionUser(session.accessToken), 200, setCookies)
        : json({ code: "SESSION_EXPIRED" }, 401, setCookies);
    },
  };
}

/** `header.payload.` del JWT: los claims para dibujar la interfaz, sin la firma que lo haría usable. */
export function claimsToken(accessToken: string): string {
  const [header, payload] = accessToken.split(".");
  return `${header}.${payload}.`;
}

/** Solo rutas relativas de esta misma app (evita un redireccionamiento abierto). */
export function safeReturnTo(value: string | null): string {
  return value && value.startsWith("/") && !value.startsWith("//") && !value.startsWith("/\\") ? value : "/";
}

function redirect(location: string, cookies: string[]): Response {
  const headers = new Headers({ location, "cache-control": "no-store" });
  for (const cookie of cookies) headers.append("set-cookie", cookie);
  return new Response(null, { status: 302, headers });
}

function json(body: unknown, status: number, cookies: string[]): Response {
  const headers = new Headers({ "content-type": "application/json", "cache-control": "no-store" });
  for (const cookie of cookies) headers.append("set-cookie", cookie);
  return new Response(JSON.stringify(body), { status, headers });
}

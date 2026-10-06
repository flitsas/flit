// Rutas de sesión de una app (contrato §8): /auth/login, /auth/callback, /auth/logout, /auth/refresh,
// /auth/session y /auth/frontchannel-logout. Authorization code con PKCE contra el hub; el token nunca llega al navegador.
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
  /** Intento silencioso (`prompt=none`): si el hub no tiene sesión, se vuelve a `returnTo` con `sso=0`, sin error. */
  silent?: boolean;
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

export function createAuthRoutes(options: AuthRoutesOptions): Record<"login" | "callback" | "logout" | "frontchannelLogout" | "refresh" | "session" | "claims", RouteHandler> {
  const callbackPath = options.callbackPath ?? "/auth/callback";
  const errorPath = options.errorPath ?? "/403";
  const config = options.config ?? (() => authConfig(options.productCode));

  return {
    /**
     * GET /auth/login?returnTo=/ruta → authorize del hub. Con `prompt=none` es un intento silencioso: entra si el hub
     * ya tiene sesión y, si no, vuelve a `returnTo` con `sso=0` (la página decide qué mostrar sin volver a intentar).
     */
    async login(request) {
      const cfg = config();
      const url = new URL(request.url);
      const silent = url.searchParams.get("prompt") === "none";
      const tx: Transaction = { state: randomToken(), verifier: randomToken(), returnTo: safeReturnTo(url.searchParams.get("returnTo")), silent };
      const authorize = new URL(`${cfg.hubUrl}/connect/authorize`);
      authorize.search = new URLSearchParams({
        client_id: cfg.productCode,
        response_type: "code",
        scope: "openid offline_access",
        redirect_uri: appOrigin(request, cfg) + callbackPath,
        code_challenge: await pkceChallenge(tx.verifier),
        code_challenge_method: "S256",
        state: tx.state,
        ...(silent ? { prompt: "none" } : {}),
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
      if (denied && tx.silent) {
        // El hub no tiene sesión (login_required) o no da acceso: la página vuelve a su estado sin sesión.
        // La sesión local que hubiera quedado tampoco sirve: el hub ya no la respalda.
        return redirect(withParam(tx.returnTo, "sso", "0"), [clearTx, ...clearSessionCookies(request, cfg)]);
      }
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

    /**
     * GET /auth/frontchannel-logout → borra la sesión de esta app. La abre en segundo plano la página de cierre de
     * sesión del hub (front-channel logout, HU #13004), así ninguna app queda con la cookie de una sesión ya revocada.
     * Se rechaza solo lo que viene de otro sitio (`Sec-Fetch-Site: cross-site`): otra web no puede cerrarle la sesión a
     * nadie. El hub del ambiente o de la red es el mismo sitio; abrir la ruta a mano (`none`) o un navegador viejo sin
     * la cabecera se aceptan: lo peor que pasa es cerrar una sesión.
     */
    async frontchannelLogout(request) {
      if (request.headers.get("sec-fetch-site") === "cross-site") return new Response(null, { status: 403 });
      const headers = new Headers({
        "content-type": "text/html; charset=utf-8",
        "cache-control": "no-store",
        // Se carga dentro de la página del hub; frame-ancestors gana a un X-Frame-Options del borde.
        "content-security-policy": "default-src 'none'; frame-ancestors *",
      });
      for (const cookie of clearSessionCookies(request, config())) headers.append("set-cookie", cookie);
      return new Response("<!doctype html><title>Sesión cerrada</title>", { status: 200, headers });
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

function withParam(path: string, key: string, value: string): string {
  const url = new URL(path, "http://app.local");
  url.searchParams.set(key, value);
  return `${url.pathname}${url.search}`;
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

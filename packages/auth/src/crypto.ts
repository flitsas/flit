// Cifrado de la cookie de sesión con AES-GCM (Web Crypto, sin dependencias). La llave sale de SHA-256 del secreto
// del servidor: el navegador guarda un blob que no puede leer ni alterar sin que falle la autenticación del cifrado.

const encoder = new TextEncoder();
const decoder = new TextDecoder();

async function keyFrom(secret: string): Promise<CryptoKey> {
  const raw = await crypto.subtle.digest("SHA-256", encoder.encode(secret));
  return crypto.subtle.importKey("raw", raw, "AES-GCM", false, ["encrypt", "decrypt"]);
}

// Se comprime antes de cifrar: el access token repite mucho (slugs de permisos) y la sesión viaja en cada petición en
// la cabecera Cookie, que Node limita a 16 KB en total.
async function transform(bytes: Uint8Array, stream: CompressionStream | DecompressionStream): Promise<Uint8Array> {
  const piped = new Blob([bytes as BlobPart]).stream().pipeThrough(stream);
  return new Uint8Array(await new Response(piped).arrayBuffer());
}

export async function seal(value: unknown, secret: string): Promise<string> {
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const compressed = await transform(encoder.encode(JSON.stringify(value)), new CompressionStream("deflate-raw"));
  const data = await crypto.subtle.encrypt({ name: "AES-GCM", iv }, await keyFrom(secret), compressed as BufferSource);
  const out = new Uint8Array(iv.length + data.byteLength);
  out.set(iv);
  out.set(new Uint8Array(data), iv.length);
  return base64UrlEncode(out);
}

/** `null` si el valor no se puede descifrar (otro secreto, alterado o mal formado). */
export async function unseal<T>(sealed: string, secret: string): Promise<T | null> {
  try {
    const bytes = base64UrlDecode(sealed);
    const compressed = await crypto.subtle.decrypt({ name: "AES-GCM", iv: bytes.slice(0, 12) }, await keyFrom(secret), bytes.slice(12));
    const plain = await transform(new Uint8Array(compressed), new DecompressionStream("deflate-raw"));
    return JSON.parse(decoder.decode(plain)) as T;
  } catch {
    return null;
  }
}

export function randomToken(bytes = 32): string {
  return base64UrlEncode(crypto.getRandomValues(new Uint8Array(bytes)));
}

/** code_challenge S256 de PKCE. */
export async function pkceChallenge(verifier: string): Promise<string> {
  return base64UrlEncode(new Uint8Array(await crypto.subtle.digest("SHA-256", encoder.encode(verifier))));
}

export function base64UrlEncode(bytes: Uint8Array): string {
  let binary = "";
  for (const b of bytes) binary += String.fromCharCode(b);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

export function base64UrlDecode(value: string): Uint8Array {
  const b64 = value.replace(/-/g, "+").replace(/_/g, "/");
  const binary = atob(b64.padEnd(b64.length + ((4 - (b64.length % 4)) % 4), "="));
  return Uint8Array.from(binary, (c) => c.charCodeAt(0));
}

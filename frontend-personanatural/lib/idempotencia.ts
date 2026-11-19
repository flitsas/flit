/** Llave de idempotencia generada en el navegador (Idempotency-Key): la misma llave en un reintento evita el doble envío. */
export function nuevaLlave(): string {
  const cripto = globalThis.crypto;
  if (typeof cripto?.randomUUID === "function") return cripto.randomUUID();
  if (typeof cripto?.getRandomValues === "function") {
    return Array.from(cripto.getRandomValues(new Uint8Array(16)), (b) => b.toString(16).padStart(2, "0")).join("");
  }
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

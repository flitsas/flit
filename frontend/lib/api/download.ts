// Descarga de archivos binarios desde la API (HU #10249). El cliente JSON (`apiFetch`)
// no sirve para respuestas binarias (Excel/PDF), así que este helper hace `fetch`,
// adjunta el JWT, valida el status y dispara la descarga en el navegador.
import { API_BASE_URL, getToken } from "./client";
import { ApiError } from "./types";

export interface DownloadOptions {
  method?: string;
  query?: Record<string, string | number | boolean | undefined | null>;
  body?: unknown;
  signal?: AbortSignal;
  /** Nombre usado si la respuesta no trae `Content-Disposition`. */
  fallbackFilename: string;
}

/**
 * Descarga un archivo de la API. Lanza `ApiError` con el status del backend ante
 * 4xx/5xx (AC3) para que el caller muestre el error sin bloquear la pantalla.
 */
export async function downloadFile(path: string, options: DownloadOptions): Promise<void> {
  const { method = "GET", query, body, signal, fallbackFilename } = options;
  const base = API_BASE_URL || (typeof window !== "undefined" ? window.location.origin : "http://localhost:3000");
  const url = new URL(path, base);

  if (query) {
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== "") {
        url.searchParams.set(key, String(value));
      }
    }
  }

  const token = getToken();
  const headers: Record<string, string> = {};
  if (body !== undefined) headers["Content-Type"] = "application/json";
  if (token) headers.Authorization = `Bearer ${token}`;

  const response = await fetch(url.toString(), {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  });

  if (!response.ok) {
    let detail: unknown = null;
    try {
      const text = await response.text();
      detail = text ? JSON.parse(text) : null;
    } catch {
      /* respuesta de error sin cuerpo JSON */
    }
    throw new ApiError(response.status, `Error ${response.status} al exportar ${path}`, detail);
  }

  const blob = await response.blob();
  const filename = parseFilename(response.headers.get("Content-Disposition")) ?? fallbackFilename;
  triggerBrowserDownload(blob, filename);
}

/** Extrae el nombre de archivo de un header `Content-Disposition`, si existe. */
function parseFilename(header: string | null): string | null {
  if (!header) return null;
  const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (utf8?.[1]) return decodeURIComponent(utf8[1]);
  const plain = /filename="?([^";]+)"?/i.exec(header);
  return plain?.[1] ?? null;
}

/** Crea un enlace temporal y dispara la descarga del blob. No-op en SSR. */
function triggerBrowserDownload(blob: Blob, filename: string): void {
  if (typeof document === "undefined" || typeof URL.createObjectURL !== "function") return;
  const objectUrl = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = objectUrl;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(objectUrl);
}

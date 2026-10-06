// Cliente tipado de la captura manual pública (contrato Épica #13202 §2).
// Cliente HTTP real por defecto; el adaptador simulado solo con NEXT_PUBLIC_MANUAL_CAPTURE_MOCK=true.
// Endpoints públicos: sin Authorization ni X-Tenant-Id (el token del enlace es la credencial).
import { resolveApiUrl } from "@/lib/api/client";
import { RENDERED_CONSENT_TEXT_VERSION } from "./consent";
import {
  ManualCaptureError,
  type ConsentBody,
  type ManualCaptureClient,
  type ManualCaptureView,
  type ManualSubmitFiles,
} from "./types";

const BASE = "/api/v1/public/manual-capture";

async function failFrom(res: Response): Promise<never> {
  let code = "error";
  let message = "No pudimos completar la solicitud.";
  try {
    const body = (await res.json()) as { code?: string; message?: string };
    code = body.code ?? code;
    message = body.message ?? message;
  } catch {
    /* cuerpo vacío o no JSON */
  }
  throw new ManualCaptureError(res.status, code, message);
}

export function createHttpClient(fetchImpl: typeof fetch = (...a) => fetch(...a)): ManualCaptureClient {
  const url = (token: string, suffix = "") => resolveApiUrl(`${BASE}/${encodeURIComponent(token)}${suffix}`);
  return {
    async getManualCapture(token) {
      const res = await fetchImpl(url(token), { headers: { Accept: "application/json" } });
      if (!res.ok) return failFrom(res);
      return (await res.json()) as ManualCaptureView;
    },
    async postConsent(token, body: ConsentBody) {
      const res = await fetchImpl(url(token, "/consent"), {
        method: "POST",
        headers: { "Content-Type": "application/json", Accept: "application/json" },
        body: JSON.stringify(body),
      });
      if (!res.ok) return failFrom(res);
    },
    async submit(token, files: ManualSubmitFiles) {
      const form = new FormData();
      form.append("rostro", files.rostro, "rostro.jpg");
      form.append("anverso", files.anverso, "anverso.jpg");
      form.append("reverso", files.reverso, "reverso.jpg");
      form.append("firma", files.firma, "firma.png");
      const res = await fetchImpl(url(token, "/submit"), {
        method: "POST",
        headers: { Accept: "application/json" }, // sin Content-Type: el navegador fija el boundary
        body: form,
      });
      if (!res.ok) return failFrom(res);
      return (await res.json()) as { status: string };
    },
  };
}

/**
 * Adaptador simulado con datos sintéticos (sin personas reales). Tokens de prueba:
 * «vencido» → 410 expirada · «reemplazado» → 410 reemplazado · «usado» → 409 · «invalido» → 404 ·
 * «red» → fallo de red · «consent-falla» → el consentimiento falla (500). Cualquier otro: válido.
 */
export function createMockClient(delayMs = 250): ManualCaptureClient {
  const wait = () => new Promise<void>((r) => setTimeout(r, delayMs));
  return {
    async getManualCapture(token) {
      await wait();
      if (token === "vencido") throw new ManualCaptureError(410, "expirada", "Este enlace venció.");
      if (token === "reemplazado") throw new ManualCaptureError(410, "reemplazado", "Enlace reemplazado.");
      if (token === "usado") throw new ManualCaptureError(409, "estado_invalido", "Enlace ya usado.");
      if (token === "invalido") throw new ManualCaptureError(404, "not_found", "Enlace no válido.");
      if (token === "red") throw new TypeError("Failed to fetch");
      return {
        fullName: "Persona de Prueba",
        documentType: "CC",
        documentNumber: "1000000000",
        productName: "FLIT 2.0",
        expiresAt: new Date(Date.now() + 24 * 3600_000).toISOString(),
        consentTextVersion: RENDERED_CONSENT_TEXT_VERSION,
      };
    },
    async postConsent(token) {
      await wait();
      if (token === "consent-falla") throw new ManualCaptureError(500, "error", "Error del servidor.");
    },
    async submit() {
      await wait();
      return { status: "pendiente_revision_manual" };
    },
  };
}

/** Elige el adaptador: el real, salvo que NEXT_PUBLIC_MANUAL_CAPTURE_MOCK sea «true». */
export function getManualCaptureClient(): ManualCaptureClient {
  return process.env.NEXT_PUBLIC_MANUAL_CAPTURE_MOCK === "true" ? createMockClient() : createHttpClient();
}

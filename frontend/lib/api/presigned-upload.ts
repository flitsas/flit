/**
 * Subida directa navegador → storage (ADR-0057).
 * El file-manager decide el método en `method`: POST (multipart + fields) o PUT (bytes crudos).
 * Contabo Object Storage rechaza el POST policy (Kong no ve las credenciales del multipart).
 * Ausente ⇒ POST (retrocompatible con MinIO / AWS S3).
 */
export type PresignedUploadTicket = {
  url: string;
  fields?: Record<string, string> | null;
  method?: string | null;
};

export async function uploadFileToPresignedUrl(
  ticket: PresignedUploadTicket,
  file: File,
): Promise<void> {
  const method = (ticket.method ?? "POST").trim().toUpperCase() || "POST";
  let res: Response;

  if (method === "PUT") {
    // Bytes crudos; Content-Type alineado con FileManagerAttachmentStorage (PUT Contabo).
    res = await fetch(ticket.url, {
      method: "PUT",
      body: file,
      headers: { "Content-Type": "application/octet-stream" },
    });
  } else {
    const form = new FormData();
    for (const [key, value] of Object.entries(ticket.fields ?? {})) {
      form.append(key, value);
    }
    form.append("file", file);
    // No fijar Content-Type: el navegador pone el boundary del multipart.
    res = await fetch(ticket.url, { method: "POST", body: form });
  }

  if (!res.ok) {
    const detail = await res.text().catch(() => "");
    throw new Error(
      `Error subiendo a almacenamiento (${method} ${res.status})${detail ? `: ${detail}` : ""}`,
    );
  }
}

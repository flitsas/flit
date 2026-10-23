// Llamadas del navegador del hub a su propio host (/api/v1/* y /connect/*, que el servidor reenvía al gateway).
// Sin tokens en el navegador: la sesión del hub viaja en la cookie HttpOnly flit_hub.

export interface ApiResult<T = unknown> {
  ok: boolean;
  status: number;
  body: T | null;
}

export async function postJson<T = unknown>(path: string, payload: unknown): Promise<ApiResult<T>> {
  const response = await fetch(path, {
    method: "POST",
    credentials: "same-origin",
    headers: { "content-type": "application/json", accept: "application/json" },
    body: JSON.stringify(payload),
  });
  let body: T | null = null;
  try {
    body = (await response.json()) as T;
  } catch {
    body = null;
  }
  return { ok: response.ok, status: response.status, body };
}

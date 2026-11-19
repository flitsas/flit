// Sonda del contenedor (healthcheck del compose). No toca la API: solo dice que la app responde.
export const dynamic = "force-dynamic";

export function GET(): Response {
  return Response.json({ status: "alive" });
}

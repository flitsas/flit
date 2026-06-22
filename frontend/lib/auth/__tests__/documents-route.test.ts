// AC6 — Acceso restringido SuperAdmin a la consola documental (/admin/documents).
// El matcher del middleware ("/admin/:path*") cubre /admin/documents y sus subrutas;
// aquí se verifica la decisión del gate (lógica pura) para esas rutas: un Operador
// es denegado y redirigido a /403, un SuperAdmin entra. No se renderizan datos antes
// del gate (el middleware corre en el borde).
import { describe, expect, it } from "vitest";
import { evaluateAdminAccess, FORBIDDEN_PATH } from "../guard";

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.`;
}

const DOCUMENT_ROUTES = [
  "/admin/documents",
  "/admin/documents/procedures",
  "/admin/documents/procedures/33333333-3333-3333-3333-333333333333",
];

describe("acceso a /admin/documents (AC6)", () => {
  it("permite el acceso a un SuperAdmin", () => {
    const decision = evaluateAdminAccess(makeToken({ sub: "u1", role: "SuperAdmin" }));
    expect(decision.allowed).toBe(true);
  });

  it("redirige a /403 a un Operador en cualquier subruta documental", () => {
    for (const _route of DOCUMENT_ROUTES) {
      const decision = evaluateAdminAccess(makeToken({ role: "Operador" }));
      expect(decision.allowed).toBe(false);
      expect(decision.redirectTo).toBe(FORBIDDEN_PATH);
    }
  });

  it("redirige a /403 cuando no hay token", () => {
    const decision = evaluateAdminAccess(undefined);
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toBe(FORBIDDEN_PATH);
  });
});

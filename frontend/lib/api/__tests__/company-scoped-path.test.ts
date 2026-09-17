import { describe, expect, it } from "vitest";
import { companyScopedPath } from "../company-scoped-path";

describe("companyScopedPath", () => {
  it("usa la ruta propia cuando no hay cabeza de red", () => {
    expect(companyScopedPath("child-1", "/mandate-signers")).toBe(
      "/api/v1/admin/companies/child-1/mandate-signers",
    );
  });

  it("enruta por children cuando la cabeza gestiona al asociado", () => {
    expect(companyScopedPath("child-1", "/legal-representatives", "head-9")).toBe(
      "/api/v1/admin/companies/head-9/children/child-1/legal-representatives",
    );
    expect(companyScopedPath("child-1", "/mandate-signers", "head-9")).toBe(
      "/api/v1/admin/companies/head-9/children/child-1/mandate-signers",
    );
    expect(companyScopedPath("child-1", "/document-params", "head-9")).toBe(
      "/api/v1/admin/companies/head-9/children/child-1/document-params",
    );
  });
});

// HU #11757 (ADR-0050) — el mandatario adopta CF-01/CF-03/CF-04 sin variación respecto de la ficha
// del representante legal (HU #11755/#11756): solo consulta (sin Enviar/Reenviar/Vincular), los dos
// rótulos siempre presentes y el copy por estado, incluido el caso NIT (real aquí: DOC_TYPES incluye
// "NIT" en CompanyMandatarioForm).

import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { MandatarioIdentidadBlock } from "../MandatarioIdentidadBlock";
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";

// Uso de ejemplo: <MandatarioIdentidadBlock signer={signer} /> — solo consulta, sin props de acción.

function signer(overrides: Partial<MandateSigner> = {}): MandateSigner {
  return {
    id: "ms-1",
    transitOfficeId: "ot-medellin",
    fullName: "Ana Restrepo",
    documentType: "CC",
    documentNumber: "1020304050",
    integrityHash: "a".repeat(64),
    email: "ana@ejemplo.com",
    userId: null,
    identityValidationRef: null,
    identityStatus: "none",
    identityValidUntil: null,
    signatureVaultId: null,
    registeredAt: "2026-08-01T10:00:00Z",
    isActive: true,
    companyTenantIds: ["tenant-1"],
    ...overrides,
  };
}

describe("HU #11757 — CF-01: MandatarioIdentidadBlock queda en solo consulta", () => {
  it("happy path: no renderiza ningún control de escritura (Enviar/Reenviar/Vincular)", () => {
    render(<MandatarioIdentidadBlock signer={signer()} />);

    expect(screen.queryByRole("button", { name: /enviar/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /reenviar/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /vincular/i })).not.toBeInTheDocument();
  });

  it.each(["none", "pending", "valid", "expired"] as const)(
    "edge case: sin controles con identityStatus «%s»",
    (identityStatus) => {
      render(<MandatarioIdentidadBlock signer={signer({ identityStatus })} />);
      expect(screen.queryAllByRole("button")).toHaveLength(0);
    },
  );
});

describe("HU #11757 — CF-04: los dos rótulos siempre presentes", () => {
  it("happy path: muestra «Identidad: ...» y «Firma del baúl: ...» a la vez", () => {
    render(<MandatarioIdentidadBlock signer={signer()} />);

    expect(screen.getByTestId("mandatario-identidad-rotulo")).toHaveTextContent(
      "Identidad: sin validación",
    );
    expect(screen.getByTestId("mandatario-firma-baul-rotulo")).toHaveTextContent(
      "Firma del baúl: sin firma vigente",
    );
  });

  it("contrato: con signatureVaultId presente, el rótulo de firma cambia a vigente", () => {
    render(<MandatarioIdentidadBlock signer={signer({ signatureVaultId: "sig-1" })} />);

    expect(screen.getByTestId("mandatario-firma-baul-rotulo")).toHaveTextContent(
      "Firma del baúl: vigente",
    );
  });

  it("contrato: identidad aprobada y vigente usa el rótulo canónico del ADR-0050", () => {
    render(<MandatarioIdentidadBlock signer={signer({ identityStatus: "valid" })} />);

    expect(screen.getByTestId("mandatario-identidad-rotulo")).toHaveTextContent(
      "Identidad: aprobada y vigente",
    );
  });
});

describe("HU #11757 — CF-03: copy por estado (reutiliza el módulo de HU #11756, sin duplicar)", () => {
  it("happy path: sin validación y sin firma vigente invita al módulo Identidad", () => {
    render(<MandatarioIdentidadBlock signer={signer()} />);

    expect(screen.getByTestId("mandatario-identidad-copy")).toBeInTheDocument();
    expect(screen.getByTestId("mandatario-identidad-module-link")).toBeInTheDocument();
  });

  it("edge case: con firma del baúl (signatureVaultId) NO invita a prevalidar (D8 ADR-0025 manda)", () => {
    render(<MandatarioIdentidadBlock signer={signer({ signatureVaultId: "sig-1" })} />);

    expect(screen.queryByTestId("mandatario-identidad-copy")).not.toBeInTheDocument();
  });

  it("contrato: caso NIT (persona jurídica) ve «no aplica prevalidación», no el enlace", () => {
    // DOC_TYPES de CompanyMandatarioForm incluye "NIT": este caso es real para mandatarios.
    render(<MandatarioIdentidadBlock signer={signer({ documentType: "NIT" })} />);

    expect(screen.getByTestId("mandatario-identidad-copy")).toHaveTextContent(
      /no aplica prevalidación/i,
    );
    expect(screen.queryByTestId("mandatario-identidad-module-link")).not.toBeInTheDocument();
  });

  it("contrato: identidad vencida sin firma vigente invita a renovar (copy distinto de «sin validación»)", () => {
    render(<MandatarioIdentidadBlock signer={signer({ identityStatus: "expired" })} />);

    expect(screen.getByTestId("mandatario-identidad-copy")).toHaveTextContent(/venció/i);
    expect(screen.getByTestId("mandatario-identidad-module-link")).toBeInTheDocument();
  });
});

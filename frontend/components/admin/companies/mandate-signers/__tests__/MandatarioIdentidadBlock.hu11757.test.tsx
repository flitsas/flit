// HU #11757 → HU #13248 (F9 #13245) — la ficha del mandatario muestra el estado de SU validación de
// identidad y ofrece «Reenviar validación» (antes era solo consulta y remitía al módulo Identidad).

import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MandatarioIdentidadBlock } from "../MandatarioIdentidadBlock";
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";
import { ApiError, ApiValidationError } from "@/lib/api/types";

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
    identityStatus: "none",
    identityValidUntil: null,
    signatureVaultId: null,
    registeredAt: "2026-08-01T10:00:00Z",
    isActive: true,
    companyTenantIds: ["tenant-1"],
    signerModel: "natural",
    signatureMethod: "biometria",
    ...overrides,
  };
}

const estado = () => screen.getByTestId("mandatario-validacion-estado");

describe("HU #13248 AC1 — estado de la validación propia", () => {
  it.each([
    ["valid", "Aprobada"],
    ["pending", "En curso"],
    ["expired", "Enlace vencido"],
    ["none", "Pendiente de validación"],
  ] as const)("identityStatus «%s» se lee como «%s»", (identityStatus, texto) => {
    render(<MandatarioIdentidadBlock signer={signer({ identityStatus })} />);
    expect(estado()).toHaveTextContent(texto);
    expect(estado()).toHaveAttribute("data-estado", identityStatus);
  });

  it("no habla de ADR ni del módulo Identidad", () => {
    render(<MandatarioIdentidadBlock signer={signer()} onResend={vi.fn()} />);
    expect(screen.getByTestId("mandatario-identidad").textContent).not.toMatch(/ADR|módulo Identidad/i);
  });
});

describe("HU #13248 AC6 — sin validación propia y bloque condicionado", () => {
  it("biometría sin validación: pendiente, sin firma válida y con «Reenviar validación» visible", () => {
    render(<MandatarioIdentidadBlock signer={signer()} onResend={vi.fn()} />);
    expect(estado()).toHaveTextContent("Pendiente de validación");
    expect(screen.getByTestId("mandatario-validacion-detalle")).toHaveTextContent(/sin firma válida/i);
    expect(screen.getByRole("button", { name: "Reenviar validación" })).toBeEnabled();
  });

  it("legado sin forma de firma y sin baúl se trata como validación de identidad", () => {
    render(<MandatarioIdentidadBlock signer={signer({ signatureMethod: null })} onResend={vi.fn()} />);
    expect(screen.getByRole("button", { name: "Reenviar validación" })).toBeInTheDocument();
  });

  it.each([
    ["Baúl de firmas", { signatureMethod: "baul" as const, signatureVaultId: "sig-1" }],
    ["Persona jurídica", { signerModel: "juridica" as const, signatureMethod: null }],
    ["Formato en blanco", { signerModel: "formato_blanco" as const, signatureMethod: null }],
  ])("con %s no aparece el bloque ni el botón", (_n, extra) => {
    render(<MandatarioIdentidadBlock signer={signer(extra)} onResend={vi.fn()} />);
    expect(screen.queryByTestId("mandatario-identidad")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /reenviar/i })).not.toBeInTheDocument();
  });

  it("aprobada: no se ofrece reenviar", () => {
    render(<MandatarioIdentidadBlock signer={signer({ identityStatus: "valid" })} onResend={vi.fn()} />);
    expect(screen.queryByRole("button", { name: /reenviar/i })).not.toBeInTheDocument();
  });
});

describe("HU #13248 AC7 — mandatario inactivo", () => {
  it("inactivo: no se ofrece ni se dispara ningún envío", () => {
    const onResend = vi.fn();
    render(<MandatarioIdentidadBlock signer={signer({ isActive: false })} onResend={onResend} />);
    expect(screen.queryByRole("button", { name: /reenviar/i })).not.toBeInTheDocument();
    expect(onResend).not.toHaveBeenCalled();
  });

  it("sin permiso de edición (puedeEditar=false) no se ofrece", () => {
    render(<MandatarioIdentidadBlock signer={signer({ puedeEditar: false })} onResend={vi.fn()} />);
    expect(screen.queryByRole("button", { name: /reenviar/i })).not.toBeInTheDocument();
  });
});

describe("HU #13248 AC2 — reenviar validación", () => {
  it("deshabilita el botón mientras envía y confirma con el correo de destino", async () => {
    const user = userEvent.setup();
    let resolver: (v: { identity: "sent" }) => void = () => {};
    const onResend = vi.fn(() => new Promise<{ identity: "sent" }>((r) => (resolver = r)));
    render(<MandatarioIdentidadBlock signer={signer()} onResend={onResend} />);

    await user.click(screen.getByRole("button", { name: "Reenviar validación" }));
    expect(screen.getByRole("button", { name: /Enviando/ })).toBeDisabled();
    await user.click(screen.getByRole("button", { name: /Enviando/ }));
    expect(onResend).toHaveBeenCalledTimes(1);

    resolver({ identity: "sent" });
    expect(await screen.findByTestId("mandatario-validacion-mensaje")).toHaveTextContent("Enviamos el enlace de validación a ana@ejemplo.com.");
    expect(estado()).toHaveTextContent("En curso");
    expect(screen.getByRole("button", { name: "Reenviar validación" })).toBeEnabled();
  });

  it("proveedor caído (queued): avisa que reintentará, sin error", async () => {
    const user = userEvent.setup();
    render(<MandatarioIdentidadBlock signer={signer()} onResend={vi.fn().mockResolvedValue({ identity: "queued" })} />);
    await user.click(screen.getByRole("button", { name: "Reenviar validación" }));
    expect(await screen.findByTestId("mandatario-validacion-mensaje")).toHaveTextContent(/Reintentaremos el envío/);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});

describe("HU #13248 AC5 — errores del reenvío", () => {
  it("422 por correo pide completar el correo", async () => {
    const user = userEvent.setup();
    const onResend = vi
      .fn()
      .mockRejectedValue(new ApiValidationError([{ field: "email", message: "El correo es obligatorio", value: null }], 422));
    render(<MandatarioIdentidadBlock signer={signer({ email: null })} onResend={onResend} />);
    await user.click(screen.getByRole("button", { name: "Reenviar validación" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(/Falta el correo/);
    expect(estado()).toHaveTextContent("Pendiente de validación");
    await waitFor(() => expect(screen.getByRole("button", { name: "Reenviar validación" })).toBeEnabled());
  });

  it("409 y 502 muestran un mensaje sin jerga", async () => {
    const user = userEvent.setup();
    const onResend = vi
      .fn()
      .mockRejectedValueOnce(new ApiError(409, "x", { code: "mandatario_no_requiere_validacion" }))
      .mockRejectedValueOnce(new ApiError(502, "x", { code: "proveedor_error" }));
    render(<MandatarioIdentidadBlock signer={signer()} onResend={onResend} />);
    await user.click(screen.getByRole("button", { name: "Reenviar validación" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(/no requiere validación/i);
    await user.click(screen.getByRole("button", { name: "Reenviar validación" }));
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent(/No pudimos enviar el enlace/));
  });
});

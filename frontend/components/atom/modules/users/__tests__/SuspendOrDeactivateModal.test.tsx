// HU #10620 (Feature #10618) — modal unificado de suspensión/desactivación de usuarios.
// Reemplaza `SuspendModal` (Usuarios.tsx) y `OtSuspendUserDialog` (OtUsersSection.tsx).
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SuspendOrDeactivateModal } from "../SuspendOrDeactivateModal";
import { ApiError } from "@/lib/api/types";

const user = { id: "u-1", fullName: "Ana Torres" };

describe("SuspendOrDeactivateModal", () => {
  it("AC2 — modo 'temporary': exige fecha de fin y la envía en ISO al confirmar", async () => {
    const onConfirm = vi.fn().mockResolvedValue(undefined);
    const u = userEvent.setup();
    render(<SuspendOrDeactivateModal user={user} mode="temporary" onClose={vi.fn()} onConfirm={onConfirm} />);

    expect(screen.getByRole("heading", { name: "Suspender usuario" })).toBeInTheDocument();
    expect(screen.getByLabelText(/Suspendido hasta/i)).toBeRequired();

    await u.type(screen.getByLabelText(/Motivo/i), "Incumplimiento de políticas");
    await u.click(screen.getByRole("button", { name: /^Suspender usuario$/i }));

    await waitFor(() => expect(onConfirm).toHaveBeenCalled());
    const [reason, endsAt] = onConfirm.mock.calls[0]!;
    expect(reason).toBe("Incumplimiento de políticas");
    expect(endsAt).not.toBeNull();
    expect(() => new Date(endsAt as string).toISOString()).not.toThrow();
  });

  it("AC1 — modo 'indefinite': oculta el campo de fecha y no la exige; envía endsAt=null", async () => {
    const onConfirm = vi.fn().mockResolvedValue(undefined);
    const u = userEvent.setup();
    render(<SuspendOrDeactivateModal user={user} mode="indefinite" onClose={vi.fn()} onConfirm={onConfirm} />);

    expect(screen.getByRole("heading", { name: "Desactivar usuario" })).toBeInTheDocument();
    expect(screen.queryByLabelText(/Suspendido hasta/i)).not.toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: /Sin fecha de fin/i })).toBeChecked();

    await u.type(screen.getByLabelText(/Motivo/i), "Baja definitiva");
    await u.click(screen.getByRole("button", { name: /^Desactivar usuario$/i }));

    await waitFor(() => expect(onConfirm).toHaveBeenCalledWith("Baja definitiva", null));
  });

  it("el toggle 'Sin fecha de fin' permite cambiar de modo dentro del mismo modal", async () => {
    const onConfirm = vi.fn().mockResolvedValue(undefined);
    const u = userEvent.setup();
    render(<SuspendOrDeactivateModal user={user} mode="temporary" onClose={vi.fn()} onConfirm={onConfirm} />);

    const toggle = screen.getByRole("checkbox", { name: /Sin fecha de fin/i });
    expect(toggle).not.toBeChecked();
    expect(screen.getByLabelText(/Suspendido hasta/i)).toBeInTheDocument();

    await u.click(toggle);
    expect(toggle).toBeChecked();
    expect(screen.queryByLabelText(/Suspendido hasta/i)).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Desactivar usuario" })).toBeInTheDocument();

    await u.click(toggle);
    expect(screen.getByLabelText(/Suspendido hasta/i)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Suspender usuario" })).toBeInTheDocument();
  });

  it("no envía el formulario si el motivo está vacío (campo requerido)", async () => {
    const onConfirm = vi.fn();
    const u = userEvent.setup();
    render(<SuspendOrDeactivateModal user={user} mode="indefinite" onClose={vi.fn()} onConfirm={onConfirm} />);

    await u.click(screen.getByRole("button", { name: /^Desactivar usuario$/i }));
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it("AC3 (negativo) — mapea el 409 LAST_ACTIVE_ADMIN a un mensaje claro y mantiene el modal abierto", async () => {
    const onConfirm = vi.fn().mockRejectedValue(
      new ApiError(409, "Conflict", { code: "LAST_ACTIVE_ADMIN", message: "…" }),
    );
    const onClose = vi.fn();
    const u = userEvent.setup();
    render(<SuspendOrDeactivateModal user={user} mode="indefinite" onClose={onClose} onConfirm={onConfirm} />);

    await u.type(screen.getByLabelText(/Motivo/i), "Intento de desactivación");
    await u.click(screen.getByRole("button", { name: /^Desactivar usuario$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      /No puedes dejar este tenant sin administradores activos/i,
    );
    expect(onClose).not.toHaveBeenCalled();
  });

  it("AC3/AC4 — también mapea el 409 cuando el body usa el campo `error` (contrato de AdminOtEndpoints)", async () => {
    const onConfirm = vi.fn().mockRejectedValue(
      new ApiError(409, "Conflict", { error: "LAST_ACTIVE_ADMIN", message: "…" }),
    );
    const u = userEvent.setup();
    render(<SuspendOrDeactivateModal user={user} mode="temporary" onClose={vi.fn()} onConfirm={onConfirm} />);

    await u.type(screen.getByLabelText(/Motivo/i), "Intento de suspensión");
    await u.click(screen.getByRole("button", { name: /^Suspender usuario$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      /No puedes dejar este tenant sin administradores activos/i,
    );
  });

  it("muestra un mensaje genérico para errores distintos de LAST_ACTIVE_ADMIN", async () => {
    const onConfirm = vi.fn().mockRejectedValue(new Error("network down"));
    const u = userEvent.setup();
    render(<SuspendOrDeactivateModal user={user} mode="temporary" onClose={vi.fn()} onConfirm={onConfirm} />);

    await u.type(screen.getByLabelText(/Motivo/i), "Motivo cualquiera");
    await u.click(screen.getByRole("button", { name: /^Suspender usuario$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/No se pudo aplicar la acción/i);
  });

  it("el botón Cancelar invoca onClose", async () => {
    const onClose = vi.fn();
    const u = userEvent.setup();
    render(<SuspendOrDeactivateModal user={user} mode="temporary" onClose={onClose} onConfirm={vi.fn()} />);

    await u.click(screen.getByRole("button", { name: /Cancelar/i }));
    expect(onClose).toHaveBeenCalled();
  });
});

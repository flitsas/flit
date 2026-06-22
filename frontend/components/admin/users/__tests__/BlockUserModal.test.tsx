import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { adminBlockUser } from "@/lib/api/auth";
import { BlockUserModal } from "../BlockUserModal";

vi.mock("@/lib/api/auth", () => ({ adminBlockUser: vi.fn() }));
const blockMock = vi.mocked(adminBlockUser);

describe("BlockUserModal (HU #10174 AC2)", () => {
  beforeEach(() => vi.clearAllMocks());

  it("AC2 — bloqueo fuera de ámbito muestra 403 sin aplicar cambios", async () => {
    blockMock.mockRejectedValue({ status: 403 });
    render(<BlockUserModal email="otro-tenant@flit.local" onClose={() => {}} />);

    fireEvent.click(screen.getByRole("button", { name: /aplicar bloqueo/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/acceso restringido/i);
    // No se muestra el estado de éxito (no se aplicó el bloqueo).
    expect(screen.queryByRole("status")).toBeNull();
    expect(blockMock).toHaveBeenCalledWith("otro-tenant@flit.local", 7);
  });

  it("aplica el bloqueo en el ámbito permitido", async () => {
    blockMock.mockResolvedValue(undefined);
    render(<BlockUserModal email="demo@flit.local" onClose={() => {}} />);

    fireEvent.click(screen.getByRole("button", { name: /aplicar bloqueo/i }));

    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent(/bloqueado temporalmente/i));
  });
});

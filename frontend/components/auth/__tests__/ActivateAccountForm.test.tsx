import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { activateAccount } from "@/lib/api/auth";
import { getToken } from "@/lib/api/client";
import { ApiError } from "@/lib/api/types";
import { clearToken } from "@/lib/auth/session";
import { ActivateAccountForm } from "../ActivateAccountForm";

vi.mock("@/lib/api/auth", () => ({ activateAccount: vi.fn() }));
vi.mock("@/lib/api/client", () => ({ getToken: vi.fn() }));
vi.mock("@/lib/auth/session", () => ({ clearToken: vi.fn() }));
vi.mock("next/link", () => ({
  default: ({ href, children }: { href: string; children: React.ReactNode }) => (
    <a href={href}>{children}</a>
  ),
}));
const activateMock = vi.mocked(activateAccount);
const getTokenMock = vi.mocked(getToken);
const clearTokenMock = vi.mocked(clearToken);

describe("ActivateAccountForm (HU #10179)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getTokenMock.mockReturnValue(null);
  });

  // AC1 — token válido, contraseña válida y coincide → activa cuenta y muestra éxito
  it("AC1: activa cuenta correctamente y muestra botón ir a login", async () => {
    activateMock.mockResolvedValue(undefined);
    render(<ActivateAccountForm token="valid-token-abc" />);

    fireEvent.change(screen.getByLabelText(/nueva contraseña/i), {
      target: { value: "FlitPass1!" },
    });
    fireEvent.change(screen.getByLabelText(/confirmar contraseña/i), {
      target: { value: "FlitPass1!" },
    });
    fireEvent.click(screen.getByRole("button", { name: /activar mi cuenta/i }));

    await waitFor(() =>
      expect(screen.getByRole("status")).toHaveTextContent(/cuenta fue activada/i),
    );
    expect(activateMock).toHaveBeenCalledWith("valid-token-abc", "FlitPass1!");
    expect(screen.getByRole("link", { name: /ir a iniciar sesión/i })).toHaveAttribute("href", "/login");
  });

  // Caso reportado: activar desde un navegador con otra sesión ya abierta debe
  // limpiarla y avisar, para que /login no rebote a la sesión vieja.
  it("si había una sesión activa en el navegador, la limpia y muestra el aviso", async () => {
    activateMock.mockResolvedValue(undefined);
    getTokenMock.mockReturnValue("admin-jwt");
    render(<ActivateAccountForm token="valid-token-abc" />);

    fireEvent.change(screen.getByLabelText(/nueva contraseña/i), {
      target: { value: "FlitPass1!" },
    });
    fireEvent.change(screen.getByLabelText(/confirmar contraseña/i), {
      target: { value: "FlitPass1!" },
    });
    fireEvent.click(screen.getByRole("button", { name: /activar mi cuenta/i }));

    await waitFor(() => expect(clearTokenMock).toHaveBeenCalled());
    expect(screen.getByText(/cerramos la sesión que estaba activa/i)).toBeInTheDocument();
  });

  it("sin sesión previa activa, no muestra el aviso", async () => {
    activateMock.mockResolvedValue(undefined);
    getTokenMock.mockReturnValue(null);
    render(<ActivateAccountForm token="valid-token-abc" />);

    fireEvent.change(screen.getByLabelText(/nueva contraseña/i), {
      target: { value: "FlitPass1!" },
    });
    fireEvent.change(screen.getByLabelText(/confirmar contraseña/i), {
      target: { value: "FlitPass1!" },
    });
    fireEvent.click(screen.getByRole("button", { name: /activar mi cuenta/i }));

    await waitFor(() => expect(clearTokenMock).toHaveBeenCalled());
    expect(screen.queryByText(/cerramos la sesión que estaba activa/i)).toBeNull();
  });

  // AC2 — contraseña débil → NO llama a la API, muestra política inline
  it("AC2: contraseña débil muestra política inline sin llamar la API", async () => {
    render(<ActivateAccountForm token="valid-token-abc" />);

    fireEvent.change(screen.getByLabelText(/nueva contraseña/i), {
      target: { value: "weak" },
    });
    fireEvent.change(screen.getByLabelText(/confirmar contraseña/i), {
      target: { value: "weak" },
    });
    fireEvent.click(screen.getByRole("button", { name: /activar mi cuenta/i }));

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(activateMock).not.toHaveBeenCalled();
  });

  // Token null → muestra error sin formulario
  it("sin token muestra error de enlace inválido", () => {
    render(<ActivateAccountForm token={null} />);
    expect(screen.getByRole("alert")).toHaveTextContent(/enlace de activación es inválido/i);
    expect(screen.queryByRole("button")).toBeNull();
  });

  // Token válido pero API responde 400 INVITATION_INVALID
  it("error 400 muestra mensaje de enlace inválido", async () => {
    activateMock.mockRejectedValue(new ApiError(400, "INVITATION_INVALID"));
    render(<ActivateAccountForm token="expired-token" />);

    fireEvent.change(screen.getByLabelText(/nueva contraseña/i), {
      target: { value: "FlitPass1!" },
    });
    fireEvent.change(screen.getByLabelText(/confirmar contraseña/i), {
      target: { value: "FlitPass1!" },
    });
    fireEvent.click(screen.getByRole("button", { name: /activar mi cuenta/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/inválido o ya fue utilizado/i);
  });
});

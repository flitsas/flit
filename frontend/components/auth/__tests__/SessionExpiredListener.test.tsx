import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SESSION_EXPIRED_EVENT } from "@/lib/auth/session";
import { SessionExpiredListener } from "../SessionExpiredListener";

const push = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push }),
  usePathname: () => "/admin/companies",
}));

const reauthenticate = vi.fn();
vi.mock("@flit/auth/client", () => ({ reauthenticate: () => reauthenticate() }));
const oidc = vi.fn(() => false);
vi.mock("@/lib/auth/session-mode", () => ({ isOidcSession: () => oidc() }));

describe("SessionExpiredListener (HU #10172 AC2)", () => {
  beforeEach(() => {
    push.mockClear();
    reauthenticate.mockReset();
    oidc.mockReturnValue(false);
  });

  it("no muestra nada hasta que la sesión expira", () => {
    render(<SessionExpiredListener />);
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("muestra el modal accesible y redirige a /login preservando returnUrl", () => {
    render(<SessionExpiredListener />);

    act(() => {
      window.dispatchEvent(new CustomEvent(SESSION_EXPIRED_EVENT));
    });

    const dialog = screen.getByRole("dialog");
    expect(dialog).toHaveAttribute("aria-modal", "true");

    fireEvent.click(screen.getByRole("button", { name: /iniciar sesión/i }));
    expect(push).toHaveBeenCalledWith("/login?returnUrl=%2Fadmin%2Fcompanies");
  });

  it("con la sesión de la suite pide una sesión nueva al hub sin mostrar el modal (cookie de otro usuario o cerrada)", () => {
    oidc.mockReturnValue(true);
    reauthenticate.mockReturnValue(true);
    render(<SessionExpiredListener />);

    act(() => {
      window.dispatchEvent(new CustomEvent(SESSION_EXPIRED_EVENT));
    });

    expect(reauthenticate).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("dialog")).toBeNull();
    // HU #13004: mientras recarga, una capa «Reconectando…» tapa los errores sueltos de la pantalla.
    expect(screen.getByRole("status")).toHaveTextContent("Reconectando tu sesión…");
  });

  it("con la sesión de la suite muestra el modal si ya se pidió una sesión nueva hace poco (sin bucle)", () => {
    oidc.mockReturnValue(true);
    reauthenticate.mockReturnValue(false);
    render(<SessionExpiredListener />);

    act(() => {
      window.dispatchEvent(new CustomEvent(SESSION_EXPIRED_EVENT));
    });

    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("con la sesión antigua no intenta nada más que el modal", () => {
    render(<SessionExpiredListener />);

    act(() => {
      window.dispatchEvent(new CustomEvent(SESSION_EXPIRED_EVENT));
    });

    expect(reauthenticate).not.toHaveBeenCalled();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
});

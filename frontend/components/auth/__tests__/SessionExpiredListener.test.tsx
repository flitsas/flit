import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SESSION_EXPIRED_EVENT } from "@/lib/auth/session";
import { SessionExpiredListener } from "../SessionExpiredListener";

const push = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push }),
  usePathname: () => "/admin/companies",
}));

describe("SessionExpiredListener (HU #10172 AC2)", () => {
  beforeEach(() => push.mockClear());

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
});

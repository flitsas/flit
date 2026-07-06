"use client";

import { type ReactNode } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { ArrowLeft, Users, ShieldCheck } from "lucide-react";

const logoWhite = "/assets/logo-flit-white.svg";
const logoDark = "/assets/logo-flit-dark.svg";

const NAV_ITEMS = [
  { href: "/empresa/usuarios", label: "Usuarios", icon: Users },
  { href: "/empresa/roles", label: "Roles", icon: ShieldCheck },
];

export default function EmpresaLayout({ children }: { children: ReactNode }) {
  const pathname = usePathname();

  return (
    <div
      className="min-h-screen flex flex-col"
      style={{ background: "#EEF5FF", color: "#162744", fontFamily: "Poppins, sans-serif" }}
    >
      <header
        className="shrink-0 flex items-center justify-between px-6 py-3 border-b bg-white"
      >
        <div className="flex items-center gap-4">
          <img src={logoDark} alt="FLIT 2.0" className="h-9 w-auto" />
          <div className="h-6 w-px" style={{ background: "#DFE5ED" }} />
          <span className="text-sm font-semibold" style={{ color: "#557EFF" }}>Mi Empresa</span>
        </div>
        <Link
          href="/"
          className="flex items-center gap-1.5 text-xs font-medium px-3 py-1.5 rounded-lg transition hover:bg-gray-100"
          style={{ color: "#162744" }}
        >
          <ArrowLeft className="h-3.5 w-3.5" />
          Volver al portal
        </Link>
      </header>

      <div className="flex flex-1 min-h-0">
        <nav
          className="w-56 shrink-0 border-r py-4 px-3 flex flex-col gap-1"
          style={{ background: "#FFFFFF" }}
        >
          {NAV_ITEMS.map(({ href, label, icon: Icon }) => {
            const active = pathname === href || pathname.startsWith(href + "/");
            return (
              <Link
                key={href}
                href={href}
                className="flex items-center gap-2.5 px-3 py-2 rounded-lg text-sm font-medium transition"
                style={{
                  background: active ? "rgba(85,126,255,0.10)" : "transparent",
                  color: active ? "#557EFF" : "#162744",
                }}
              >
                <Icon className="h-4 w-4" strokeWidth={active ? 2.4 : 1.8} />
                {label}
              </Link>
            );
          })}
        </nav>

        <main className="flex-1 min-h-0 overflow-y-auto p-6">{children}</main>
      </div>
    </div>
  );
}

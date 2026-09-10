import type { Metadata } from "next";
import "@/components/manual/manual-tokens.css";

export const metadata: Metadata = {
  title: {
    default: "Centro de Ayuda FLIT",
    template: "%s · Manual FLIT",
  },
  description: "Documentación operativa para Gestor y Organismo de Tránsito",
};

export default function ManualLayout({ children }: { children: React.ReactNode }) {
  return children;
}

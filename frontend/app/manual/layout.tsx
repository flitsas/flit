import type { Metadata } from "next";
import "@/components/manual/manual-tokens.css";
import { COPY } from "@/lib/copy/copy-catalog";

export const metadata: Metadata = {
  title: {
    default: "Centro de Ayuda FLIT",
    template: "%s · Manual FLIT",
  },
  description: `Documentación operativa para ${COPY.A06} y ${COPY.A05}`,
};

export default function ManualLayout({ children }: { children: React.ReactNode }) {
  return children;
}

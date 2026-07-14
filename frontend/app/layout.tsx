import type { Metadata } from "next";
import { JetBrains_Mono, Poppins } from "next/font/google";
import { SessionExpiredListener } from "@/components/auth/SessionExpiredListener";
import "./globals.css";

const poppins = Poppins({
  variable: "--font-poppins",
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
});

const jetbrainsMono = JetBrains_Mono({
  variable: "--font-jetbrains-mono",
  subsets: ["latin"],
  weight: ["400", "500", "700"],
});

export const metadata: Metadata = {
  title: "FLIT 2.0",
  description: "Plataforma de trámites vehiculares",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html
      lang="es"
      suppressHydrationWarning
      className={`${poppins.variable} ${jetbrainsMono.variable} h-full`}
    >
      <body suppressHydrationWarning className="h-full font-sans antialiased">
        {children}
        <SessionExpiredListener />
      </body>
    </html>
  );
}

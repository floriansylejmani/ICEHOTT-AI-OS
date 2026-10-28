import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "ICEHOTT AI OS | AI Workspace Platform",
  description: "ICEHOTT AI OS V2 — a secure AI workspace platform for teams, agents, knowledge, tools and workflows.",
  robots: { index: false, follow: false },
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body className="min-h-screen font-sans antialiased">{children}</body>
    </html>
  );
}

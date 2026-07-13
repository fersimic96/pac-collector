

import { type ReactNode } from "react";
import { Sidebar, type SidebarKey } from "./Sidebar";
import { TopBar } from "./TopBar";

interface Props {
  current: SidebarKey;
  onNavigate: (k: SidebarKey) => void;
  children: ReactNode;
}

export function AppShell({ current, onNavigate, children }: Props) {
  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: "240px 1fr",
        gridTemplateRows: "44px 1fr",
        gridTemplateAreas: `"topbar topbar" "sidebar main"`,
        height: "100vh",
        width: "100vw",
      }}
    >
      <div style={{ gridArea: "topbar" }}>
        <TopBar />
      </div>
      <div
        style={{
          gridArea: "sidebar",
          background: "var(--bg-elev-1)",
          borderRight: "1px solid var(--border)",
          overflow: "auto",
        }}
      >
        <Sidebar current={current} onNavigate={onNavigate} />
      </div>
      <main
        style={{
          gridArea: "main",
          overflow: "auto",
          padding: 24,
          background: "var(--bg)",
        }}
      >
        {children}
      </main>
    </div>
  );
}

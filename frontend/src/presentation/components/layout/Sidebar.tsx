

export type SidebarKey =
  | "dashboard"
  | "instruments"
  | "samples"
  | "analysis"
  | "plugins"
  | "config"
  | "logs";

interface NavItem {
  key: SidebarKey;
  label: string;
  icon: string; 
}

const NAV: NavItem[] = [
  { key: "dashboard", label: "Dashboard", icon: "M3 3h7v7H3zM14 3h7v7h-7zM14 14h7v7h-7zM3 14h7v7H3z" },
  { key: "instruments", label: "Equipos", icon: "M9 3h6m-6 0a3 3 0 003 3v3m0 0h6m-9-6v6m6-6v6m-3 0v12m0 0h-6m6 0h6" },
  { key: "samples", label: "Muestras", icon: "M3 7h18M3 12h18M3 17h18" },
  { key: "analysis", label: "Análisis", icon: "M3 3v18h18M7 14l3-3 4 4 5-7" },
  { key: "plugins", label: "Plugins", icon: "M10 2v6h4V2M2 10h6v4H2M14 22v-6h6v6M16 10h6v4h-6" },
  { key: "config", label: "Configuración", icon: "M12 15a3 3 0 100-6 3 3 0 000 6z M19.4 15a1.65 1.65 0 00.33 1.82l.06.06a2 2 0 010 2.83 2 2 0 01-2.83 0l-.06-.06a1.65 1.65 0 00-1.82-.33 1.65 1.65 0 00-1 1.51V21a2 2 0 01-2 2 2 2 0 01-2-2v-.09A1.65 1.65 0 009 19.4a1.65 1.65 0 00-1.82.33l-.06.06a2 2 0 01-2.83 0 2 2 0 010-2.83l.06-.06a1.65 1.65 0 00.33-1.82 1.65 1.65 0 00-1.51-1H3a2 2 0 01-2-2 2 2 0 012-2h.09A1.65 1.65 0 004.6 9a1.65 1.65 0 00-.33-1.82l-.06-.06a2 2 0 010-2.83 2 2 0 012.83 0l.06.06a1.65 1.65 0 001.82.33H9a1.65 1.65 0 001-1.51V3a2 2 0 012-2 2 2 0 012 2v.09a1.65 1.65 0 001 1.51 1.65 1.65 0 001.82-.33l.06-.06a2 2 0 012.83 0 2 2 0 010 2.83l-.06.06a1.65 1.65 0 00-.33 1.82V9a1.65 1.65 0 001.51 1H21a2 2 0 012 2 2 2 0 01-2 2h-.09a1.65 1.65 0 00-1.51 1z" },
  { key: "logs", label: "Logs", icon: "M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z M14 2v6h6 M16 13H8 M16 17H8 M10 9H8" },
];

interface Props {
  current: SidebarKey;
  onNavigate: (k: SidebarKey) => void;
}

export function Sidebar({ current, onNavigate }: Props) {
  return (
    <nav style={{ padding: 8, display: "flex", flexDirection: "column", gap: 2 }}>
      {NAV.map((item) => {
        const active = item.key === current;
        return (
          <button
            key={item.key}
            onClick={() => onNavigate(item.key)}
            style={{
              display: "flex",
              alignItems: "center",
              gap: 10,
              padding: "8px 10px",
              borderRadius: "var(--radius-sm)",
              background: active ? "var(--bg-elev-3)" : "transparent",
              color: active ? "var(--fg)" : "var(--fg-muted)",
              fontSize: 12,
              fontWeight: active ? 500 : 400,
              transition: "background 80ms ease",
              textAlign: "left",
            }}
            onMouseEnter={(e) => {
              if (!active) (e.currentTarget.style.background = "var(--bg-elev-2)");
            }}
            onMouseLeave={(e) => {
              if (!active) (e.currentTarget.style.background = "transparent");
            }}
          >
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d={item.icon} />
            </svg>
            {item.label}
          </button>
        );
      })}
    </nav>
  );
}

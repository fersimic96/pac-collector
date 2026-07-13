

import { useEffect, useState } from "react";
import { ipcInvoke } from "@/infrastructure/ipc/tauriClient";
import type { PluginOutputDTO } from "@/domain/outputDTO/PluginOutputDTO";

export function PluginsView() {
  const [plugins, setPlugins] = useState<PluginOutputDTO[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<string | null>(null);

  const refresh = async () => {
    const list = await ipcInvoke<PluginOutputDTO[]>("list_plugins");
    setPlugins(list);
  };

  useEffect(() => {
    refresh().finally(() => setLoading(false));
  }, []);

  const toggle = async (id: string, enabled: boolean) => {
    setBusy(id);
    try {
      await ipcInvoke<void>("set_plugin_enabled", { id, enabled });
      await refresh();
    } finally {
      setBusy(null);
    }
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      <header style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline" }}>
        <div>
          <h1 style={{ margin: 0, fontSize: 22, fontWeight: 600 }}>Plugins</h1>
          <p style={{ margin: "4px 0 0", color: "var(--fg-muted)", fontSize: 12 }}>
            Cada modelo de equipo tiene su propio plugin que sabe interpretar su payload.
            Deshabilitá un plugin si querés que sus payloads vayan a <code>_unknown/</code>{" "}
            en lugar de procesarse.
          </p>
        </div>
        <button
          disabled
          title="Disponible en Fase F (carga de plugins externos WASM)"
          style={{
            background: "var(--bg-elev-3)",
            border: "1px solid var(--border)",
            borderRadius: "var(--radius-sm)",
            padding: "6px 12px",
            color: "var(--fg-dim)",
            fontSize: 12,
            cursor: "not-allowed",
          }}
        >
          + Subir plugin externo (.wasm)
        </button>
      </header>

      {loading ? (
        <div style={{ color: "var(--fg-muted)", fontSize: 12 }}>Cargando…</div>
      ) : (
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(280px, 1fr))", gap: 12 }}>
          {plugins.map((p) => (
            <PluginCard
              key={p.id}
              plugin={p}
              busy={busy === p.id}
              onToggle={(enabled) => toggle(p.id, enabled)}
            />
          ))}
        </div>
      )}
    </div>
  );
}

function PluginCard({
  plugin,
  busy,
  onToggle,
}: {
  plugin: PluginOutputDTO;
  busy: boolean;
  onToggle: (enabled: boolean) => void;
}) {
  const isBuiltin = plugin.source.kind === "builtin";
  return (
    <div
      style={{
        background: "var(--bg-elev-2)",
        border: "1px solid var(--border)",
        borderRadius: "var(--radius)",
        padding: 16,
        opacity: plugin.enabled ? 1 : 0.65,
      }}
    >
      <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 8 }}>
        <strong style={{ fontSize: 13 }}>{plugin.displayName}</strong>
        <span
          style={{
            fontSize: 10,
            padding: "2px 6px",
            borderRadius: 4,
            background: isBuiltin ? "rgba(59,130,246,0.2)" : "rgba(16,185,129,0.2)",
            color: isBuiltin ? "var(--accent)" : "var(--green)",
            border: `1px solid ${isBuiltin ? "var(--accent)" : "var(--green)"}`,
          }}
        >
          {isBuiltin ? "built-in" : "external"}
        </span>
      </div>
      <div style={{ fontSize: 11, color: "var(--fg-muted)", marginBottom: 8 }}>
        v{plugin.version} · {plugin.vendor}
      </div>
      <div style={{ fontSize: 11, color: "var(--fg-muted)", marginBottom: 12 }}>
        Soporta: {plugin.supportedTypes.join(", ")}
      </div>
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          paddingTop: 10,
          borderTop: "1px solid var(--border)",
        }}
      >
        <span
          style={{
            fontSize: 11,
            color: plugin.enabled ? "var(--green)" : "var(--fg-dim)",
          }}
        >
          {plugin.enabled ? "✓ activo" : "○ deshabilitado"}
        </span>
        <button
          onClick={() => onToggle(!plugin.enabled)}
          disabled={busy}
          style={{
            fontSize: 11,
            padding: "4px 10px",
            borderRadius: 6,
            border: "1px solid var(--border)",
            background: plugin.enabled ? "transparent" : "var(--accent)",
            color: plugin.enabled ? "var(--fg)" : "white",
            cursor: busy ? "wait" : "pointer",
            opacity: busy ? 0.6 : 1,
          }}
          aria-label={plugin.enabled ? "Deshabilitar plugin" : "Habilitar plugin"}
        >
          {busy ? "..." : plugin.enabled ? "Deshabilitar" : "Habilitar"}
        </button>
      </div>
    </div>
  );
}

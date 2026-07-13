

import { useEffect, useRef, useState } from "react";
import { ipcListenAppEvent } from "@/infrastructure/ipc/tauriClient";
import type { AppEvent } from "@/lib/events";

interface LogLine {
  ts: string;
  level: "info" | "ok" | "warn" | "error";
  text: string;
}

export function LogsView() {
  const [lines, setLines] = useState<LogLine[]>([]);
  const [paused, setPaused] = useState(false);
  const [filter, setFilter] = useState("");
  const containerRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const unsub = ipcListenAppEvent<AppEvent>((e) => {
      if (paused) return;
      const line = formatLine(e);
      if (line) {
        setLines((prev) => [...prev.slice(-499), line]);
      }
    });
    return () => {
      unsub.then((fn) => fn());
    };
  }, [paused]);

  
  useEffect(() => {
    const c = containerRef.current;
    if (c) c.scrollTop = c.scrollHeight;
  }, [lines]);

  const visible = filter
    ? lines.filter((l) => l.text.toLowerCase().includes(filter.toLowerCase()))
    : lines;

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 12, height: "100%" }}>
      <header style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline" }}>
        <div>
          <h1 style={{ margin: 0, fontSize: 22, fontWeight: 600 }}>Logs</h1>
          <p style={{ margin: "4px 0 0", color: "var(--fg-muted)", fontSize: 12 }}>
            {lines.length} eventos · {paused ? "pausado" : "en vivo"}
          </p>
        </div>
        <div style={{ display: "flex", gap: 8 }}>
          <input
            placeholder="Filtrar"
            value={filter}
            onChange={(e) => setFilter(e.currentTarget.value)}
            style={{
              background: "var(--bg-elev-3)",
              border: "1px solid var(--border)",
              borderRadius: "var(--radius-sm)",
              padding: "6px 10px",
              color: "var(--fg)",
              fontSize: 12,
              width: 160,
            }}
          />
          <button
            onClick={() => setPaused((p) => !p)}
            style={{
              background: "var(--bg-elev-3)",
              border: "1px solid var(--border)",
              borderRadius: "var(--radius-sm)",
              padding: "6px 12px",
              color: "var(--fg)",
              fontSize: 12,
            }}
          >
            {paused ? "▶ reanudar" : "❙❙ pausar"}
          </button>
          <button
            onClick={() => setLines([])}
            style={{
              background: "var(--bg-elev-3)",
              border: "1px solid var(--border)",
              borderRadius: "var(--radius-sm)",
              padding: "6px 12px",
              color: "var(--fg)",
              fontSize: 12,
            }}
          >
            limpiar
          </button>
        </div>
      </header>

      <div
        ref={containerRef}
        style={{
          flex: 1,
          background: "#050505",
          border: "1px solid var(--border)",
          borderRadius: "var(--radius)",
          padding: 12,
          overflow: "auto",
          fontFamily: "ui-monospace, SF Mono, Consolas, monospace",
          fontSize: 11,
          lineHeight: 1.6,
        }}
      >
        {visible.length === 0 ? (
          <div style={{ color: "var(--fg-dim)", padding: 8 }}>
            Esperando eventos… (beacons UDP, conexiones TCP, muestras nuevas)
          </div>
        ) : (
          visible.map((l, i) => (
            <div key={i} style={{ display: "flex", gap: 8 }}>
              <span style={{ color: "var(--fg-dim)" }}>{l.ts}</span>
              <span style={{ color: levelColor(l.level), minWidth: 50 }}>[{l.level}]</span>
              <span style={{ color: "var(--fg)" }}>{l.text}</span>
            </div>
          ))
        )}
      </div>
    </div>
  );
}

function formatLine(e: AppEvent): LogLine | null {
  const ts = new Date().toLocaleTimeString("es-AR", { hour12: false });
  switch (e.type) {
    case "beacon_received":
      return { ts, level: "info", text: `beacon ← ${e.data.ip}` };
    case "instrument_discovered":
      return {
        ts,
        level: "ok",
        text: `equipo nuevo: ${e.data.serial} (${e.data.analyzer_type}) ${e.data.ip ?? ""}`,
      };
    case "instrument_touched":
      return null; 
    case "sample_received":
      return {
        ts,
        level: "ok",
        text: `muestra ${e.data.sample_identifier} de SN ${e.data.serial} (IBP=${e.data.ibp ?? "?"}, FBP=${e.data.fbp ?? "?"})`,
      };
    case "sample_duplicate_skipped":
      return {
        ts,
        level: "warn",
        text: `duplicado omitido: SN ${e.data.serial} muestra ${e.data.sample_identifier}`,
      };
    case "plugin_parse_failed":
      return {
        ts,
        level: "error",
        text: `parse fail (${e.data.analyzer_type}): ${e.data.reason}`,
      };
    case "unknown_payload_received":
      return {
        ts,
        level: "warn",
        text: `payload desconocido (${e.data.analyzer_type ?? "<sin AnalyzerType>"}, ${e.data.bytes}B) de ${e.data.source_ip ?? "?"} → ${e.data.saved_path}`,
      };
    case "persistence_failed":
      return {
        ts,
        level: "error",
        text: `persistencia falló (${e.data.stage}): ${e.data.reason}${e.data.serial ? ` — SN ${e.data.serial}` : ""}`,
      };
    case "server_error":
      return { ts, level: "error", text: `server: ${e.data.message}` };
  }
}

function levelColor(level: string) {
  switch (level) {
    case "ok": return "var(--green)";
    case "warn": return "var(--amber)";
    case "error": return "var(--red)";
    default: return "var(--accent)";
  }
}

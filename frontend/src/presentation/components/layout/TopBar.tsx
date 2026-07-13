

import { useEffect, useState } from "react";
import {
  getServerStatus,
  startListeners,
  stopListeners,
  startPrintListener,
  stopPrintListener,
  type ServerStatusDTO,
} from "@/infrastructure/repositories/SampleRepositoryImpl";

export function TopBar() {
  const [status, setStatus] = useState<ServerStatusDTO | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [printBusy, setPrintBusy] = useState(false);

  const refresh = async () => {
    try {
      const s = await getServerStatus();
      setStatus(s);
      setError(null);
    } catch (e) {
      setError(String(e));
    }
  };

  useEffect(() => {
    refresh();
    const id = setInterval(refresh, 5000);
    return () => clearInterval(id);
  }, []);

  const toggle = async () => {
    if (!status || busy) return;
    setBusy(true);
    try {
      if (status.running) {
        await stopListeners();
      } else {
        await startListeners();
      }
      await refresh();
    } catch (e) {
      setError(String(e));
    } finally {
      setBusy(false);
    }
  };

  const togglePrint = async () => {
    if (!status || printBusy) return;
    setPrintBusy(true);
    try {
      if (status.printRunning) {
        await stopPrintListener();
      } else {
        await startPrintListener();
      }
      await refresh();
    } catch (e) {
      setError(String(e));
    } finally {
      setPrintBusy(false);
    }
  };

  return (
    <header
      style={{
        height: 44,
        display: "flex",
        alignItems: "center",
        gap: 12,
        padding: "0 16px",
        borderBottom: "1px solid var(--border)",
        background: "var(--bg-elev-1)",
      }}
    >
      <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
        <div
          style={{
            width: 22,
            height: 22,
            borderRadius: 6,
            background: "linear-gradient(135deg, #3b82f6 0%, #8b5cf6 100%)",
          }}
        />
        <strong style={{ fontSize: 13, letterSpacing: 0.2 }}>
          PAC Collector
        </strong>
      </div>

      <div style={{ flex: 1 }} />

      {error ? (
        <span
          style={{
            fontSize: 11,
            color: "var(--red)",
            display: "flex",
            alignItems: "center",
            gap: 6,
            maxWidth: 600,
          }}
          title={error}
        >
          <Dot color="var(--red)" /> Backend: {error.slice(0, 80)}
        </span>
      ) : status ? (
        <>
          <span
            style={{
              fontSize: 11,
              color: "var(--fg-muted)",
              display: "flex",
              alignItems: "center",
              gap: 12,
            }}
          >
            <span style={{ display: "flex", alignItems: "center", gap: 6 }}>
              <Dot color={status.running ? "var(--green)" : "var(--fg-muted)"} />
              {status.running ? "Server vivo" : "Detenido"}
            </span>
            <span>UDP {status.udpPort} · TCP {status.tcpPort}</span>
            <span>{status.instrumentsCount} equipos · {status.samplesToday} muestras hoy</span>
          </span>
          <button
            onClick={toggle}
            disabled={busy}
            style={{
              fontSize: 11,
              padding: "4px 10px",
              borderRadius: 6,
              border: "1px solid var(--border)",
              background: status.running ? "transparent" : "var(--accent)",
              color: status.running ? "var(--fg)" : "white",
              cursor: busy ? "wait" : "pointer",
              opacity: busy ? 0.6 : 1,
            }}
            aria-label={status.running ? "Detener listeners" : "Iniciar listeners"}
          >
            {busy ? "..." : status.running ? "Detener" : "Iniciar"}
          </button>

          <span
            style={{
              fontSize: 11,
              color: "var(--fg-muted)",
              display: "flex",
              alignItems: "center",
              gap: 8,
              marginLeft: 8,
              paddingLeft: 12,
              borderLeft: "1px solid var(--border)",
            }}
          >
            <Dot color={status.printRunning ? "var(--green)" : "var(--fg-muted)"} />
            {status.printRunning ? "Print vivo" : "Print detenido"}
            <span>· IPP {status.printPort}</span>
          </span>
          <button
            onClick={togglePrint}
            disabled={printBusy}
            style={{
              fontSize: 11,
              padding: "4px 10px",
              borderRadius: 6,
              border: "1px solid var(--border)",
              background: status.printRunning ? "transparent" : "var(--accent)",
              color: status.printRunning ? "var(--fg)" : "white",
              cursor: printBusy ? "wait" : "pointer",
              opacity: printBusy ? 0.6 : 1,
            }}
            aria-label={
              status.printRunning ? "Detener print server" : "Iniciar print server"
            }
          >
            {printBusy ? "..." : status.printRunning ? "Detener Print" : "Iniciar Print"}
          </button>
        </>
      ) : (
        <span style={{ fontSize: 11, color: "var(--fg-muted)" }}>conectando…</span>
      )}
    </header>
  );
}

function Dot({ color }: { color: string }) {
  return (
    <span
      style={{
        width: 8,
        height: 8,
        borderRadius: "50%",
        background: color,
        boxShadow: `0 0 6px ${color}`,
        display: "inline-block",
      }}
    />
  );
}

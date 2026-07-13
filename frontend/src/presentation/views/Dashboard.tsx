

import { useEffect, useState } from "react";
import { ipcListenAppEvent } from "@/infrastructure/ipc/tauriClient";
import {
  getServerStatus,
  type ServerStatusDTO,
} from "@/infrastructure/repositories/SampleRepositoryImpl";
import { InstrumentRepositoryImpl } from "@/infrastructure/repositories/InstrumentRepositoryImpl";
import { ConfigRepositoryImpl } from "@/infrastructure/repositories/ConfigRepositoryImpl";
import type { InstrumentOutputDTO } from "@/domain/outputDTO/InstrumentOutputDTO";
import type { AppEvent } from "@/lib/events";
import { Instrument } from "@/domain/entities/Instrument";

interface PendingInstrument {
  serial: string;
  analyzerType: string;
  ip: string | null;
}

const configRepo = new ConfigRepositoryImpl();

const instrumentRepo = new InstrumentRepositoryImpl();

export function Dashboard() {
  const [status, setStatus] = useState<ServerStatusDTO | null>(null);
  const [instruments, setInstruments] = useState<InstrumentOutputDTO[]>([]);
  const [feed, setFeed] = useState<FeedEntry[]>([]);
  const [pendingInstrument, setPendingInstrument] = useState<PendingInstrument | null>(null);

  
  useEffect(() => {
    refresh();
  }, []);

  
  useEffect(() => {
    const unsub = ipcListenAppEvent<AppEvent>((e) => {
      handleEvent(e, setFeed, refresh, setPendingInstrument);
    });
    return () => {
      unsub.then((fn) => fn());
    };
  }, []);

  const refresh = async () => {
    try {
      const [s, list] = await Promise.all([
        getServerStatus(),
        instrumentRepo.listAll(),
      ]);
      setStatus(s);
      setInstruments(list);
    } catch (e) {
      console.warn("refresh failed:", e);
    }
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 24 }}>
      <header>
        <h1 style={{ margin: 0, fontSize: 22, fontWeight: 600 }}>Dashboard</h1>
        <p style={{ margin: "4px 0 0", color: "var(--fg-muted)", fontSize: 12 }}>
          Estado en tiempo real de los equipos PAC
        </p>
      </header>

      <section style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 12 }}>
        <Stat label="Equipos detectados" value={instruments.length} />
        <Stat
          label="Equipos online"
          value={instruments.filter((i) => Instrument.isOnline(i)).length}
          accent="var(--green)"
        />
        <Stat label="Muestras hoy" value={status?.samplesToday ?? "—"} />
        <Stat
          label="Servidor"
          value={status ? `${status.serverIp}` : "…"}
          subtitle={status ? `UDP ${status.udpPort} · TCP ${status.tcpPort}` : ""}
          small
        />
      </section>

      <section
        style={{
          display: "grid",
          gridTemplateColumns: "1.4fr 1fr",
          gap: 16,
          alignItems: "start",
        }}
      >
        <Card title="Equipos">
          {instruments.length === 0 ? (
            <Empty
              text="Ningún equipo detectado todavía"
              hint="Conectá cualquier equipo PAC (OptiPMD, OptiCPP, OptiFPP, OptiFZP, OptiMPP, OptiMVD, OptiFuel) al cable Ethernet."
            />
          ) : (
            <ul style={{ listStyle: "none", padding: 0, margin: 0, display: "flex", flexDirection: "column", gap: 8 }}>
              {instruments.map((inst) => (
                <InstrumentRow key={inst.serial} instrument={inst} />
              ))}
            </ul>
          )}
        </Card>

        <Card title="Actividad en vivo">
          {feed.length === 0 ? (
            <Empty text="Esperando eventos…" hint="Beacons UDP, conexiones TCP, muestras nuevas." />
          ) : (
            <ul style={{ listStyle: "none", padding: 0, margin: 0, maxHeight: 360, overflow: "auto", display: "flex", flexDirection: "column", gap: 6 }}>
              {feed.slice(0, 50).map((entry, i) => (
                <li
                  key={i}
                  style={{
                    fontSize: 11,
                    color: "var(--fg-muted)",
                    fontFamily: "ui-monospace, SF Mono, Consolas, monospace",
                    display: "flex",
                    gap: 8,
                  }}
                >
                  <span style={{ color: entry.color, minWidth: 8 }}>●</span>
                  <span style={{ color: "var(--fg-dim)", minWidth: 60 }}>{entry.time}</span>
                  <span style={{ color: "var(--fg)" }}>{entry.title}</span>
                  {entry.subtitle && <span>{entry.subtitle}</span>}
                </li>
              ))}
            </ul>
          )}
        </Card>
      </section>

      {pendingInstrument && (
        <NewInstrumentModal
          instrument={pendingInstrument}
          onClose={() => setPendingInstrument(null)}
        />
      )}
    </div>
  );
}

interface FeedEntry {
  time: string;
  color: string;
  title: string;
  subtitle?: string;
}

function handleEvent(
  e: AppEvent,
  setFeed: React.Dispatch<React.SetStateAction<FeedEntry[]>>,
  refresh: () => void,
  setPending: React.Dispatch<React.SetStateAction<PendingInstrument | null>>,
) {
  const time = new Date().toLocaleTimeString("es-AR", { hour12: false });
  let entry: FeedEntry | null = null;

  switch (e.type) {
    case "beacon_received":
      entry = { time, color: "var(--fg-dim)", title: "beacon", subtitle: e.data.ip };
      break;
    case "instrument_discovered":
      entry = {
        time,
        color: "var(--accent)",
        title: `equipo nuevo: ${e.data.serial} (${e.data.analyzer_type})`,
        subtitle: e.data.ip ?? "",
      };
      
      configRepo.load().then((cfg) => {
        const inst = cfg.instruments[e.data.analyzer_type];
        if (!inst?.outputDir) {
          setPending({ serial: e.data.serial, analyzerType: e.data.analyzer_type, ip: e.data.ip });
        }
      });
      refresh();
      break;
    case "sample_received":
      entry = {
        time,
        color: "var(--green)",
        title: `muestra ${e.data.sample_identifier}`,
        subtitle: `SN ${e.data.serial}` + (e.data.ibp ? ` · IBP ${e.data.ibp.toFixed(1)}` : ""),
      };
      refresh();
      break;
    case "sample_duplicate_skipped":
      entry = {
        time,
        color: "var(--amber)",
        title: `duplicado omitido`,
        subtitle: `${e.data.serial} ${e.data.sample_identifier}`,
      };
      break;
    case "plugin_parse_failed":
      entry = {
        time,
        color: "var(--red)",
        title: `parse fail: ${e.data.analyzer_type}`,
        subtitle: e.data.reason,
      };
      break;
    case "unknown_payload_received":
      entry = {
        time,
        color: "var(--amber)",
        title: `payload desconocido (${e.data.analyzer_type ?? "sin tipo"})`,
        subtitle: `${e.data.bytes}B de ${e.data.source_ip ?? "?"} → guardado en _unknown/`,
      };
      break;
    case "server_error":
      entry = { time, color: "var(--red)", title: "server error", subtitle: e.data.message };
      break;
  }

  if (entry) setFeed((prev) => [entry!, ...prev].slice(0, 200));
}

function Stat({
  label,
  value,
  subtitle,
  accent,
  small,
}: {
  label: string;
  value: string | number;
  subtitle?: string;
  accent?: string;
  small?: boolean;
}) {
  return (
    <div
      style={{
        background: "var(--bg-elev-2)",
        border: "1px solid var(--border)",
        borderRadius: "var(--radius)",
        padding: 16,
      }}
    >
      <div style={{ fontSize: 11, color: "var(--fg-muted)", marginBottom: 6 }}>{label}</div>
      <div
        style={{
          fontSize: small ? 16 : 24,
          fontWeight: 600,
          color: accent ?? "var(--fg)",
          fontFamily: "ui-monospace, SF Mono, Consolas, monospace",
          letterSpacing: -0.5,
        }}
      >
        {value}
      </div>
      {subtitle && (
        <div style={{ fontSize: 11, color: "var(--fg-muted)", marginTop: 4 }}>{subtitle}</div>
      )}
    </div>
  );
}

function Card({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div
      style={{
        background: "var(--bg-elev-2)",
        border: "1px solid var(--border)",
        borderRadius: "var(--radius)",
        padding: 16,
      }}
    >
      <h2 style={{ margin: "0 0 12px", fontSize: 13, fontWeight: 500, color: "var(--fg)" }}>
        {title}
      </h2>
      {children}
    </div>
  );
}

function Empty({ text, hint }: { text: string; hint?: string }) {
  return (
    <div
      style={{
        padding: "24px 8px",
        textAlign: "center",
        color: "var(--fg-muted)",
        fontSize: 12,
      }}
    >
      <div>{text}</div>
      {hint && <div style={{ marginTop: 6, color: "var(--fg-dim)", fontSize: 11 }}>{hint}</div>}
    </div>
  );
}

function NewInstrumentModal({
  instrument,
  onClose,
}: {
  instrument: PendingInstrument;
  onClose: () => void;
}) {
  const [outputDir, setOutputDir] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const pick = async () => {
    const folder = await configRepo.pickFolder("Carpeta de salida para " + instrument.analyzerType);
    if (folder) setOutputDir(folder);
  };

  const save = async () => {
    if (!outputDir) return;
    setSaving(true);
    try {
      const cfg = await configRepo.load();
      const existing = cfg.instruments[instrument.analyzerType] ?? {
        enabled: true,
        alias: null,
        outputDir: null,
        recentDir: null,
        showKey: null,
        showUnit: null,
        selectedParameters: null,
        hotFolderDir: null,
        hotFolderFormat: null,
      };
      await configRepo.save({
        ...cfg,
        instruments: {
          ...cfg.instruments,
          [instrument.analyzerType]: { ...existing, outputDir },
        },
      });
      onClose();
    } finally {
      setSaving(false);
    }
  };

  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.6)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 1000,
      }}
    >
      <div
        style={{
          background: "var(--bg-elev-2)",
          border: "1px solid var(--border)",
          borderRadius: "var(--radius)",
          padding: 24,
          width: 480,
          display: "flex",
          flexDirection: "column",
          gap: 16,
        }}
      >
        <div>
          <h2 style={{ margin: "0 0 6px", fontSize: 15, fontWeight: 600 }}>
            Equipo nuevo detectado
          </h2>
          <p style={{ margin: 0, fontSize: 12, color: "var(--fg-muted)" }}>
            {instrument.analyzerType} · SN {instrument.serial}
            {instrument.ip ? ` · ${instrument.ip}` : ""}
          </p>
        </div>

        <p style={{ margin: 0, fontSize: 12, color: "var(--fg)" }}>
          Elegí la carpeta donde se van a guardar las muestras de este equipo.
          Si omitís, se usa la carpeta global de la configuración.
        </p>

        <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
          <div
            style={{
              flex: 1,
              background: "var(--bg-elev-3)",
              border: "1px solid var(--border)",
              borderRadius: "var(--radius-sm)",
              padding: "6px 10px",
              fontSize: 12,
              color: outputDir ? "var(--fg)" : "var(--fg-dim)",
              fontFamily: "ui-monospace, SF Mono, Consolas, monospace",
              overflow: "hidden",
              textOverflow: "ellipsis",
              whiteSpace: "nowrap",
            }}
          >
            {outputDir ?? "— ninguna carpeta elegida —"}
          </div>
          <button
            onClick={pick}
            style={{
              padding: "6px 14px",
              background: "var(--bg-elev-3)",
              border: "1px solid var(--border)",
              borderRadius: "var(--radius-sm)",
              color: "var(--fg)",
              fontSize: 12,
              cursor: "pointer",
            }}
          >
            Elegir…
          </button>
        </div>

        <div style={{ display: "flex", justifyContent: "flex-end", gap: 8, marginTop: 4 }}>
          <button
            onClick={onClose}
            style={{
              padding: "7px 18px",
              background: "transparent",
              border: "1px solid var(--border)",
              borderRadius: "var(--radius-sm)",
              color: "var(--fg-muted)",
              fontSize: 12,
              cursor: "pointer",
            }}
          >
            Omitir por ahora
          </button>
          <button
            onClick={save}
            disabled={!outputDir || saving}
            style={{
              padding: "7px 18px",
              background: outputDir ? "var(--accent)" : "var(--bg-elev-3)",
              border: "1px solid " + (outputDir ? "var(--accent)" : "var(--border)"),
              borderRadius: "var(--radius-sm)",
              color: outputDir ? "#fff" : "var(--fg-dim)",
              fontSize: 12,
              fontWeight: 500,
              cursor: outputDir ? "pointer" : "not-allowed",
            }}
          >
            {saving ? "Guardando…" : "Guardar carpeta"}
          </button>
        </div>
      </div>
    </div>
  );
}

function InstrumentRow({ instrument }: { instrument: InstrumentOutputDTO }) {
  const online = Instrument.isOnline(instrument);
  return (
    <li
      style={{
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        padding: 10,
        background: "var(--bg-elev-3)",
        borderRadius: "var(--radius-sm)",
        border: "1px solid var(--border)",
      }}
    >
      <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
        <span
          style={{
            width: 6,
            height: 6,
            borderRadius: "50%",
            background: online ? "var(--green)" : "var(--fg-dim)",
            boxShadow: online ? "0 0 6px var(--green)" : "none",
          }}
        />
        <div>
          <div style={{ fontWeight: 500, fontSize: 13 }}>{Instrument.displayName(instrument)}</div>
          <div style={{ fontSize: 11, color: "var(--fg-muted)" }}>
            {instrument.lastIp ?? "?"} · {instrument.totalSamples} muestras
          </div>
        </div>
      </div>
      <div style={{ fontSize: 11, color: "var(--fg-muted)" }}>
        {online ? "online" : "offline"}
      </div>
    </li>
  );
}

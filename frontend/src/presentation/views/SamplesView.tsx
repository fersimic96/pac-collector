

import { useEffect, useState, useMemo } from "react";
import { ipcListenAppEvent } from "@/infrastructure/ipc/tauriClient";
import {
  SampleRepositoryImpl,
  type SampleFilters,
  type SamplePage,
} from "@/infrastructure/repositories/SampleRepositoryImpl";
import type { SampleOutputDTO } from "@/domain/outputDTO/SampleOutputDTO";
import type { AppEvent } from "@/lib/events";

const repo = new SampleRepositoryImpl();
const PAGE_SIZE = 50;

export function SamplesView() {
  const [page, setPage] = useState<SamplePage | null>(null);
  const [offset, setOffset] = useState(0);
  const [filters, setFilters] = useState<SampleFilters>({});
  const [selectedUuid, setSelectedUuid] = useState<string | null>(null);
  const [selectedSample, setSelectedSample] = useState<SampleOutputDTO | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  
  useEffect(() => {
    fetchPage();
  }, [filters, offset]);

  
  useEffect(() => {
    const unsub = ipcListenAppEvent<AppEvent>((e) => {
      if (e.type === "sample_received") {
        
        if (offset === 0) fetchPage();
      }
    });
    return () => {
      unsub.then((fn) => fn());
    };
  }, [offset]);

  
  useEffect(() => {
    if (!selectedUuid) {
      setSelectedSample(null);
      return;
    }
    repo.getByUuid(selectedUuid).then(setSelectedSample);
  }, [selectedUuid]);

  const fetchPage = async () => {
    setLoading(true);
    setError(null);
    try {
      const p = await repo.listPaginated(filters, offset, PAGE_SIZE);
      setPage(p);
    } catch (e) {
      setError(String(e));
    } finally {
      setLoading(false);
    }
  };

  const total = page?.total ?? 0;
  const items = page?.items ?? [];

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16, height: "100%" }}>
      <header style={{ display: "flex", alignItems: "baseline", justifyContent: "space-between" }}>
        <div>
          <h1 style={{ margin: 0, fontSize: 22, fontWeight: 600 }}>Muestras</h1>
          <p style={{ margin: "4px 0 0", color: "var(--fg-muted)", fontSize: 12 }}>
            {total} {total === 1 ? "muestra" : "muestras"} · página {Math.floor(offset / PAGE_SIZE) + 1}
          </p>
        </div>
        <FilterBar filters={filters} onChange={(f) => { setOffset(0); setFilters(f); }} />
      </header>

      <div
        style={{
          display: "grid",
          gridTemplateColumns: selectedUuid ? "1fr 420px" : "1fr",
          gap: 16,
          flex: 1,
          minHeight: 0,
        }}
      >
        <div
          style={{
            background: "var(--bg-elev-2)",
            border: "1px solid var(--border)",
            borderRadius: "var(--radius)",
            overflow: "hidden",
            display: "flex",
            flexDirection: "column",
          }}
        >
          {error ? (
            <Empty text={`Error: ${error}`} />
          ) : items.length === 0 && !loading ? (
            <Empty
              text="Sin muestras todavía"
              hint="Cuando llegue una corrida del equipo va a aparecer acá."
            />
          ) : (
            <SampleTable
              items={items}
              selectedUuid={selectedUuid}
              onSelect={setSelectedUuid}
            />
          )}
          <Pagination
            offset={offset}
            limit={PAGE_SIZE}
            total={total}
            onChange={setOffset}
          />
        </div>

        {selectedUuid && (
          <SampleDetailPanel
            sample={selectedSample}
            onClose={() => setSelectedUuid(null)}
          />
        )}
      </div>
    </div>
  );
}

function FilterBar({
  filters,
  onChange,
}: {
  filters: SampleFilters;
  onChange: (f: SampleFilters) => void;
}) {
  return (
    <div style={{ display: "flex", gap: 8 }}>
      <input
        placeholder="Filtrar por SN"
        value={filters.serial ?? ""}
        onChange={(e) =>
          onChange({ ...filters, serial: e.currentTarget.value || null })
        }
        style={inputStyle}
      />
      <input
        placeholder="Filtrar por operador"
        value={filters.operator ?? ""}
        onChange={(e) =>
          onChange({ ...filters, operator: e.currentTarget.value || null })
        }
        style={inputStyle}
      />
    </div>
  );
}

const inputStyle: React.CSSProperties = {
  background: "var(--bg-elev-3)",
  border: "1px solid var(--border)",
  borderRadius: "var(--radius-sm)",
  padding: "6px 10px",
  color: "var(--fg)",
  fontSize: 12,
  width: 160,
};

function SampleTable({
  items,
  selectedUuid,
  onSelect,
}: {
  items: SampleOutputDTO[];
  selectedUuid: string | null;
  onSelect: (uuid: string | null) => void;
}) {
  return (
    <div style={{ overflow: "auto", flex: 1 }}>
      <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
        <thead>
          <tr>
            {["Recibido", "Equipo", "Muestra", "Operador", "Programa", "Resultado"].map((h) => (
              <th
                key={h}
                style={{
                  textAlign: "left",
                  padding: "10px 12px",
                  borderBottom: "1px solid var(--border)",
                  fontSize: 11,
                  fontWeight: 500,
                  color: "var(--fg-muted)",
                  position: "sticky",
                  top: 0,
                  background: "var(--bg-elev-2)",
                }}
              >
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {items.map((s) => {
            const active = s.uuid === selectedUuid;
            return (
              <tr
                key={s.uuid}
                onClick={() => onSelect(active ? null : s.uuid)}
                style={{
                  cursor: "pointer",
                  background: active ? "var(--bg-elev-3)" : "transparent",
                  transition: "background 80ms ease",
                }}
                onMouseEnter={(e) => {
                  if (!active) (e.currentTarget.style.background = "var(--bg-elev-3)");
                }}
                onMouseLeave={(e) => {
                  if (!active) (e.currentTarget.style.background = "transparent");
                }}
              >
                <Td>{new Date(s.receivedAt).toLocaleString("es-AR", { dateStyle: "short", timeStyle: "medium" })}</Td>
                <Td mono>{s.serial} · {s.analyzerType}</Td>
                <Td>{s.sampleIdentifier}</Td>
                <Td>{s.operator ?? "—"}</Td>
                <Td>{s.program ?? "—"}</Td>
                <Td mono>{resolvePrimaryResult(s)}</Td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function resolvePrimaryResult(s: SampleOutputDTO): string {
  const e = s.extra ?? {};
  switch (s.analyzerType) {
    case "OptiPMD": {
      const ibp = s.ibp?.toFixed(1);
      const fbp = s.fbp?.toFixed(1);
      if (ibp && fbp) return `IBP ${ibp} / FBP ${fbp} °C`;
      if (ibp) return `IBP ${ibp} °C`;
      if (fbp) return `FBP ${fbp} °C`;
      return "—";
    }
    case "OptiCPP": {
      const cp = e["CloudPoint"] ?? e["Cloud_Result"];
      return cp ? `Cloud ${cp} °C` : "—";
    }
    case "OptiFZP": {
      const fp = e["FreezePoint"];
      return fp ? `Freeze ${fp} °C` : "—";
    }
    case "OptiFPP": {
      const fp = e["FilterPluggingPoint"] ?? e["CFPP"];
      return fp ? `CFPP ${fp} °C` : "—";
    }
    default: {
      const headline = e["Result"] ?? e["Value"] ?? e["Measured"];
      return headline ?? "—";
    }
  }
}

function Td({ children, mono }: { children: React.ReactNode; mono?: boolean }) {
  return (
    <td
      style={{
        padding: "8px 12px",
        borderBottom: "1px solid var(--border)",
        fontFamily: mono ? "ui-monospace, SF Mono, Consolas, monospace" : undefined,
        whiteSpace: "nowrap",
      }}
    >
      {children}
    </td>
  );
}

function Pagination({
  offset,
  limit,
  total,
  onChange,
}: {
  offset: number;
  limit: number;
  total: number;
  onChange: (o: number) => void;
}) {
  const page = Math.floor(offset / limit) + 1;
  const totalPages = Math.max(1, Math.ceil(total / limit));
  return (
    <div
      style={{
        padding: 8,
        display: "flex",
        gap: 8,
        alignItems: "center",
        justifyContent: "flex-end",
        borderTop: "1px solid var(--border)",
        fontSize: 11,
        color: "var(--fg-muted)",
      }}
    >
      <button onClick={() => onChange(Math.max(0, offset - limit))} disabled={offset === 0} style={btnStyle}>
        ← anterior
      </button>
      <span>
        {page} / {totalPages}
      </span>
      <button
        onClick={() => onChange(offset + limit)}
        disabled={offset + limit >= total}
        style={btnStyle}
      >
        siguiente →
      </button>
    </div>
  );
}

const btnStyle: React.CSSProperties = {
  background: "var(--bg-elev-3)",
  border: "1px solid var(--border)",
  borderRadius: "var(--radius-sm)",
  padding: "4px 10px",
  color: "var(--fg)",
  fontSize: 11,
};

function Empty({ text, hint }: { text: string; hint?: string }) {
  return (
    <div style={{ padding: 32, textAlign: "center", color: "var(--fg-muted)", fontSize: 12 }}>
      <div>{text}</div>
      {hint && <div style={{ marginTop: 6, color: "var(--fg-dim)", fontSize: 11 }}>{hint}</div>}
    </div>
  );
}

function SampleDetailPanel({
  sample,
  onClose,
}: {
  sample: SampleOutputDTO | null;
  onClose: () => void;
}) {
  if (!sample) {
    return (
      <div
        style={{
          background: "var(--bg-elev-2)",
          border: "1px solid var(--border)",
          borderRadius: "var(--radius)",
          padding: 16,
          color: "var(--fg-muted)",
          fontSize: 12,
        }}
      >
        Cargando…
      </div>
    );
  }
  return (
    <aside
      style={{
        background: "var(--bg-elev-2)",
        border: "1px solid var(--border)",
        borderRadius: "var(--radius)",
        overflow: "auto",
        display: "flex",
        flexDirection: "column",
      }}
    >
      <header
        style={{
          padding: "12px 16px",
          borderBottom: "1px solid var(--border)",
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
        }}
      >
        <strong style={{ fontSize: 13 }}>
          Muestra {sample.sampleIdentifier}
        </strong>
        <button onClick={onClose} style={{ ...btnStyle, padding: "2px 8px" }}>×</button>
      </header>
      <div style={{ padding: 16, fontSize: 12 }}>
        <Section title="Identificación">
          <KV label="Equipo" value={`${sample.serial} (${sample.analyzerType})`} />
          <KV label="Operador" value={sample.operator ?? "—"} />
          <KV label="Programa" value={sample.program ?? "—"} />
          <KV label="Inicio" value={sample.startAt ? new Date(sample.startAt).toLocaleString("es-AR") : "—"} />
          <KV label="Fin" value={sample.endAt ? new Date(sample.endAt).toLocaleString("es-AR") : "—"} />
        </Section>
        <Section title="Resultados">
          <ResultsByType sample={sample} />
        </Section>
        {sample.curve.length > 0 && (
          <Section title={`Curva (${sample.curve.length} puntos)`}>
            <DistillationChart curve={sample.curve} />
          </Section>
        )}
        {sample.extra && Object.keys(sample.extra).length > 0 && (
          <Section title="Datos adicionales">
            <ExtraFields extra={sample.extra} />
          </Section>
        )}
      </div>
    </aside>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div style={{ marginBottom: 16 }}>
      <div
        style={{
          fontSize: 11,
          fontWeight: 500,
          color: "var(--fg-muted)",
          textTransform: "uppercase",
          letterSpacing: 0.5,
          marginBottom: 8,
        }}
      >
        {title}
      </div>
      {children}
    </div>
  );
}

function KV({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: "flex", justifyContent: "space-between", padding: "4px 0", fontSize: 12 }}>
      <span style={{ color: "var(--fg-muted)" }}>{label}</span>
      <span style={{ fontFamily: "ui-monospace, SF Mono, Consolas, monospace" }}>{value}</span>
    </div>
  );
}

function ResultsByType({ sample }: { sample: SampleOutputDTO }) {
  const t = sample.analyzerType;
  const e = sample.extra ?? {};

  const DISTILLATION = ["OptiPMD"];
  if (DISTILLATION.includes(t)) {
    const rows: Array<[string, string | null]> = [
      ["IBP", sample.ibp != null ? `${sample.ibp.toFixed(1)} °C` : null],
      ["FBP", sample.fbp != null ? `${sample.fbp.toFixed(1)} °C` : null],
      ["Residuo", sample.residue != null ? `${sample.residue.toFixed(1)} %` : null],
      ["Recovery", sample.recovery != null ? `${sample.recovery.toFixed(1)} %` : null],
      ["Volumen FBP", sample.fbpVolume != null ? `${sample.fbpVolume.toFixed(1)} %` : null],
    ];
    return <KVList rows={rows} />;
  }

  
  if (t === "OptiFZP") {
    const rows: Array<[string, string | null]> = [
      ["Freeze Point", e.FreezePoint ? `${e.FreezePoint} °C` : null],
      ["Cd", e.Cd ? `${e.Cd} °C` : null],
      ["Co", e.Co ? `${e.Co} °C` : null],
      ["Do", e.Do ? `${e.Do} °C` : null],
      ["Estado final", e.Ending ?? null],
    ];
    return <KVList rows={rows} />;
  }

  
  if (t === "OptiCPP") {
    const rows: Array<[string, string | null]> = [
      ["Cloud Point", e.CloudPoint ? `${e.CloudPoint} °C` : null],
      ["Tipo de análisis", e.TypeOfAnalysis ?? null],
      ["Perfil de enfriamiento", e.CoolingProfile ?? null],
      ["Modo de test", e.TestMode ?? null],
      ["Pre-calentamiento", e.Preheating ?? null],
      ["Detección CP", e.DetectionLevelCP ?? null],
      ["Estado final", e.Ending ?? null],
    ];
    return <KVList rows={rows} />;
  }

  
  return (
    <div style={{ color: "var(--fg-dim)", fontSize: 12 }}>
      Tipo de equipo no reconocido: <code>{t}</code>. Mirá los datos abajo en "Datos adicionales".
    </div>
  );
}

function KVList({ rows }: { rows: Array<[string, string | null]> }) {
  const visible = rows.filter(([, v]) => v != null && v !== "");
  if (visible.length === 0) {
    return <div style={{ color: "var(--fg-dim)", fontSize: 12 }}>Sin datos.</div>;
  }
  return (
    <>
      {visible.map(([k, v]) => (
        <KV key={k} label={k} value={v as string} />
      ))}
    </>
  );
}

function ExtraFields({ extra }: { extra: Record<string, string> }) {
  const SKIP = new Set([
    
    "FreezePoint", "Cd", "Co", "Do",
    "CloudPoint", "TypeOfAnalysis", "CoolingProfile",
    "TestMode", "Preheating", "DetectionLevelCP", "Ending",
    
    "hpgl_curve",
  ]);
  const visible = Object.entries(extra).filter(([k, v]) => !SKIP.has(k) && v !== "");
  if (visible.length === 0) {
    return null;
  }
  return (
    <>
      {visible.map(([k, v]) => (
        <KV key={k} label={k} value={v} />
      ))}
    </>
  );
}

function DistillationChart({ curve }: { curve: { pctRecovered: number; temperatureC: number }[] }) {
  if (curve.length === 0) return <div style={{ color: "var(--fg-dim)", fontSize: 12 }}>(sin curva)</div>;
  const W = 360, H = 160, pad = 24;
  const ys = curve.map((p) => p.temperatureC);
  const minX = 0, maxX = 100;
  const minY = Math.min(...ys), maxY = Math.max(...ys);
  const sx = (v: number) => pad + ((v - minX) / (maxX - minX)) * (W - 2 * pad);
  const sy = (v: number) => H - pad - ((v - minY) / (maxY - minY || 1)) * (H - 2 * pad);
  const path = useMemo(
    () =>
      curve
        .map((p, i) => `${i === 0 ? "M" : "L"}${sx(p.pctRecovered).toFixed(1)},${sy(p.temperatureC).toFixed(1)}`)
        .join(" "),
    [curve],
  );
  return (
    <svg width={W} height={H} style={{ display: "block" }}>
      <rect x={pad} y={pad} width={W - 2 * pad} height={H - 2 * pad} fill="none" stroke="var(--border)" />
      <path d={path} fill="none" stroke="var(--accent)" strokeWidth="1.5" />
      {curve.map((p, i) => (
        <circle key={i} cx={sx(p.pctRecovered)} cy={sy(p.temperatureC)} r="1.5" fill="var(--accent)" />
      ))}
      <text x={pad} y={H - 6} fontSize="9" fill="var(--fg-dim)">0%</text>
      <text x={W - pad - 18} y={H - 6} fontSize="9" fill="var(--fg-dim)">100%</text>
      <text x={4} y={pad + 4} fontSize="9" fill="var(--fg-dim)">{maxY.toFixed(0)}°</text>
      <text x={4} y={H - pad - 2} fontSize="9" fill="var(--fg-dim)">{minY.toFixed(0)}°</text>
    </svg>
  );
}

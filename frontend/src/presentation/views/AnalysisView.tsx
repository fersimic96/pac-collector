

import { useEffect, useMemo, useState } from "react";
import {
  SampleRepositoryImpl,
  type SamplePage,
} from "@/infrastructure/repositories/SampleRepositoryImpl";
import type { SampleOutputDTO } from "@/domain/outputDTO/SampleOutputDTO";

const repo = new SampleRepositoryImpl();

const COLORS = [
  "#3b82f6",
  "#10b981",
  "#f59e0b",
  "#ef4444",
  "#8b5cf6",
  "#ec4899",
  "#14b8a6",
  "#f97316",
];

type VizType = "distillation-overlay" | "scalar-trend" | "dual-axis" | "composition" | "unknown";

const VIZ_BY_TYPE: Record<string, VizType> = {
  OptiPMD: "distillation-overlay",
  OptiCPP: "scalar-trend",
  OptiFPP: "scalar-trend",
  OptiFZP: "scalar-trend",
  OptiMPP: "scalar-trend",
  OptiMVD: "dual-axis",
  OptiFuel: "composition",
};

const SCALAR_FIELD_BY_TYPE: Record<string, { key: string; label: string; unit: string }> = {
  OptiCPP: { key: "CloudpointResult", label: "Cloud Point", unit: "°C" },
  OptiFPP: { key: "Cfpp_Result", label: "CFPP", unit: "°C" },
  OptiFZP: { key: "Freeze", label: "Freeze Point", unit: "°C" },
  OptiMPP: { key: "PourpointResult", label: "Pour Point", unit: "°C" },
};

export function AnalysisView() {
  const [samples, setSamples] = useState<SampleOutputDTO[]>([]);
  const [selected, setSelected] = useState<Set<string>>(new Set());

  useEffect(() => {
    repo.listPaginated({}, 0, 200).then((p: SamplePage) => setSamples(p.items));
  }, []);

  const grouped = useMemo(() => {
    const m: Record<string, SampleOutputDTO[]> = {};
    for (const s of samples) {
      (m[s.analyzerType] ??= []).push(s);
    }
    return m;
  }, [samples]);

  const selectedSamples = useMemo(
    () => samples.filter((s) => selected.has(s.uuid)),
    [samples, selected],
  );

  
  
  const vizType: VizType = useMemo(() => {
    if (selectedSamples.length === 0) return "unknown";
    return VIZ_BY_TYPE[selectedSamples[0].analyzerType] ?? "unknown";
  }, [selectedSamples]);

  const lockedAnalyzerType = selectedSamples[0]?.analyzerType ?? null;

  const toggle = (uuid: string, sample: SampleOutputDTO) => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(uuid)) {
        next.delete(uuid);
      } else {
        
        if (lockedAnalyzerType && sample.analyzerType !== lockedAnalyzerType) {
          return prev;
        }
        if (next.size >= COLORS.length) return prev;
        next.add(uuid);
      }
      return next;
    });
  };
  const clearSelection = () => setSelected(new Set());

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16, height: "100%" }}>
      <header>
        <h1 style={{ margin: 0, fontSize: 22, fontWeight: 600 }}>Análisis</h1>
        <p style={{ margin: "4px 0 0", color: "var(--fg-muted)", fontSize: 12 }}>
          Compará muestras del mismo tipo de equipo. La visualización se elige
          automáticamente según el tipo (curvas, tendencias, composición).
        </p>
      </header>

      <div style={{ display: "grid", gridTemplateColumns: "300px 1fr", gap: 16, flex: 1, minHeight: 0 }}>
        <aside
          style={{
            background: "var(--bg-elev-2)",
            border: "1px solid var(--border)",
            borderRadius: "var(--radius)",
            padding: 12,
            overflow: "auto",
            display: "flex",
            flexDirection: "column",
            gap: 12,
          }}
        >
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
            <span style={{ fontSize: 11, color: "var(--fg-muted)" }}>
              {selected.size} seleccionada{selected.size === 1 ? "" : "s"}
              {lockedAnalyzerType ? ` · ${lockedAnalyzerType}` : ""}
            </span>
            {selected.size > 0 && (
              <button
                onClick={clearSelection}
                style={{
                  background: "var(--bg-elev-3)",
                  border: "1px solid var(--border)",
                  borderRadius: 4,
                  padding: "2px 8px",
                  color: "var(--fg-muted)",
                  fontSize: 11,
                }}
              >
                limpiar
              </button>
            )}
          </div>

          {Object.keys(grouped).length === 0 ? (
            <div style={{ color: "var(--fg-dim)", fontSize: 12, padding: 8 }}>
              Sin muestras todavía.
            </div>
          ) : (
            Object.entries(grouped).map(([type, list]) => (
              <SamplesGroup
                key={type}
                type={type}
                samples={list}
                selected={selected}
                lockedType={lockedAnalyzerType}
                onToggle={toggle}
                colorOf={(uuid) => {
                  const idx = Array.from(selected).indexOf(uuid);
                  return idx >= 0 ? COLORS[idx % COLORS.length] : null;
                }}
              />
            ))
          )}
        </aside>

        <main
          style={{
            background: "var(--bg-elev-2)",
            border: "1px solid var(--border)",
            borderRadius: "var(--radius)",
            padding: 16,
            overflow: "auto",
          }}
        >
          {selectedSamples.length === 0 ? (
            <Empty
              text="Seleccioná muestras de la barra izquierda"
              hint="La visualización se adapta al tipo del equipo (curva, tendencia, etc.)"
            />
          ) : vizType === "distillation-overlay" ? (
            <DistillationOverlay samples={selectedSamples} />
          ) : vizType === "scalar-trend" ? (
            <ScalarTrend samples={selectedSamples} />
          ) : vizType === "dual-axis" ? (
            <DualAxisStub samples={selectedSamples} />
          ) : vizType === "composition" ? (
            <CompositionStub samples={selectedSamples} />
          ) : (
            <Empty text={`Tipo desconocido: ${selectedSamples[0].analyzerType}`} />
          )}
        </main>
      </div>
    </div>
  );
}

function SamplesGroup({
  type,
  samples,
  selected,
  lockedType,
  onToggle,
  colorOf,
}: {
  type: string;
  samples: SampleOutputDTO[];
  selected: Set<string>;
  lockedType: string | null;
  onToggle: (uuid: string, sample: SampleOutputDTO) => void;
  colorOf: (uuid: string) => string | null;
}) {
  const disabled = lockedType !== null && lockedType !== type;
  return (
    <div>
      <div
        style={{
          fontSize: 11,
          color: "var(--fg-muted)",
          textTransform: "uppercase",
          letterSpacing: 0.5,
          marginBottom: 6,
          opacity: disabled ? 0.4 : 1,
        }}
      >
        {type} · {samples.length}
      </div>
      <ul style={{ listStyle: "none", padding: 0, margin: 0, display: "flex", flexDirection: "column", gap: 4 }}>
        {samples.map((s) => {
          const isSelected = selected.has(s.uuid);
          const color = colorOf(s.uuid);
          return (
            <li
              key={s.uuid}
              onClick={() => !disabled && onToggle(s.uuid, s)}
              style={{
                padding: "6px 8px",
                background: isSelected ? "var(--bg-elev-3)" : "transparent",
                borderLeft: `3px solid ${color ?? "transparent"}`,
                borderRadius: 4,
                cursor: disabled ? "not-allowed" : "pointer",
                opacity: disabled ? 0.4 : 1,
                fontSize: 12,
                fontFamily: "ui-monospace, SF Mono, Consolas, monospace",
              }}
            >
              <div style={{ color: "var(--fg)" }}>{s.sampleIdentifier}</div>
              <div style={{ fontSize: 10, color: "var(--fg-muted)" }}>
                {s.serial} · {new Date(s.startAt ?? s.receivedAt).toLocaleDateString("es-AR")}
              </div>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

function DistillationOverlay({ samples }: { samples: SampleOutputDTO[] }) {
  const W = 720, H = 420, padL = 50, padR = 20, padT = 16, padB = 32;

  const lines = samples.map((s, i) => ({
    sample: s,
    color: COLORS[i % COLORS.length],
    points: buildCurvePoints(s),
  }));

  const allTemps = lines.flatMap((l) => l.points.map((p) => p.temperatureC));
  const minY = Math.min(...allTemps, 0);
  const maxY = Math.max(...allTemps, 100);
  const yRange = maxY - minY || 1;
  const sx = (pct: number) => padL + (pct / 100) * (W - padL - padR);
  const sy = (t: number) => H - padB - ((t - minY) / yRange) * (H - padT - padB);

  return (
    <div>
      <h3 style={{ margin: "0 0 12px", fontSize: 13 }}>
        Curvas de destilación · {samples.length} muestras
      </h3>
      <svg width={W} height={H} style={{ background: "#0b0b0b", borderRadius: 6 }}>
        {}
        {[0, 25, 50, 75, 100].map((p) => (
          <line key={`vx-${p}`} x1={sx(p)} y1={padT} x2={sx(p)} y2={H - padB} stroke="var(--border)" strokeWidth="0.5" />
        ))}
        {[0, 0.25, 0.5, 0.75, 1].map((f) => {
          const t = minY + f * yRange;
          return (
            <g key={`hy-${f}`}>
              <line x1={padL} y1={sy(t)} x2={W - padR} y2={sy(t)} stroke="var(--border)" strokeWidth="0.5" />
              <text x={padL - 6} y={sy(t) + 3} fontSize="9" fill="var(--fg-dim)" textAnchor="end">
                {t.toFixed(0)}°
              </text>
            </g>
          );
        })}
        {[0, 25, 50, 75, 100].map((p) => (
          <text key={`tx-${p}`} x={sx(p)} y={H - padB + 14} fontSize="9" fill="var(--fg-dim)" textAnchor="middle">
            {p}%
          </text>
        ))}
        {}
        {lines.map((l) => (
          <g key={l.sample.uuid}>
            <path
              d={l.points
                .map((p, i) => `${i === 0 ? "M" : "L"}${sx(p.pctRecovered).toFixed(1)},${sy(p.temperatureC).toFixed(1)}`)
                .join(" ")}
              fill="none"
              stroke={l.color}
              strokeWidth="1.5"
            />
          </g>
        ))}
      </svg>
      <Legend
        items={lines.map((l) => ({
          color: l.color,
          label: `${l.sample.sampleIdentifier} (${l.sample.serial})`,
          subtitle: `${l.points.length} puntos · IBP ${l.sample.ibp?.toFixed(1) ?? "—"}°C / FBP ${l.sample.fbp?.toFixed(1) ?? "—"}°C`,
        }))}
      />
    </div>
  );
}

function ScalarTrend({ samples }: { samples: SampleOutputDTO[] }) {
  const type = samples[0].analyzerType;
  const meta = SCALAR_FIELD_BY_TYPE[type];
  if (!meta) return <Empty text={`No hay configuración de scalar para ${type}`} />;

  const values = samples
    .map((s) => {
      const raw = s.extra[meta.key];
      const v = raw ? parseFloat(raw) : NaN;
      return { sample: s, x: new Date(s.startAt ?? s.receivedAt).getTime(), y: v };
    })
    .filter((p) => Number.isFinite(p.y))
    .sort((a, b) => a.x - b.x);

  if (values.length === 0) {
    return <Empty text={`Las muestras seleccionadas no tienen ${meta.label}`} />;
  }

  const W = 720, H = 380, padL = 60, padR = 20, padT = 16, padB = 32;
  const xs = values.map((v) => v.x);
  const ys = values.map((v) => v.y);
  const minX = Math.min(...xs), maxX = Math.max(...xs);
  const minY = Math.min(...ys), maxY = Math.max(...ys);
  const xRange = maxX - minX || 1;
  const yRange = maxY - minY || 1;
  const sx = (x: number) => padL + ((x - minX) / xRange) * (W - padL - padR);
  const sy = (y: number) => H - padB - ((y - minY) / yRange) * (H - padT - padB);

  return (
    <div>
      <h3 style={{ margin: "0 0 12px", fontSize: 13 }}>
        Tendencia de {meta.label} ({meta.unit}) · {values.length} puntos
      </h3>
      <svg width={W} height={H} style={{ background: "#0b0b0b", borderRadius: 6 }}>
        {[0, 0.25, 0.5, 0.75, 1].map((f) => {
          const y = minY + f * yRange;
          return (
            <g key={`hy-${f}`}>
              <line x1={padL} y1={sy(y)} x2={W - padR} y2={sy(y)} stroke="var(--border)" strokeWidth="0.5" />
              <text x={padL - 6} y={sy(y) + 3} fontSize="9" fill="var(--fg-dim)" textAnchor="end">
                {y.toFixed(1)}
              </text>
            </g>
          );
        })}
        <path
          d={values
            .map((p, i) => `${i === 0 ? "M" : "L"}${sx(p.x).toFixed(1)},${sy(p.y).toFixed(1)}`)
            .join(" ")}
          fill="none"
          stroke="var(--accent)"
          strokeWidth="1.5"
        />
        {values.map((p, i) => (
          <circle key={i} cx={sx(p.x)} cy={sy(p.y)} r="2.5" fill={COLORS[i % COLORS.length]} />
        ))}
      </svg>
      <table style={{ marginTop: 12, fontSize: 11, borderCollapse: "collapse" }}>
        <thead>
          <tr>
            <th style={thStyle}>Muestra</th>
            <th style={thStyle}>Fecha</th>
            <th style={thStyle}>{meta.label} ({meta.unit})</th>
          </tr>
        </thead>
        <tbody>
          {values.map((p) => (
            <tr key={p.sample.uuid}>
              <td style={tdStyle}>{p.sample.sampleIdentifier}</td>
              <td style={tdStyle}>{new Date(p.x).toLocaleString("es-AR")}</td>
              <td style={{ ...tdStyle, fontFamily: "ui-monospace, monospace" }}>{p.y.toFixed(2)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function DualAxisStub({ samples }: { samples: SampleOutputDTO[] }) {
  return (
    <Empty
      text={`Doble eje (viscosidad + densidad) — pendiente`}
      hint={`Cuando lleguen muestras reales del OptiMVD vamos a graficar Dynamic Viscosity (cP) en un eje y Density (g/cm³) en el otro. Tenés ${samples.length} muestra${samples.length === 1 ? "" : "s"} para visualizar.`}
    />
  );
}

function CompositionStub({ samples }: { samples: SampleOutputDTO[] }) {
  
  const fields = ["Benzene", "Saturates", "Olefins", "MonoAromatics", "DiAromatics", "PolycyclicAromatics", "TotalAromatics"];
  return (
    <div>
      <h3 style={{ margin: "0 0 12px", fontSize: 13 }}>
        Composición · {samples.length} muestra{samples.length === 1 ? "" : "s"}
      </h3>
      <table style={{ width: "100%", fontSize: 12, borderCollapse: "collapse" }}>
        <thead>
          <tr>
            <th style={thStyle}>Muestra</th>
            {fields.map((f) => (
              <th key={f} style={thStyle}>
                {f}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {samples.map((s) => (
            <tr key={s.uuid}>
              <td style={tdStyle}>{s.sampleIdentifier}</td>
              {fields.map((f) => (
                <td key={f} style={{ ...tdStyle, fontFamily: "ui-monospace, monospace" }}>
                  {s.extra[f] ?? "—"}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      <p style={{ marginTop: 12, fontSize: 11, color: "var(--fg-muted)" }}>
        Cuando lleguen muestras reales del OptiFuel agregamos barras apiladas + radar chart.
      </p>
    </div>
  );
}

function buildCurvePoints(s: SampleOutputDTO): { pctRecovered: number; temperatureC: number }[] {
  const pts = [...s.curve];
  if (s.ibp != null) pts.unshift({ pctRecovered: 0, temperatureC: s.ibp });
  if (s.fbp != null && s.fbpVolume != null) {
    pts.push({ pctRecovered: s.fbpVolume, temperatureC: s.fbp });
  }
  return pts.sort((a, b) => a.pctRecovered - b.pctRecovered);
}

function Empty({ text, hint }: { text: string; hint?: string }) {
  return (
    <div style={{ padding: 32, textAlign: "center", color: "var(--fg-muted)", fontSize: 12 }}>
      <div>{text}</div>
      {hint && <div style={{ marginTop: 8, color: "var(--fg-dim)", fontSize: 11 }}>{hint}</div>}
    </div>
  );
}

function Legend({ items }: { items: { color: string; label: string; subtitle: string }[] }) {
  return (
    <ul style={{ listStyle: "none", padding: 0, margin: "12px 0 0", display: "flex", flexDirection: "column", gap: 6 }}>
      {items.map((it, i) => (
        <li key={i} style={{ display: "flex", alignItems: "center", gap: 8, fontSize: 11 }}>
          <span style={{ width: 14, height: 2, background: it.color, borderRadius: 1 }} />
          <span style={{ color: "var(--fg)" }}>{it.label}</span>
          <span style={{ color: "var(--fg-muted)" }}>{it.subtitle}</span>
        </li>
      ))}
    </ul>
  );
}

const thStyle: React.CSSProperties = {
  textAlign: "left",
  padding: "6px 10px",
  borderBottom: "1px solid var(--border)",
  fontSize: 10,
  fontWeight: 500,
  color: "var(--fg-muted)",
  textTransform: "uppercase",
  letterSpacing: 0.5,
};

const tdStyle: React.CSSProperties = {
  padding: "6px 10px",
  borderBottom: "1px solid var(--border)",
};

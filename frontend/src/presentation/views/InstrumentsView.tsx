

import { useEffect, useState } from "react";
import { InstrumentRepositoryImpl } from "@/infrastructure/repositories/InstrumentRepositoryImpl";
import type { InstrumentOutputDTO } from "@/domain/outputDTO/InstrumentOutputDTO";
import { Instrument } from "@/domain/entities/Instrument";

const repo = new InstrumentRepositoryImpl();

export function InstrumentsView() {
  const [list, setList] = useState<InstrumentOutputDTO[]>([]);
  const [editing, setEditing] = useState<string | null>(null);
  const [alias, setAlias] = useState("");

  const refresh = async () => {
    const items = await repo.listAll();
    setList(items);
  };

  useEffect(() => {
    refresh();
    const id = setInterval(refresh, 5000);
    return () => clearInterval(id);
  }, []);

  const startEdit = (inst: InstrumentOutputDTO) => {
    setEditing(inst.serial);
    setAlias(inst.alias ?? "");
  };
  const save = async (serial: string) => {
    const v = Instrument.validateAlias(alias);
    if (!v.valid) return;
    await repo.updateAlias(serial, alias.trim() || null);
    setEditing(null);
    refresh();
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      <header>
        <h1 style={{ margin: 0, fontSize: 22, fontWeight: 600 }}>Equipos</h1>
        <p style={{ margin: "4px 0 0", color: "var(--fg-muted)", fontSize: 12 }}>
          {list.length} equipo{list.length === 1 ? "" : "s"} detectado{list.length === 1 ? "" : "s"}
        </p>
      </header>

      {list.length === 0 ? (
        <div
          style={{
            background: "var(--bg-elev-2)",
            border: "1px solid var(--border)",
            borderRadius: "var(--radius)",
            padding: 32,
            textAlign: "center",
            color: "var(--fg-muted)",
            fontSize: 12,
          }}
        >
          Ningún equipo detectado. Cuando un equipo PAC mande datos a la red,
          va a aparecer acá automáticamente.
        </div>
      ) : (
        <div
          style={{
            background: "var(--bg-elev-2)",
            border: "1px solid var(--border)",
            borderRadius: "var(--radius)",
            overflow: "hidden",
          }}
        >
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
            <thead>
              <tr>
                {["Estado", "Serie", "Tipo", "Alias", "IP", "Muestras", "Última actividad"].map(
                  (h) => (
                    <th
                      key={h}
                      style={{
                        textAlign: "left",
                        padding: "10px 12px",
                        borderBottom: "1px solid var(--border)",
                        fontSize: 11,
                        fontWeight: 500,
                        color: "var(--fg-muted)",
                      }}
                    >
                      {h}
                    </th>
                  ),
                )}
              </tr>
            </thead>
            <tbody>
              {list.map((inst) => {
                const online = Instrument.isOnline(inst);
                return (
                  <tr key={inst.serial}>
                    <td style={cellStyle}>
                      <span
                        style={{
                          width: 6,
                          height: 6,
                          borderRadius: "50%",
                          background: online ? "var(--green)" : "var(--fg-dim)",
                          boxShadow: online ? "0 0 6px var(--green)" : "none",
                          display: "inline-block",
                        }}
                      />{" "}
                      <span style={{ marginLeft: 6, color: online ? "var(--green)" : "var(--fg-muted)" }}>
                        {online ? "online" : "offline"}
                      </span>
                    </td>
                    <td style={{ ...cellStyle, fontFamily: "ui-monospace, monospace" }}>{inst.serial}</td>
                    <td style={cellStyle}>{inst.analyzerType}</td>
                    <td style={cellStyle}>
                      {editing === inst.serial ? (
                        <span style={{ display: "flex", gap: 4 }}>
                          <input
                            value={alias}
                            onChange={(e) => setAlias(e.currentTarget.value)}
                            autoFocus
                            onKeyDown={(e) => {
                              if (e.key === "Enter") save(inst.serial);
                              if (e.key === "Escape") setEditing(null);
                            }}
                            style={{
                              background: "var(--bg-elev-3)",
                              border: "1px solid var(--accent)",
                              borderRadius: 4,
                              padding: "2px 6px",
                              color: "var(--fg)",
                              fontSize: 12,
                              width: 140,
                            }}
                          />
                          <button onClick={() => save(inst.serial)} style={miniBtn}>✓</button>
                          <button onClick={() => setEditing(null)} style={miniBtn}>×</button>
                        </span>
                      ) : (
                        <span
                          onClick={() => startEdit(inst)}
                          style={{ cursor: "pointer", color: inst.alias ? "var(--fg)" : "var(--fg-dim)" }}
                          title="Click para editar"
                        >
                          {inst.alias ?? "(sin alias)"}
                        </span>
                      )}
                    </td>
                    <td style={{ ...cellStyle, fontFamily: "ui-monospace, monospace" }}>{inst.lastIp ?? "—"}</td>
                    <td style={{ ...cellStyle, fontFamily: "ui-monospace, monospace" }}>{inst.totalSamples}</td>
                    <td style={cellStyle}>
                      {new Date(inst.lastSeenAt).toLocaleString("es-AR", { dateStyle: "short", timeStyle: "medium" })}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

const cellStyle: React.CSSProperties = {
  padding: "8px 12px",
  borderBottom: "1px solid var(--border)",
};

const miniBtn: React.CSSProperties = {
  background: "var(--bg-elev-3)",
  border: "1px solid var(--border)",
  borderRadius: 3,
  padding: "0 6px",
  color: "var(--fg)",
  fontSize: 11,
};

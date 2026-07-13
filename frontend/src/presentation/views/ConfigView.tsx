

import React, { useEffect, useState } from "react";
import { ConfigRepositoryImpl } from "@/infrastructure/repositories/ConfigRepositoryImpl";
import type {
  AppConfigDTO,
  HotFolderFormat,
  InstrumentRouteDTO,
  InstrumentSettingsDTO,
} from "@/domain/outputDTO/AppConfigDTO";
import type { InstrumentOutputDTO } from "@/domain/outputDTO/InstrumentOutputDTO";
import { ipcInvoke, ipcListenAppEvent } from "@/infrastructure/ipc/tauriClient";
import type { PluginOutputDTO } from "@/domain/outputDTO/PluginOutputDTO";
import type { AppEvent } from "@/lib/events";
import { NetworkDiagnostics } from "@/presentation/components/network/NetworkDiagnostics";

const repo = new ConfigRepositoryImpl();

export function ConfigView() {
  const [config, setConfig] = useState<AppConfigDTO | null>(null);
  const [plugins, setPlugins] = useState<PluginOutputDTO[]>([]);
  const [instruments, setInstruments] = useState<InstrumentOutputDTO[]>([]);
  const [ips, setIps] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);
  const [savedAt, setSavedAt] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  const refreshInstruments = async () => {
    try {
      const list = await ipcInvoke<InstrumentOutputDTO[]>("list_instruments");
      setInstruments(list);
    } catch (e) {
      
      console.warn("list_instruments failed", e);
    }
  };

  useEffect(() => {
    Promise.all([
      repo.load(),
      ipcInvoke<PluginOutputDTO[]>("list_plugins"),
      repo.listLocalIps(),
      ipcInvoke<InstrumentOutputDTO[]>("list_instruments"),
    ])
      .then(([cfg, plg, lips, insts]) => {
        const instrumentsMap = { ...cfg.instruments };
        for (const p of plg) {
          const t = p.supportedTypes[0];
          if (!instrumentsMap[t]) {
            instrumentsMap[t] = {
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
          }
        }
        setConfig({
          ...cfg,
          instruments: instrumentsMap,
          instrumentRoutes: cfg.instrumentRoutes ?? {},
        });
        setPlugins(plg);
        setIps(lips);
        setInstruments(insts);
      })
      .catch((e) => setError(String(e)));

    
    
    
    const unsub = ipcListenAppEvent<AppEvent>((e) => {
      if (e.type === "instrument_discovered") {
        refreshInstruments();
      }
    });
    return () => {
      unsub.then((u) => u()).catch(() => undefined);
    };
  }, []);

  const save = async () => {
    if (!config) return;
    setSaving(true);
    setError(null);
    try {
      await repo.save(config);
      setSavedAt(Date.now());
    } catch (e) {
      setError(String(e));
    } finally {
      setSaving(false);
    }
  };

  if (error) {
    return <div style={{ color: "var(--red)", fontSize: 12 }}>Error: {error}</div>;
  }
  if (!config) {
    return <div style={{ color: "var(--fg-muted)", fontSize: 12 }}>Cargando…</div>;
  }

  const updateGeneral = (patch: Partial<AppConfigDTO["general"]>) => {
    setConfig({ ...config, general: { ...config.general, ...patch } });
    setSavedAt(null);
  };
  const updateOutputFormats = (patch: Partial<AppConfigDTO["outputFormats"]>) => {
    setConfig({ ...config, outputFormats: { ...config.outputFormats, ...patch } });
    setSavedAt(null);
  };
  const updateInstrument = (type: string, patch: Partial<InstrumentSettingsDTO>) => {
    setConfig({
      ...config,
      instruments: {
        ...config.instruments,
        [type]: { ...config.instruments[type], ...patch },
      },
    });
    setSavedAt(null);
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      <header style={{ display: "flex", alignItems: "baseline", justifyContent: "space-between" }}>
        <div>
          <h1 style={{ margin: 0, fontSize: 22, fontWeight: 600 }}>Configuración</h1>
          <p style={{ margin: "4px 0 0", color: "var(--fg-muted)", fontSize: 12 }}>
            Cambios en delimiter, EOL, paths y parámetros se persisten
            automáticamente al cerrar la app.
          </p>
        </div>
        <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
          {savedAt && (
            <span style={{ fontSize: 11, color: "var(--green)" }}>
              ✓ Guardado
            </span>
          )}
          <button
            onClick={save}
            disabled={saving}
            style={{
              background: "var(--accent)",
              border: "none",
              borderRadius: "var(--radius-sm)",
              padding: "8px 16px",
              color: "white",
              fontSize: 12,
              fontWeight: 500,
              cursor: saving ? "wait" : "pointer",
              opacity: saving ? 0.6 : 1,
            }}
          >
            {saving ? "Guardando..." : "Guardar configuración"}
          </button>
        </div>
      </header>

      <Card title="General — Formato de archivos de salida">
        <Row label="Delimitador entre campos">
          <input
            value={config.general.delimiter}
            onChange={(e) => updateGeneral({ delimiter: e.currentTarget.value })}
            maxLength={4}
            style={{ ...inputStyle, width: 80, fontFamily: "ui-monospace, monospace" }}
          />
          <Hint>Default: <code>;</code> · Tab: escribí "\t" o usá <code>TAB</code></Hint>
        </Row>
        <Row label="Fin de línea (EOL)">
          <select
            value={config.general.eol}
            onChange={(e) => updateGeneral({ eol: e.currentTarget.value })}
            style={{ ...inputStyle, width: 140 }}
          >
            <option value="<none>">{"<none>"}</option>
            <option value="CR">CR</option>
            <option value="LF">LF</option>
            <option value="CR-LF">CR-LF</option>
          </select>
        </Row>
        <Row label="Mostrar clave del campo (key)">
          <Switch
            checked={config.general.showKey}
            onChange={(v) => updateGeneral({ showKey: v })}
            hint="Si está OFF, el output solo contiene valores"
          />
        </Row>
        <Row label="Mostrar unidad de medida">
          <Switch
            checked={config.general.showUnit}
            onChange={(v) => updateGeneral({ showUnit: v })}
            hint="Agrega 'unit' entre key y value"
          />
        </Row>
      </Card>

      <Card title="Naming de archivos">
        <Row label="Incluir N° de serie del analizador">
          <Switch
            checked={config.general.showAnalyzerSn}
            onChange={(v) => updateGeneral({ showAnalyzerSn: v })}
          />
        </Row>
        <Row label="Incluir SampleID">
          <Switch
            checked={config.general.showSampleId}
            onChange={(v) => updateGeneral({ showSampleId: v })}
          />
        </Row>
        <Row label="Incluir hora de inicio">
          <Switch
            checked={config.general.showStartTime}
            onChange={(v) => updateGeneral({ showStartTime: v })}
          />
        </Row>
        <Row label="">
          <span style={{ fontSize: 11, color: "var(--fg-muted)" }}>
            Ejemplo: <code style={{ color: "var(--fg)" }}>2125_29_20260425_1445.txt</code>
          </span>
        </Row>
      </Card>

      <Card title="Formatos de salida — qué archivos generar por cada muestra">
        <Row label="JSON crudo (auditoría)">
          <Switch
            checked={config.outputFormats.writeJson}
            onChange={(v) => updateOutputFormats({ writeJson: v })}
            hint="Backup byte-a-byte del equipo. Re-procesable. Recomendado dejar ON."
          />
        </Row>
        <Row label="TXT formato LIMS clásico (key;value;)">
          <Switch
            checked={config.outputFormats.writeLimsTxt}
            onChange={(v) => updateOutputFormats({ writeLimsTxt: v })}
            hint="Formato clásico Key;Value para sistemas LIMS"
          />
        </Row>
        <Row label="TXT legible (humano)">
          <Switch
            checked={config.outputFormats.writeLegibleTxt}
            onChange={(v) => updateOutputFormats({ writeLegibleTxt: v })}
            hint="Reporte agrupado en español con descripciones"
          />
        </Row>
        <Row label="CSV curva de destilación">
          <Switch
            checked={config.outputFormats.writeCurveCsv}
            onChange={(v) => updateOutputFormats({ writeCurveCsv: v })}
            hint="2 columnas (% recuperado, °C). Solo se genera si la muestra tiene curva."
          />
        </Row>
        <Row label="master.csv por equipo (1 fila/muestra)">
          <Switch
            checked={config.outputFormats.writeMasterCsv}
            onChange={(v) => updateOutputFormats({ writeMasterCsv: v })}
          />
        </Row>
        <Row label="master.csv global (consolidado)">
          <Switch
            checked={config.outputFormats.writeGlobalMasterCsv}
            onChange={(v) => updateOutputFormats({ writeGlobalMasterCsv: v })}
            hint="DB/_global/master.csv con TODAS las muestras de todos los equipos"
          />
        </Row>
        <Row label="Espejar a carpeta Recientes">
          <Switch
            checked={config.outputFormats.mirrorToRecent}
            onChange={(v) => updateOutputFormats({ mirrorToRecent: v })}
            hint="Copia los archivos a recent_dir además de db_dir"
          />
        </Row>
      </Card>

      <Card title="Carpetas de almacenamiento">
        <p style={{ margin: "0 0 8px", fontSize: 11, color: "var(--fg-muted)" }}>
          El archivo histórico (json/reports/curves/master.csv) se guarda automáticamente en{" "}
          <code style={{ background: "var(--bg-elev-3)", padding: "1px 4px", borderRadius: 3 }}>
            ~/Documents/PAC-Collector/DB
          </code>{" "}
          y no se puede mover por la UI (evita que se confunda con la carpeta destino LIMS). Si
          <em> realmente</em> necesitás moverlo, editá <code>settings.json</code> a mano.
        </p>
        <Row label="Mantener últimas N en Recientes">
          <input
            type="number"
            min={1}
            max={1000}
            value={config.general.recentKeep}
            onChange={(e) =>
              updateGeneral({ recentKeep: Math.max(1, parseInt(e.currentTarget.value) || 50) })
            }
            style={{ ...inputStyle, width: 80 }}
          />
        </Row>
      </Card>

      <Card title="Servidor de red">
        <Row label="IP del servidor (override)">
          <select
            value={config.general.selectedIp ?? ""}
            onChange={(e) => updateGeneral({ selectedIp: e.currentTarget.value || null })}
            style={{ ...inputStyle, width: 240 }}
          >
            <option value="">(automático — IP por routing)</option>
            {ips.map((ip) => (
              <option key={ip} value={ip}>{ip}</option>
            ))}
          </select>
          <Hint>
            En automático la app responde a cada equipo con la IP local que rutea hacia él (multi-NIC OK).
            Override solo si tenés VPN / NAT que confunda al kernel.
          </Hint>
        </Row>
        <Row label="Auto-start del servidor al abrir">
          <Switch
            checked={config.general.autoStartServer}
            onChange={(v) => updateGeneral({ autoStartServer: v })}
          />
        </Row>
      </Card>

      <Card title="Print Server (modo Iris)">
        <Row label="Habilitar print server">
          <Switch
            checked={config.general.printServerEnabled}
            onChange={(v) => updateGeneral({ printServerEnabled: v })}
          />
          <Hint>
            Activar para equipos en modo Iris/Print (e.g. OptiFZP). El equipo apunta su Printer IP
            al server. Por defecto desactivado para no chocar con CUPS en macOS dev.
          </Hint>
        </Row>
        <Row label="Puerto IPP">
          <input
            type="number"
            min={1}
            max={65535}
            value={config.general.printPort}
            onChange={(e) =>
              updateGeneral({
                printPort: Math.max(1, Math.min(65535, parseInt(e.currentTarget.value) || 631)),
              })
            }
            style={{ ...inputStyle, width: 100 }}
          />
          <Hint>Default 631 (IPP). Algunos equipos usan 9100 (JetDirect raw).</Hint>
        </Row>
      </Card>

      <Card title="Diagnóstico de red">
        <NetworkDiagnostics />
      </Card>

      <Card title="Salidas para automatización (hot folders)">
        <p style={{ margin: "0 0 12px", fontSize: 11, color: "var(--fg-muted)" }}>
          Lista dinámica de equipos detectados. Cuando un equipo nuevo conecta, aparece
          automáticamente. Por cada equipo elegí formato de salida y carpeta destino —
          cuando llegue una muestra, el archivo se escribe ahí para que una automatización
          (script, tarea programada, LIMS) lo consuma. El histórico completo en DB siempre
          se guarda aparte, independientemente de esta configuración.
        </p>
        {instruments.length === 0 ? (
          <div style={{ color: "var(--fg-dim)", fontSize: 12, padding: "12px 0" }}>
            Ningún equipo detectado todavía. Cuando un equipo PAC mande su
            primer mensaje, aparece acá automáticamente para que le
            asignes carpeta destino.
          </div>
        ) : (
          <EquipmentRoutePanel
            instruments={instruments}
            routes={config.instrumentRoutes ?? {}}
            onChange={async (serial, patch) => {
              const prev = (config.instrumentRoutes ?? {})[serial] ?? {
                hotFolderFormat: null,
                hotFolderDir: null,
                alias: null,
              };
              const next: InstrumentRouteDTO = { ...prev, ...patch };
              
              
              setConfig((c) =>
                c
                  ? {
                      ...c,
                      instrumentRoutes: { ...c.instrumentRoutes, [serial]: next },
                    }
                  : c,
              );
              try {
                await ipcInvoke<void>("set_instrument_route", {
                  serial,
                  format: next.hotFolderFormat,
                  dir: next.hotFolderDir,
                  alias: next.alias,
                });
              } catch (e) {
                setError(String(e));
              }
            }}
            onPickFolder={async (serial, label) => {
              const folder = await repo.pickFolder(label);
              if (folder) {
                
                const prev = (config.instrumentRoutes ?? {})[serial] ?? {
                  hotFolderFormat: null,
                  hotFolderDir: null,
                  alias: null,
                };
                const next: InstrumentRouteDTO = { ...prev, hotFolderDir: folder };
                setConfig((c) =>
                  c
                    ? {
                        ...c,
                        instrumentRoutes: { ...c.instrumentRoutes, [serial]: next },
                      }
                    : c,
                );
                try {
                  await ipcInvoke<void>("set_instrument_route", {
                    serial,
                    format: next.hotFolderFormat,
                    dir: next.hotFolderDir,
                    alias: next.alias,
                  });
                } catch (e) {
                  setError(String(e));
                }
              }
            }}
          />
        )}
      </Card>

      <Card title="Equipos — Configuración por modelo">
        {plugins.map((p) => {
          const type = p.supportedTypes[0];
          const inst = config.instruments[type];
          if (!inst) return null;
          return (
            <details
              key={type}
              style={{
                background: "var(--bg-elev-3)",
                border: "1px solid var(--border)",
                borderRadius: "var(--radius-sm)",
                padding: "10px 12px",
                marginBottom: 8,
              }}
            >
              <summary style={{ cursor: "pointer", display: "flex", alignItems: "center", gap: 10 }}>
                <input
                  type="checkbox"
                  checked={inst.enabled}
                  onClick={(e) => e.stopPropagation()}
                  onChange={(e) => updateInstrument(type, { enabled: e.currentTarget.checked })}
                />
                <strong style={{ fontSize: 13 }}>{p.displayName}</strong>
                <span style={{ fontSize: 11, color: "var(--fg-muted)" }}>{type}</span>
              </summary>
              <div style={{ marginTop: 12, display: "flex", flexDirection: "column", gap: 8 }}>
                <p style={{ margin: 0, fontSize: 11, color: "var(--fg-muted)" }}>
                  Las carpetas destino se configuran arriba en{" "}
                  <strong>“Salidas para automatización (hot folders)”</strong>{" "}
                  por equipo y serial. Acá quedan sólo overrides de formato.
                </p>
                <Row label="Override ShowKey (override del global)">
                  <select
                    value={inst.showKey === null ? "" : inst.showKey ? "true" : "false"}
                    onChange={(e) => {
                      const v = e.currentTarget.value;
                      updateInstrument(type, {
                        showKey: v === "" ? null : v === "true",
                      });
                    }}
                    style={{ ...inputStyle, width: 140 }}
                  >
                    <option value="">(usar global: {String(config.general.showKey)})</option>
                    <option value="true">true</option>
                    <option value="false">false</option>
                  </select>
                </Row>
                <Row label="Override ShowUnit">
                  <select
                    value={inst.showUnit === null ? "" : inst.showUnit ? "true" : "false"}
                    onChange={(e) => {
                      const v = e.currentTarget.value;
                      updateInstrument(type, {
                        showUnit: v === "" ? null : v === "true",
                      });
                    }}
                    style={{ ...inputStyle, width: 140 }}
                  >
                    <option value="">(usar global: {String(config.general.showUnit)})</option>
                    <option value="true">true</option>
                    <option value="false">false</option>
                  </select>
                </Row>
              </div>
            </details>
          );
        })}
      </Card>
    </div>
  );
}

function EquipmentRoutePanel({
  instruments,
  routes,
  onChange,
  onPickFolder,
}: {
  instruments: InstrumentOutputDTO[];
  routes: Record<string, InstrumentRouteDTO>;
  onChange: (serial: string, patch: Partial<InstrumentRouteDTO>) => void;
  onPickFolder: (serial: string, label: string) => void;
}) {
  
  const sorted = [...instruments].sort((a, b) => a.serial.localeCompare(b.serial));
  const [selectedSerial, setSelectedSerial] = useState<string>(() => sorted[0]?.serial ?? "");

  
  
  
  useEffect(() => {
    if (sorted.length === 0) {
      setSelectedSerial("");
      return;
    }
    if (!sorted.find((i) => i.serial === selectedSerial)) {
      setSelectedSerial(sorted[0].serial);
    }
  }, [sorted, selectedSerial]);

  const isConfigured = (serial: string) => {
    const r = routes[serial];
    return !!(r && r.hotFolderFormat !== null && r.hotFolderDir && r.hotFolderDir.length > 0);
  };
  const configuredCount = sorted.filter((i) => isConfigured(i.serial)).length;
  const inst = sorted.find((i) => i.serial === selectedSerial);
  const route = inst
    ? routes[inst.serial] ?? { hotFolderFormat: null, hotFolderDir: null, alias: null }
    : null;
  const active = route?.hotFolderFormat != null;

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 12, fontSize: 12 }}>
      <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
        <label style={{ color: "var(--fg-muted)", minWidth: 110 }}>Equipo:</label>
        <select
          value={selectedSerial}
          onChange={(e) => setSelectedSerial(e.currentTarget.value)}
          style={{ ...inputStyle, flex: 1 }}
        >
          {sorted.map((i) => {
            const r = routes[i.serial];
            const tag = isConfigured(i.serial) ? "✓" : "·";
            const alias = r?.alias ? ` · ${r.alias}` : "";
            return (
              <option key={i.serial} value={i.serial}>
                {tag}  {i.serial} · {i.analyzerType}{alias}
              </option>
            );
          })}
        </select>
        <span
          title={`${configuredCount} de ${sorted.length} con carpeta configurada`}
          style={{
            color: "var(--fg-muted)",
            fontSize: 11,
            background: "var(--bg-elev-3)",
            padding: "2px 8px",
            borderRadius: "var(--radius-sm)",
            border: "1px solid var(--border)",
            whiteSpace: "nowrap",
          }}
        >
          {configuredCount}/{sorted.length} configurados
        </span>
      </div>

      {inst && route ? (
        <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
            <label style={{ color: "var(--fg-muted)", minWidth: 110 }}>Alias:</label>
            <input
              type="text"
              placeholder={`p.ej. ${inst.analyzerType}-${inst.serial.slice(0, 4)}`}
              value={route.alias ?? ""}
              onChange={(e) =>
                onChange(inst.serial, { alias: e.currentTarget.value || null })
              }
              style={{ ...inputStyle, flex: 1 }}
            />
          </div>

          <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
            <label style={{ color: "var(--fg-muted)", minWidth: 110 }}>Formato:</label>
            <select
              value={route.hotFolderFormat ?? ""}
              onChange={(e) => {
                const v = e.currentTarget.value;
                onChange(inst.serial, {
                  hotFolderFormat: v === "" ? null : (v as HotFolderFormat),
                });
              }}
              style={{ ...inputStyle, flex: 1 }}
            >
              <option value="">— desactivado —</option>
              <option value="csv_all">CSV (todos los datos)</option>
              {inst.analyzerType === "OptiPMD" && (
                <option value="lims_ethernet">TXT LIMS clásico</option>
              )}
            </select>
          </div>

          <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
            <label style={{ color: "var(--fg-muted)", minWidth: 110 }}>Carpeta:</label>
            <span style={{ flex: 1 }}>
              {active ? (
                <FolderInput
                  value={route.hotFolderDir ?? ""}
                  placeholder="(elegí la carpeta que LIMS watchea)"
                  onChange={(v) => onChange(inst.serial, { hotFolderDir: v || null })}
                  onBrowse={() =>
                    onPickFolder(
                      inst.serial,
                      `Carpeta destino para ${route.alias ?? inst.serial} (${inst.analyzerType})`,
                    )
                  }
                />
              ) : (
                <span style={{ color: "var(--fg-dim)", fontSize: 11 }}>
                  Elegí un formato primero para habilitar la carpeta destino.
                </span>
              )}
            </span>
          </div>
        </div>
      ) : null}
    </div>
  );
}

function Card({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section
      style={{
        background: "var(--bg-elev-2)",
        border: "1px solid var(--border)",
        borderRadius: "var(--radius)",
        padding: 16,
      }}
    >
      <h3 style={{ margin: "0 0 12px", fontSize: 13, fontWeight: 500 }}>{title}</h3>
      <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>{children}</div>
    </section>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: "260px 1fr",
        gap: 12,
        alignItems: "center",
        fontSize: 12,
      }}
    >
      <label style={{ color: "var(--fg-muted)" }}>{label}</label>
      <div style={{ display: "flex", gap: 10, alignItems: "center" }}>{children}</div>
    </div>
  );
}

function Switch({
  checked,
  onChange,
  hint,
}: {
  checked: boolean;
  onChange: (v: boolean) => void;
  hint?: string;
}) {
  return (
    <label style={{ display: "flex", alignItems: "center", gap: 8, cursor: "pointer" }}>
      <span
        style={{
          width: 32,
          height: 18,
          borderRadius: 18,
          background: checked ? "var(--accent)" : "var(--bg-elev-3)",
          border: "1px solid var(--border)",
          position: "relative",
          transition: "background 100ms ease",
        }}
      >
        <span
          style={{
            position: "absolute",
            top: 1,
            left: checked ? 14 : 1,
            width: 14,
            height: 14,
            borderRadius: "50%",
            background: "white",
            transition: "left 100ms ease",
          }}
        />
      </span>
      <input
        type="checkbox"
        checked={checked}
        onChange={(e) => onChange(e.currentTarget.checked)}
        style={{ position: "absolute", opacity: 0, pointerEvents: "none" }}
      />
      <span style={{ fontSize: 11, color: "var(--fg)" }}>{checked ? "ON" : "OFF"}</span>
      {hint && <span style={{ fontSize: 11, color: "var(--fg-dim)" }}>{hint}</span>}
    </label>
  );
}

function FolderInput({
  value,
  placeholder,
  onChange,
  onBrowse,
}: {
  value: string;
  placeholder: string;
  onChange: (v: string) => void;
  onBrowse: () => void;
}) {
  return (
    <span style={{ display: "flex", gap: 6, flex: 1, alignItems: "center" }}>
      <input
        value={value}
        placeholder={placeholder}
        onChange={(e) => onChange(e.currentTarget.value)}
        style={{ ...inputStyle, flex: 1, fontFamily: "ui-monospace, monospace" }}
      />
      <button
        onClick={onBrowse}
        style={{
          background: "var(--bg-elev-3)",
          border: "1px solid var(--border)",
          borderRadius: "var(--radius-sm)",
          padding: "6px 10px",
          color: "var(--fg)",
          fontSize: 11,
          cursor: "pointer",
        }}
      >
        Examinar…
      </button>
    </span>
  );
}

function Hint({ children }: { children: React.ReactNode }) {
  return (
    <span style={{ fontSize: 11, color: "var(--fg-dim)" }}>{children}</span>
  );
}

const inputStyle: React.CSSProperties = {
  background: "var(--bg-elev-3)",
  border: "1px solid var(--border)",
  borderRadius: "var(--radius-sm)",
  padding: "6px 10px",
  color: "var(--fg)",
  fontSize: 12,
};

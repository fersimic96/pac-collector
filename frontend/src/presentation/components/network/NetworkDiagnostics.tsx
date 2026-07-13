

import { useEffect, useMemo, useState } from "react";
import { ipcInvoke } from "@/infrastructure/ipc/tauriClient";

interface NetInterface {
  name: string;
  ip: string;
  netmask: string;
  prefixLen: number;
  isLinkLocal: boolean;
}

type OsPlatform = "macos" | "windows" | "linux" | "unknown";

export function NetworkDiagnostics() {
  const [ifaces, setIfaces] = useState<NetInterface[]>([]);
  const [equipmentIp, setEquipmentIp] = useState("");
  const [os, setOs] = useState<OsPlatform>("unknown");
  const [loading, setLoading] = useState(true);
  
  const [pickedNicName, setPickedNicName] = useState<string>("");

  const refresh = async () => {
    setLoading(true);
    try {
      const [nics, plat] = await Promise.all([
        ipcInvoke<NetInterface[]>("list_network_interfaces"),
        ipcInvoke<OsPlatform>("os_platform"),
      ]);
      setIfaces(nics);
      setOs(plat);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    refresh();
  }, []);

  
  
  
  
  
  
  
  
  useEffect(() => {
    if (pickedNicName || ifaces.length === 0) return;
    const linkLocal = ifaces.find((n) => n.isLinkLocal);
    const nonWifiEthernet = ifaces.find(
      (n) => !n.isLinkLocal && n.name !== "en0" && /^(en|eth|enp|Ethernet)/i.test(n.name),
    );
    const fallback = ifaces[0];
    setPickedNicName((linkLocal ?? nonWifiEthernet ?? fallback)?.name ?? "");
  }, [ifaces, pickedNicName]);

  const pickedNic = useMemo(
    () => ifaces.find((n) => n.name === pickedNicName) ?? null,
    [ifaces, pickedNicName],
  );

  const match = useMemo(() => analyzeMatch(equipmentIp, ifaces), [equipmentIp, ifaces]);

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <strong style={{ fontSize: 13 }}>Interfaces de red detectadas</strong>
        <button
          onClick={refresh}
          disabled={loading}
          style={{
            fontSize: 11,
            padding: "4px 10px",
            borderRadius: 6,
            border: "1px solid var(--border)",
            background: "transparent",
            color: "var(--fg)",
            cursor: loading ? "wait" : "pointer",
          }}
        >
          {loading ? "..." : "Refrescar"}
        </button>
      </div>

      <table style={{ width: "100%", fontSize: 12, borderCollapse: "collapse" }}>
        <thead>
          <tr style={{ color: "var(--fg-muted)", textAlign: "left", borderBottom: "1px solid var(--border)" }}>
            <th style={{ padding: "6px 8px" }}>Interfaz</th>
            <th style={{ padding: "6px 8px" }}>IP</th>
            <th style={{ padding: "6px 8px" }}>Máscara</th>
            <th style={{ padding: "6px 8px" }}>Subred</th>
          </tr>
        </thead>
        <tbody>
          {ifaces.length === 0 ? (
            <tr>
              <td colSpan={4} style={{ padding: 12, color: "var(--fg-muted)", textAlign: "center" }}>
                No se detectaron interfaces (¿conectaste cable / activaste Wi-Fi?)
              </td>
            </tr>
          ) : (
            ifaces.map((nic) => {
              const subnet = subnetOf(nic.ip, nic.prefixLen);
              const isMatching = match && match.matchingNic?.name === nic.name;
              return (
                <tr
                  key={`${nic.name}-${nic.ip}`}
                  style={{
                    borderBottom: "1px solid var(--border)",
                    background: isMatching ? "rgba(34, 197, 94, 0.08)" : undefined,
                  }}
                >
                  <td style={{ padding: "6px 8px" }}>
                    <code style={{ color: nic.isLinkLocal ? "var(--fg-muted)" : "var(--fg)" }}>{nic.name}</code>
                    {nic.isLinkLocal && <span style={{ marginLeft: 6, fontSize: 10, color: "var(--fg-muted)" }}>(link-local)</span>}
                  </td>
                  <td style={{ padding: "6px 8px" }}>
                    <code>{nic.ip}</code>
                  </td>
                  <td style={{ padding: "6px 8px", color: "var(--fg-muted)" }}>
                    <code>{nic.netmask}</code>
                  </td>
                  <td style={{ padding: "6px 8px", color: "var(--fg-muted)" }}>
                    <code>{subnet}/{nic.prefixLen}</code>
                  </td>
                </tr>
              );
            })
          )}
        </tbody>
      </table>

      <div style={{ display: "flex", gap: 16, marginTop: 6, flexWrap: "wrap" }}>
        <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
          <label style={{ fontSize: 12, color: "var(--fg-muted)" }}>IP del equipo PAC (la que muestra en su pantalla / Service)</label>
          <input
            type="text"
            placeholder="ej. 192.168.0.100"
            value={equipmentIp}
            onChange={(e) => setEquipmentIp(e.currentTarget.value.trim())}
            style={{
              fontSize: 13,
              padding: "8px 10px",
              border: "1px solid var(--border)",
              borderRadius: 6,
              background: "var(--bg-elev-3)",
              color: "var(--fg)",
              width: 220,
            }}
          />
        </div>
        <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
          <label style={{ fontSize: 12, color: "var(--fg-muted)" }}>Interfaz a configurar (la del cable al equipo)</label>
          <select
            value={pickedNicName}
            onChange={(e) => setPickedNicName(e.currentTarget.value)}
            style={{
              fontSize: 13,
              padding: "8px 10px",
              border: "1px solid var(--border)",
              borderRadius: 6,
              background: "var(--bg-elev-3)",
              color: "var(--fg)",
              width: 280,
            }}
          >
            {ifaces.length === 0 && <option value="">(sin interfaces)</option>}
            {ifaces.map((nic) => (
              <option key={nic.name} value={nic.name}>
                {nic.name} — {nic.ip}
                {nic.isLinkLocal ? " (link-local — probable Ethernet)" : ""}
                {nic.name === "en0" ? " (Wi-Fi — NO elegir)" : ""}
              </option>
            ))}
          </select>
        </div>
      </div>

      {match && <Verdict match={match} os={os} pickedNic={pickedNic} />}
    </div>
  );
}

function Verdict({
  match,
  os,
  pickedNic,
}: {
  match: NonNullable<ReturnType<typeof analyzeMatch>>;
  os: OsPlatform;
  pickedNic: NetInterface | null;
}) {
  if (match.kind === "ok") {
    return (
      <div
        style={{
          padding: 12,
          borderRadius: 6,
          background: "rgba(34, 197, 94, 0.08)",
          border: "1px solid rgba(34, 197, 94, 0.3)",
          fontSize: 12,
          color: "var(--green)",
        }}
      >
        ✓ Tu PC tiene la interfaz <code>{match.matchingNic.name}</code> ({match.matchingNic.ip})
        en la misma subred que el equipo. El UDP broadcast debería llegar.
      </div>
    );
  }

  const suggestedIp = suggestSiblingIp(match.equipmentIp);
  const nicName = pickedNic?.name ?? "Ethernet";
  const isWifiPick = nicName === "en0";

  return (
    <div
      style={{
        padding: 12,
        borderRadius: 6,
        background: "rgba(239, 68, 68, 0.06)",
        border: "1px solid rgba(239, 68, 68, 0.3)",
        fontSize: 12,
        color: "var(--fg)",
        display: "flex",
        flexDirection: "column",
        gap: 10,
      }}
    >
      <div style={{ color: "var(--red)" }}>
        ⚠️ Tu PC <strong>no tiene</strong> ninguna interfaz en la subred del equipo
        ({match.equipmentSubnet}). El UDP broadcast no cruza subredes — el equipo no
        va a llegar a este server.
      </div>

      {isWifiPick && (
        <div style={{ color: "var(--amber)", fontWeight: 500 }}>
          ⚠️ Estás por configurar manualmente la <strong>Wi-Fi (en0)</strong>. Eso te corta el internet.
          Cambiá el dropdown de arriba a una interfaz Ethernet (típicamente la que diga "link-local").
        </div>
      )}

      <PlatformSteps os={os} suggestedIp={suggestedIp} suggestedNic={nicName} />

      <button
        onClick={() => ipcInvoke("open_network_settings").catch(() => undefined)}
        style={{
          alignSelf: "flex-start",
          fontSize: 12,
          padding: "6px 12px",
          borderRadius: 6,
          border: "none",
          background: "var(--accent)",
          color: "white",
          cursor: "pointer",
        }}
      >
        Abrir Configuración de red {osLabel(os)}
      </button>
    </div>
  );
}

function PlatformSteps({
  os,
  suggestedIp,
  suggestedNic,
}: {
  os: OsPlatform;
  suggestedIp: string;
  suggestedNic: string;
}) {
  if (os === "macos") {
    return (
      <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
        <strong>macOS — pasos:</strong>
        <ol style={{ margin: 0, paddingLeft: 20, lineHeight: 1.7 }}>
          <li>Apple menu → System Settings → Network</li>
          <li>Seleccioná la interfaz <code>{suggestedNic}</code> (la del cable al equipo)</li>
          <li>Details → TCP/IP → Configure IPv4: <strong>Manually</strong></li>
          <li>IP Address: <code>{suggestedIp}</code></li>
          <li>Subnet Mask: <code>255.255.255.0</code></li>
          <li>Router: dejar vacío</li>
          <li>OK → Apply. Después en esta app: Detener/Iniciar listeners</li>
        </ol>
      </div>
    );
  }
  if (os === "windows") {
    return (
      <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
        <strong>Windows — pasos:</strong>
        <ol style={{ margin: 0, paddingLeft: 20, lineHeight: 1.7 }}>
          <li>Start → Settings → Network &amp; Internet → Ethernet</li>
          <li>Click en la conexión Ethernet activa (la del cable al equipo)</li>
          <li>IP assignment → Edit → Manual → activá IPv4</li>
          <li>IP address: <code>{suggestedIp}</code></li>
          <li>Subnet mask: <code>255.255.255.0</code></li>
          <li>Gateway / DNS: en blanco</li>
          <li>Save. Después en esta app: Detener/Iniciar listeners</li>
        </ol>
        <span style={{ fontSize: 11, color: "var(--fg-muted)" }}>
          Equivalente CLI (PowerShell admin):
          {" "}<code>New-NetIPAddress -InterfaceAlias "Ethernet" -IPAddress {suggestedIp} -PrefixLength 24</code>
        </span>
      </div>
    );
  }
  if (os === "linux") {
    return (
      <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
        <strong>Linux — pasos:</strong>
        <ol style={{ margin: 0, paddingLeft: 20, lineHeight: 1.7 }}>
          <li>NetworkManager / Settings → Network → cableada</li>
          <li>IPv4 → Manual → Address: <code>{suggestedIp}</code> Netmask: <code>24</code></li>
          <li>Apply / Reactivar la interfaz</li>
        </ol>
        <span style={{ fontSize: 11, color: "var(--fg-muted)" }}>
          Equivalente CLI: <code>sudo ip addr add {suggestedIp}/24 dev {suggestedNic}</code>
        </span>
      </div>
    );
  }
  return null;
}

function osLabel(os: OsPlatform): string {
  return os === "macos" ? "(macOS)" : os === "windows" ? "(Windows)" : os === "linux" ? "(Linux)" : "";
}

interface MatchOk {
  kind: "ok";
  matchingNic: NetInterface;
  equipmentSubnet: string;
}
interface MatchMismatch {
  kind: "mismatch";
  equipmentIp: string;
  equipmentSubnet: string;
  matchingNic: null;
}
type MatchAnalysis = MatchOk | MatchMismatch;

function analyzeMatch(equipmentIp: string, ifaces: NetInterface[]): MatchAnalysis | null {
  const ip = parseIpv4(equipmentIp);
  if (!ip) return null;

  
  
  
  for (const nic of ifaces) {
    const nicIp = parseIpv4(nic.ip);
    if (!nicIp) continue;
    if (sameSubnet(ip, nicIp, nic.prefixLen)) {
      return { kind: "ok", matchingNic: nic, equipmentSubnet: subnetOf(equipmentIp, nic.prefixLen) };
    }
  }
  return {
    kind: "mismatch",
    equipmentIp,
    equipmentSubnet: subnetOf(equipmentIp, 24),
    matchingNic: null,
  };
}

function parseIpv4(s: string): number[] | null {
  const parts = s.split(".");
  if (parts.length !== 4) return null;
  const nums = parts.map((p) => Number(p));
  if (nums.some((n) => !Number.isInteger(n) || n < 0 || n > 255)) return null;
  return nums;
}

function sameSubnet(a: number[], b: number[], prefixLen: number): boolean {
  const mask = prefixToMaskBytes(prefixLen);
  return a.every((octet, i) => (octet & mask[i]) === (b[i] & mask[i]));
}

function prefixToMaskBytes(prefix: number): number[] {
  const out = [0, 0, 0, 0];
  let remaining = prefix;
  for (let i = 0; i < 4 && remaining > 0; i++) {
    const bits = Math.min(8, remaining);
    out[i] = (0xff << (8 - bits)) & 0xff;
    remaining -= bits;
  }
  return out;
}

function subnetOf(ip: string, prefix: number): string {
  const parts = parseIpv4(ip);
  if (!parts) return ip;
  const mask = prefixToMaskBytes(prefix);
  return parts.map((p, i) => p & mask[i]).join(".");
}

function suggestSiblingIp(equipmentIp: string): string {
  const parts = parseIpv4(equipmentIp);
  if (!parts) return "192.168.0.50";
  const last = parts[3] === 50 ? 51 : 50;
  return `${parts[0]}.${parts[1]}.${parts[2]}.${last}`;
}

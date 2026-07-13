import { describe, it, expect } from "vitest";
import { Instrument } from "./Instrument";
import type { InstrumentOutputDTO } from "@/domain/outputDTO/InstrumentOutputDTO";

const base: InstrumentOutputDTO = {
  serial: "2125",
  analyzerType: "OptiPMD",
  alias: null,
  lastIp: "192.168.50.10",
  firmware: null,
  firstSeenAt: "2026-04-25T14:00:00Z",
  lastSeenAt: "2026-05-06T19:00:00Z",
  totalSamples: 5,
  enabled: true,
};

describe("Instrument.isOnline", () => {
  it("is true when last_seen within 60s", () => {
    const now = new Date("2026-05-06T19:00:30Z");
    expect(Instrument.isOnline(base, now)).toBe(true);
  });
  it("is false when last_seen >60s ago", () => {
    const now = new Date("2026-05-06T19:02:00Z");
    expect(Instrument.isOnline(base, now)).toBe(false);
  });
});

describe("Instrument.displayName", () => {
  it("uses alias when set", () => {
    expect(Instrument.displayName({ ...base, alias: "Distill-1" })).toBe("Distill-1");
  });
  it("falls back to serial+type when no alias", () => {
    expect(Instrument.displayName(base)).toBe("2125 (OptiPMD)");
  });
  it("ignores empty alias", () => {
    expect(Instrument.displayName({ ...base, alias: "   " })).toBe("2125 (OptiPMD)");
  });
});

describe("Instrument.validateAlias", () => {
  it("accepts empty (clears alias)", () => {
    expect(Instrument.validateAlias("").valid).toBe(true);
  });
  it("accepts normal name", () => {
    expect(Instrument.validateAlias("Distill-1").valid).toBe(true);
  });
  it("rejects too long", () => {
    const long = "x".repeat(100);
    expect(Instrument.validateAlias(long).valid).toBe(false);
  });
});

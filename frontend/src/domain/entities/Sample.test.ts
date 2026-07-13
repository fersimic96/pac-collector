

import { describe, it, expect } from "vitest";
import { Sample } from "./Sample";
import type { SampleOutputDTO } from "@/domain/outputDTO/SampleOutputDTO";

const baseSample: SampleOutputDTO = {
  uuid: "abc-123",
  serial: "2125",
  analyzerType: "OptiPMD",
  sampleIdentifier: "29",
  operator: "LUCAS",
  program: "ASTM D7345#",
  startAt: "2026-04-25T14:45:00Z",
  endAt: "2026-04-25T14:55:00Z",
  ibp: 142.7,
  fbp: 366.9,
  residue: 1.8,
  recovery: 97.8,
  fbpVolume: 97.0,
  endOfTest: true,
  alarmBitmask: 0,
  curve: [],
  extra: {},
  sourceIp: "192.168.50.10",
  receivedAt: "2026-05-06T19:08:03Z",
};

describe("Sample.isComplete", () => {
  it("returns true when endOfTest is true", () => {
    expect(Sample.isComplete(baseSample)).toBe(true);
  });
  it("returns false when endOfTest is false", () => {
    expect(Sample.isComplete({ ...baseSample, endOfTest: false })).toBe(false);
  });
  it("returns false when endOfTest is null", () => {
    expect(Sample.isComplete({ ...baseSample, endOfTest: null })).toBe(false);
  });
});

describe("Sample.hasAlarms", () => {
  it("returns false on alarmBitmask=0", () => {
    expect(Sample.hasAlarms(baseSample)).toBe(false);
  });
  it("returns true on non-zero alarmBitmask", () => {
    expect(Sample.hasAlarms({ ...baseSample, alarmBitmask: 0x1000 })).toBe(true);
  });
  it("returns false on null alarmBitmask", () => {
    expect(Sample.hasAlarms({ ...baseSample, alarmBitmask: null })).toBe(false);
  });
});

describe("Sample.hasCurve", () => {
  it("returns false on empty curve", () => {
    expect(Sample.hasCurve(baseSample)).toBe(false);
  });
  it("returns true when curve has points", () => {
    expect(
      Sample.hasCurve({
        ...baseSample,
        curve: [{ pctRecovered: 1, temperatureC: 150.0 }],
      }),
    ).toBe(true);
  });
});

describe("Sample.displayLabel", () => {
  it("includes sampleIdentifier and operator", () => {
    const label = Sample.displayLabel(baseSample);
    expect(label).toContain("29");
    expect(label).toContain("LUCAS");
  });
  it("falls back to '?' for missing operator", () => {
    const label = Sample.displayLabel({ ...baseSample, operator: null });
    expect(label).toContain("?");
  });
});

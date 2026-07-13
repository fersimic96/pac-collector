

import type { SampleOutputDTO } from "@/domain/outputDTO/SampleOutputDTO";

export class Sample {
  static isComplete(sample: SampleOutputDTO): boolean {
    return sample.endOfTest === true;
  }

  static hasAlarms(sample: SampleOutputDTO): boolean {
    return (sample.alarmBitmask ?? 0) !== 0;
  }

  static hasCurve(sample: SampleOutputDTO): boolean {
    return sample.curve.length > 0;
  }

  
  static displayLabel(sample: SampleOutputDTO): string {
    const date = sample.startAt
      ? new Date(sample.startAt).toLocaleString("es-AR", { dateStyle: "short", timeStyle: "short" })
      : "sin fecha";
    return `${sample.sampleIdentifier} — ${sample.operator ?? "?"} (${date})`;
  }
}

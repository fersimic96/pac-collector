

import type { InstrumentOutputDTO } from "@/domain/outputDTO/InstrumentOutputDTO";

export class Instrument {
  
  static ONLINE_THRESHOLD_SECONDS = 60;

  static isOnline(instrument: InstrumentOutputDTO, now: Date = new Date()): boolean {
    const lastSeen = new Date(instrument.lastSeenAt);
    const ageSeconds = (now.getTime() - lastSeen.getTime()) / 1000;
    return ageSeconds < Instrument.ONLINE_THRESHOLD_SECONDS;
  }

  
  static displayName(instrument: InstrumentOutputDTO): string {
    if (instrument.alias && instrument.alias.trim().length > 0) {
      return instrument.alias.trim();
    }
    return `${instrument.serial} (${instrument.analyzerType})`;
  }

  
  static validateAlias(alias: string): { valid: boolean; reason?: string } {
    const trimmed = alias.trim();
    if (trimmed.length === 0) return { valid: true }; 
    if (trimmed.length > 64) return { valid: false, reason: "Máximo 64 caracteres" };
    return { valid: true };
  }
}

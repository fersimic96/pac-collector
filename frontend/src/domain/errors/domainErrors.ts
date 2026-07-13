

export class ConfigNotInitializedError extends Error {
  constructor() {
    super("La configuración no está inicializada (db_dir / recent_dir).");
    this.name = "ConfigNotInitializedError";
  }
}

export class ConfigInvalidError extends Error {
  constructor(public field: string, public reason: string) {
    super(`Config inválida: ${field}: ${reason}`);
    this.name = "ConfigInvalidError";
  }
}

export class InstrumentNotFoundError extends Error {
  constructor(public serial: string) {
    super(`Instrumento ${serial} no encontrado`);
    this.name = "InstrumentNotFoundError";
  }
}

export class SampleNotFoundError extends Error {
  constructor(public uuid: string) {
    super(`Muestra ${uuid} no encontrada`);
    this.name = "SampleNotFoundError";
  }
}

export class IpcError extends Error {
  constructor(public command: string, message: string) {
    super(`IPC[${command}]: ${message}`);
    this.name = "IpcError";
  }
}

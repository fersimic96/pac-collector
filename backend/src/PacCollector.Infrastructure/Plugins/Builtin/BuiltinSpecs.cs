namespace PacCollector.Infrastructure.Plugins.Builtin;

// puntero a los 7 specs LIMS de PAC cargados desde JSON (embedded + override de disco).
// El loader busca primero los .json embebidos como recursos y los pisa con los .json
// del DataDir/plugins/lims/ si existen. Esto permite agregar/cambiar equipos sin recompilar.
public static class BuiltinSpecs
{
    // fields comunes a todos los equipos PAC (universales, no se sobreescriben por equipo)
    public static readonly IReadOnlyList<PacFieldSpec> CommonFields = new PacFieldSpec[]
    {
        new("AnalyzerType", "Modelo del equipo", "", "Identificación"),
        new("AnalyzerSerialNumber", "N° de serie", "", "Identificación"),
        new("AnalyzerNumber", "ID del instrumento", "", "Identificación"),
        new("FirmwareVersion", "Versión de firmware", "", "Identificación"),
        new("SampleIdentifier", "ID de la muestra", "", "Identificación"),
        new("SampleType", "Tipo de muestra", "", "Identificación"),
        new("OperatorId", "Operador", "", "Identificación"),
        new("ProgramName", "Programa/método", "", "Identificación"),
        new("AnalyzerNetworkAddress", "IP del analizador", "", "Identificación"),
        new("StartRunDate", "Fecha de inicio", "", "Tiempos"),
        new("StartRunTime", "Hora de inicio", "", "Tiempos"),
        new("EndRunDate", "Fecha de fin", "", "Tiempos"),
        new("EndRunTime", "Hora de fin", "", "Tiempos"),
        new("DuringRunAlarm", "Códigos de alarma (bitmask)", "", "Tiempos"),
        new("EndOfTest", "Test terminó OK (1=sí)", "", "Tiempos"),
        new("AmbiantTemperatureAtStart", "Temperatura ambiente al iniciar", "°C", "Condiciones"),
        new("AtmosphericPressure", "Presión atmosférica al iniciar", "kPa", "Condiciones"),
        new("PressureUnit", "Unidad de presión", "", "Condiciones"),
        new("TemperatureUnit", "Unidad de temperatura", "", "Condiciones"),
        new("BarometricCorrection", "Corrección barométrica aplicada", "", "Condiciones"),
    };

    private static readonly IReadOnlyList<PacInstrumentSpec> _embedded = PacInstrumentSpecLoader.LoadAll();

    public static PacInstrumentSpec OptiPmd => FindByType("OptiPMD");
    public static PacInstrumentSpec OptiCpp => FindByType("OptiCPP");
    public static PacInstrumentSpec OptiFpp => FindByType("OptiFPP");
    public static PacInstrumentSpec OptiFzp => FindByType("OptiFZP");
    public static PacInstrumentSpec OptiMpp => FindByType("OptiMPP");
    public static PacInstrumentSpec OptiMvd => FindByType("OptiMVD");
    public static PacInstrumentSpec OptiFuel => FindByType("OptiFuel");

    public static IReadOnlyList<PacInstrumentSpec> All => _embedded;

    private static PacInstrumentSpec FindByType(string analyzerType)
        => _embedded.Single(s => string.Equals(s.AnalyzerType, analyzerType, StringComparison.Ordinal));
}

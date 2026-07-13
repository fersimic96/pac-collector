# PAC Collector (.NET edition)

Servidor de captura de muestras para instrumentos PAC del laboratorio
(OptiPMD, OptiCPP, OptiFPP, OptiFZP, OptiMPP, OptiMVD, OptiFuel).

Stack: .NET 10 LTS + ASP.NET Core Minimal API + Photino.NET (WebView2) +
React/TypeScript en el frontend.

## Estructura

```
src/
  PacCollector.Domain/          entidades, value objects, ports
  PacCollector.Application/     casos de uso, servicios
  PacCollector.Infrastructure/  red, persistencia, archivos, plugins
  PacCollector.Api/             ASP.NET Core minimal API + Worker Service
  PacCollector.Shell/           ventana de escritorio (Photino.NET)

tests/
  PacCollector.Domain.Tests/
  PacCollector.Application.Tests/
  PacCollector.Infrastructure.Tests/
  PacCollector.ParityTests/     compara outputs contra referencia
```

## Build

```bash
dotnet restore
dotnet build
dotnet test
```

## Deploy

Genera un único `.exe` self-contained con [Velopack](https://github.com/velopack/velopack).
Ver `docs/INSTALL.md` (pendiente).

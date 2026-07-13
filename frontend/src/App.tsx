

import { useState } from "react";
import { AppShell } from "@/presentation/components/layout/AppShell";
import type { SidebarKey } from "@/presentation/components/layout/Sidebar";
import { Dashboard } from "@/presentation/views/Dashboard";
import { SamplesView } from "@/presentation/views/SamplesView";
import { InstrumentsView } from "@/presentation/views/InstrumentsView";
import { LogsView } from "@/presentation/views/LogsView";
import { PluginsView } from "@/presentation/views/PluginsView";
import { ConfigView } from "@/presentation/views/ConfigView";
import { AnalysisView } from "@/presentation/views/AnalysisView";

function App() {
  const [view, setView] = useState<SidebarKey>("dashboard");

  return (
    <AppShell current={view} onNavigate={setView}>
      {view === "dashboard" && <Dashboard />}
      {view === "instruments" && <InstrumentsView />}
      {view === "samples" && <SamplesView />}
      {view === "analysis" && <AnalysisView />}
      {view === "plugins" && <PluginsView />}
      {view === "config" && <ConfigView />}
      {view === "logs" && <LogsView />}
    </AppShell>
  );
}

export default App;

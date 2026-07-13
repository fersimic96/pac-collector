

import { test, expect } from "@playwright/test";
import { installTauriMock } from "./setup";

test.beforeEach(async ({ page }) => {
  await installTauriMock(page);
});

test("app shell renders with sidebar + topbar + dashboard by default", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByText("PAC Collector").first()).toBeVisible();
  await expect(page.getByRole("button", { name: "Dashboard" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Equipos" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Muestras" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Análisis" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Plugins" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Configuración" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Logs" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
});

test("clicking Plugins shows plugin cards with built-in OptiPMD", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Plugins" }).click();
  await expect(page.getByRole("heading", { name: "Plugins" })).toBeVisible();
  await expect(page.getByText("PAC OptiPMD")).toBeVisible();
  await expect(page.getByText("built-in").first()).toBeVisible();
});

test("Configuration view loads all settings sections", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Configuración" }).click();
  await expect(page.getByRole("heading", { name: "Configuración", exact: true })).toBeVisible();
  await expect(page.getByText("Delimitador entre campos")).toBeVisible();
  await expect(page.getByText("Fin de línea (EOL)")).toBeVisible();
  await expect(page.getByText(/DB folder/)).toBeVisible();
  await expect(page.getByRole("button", { name: /Guardar configuración/i })).toBeVisible();
});

test("Samples view shows empty state when no samples", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Muestras" }).click();
  await expect(page.getByRole("heading", { name: "Muestras" })).toBeVisible();
  await expect(page.getByText("Sin muestras todavía")).toBeVisible();
});

test("Logs view shows waiting message", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Logs" }).click();
  await expect(page.getByRole("heading", { name: "Logs" })).toBeVisible();
  await expect(page.getByText("Esperando eventos")).toBeVisible();
});

test("Configuration view exposes output format toggles", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Configuración" }).click();
  await expect(page.getByText(/Formatos de salida/i)).toBeVisible();
  await expect(page.getByText("JSON crudo (auditoría)")).toBeVisible();
  await expect(page.getByText("TXT formato LIMS clásico (key;value;)")).toBeVisible();
  await expect(page.getByText("TXT legible (humano)")).toBeVisible();
  await expect(page.getByText("CSV curva de destilación")).toBeVisible();
});

test("server status pill shows server alive (mocked)", async ({ page }) => {
  await page.goto("/");
  
  const banner = page.getByRole("banner");
  await expect(banner.getByText("Server vivo")).toBeVisible();
  await expect(banner.getByText(/UDP 3000.*TCP 9980/)).toBeVisible();
});

test("TopBar shows print server pill + IPP port + start button", async ({ page }) => {
  await page.goto("/");
  const banner = page.getByRole("banner");
  await expect(banner.getByText("Print detenido")).toBeVisible();
  await expect(banner.getByText(/IPP 631/)).toBeVisible();
  await expect(banner.getByRole("button", { name: "Iniciar print server" })).toBeVisible();
});

test("Configuration view shows Print Server card with toggle and port", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Configuración" }).click();
  await expect(page.getByText("Print Server (modo Iris)")).toBeVisible();
  await expect(page.getByText("Habilitar print server")).toBeVisible();
  await expect(page.getByText("Puerto IPP")).toBeVisible();
});

// E2E contra el backend .NET real (via vite proxy en :1420 -> :5174).
// Recorre las vistas principales y catchea cualquier console error o exception
// no manejada que aparezca por DTO mismatches entre frontend y API .NET.

import { test, expect, ConsoleMessage, Page } from "@playwright/test";

interface CapturedError {
  type: "console" | "pageerror";
  text: string;
  url?: string;
}

function captureErrors(page: Page): CapturedError[] {
  const errors: CapturedError[] = [];

  // ignorar warnings benignos del runtime de React/Vite
  const isBenign = (text: string) =>
    text.includes("React DevTools") ||
    text.includes("Download the React DevTools") ||
    text.includes("[vite]") ||
    text.includes("net::ERR_FAILED chrome-extension"); // extensiones del browser

  page.on("console", (msg: ConsoleMessage) => {
    if (msg.type() === "error") {
      const text = msg.text();
      if (!isBenign(text)) errors.push({ type: "console", text });
    }
  });

  page.on("pageerror", (err) => {
    if (!isBenign(err.message)) {
      errors.push({ type: "pageerror", text: err.message });
    }
  });

  return errors;
}

test.describe(".NET backend smoke", () => {
  test("dashboard carga y muestra estado del server", async ({ page }) => {
    const errors = captureErrors(page);
    await page.goto("/");

    // titulo principal de la vista
    await expect(page.getByRole("heading", { level: 1, name: "Dashboard" })).toBeVisible();

    // indicador "Server vivo" o "Detenido" en el TopBar (depende de status.running)
    await expect(page.locator("text=/Server vivo|Detenido/").first()).toBeVisible();

    // tarjetas de metricas en el dashboard
    await expect(page.getByText("Equipos detectados", { exact: true })).toBeVisible();
    await expect(page.getByText("Equipos online", { exact: true })).toBeVisible();
    await expect(page.getByText("Muestras hoy", { exact: true }).first()).toBeVisible();

    expect(errors, `errores: ${JSON.stringify(errors)}`).toHaveLength(0);
  });

  test("toggle Iniciar/Detener listeners", async ({ page }) => {
    const errors = captureErrors(page);
    await page.goto("/");

    // el TopBar tiene un boton con aria-label "Iniciar listeners" o "Detener listeners"
    const toggleBtn = page.getByRole("button", { name: /^(Iniciar|Detener) listeners$/ });
    await expect(toggleBtn).toBeVisible();

    const initialLabel = await toggleBtn.getAttribute("aria-label");
    await toggleBtn.click();

    // esperar a que cambie el estado (la API responde en ms; el polling del front es ~1s)
    await page.waitForTimeout(1500);

    const expectedNext = initialLabel?.startsWith("Iniciar") ? "Detener listeners" : "Iniciar listeners";
    await expect(page.getByRole("button", { name: expectedNext })).toBeVisible({ timeout: 5000 });

    expect(errors, `errores: ${JSON.stringify(errors)}`).toHaveLength(0);
  });

  test("vista Equipos carga sin errores", async ({ page }) => {
    const errors = captureErrors(page);
    await page.goto("/");
    await page.getByRole("button", { name: "Equipos" }).click();
    await expect(page.getByRole("heading", { level: 1, name: "Equipos" })).toBeVisible();
    expect(errors, `errores: ${JSON.stringify(errors)}`).toHaveLength(0);
  });

  test("vista Muestras carga sin errores", async ({ page }) => {
    const errors = captureErrors(page);
    await page.goto("/");
    await page.getByRole("button", { name: "Muestras" }).click();
    await expect(page.getByRole("heading", { level: 1, name: "Muestras" })).toBeVisible();
    expect(errors, `errores: ${JSON.stringify(errors)}`).toHaveLength(0);
  });

  test("vista Plugins muestra los 7 equipos PAC", async ({ page }) => {
    const errors = captureErrors(page);
    await page.goto("/");
    await page.getByRole("button", { name: "Plugins" }).click();
    await expect(page.getByRole("heading", { level: 1, name: "Plugins" })).toBeVisible();

    const equipos = ["OptiPMD", "OptiCPP", "OptiFPP", "OptiFZP", "OptiMPP", "OptiMVD", "OptiFuel"];
    for (const e of equipos) {
      await expect(page.getByText(`PAC ${e}`, { exact: false }).first()).toBeVisible();
    }
    expect(errors, `errores: ${JSON.stringify(errors)}`).toHaveLength(0);
  });

  test("vista Configuracion carga sin errores", async ({ page }) => {
    const errors = captureErrors(page);
    await page.goto("/");
    await page.getByRole("button", { name: "Configuración" }).click();
    // h1 con texto exacto (hay subtitulos h3 que tambien dicen "Configuracion")
    await expect(page.getByRole("heading", { level: 1, name: "Configuración" })).toBeVisible();
    expect(errors, `errores: ${JSON.stringify(errors)}`).toHaveLength(0);
  });

  test("vista Analisis carga sin errores", async ({ page }) => {
    const errors = captureErrors(page);
    await page.goto("/");
    await page.getByRole("button", { name: "Análisis" }).click();
    await expect(page.getByRole("heading", { level: 1, name: "Análisis" })).toBeVisible();
    expect(errors, `errores: ${JSON.stringify(errors)}`).toHaveLength(0);
  });

  test("vista Logs carga sin errores", async ({ page }) => {
    const errors = captureErrors(page);
    await page.goto("/");
    await page.getByRole("button", { name: "Logs" }).click();
    await expect(page.getByRole("heading", { level: 1, name: "Logs" })).toBeVisible();
    expect(errors, `errores: ${JSON.stringify(errors)}`).toHaveLength(0);
  });
});

"use strict";

const $ = (id) => document.getElementById(id);
const webview = window.chrome && window.chrome.webview;

const pending = new Map();
let nextId = 1;
let browsers = [];

function postCmd(cmd, args = {}) {
  return new Promise((resolve, reject) => {
    if (!webview) {
      reject(new Error("not running in the app"));
      return;
    }
    const id = nextId++;
    pending.set(id, { resolve, reject });
    webview.postMessage({ id, cmd, ...args });
  });
}

if (webview) {
  webview.addEventListener("message", (e) => {
    const m = e.data;
    const p = pending.get(m.id);
    if (!p) return;
    pending.delete(m.id);
    m.ok ? p.resolve(m.data) : p.reject(new Error(m.error || "error"));
  });
}

function setStatus(text, busy) {
  const status = $("status");
  if (!status) return;
  $("status-text").textContent = text;
  status.classList.toggle("busy", !!busy);
}

function fillProfiles(browser) {
  const sel = $("profile");
  sel.innerHTML = "";
  const names = (browser && browser.profiles) || [];
  if (names.length === 0) {
    sel.innerHTML = "<option>Default</option>";
    return;
  }
  for (const name of names) {
    const o = document.createElement("option");
    o.value = o.textContent = name;
    sel.appendChild(o);
  }
}

function render(report) {
  $("empty").classList.add("hidden");
  $("migration").classList.add("hidden");
  $("results").classList.remove("hidden");

  $("r-name").textContent = report.label;
  $("r-badge").hidden = !report.sample;

  $("s-total").textContent = report.total;
  $("s-hosts").textContent = report.hosts;
  $("s-bearer").textContent = report.v10;
  $("s-bound").textContent = report.v20;
  $("dbsc").classList.toggle("hidden", !report.dbsc);

  const list = $("site-list");
  list.innerHTML = "";
  const max = report.sites.reduce((m, s) => Math.max(m, s.cookies), 1);
  for (const s of report.sites) {
    const row = document.createElement("div");
    row.className = "site";
    const pct = Math.round((s.cookies / max) * 100);
    row.innerHTML =
      `<span class="host"></span><span class="bar"><i style="width:${pct}%"></i></span><span class="n">${s.cookies}</span>`;
    row.querySelector(".host").textContent = s.host;
    list.appendChild(row);
  }
}

function showMessage(text, isError) {
  $("results").classList.add("hidden");
  $("migration").classList.add("hidden");
  const el = $("empty");
  el.classList.remove("hidden");
  el.className = "empty" + (isError ? " err" : "");
  el.textContent = text;
}

function renderMigration(r) {
  $("results").classList.add("hidden");
  $("empty").classList.add("hidden");
  $("migration").classList.remove("hidden");

  $("m-dest").textContent = r.dest;
  $("m-files").textContent = r.filesCopied;
  $("m-rekeyed").textContent = r.totalResealed;

  const list = $("store-list");
  list.innerHTML = "";
  for (const s of r.stores) {
    const row = document.createElement("div");
    row.className = "store-row" + (s.present ? "" : " absent");
    row.innerHTML = `<span class="s-name"></span><span class="s-val">${s.present ? s.resealed + " re-keyed" : "not present"}</span>`;
    row.querySelector(".s-name").textContent = s.store;
    list.appendChild(row);
  }
}

async function migrate() {
  const idx = $("browser").selectedIndex;
  const browser = idx >= 0 ? browsers[idx] : null;
  if (!browser) {
    showMessage("No Chromium browser detected to migrate.", true);
    return;
  }
  setStatus("migrating", true);
  $("migrate").disabled = true;
  $("analyze").disabled = true;
  try {
    const r = await postCmd("migrate", {
      userDataDir: browser.userDataDir,
      profile: $("profile").value,
    });
    if (!r.cancelled) {
      renderMigration(r);
    }
    setStatus("ready");
  } catch (err) {
    showMessage(`Migration failed: ${err.message}`, true);
    setStatus("ready");
  } finally {
    $("migrate").disabled = false;
    $("analyze").disabled = false;
  }
}

async function analyze() {
  const idx = $("browser").selectedIndex;
  const browser = idx >= 0 ? browsers[idx] : null;
  if (!browser) {
    showMessage("No Chromium browser detected on this machine.", false);
    return;
  }
  setStatus("analyzing", true);
  $("analyze").disabled = true;
  try {
    const report = await postCmd("analyze", {
      userDataDir: browser.userDataDir,
      profile: $("profile").value,
    });
    render(report);
    setStatus("ready");
  } catch (err) {
    showMessage(
      `Could not read this profile: ${err.message}. Close the browser and retry, or it may use App-Bound encryption.`,
      true,
    );
    setStatus("ready");
  } finally {
    $("analyze").disabled = false;
  }
}

async function init() {
  if (!webview) {
    showMessage("Open this from the session-migrate app.", false);
    return;
  }
  setStatus("scanning", true);
  try {
    const found = await postCmd("detect");
    browsers = found.browsers || [];
    const sel = $("browser");
    sel.innerHTML = "";
    if (browsers.length === 0) {
      sel.innerHTML = "<option>none found</option>";
    } else {
      for (const b of browsers) {
        const o = document.createElement("option");
        o.value = o.textContent = b.name;
        sel.appendChild(o);
      }
      fillProfiles(browsers[0]);
    }
    sel.addEventListener("change", () => fillProfiles(browsers[sel.selectedIndex]));

    showMessage("Pick a source profile and click Analyze to see what would survive a move.", false);
    setStatus("ready");
  } catch (err) {
    showMessage(err.message, true);
    setStatus("ready");
  }
}

function setButtons(disabled) {
  for (const id of ["analyze", "migrate", "export", "import"]) {
    $(id).disabled = disabled;
  }
}

async function exportBundle() {
  const idx = $("browser").selectedIndex;
  const browser = idx >= 0 ? browsers[idx] : null;
  const passphrase = $("passphrase").value;
  if (!browser) {
    showMessage("No Chromium browser selected.", true);
    return;
  }
  if (!passphrase) {
    showMessage("Enter a bundle passphrase first.", true);
    return;
  }
  setStatus("exporting", true);
  setButtons(true);
  try {
    const r = await postCmd("export", {
      userDataDir: browser.userDataDir,
      profile: $("profile").value,
      passphrase,
      browser: browser.name,
    });
    if (!r.cancelled) {
      showMessage(
        `Bundle exported to ${r.dest} — ${r.bearer} bearer cookie(s), ${r.appBound} app-bound, key ${r.fingerprint}. Copy the folder to the other machine and Import it there.`,
        false,
      );
    }
    setStatus("ready");
  } catch (err) {
    showMessage(`Export failed: ${err.message}`, true);
    setStatus("ready");
  } finally {
    setButtons(false);
  }
}

async function importBundle() {
  const passphrase = $("passphrase").value;
  if (!passphrase) {
    showMessage("Enter the bundle's passphrase.", true);
    return;
  }
  setStatus("importing", true);
  setButtons(true);
  try {
    const r = await postCmd("import", { passphrase });
    if (!r.cancelled) {
      renderMigration(r);
    }
    setStatus("ready");
  } catch (err) {
    showMessage(`Import failed: ${err.message}. Check the passphrase and that you picked the bundle folder.`, true);
    setStatus("ready");
  } finally {
    setButtons(false);
  }
}

$("analyze").addEventListener("click", analyze);
$("migrate").addEventListener("click", migrate);
$("export").addEventListener("click", exportBundle);
$("import").addEventListener("click", importBundle);
init();

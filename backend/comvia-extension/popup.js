// popup — הגדרות (כתובת שרת) + רשימת כל אתרי הספקים עם מצב התחברות ופעולות.
// רשימת האתרים מגיעה מה-background (רישום SITES — מקור אמת יחיד), כך שהוספת אתר
// חדש בתוסף מציגה אותו כאן אוטומטית בלי לגעת ב-popup.

const $ = (id) => document.getElementById(id);

let SITE_LIST = []; // [{key, label, needsLogin}]

function originOf(url) {
  try { return new URL(url).origin + "/*"; } catch { return null; }
}

// ===== בניית רשימת האתרים =====
function buildSiteRows() {
  const list = $("sitesList");
  list.innerHTML = "";
  for (const site of SITE_LIST) {
    const row = document.createElement("div");
    row.className = "site";
    row.innerHTML = `
      <span class="dot" id="dot-${site.key}"></span>
      <div class="site-info">
        <div class="site-name">${site.label}</div>
        <div class="site-state" id="state-${site.key}">לא ידוע</div>
      </div>
      <div class="site-actions">
        <button data-check="${site.key}">בדוק</button>
        <button data-login="${site.key}">התחברות</button>
      </div>`;
    list.appendChild(row);
  }

  // בדיקה שקטה ברקע (הטאב נפתח ונסגר לבד).
  list.querySelectorAll("[data-check]").forEach((btn) =>
    btn.addEventListener("click", () =>
      chrome.runtime.sendMessage({ type: "checkLogin", site: btn.dataset.check })
    )
  );
  // פתיחת דף ההתחברות של האתר בטאב קדמי.
  list.querySelectorAll("[data-login]").forEach((btn) =>
    btn.addEventListener("click", () =>
      chrome.runtime.sendMessage({ type: "openLogin", site: btn.dataset.login })
    )
  );
}

// ===== רינדור סטטוסים =====
function renderServer(status) {
  const sd = $("serverDot"), st = $("serverText");
  if (status.server === "connected") { sd.className = "dot ok"; st.textContent = "שרת: מחובר"; }
  else if (status.server === "unauthorized") { sd.className = "dot bad"; st.textContent = "שרת: token שגוי"; }
  else if (status.server === "offline" || !status.server) { sd.className = "dot"; st.textContent = "שרת: לא מחובר"; }
  else { sd.className = "dot warn"; st.textContent = "שרת: " + status.server; }
}

function renderSites(status) {
  const sites = (status && status.sites) || {};
  for (const site of SITE_LIST) {
    const dot = $("dot-" + site.key), stateEl = $("state-" + site.key);
    if (!dot || !stateEl) continue;

    // תאימות לאחור: לפני העדכון הסטטוס נשמר במפתחות שטוחים (status.nc / status.farnell).
    const s = sites[site.key] || (status[site.key] ? { state: status[site.key] } : null);

    if (!s || !s.state || s.state === "unknown") {
      dot.className = "dot";
      stateEl.className = "site-state";
      stateEl.textContent = "לא ידוע — לחץ בדוק";
    } else if (s.state === "checking") {
      dot.className = "dot checking";
      stateEl.className = "site-state";
      stateEl.textContent = "בודק...";
    } else if (s.state === "logged_in") {
      dot.className = "dot ok";
      stateEl.className = "site-state";
      stateEl.innerHTML = s.user
        ? `מחובר · <span class="user"></span>`
        : "מחובר";
      if (s.user) stateEl.querySelector(".user").textContent = s.user;
    } else if (s.state === "not_logged_in") {
      dot.className = "dot bad";
      stateEl.className = "site-state bad";
      stateEl.textContent = "לא מחובר — נדרשת התחברות";
    } else if (s.state === "blocked") {
      dot.className = "dot warn";
      stateEl.className = "site-state bad";
      stateEl.textContent = "חסום (anti-bot) — פתח את האתר ידנית";
    } else {
      dot.className = "dot warn";
      stateEl.className = "site-state";
      stateEl.textContent = s.state;
    }
  }
  if (status && status.at)
    $("updated").textContent = "עודכן: " + new Date(status.at).toLocaleTimeString("he-IL");
}

function render(status) {
  status = status || {};
  renderServer(status);
  renderSites(status);
}

// ===== אתחול =====
async function load() {
  // רשימת האתרים מה-background; fallback סטטי אם ה-SW טרם התעורר.
  const resp = await new Promise((resolve) =>
    chrome.runtime.sendMessage({ type: "getSites" }, (r) => {
      void chrome.runtime.lastError;
      resolve(r);
    })
  );
  SITE_LIST = (resp && resp.sites) || [
    { key: "nc", label: "NetComponents" },
    { key: "farnell", label: "Farnell" },
    { key: "arrow", label: "Arrow" },
    { key: "master", label: "Master Electronics" },
  ];
  buildSiteRows();

  const c = await chrome.storage.local.get({ serverUrl: "", status: {} });
  $("serverUrl").value = c.serverUrl;
  render(c.status);
}

$("save").addEventListener("click", async () => {
  const serverUrl = $("serverUrl").value.trim().replace(/\/+$/, "");

  // בקשת הרשאת host לכתובת השרת (optional_host_permissions) כדי שה-fetch יעבוד.
  const origin = originOf(serverUrl);
  if (origin) {
    try { await chrome.permissions.request({ origins: [origin] }); } catch (_) {}
  }

  await chrome.storage.local.set({ serverUrl });
  chrome.runtime.sendMessage({ type: "kickPoll" });
  $("save").textContent = "נשמר ✓";
  setTimeout(() => ($("save").textContent = "שמור"), 1200);
});

$("checkAll").addEventListener("click", () =>
  chrome.runtime.sendMessage({ type: "checkAllLogins" })
);

// רענון חיווי בזמן אמת כל עוד ה-popup פתוח.
chrome.storage.onChanged.addListener((changes) => {
  if (changes.status) render(changes.status.newValue);
});

load();
// פתיחת ה-popup מעירה את ה-service worker ומבטיחה שהלולאה רצה.
chrome.runtime.sendMessage({ type: "kickPoll" }, () => void chrome.runtime.lastError);

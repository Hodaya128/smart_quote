// Comvia Farnell Price Agent — service worker.
//
// תפקיד: לרוץ בדפדפן המחובר של המשתמש, למשוך מהשרת בקשות מחיר (jobs) של Farnell,
// לשלוף את מחיר החוזה מהסשן האמיתי של המשתמש (פתיחת הדף בטאב רקע + קריאת ה-DOM),
// ולהחזיר את המחיר לשרת.
//
// מדוע טאב רקע ולא fetch ישיר? הדף של Farnell מרנדר את טבלת המחירים ב-JS, כך ש-fetch
// של ה-HTML לא יכיל מחירים. פתיחה בטאב מריצה את ה-JS של האתר בדיוק כמו גלישה רגילה,
// בתוך הסשן המחובר — ולכן עוברת את הגנת ה-anti-bot ומציגה את מחיר החוזה.

// ה-token מובנה בתוסף (המשתמש לא מנהל אותו). חייב להיות זהה ל-appsettings "Farnell:ExtensionToken".
const EXTENSION_TOKEN = "comvia-farnell-dev-token-change-me";
const DEFAULTS = { serverUrl: "", pollWait: 25 };
let polling = false;

async function getConfig() {
  const c = await chrome.storage.local.get(DEFAULTS);
  return { ...DEFAULTS, ...c, token: EXTENSION_TOKEN };
}

function serverHeaders(cfg) {
  const h = { "Content-Type": "application/json" };
  if (cfg.token) h["X-Farnell-Token"] = cfg.token;
  return h;
}

// ===== badge: חיווי "צריך התחברות מחדש ל-Farnell" =====
function setBadge(state) {
  // state: 'ok' | 'login' | 'off'
  if (state === "login") {
    chrome.action.setBadgeText({ text: "!" });
    chrome.action.setBadgeBackgroundColor({ color: "#d9534f" });
  } else if (state === "off") {
    chrome.action.setBadgeText({ text: "·" });
    chrome.action.setBadgeBackgroundColor({ color: "#999999" });
  } else {
    chrome.action.setBadgeText({ text: "" });
  }
}

async function recordStatus(patch) {
  const prev = await chrome.storage.local.get({ status: {} });
  await chrome.storage.local.set({ status: { ...prev.status, ...patch, at: Date.now() } });
}

// ===== סטטוס פר-אתר (status.sites[key] = {state, user, at}) =====
// state: 'logged_in' | 'not_logged_in' | 'checking' | 'blocked' | 'unknown'
// שומר גם מפתחות legacy (status.nc / status.farnell) לתאימות, ומעדכן את ה-badge:
// אם אתר כלשהו שדורש התחברות לא מחובר — "!" אדום על אייקון התוסף.
async function setSiteState(key, patch) {
  const prev = await chrome.storage.local.get({ status: {} });
  const sites = { ...(prev.status.sites || {}) };
  sites[key] = { ...(sites[key] || {}), ...patch, at: Date.now() };

  const legacy = {};
  if (key === "nc" || key === "farnell") legacy[key] = sites[key].state; // תאימות לאחור

  await chrome.storage.local.set({
    status: { ...prev.status, ...legacy, sites, at: Date.now() },
  });

  const anyLoggedOut = Object.keys(SITES).some(
    (k) => SITES[k].needsLogin && sites[k] && sites[k].state === "not_logged_in"
  );
  setBadge(anyLoggedOut ? "login" : "ok");
}

// ממפה שם ספק של job (מהשרת) למפתח אתר ברישום SITES.
function siteKeyForSupplier(supplier) {
  const s = (supplier || "").toLowerCase();
  if (s.includes("master")) return "master";
  if (s.includes("arrow")) return "arrow";
  if (s.includes("farnell")) return "farnell";
  return "nc";
}

// ===== הפונקציה שמוזרקת לדף Farnell (סינכרונית — בדיקה אחת של ה-DOM כרגע) =====
// רצה בקונטקסט הדף. לא ממתינה בעצמה — אם הטבלה עוד לא רונדרה מחזירה status:"pending",
// וה-service worker יקרא לה שוב (polling). כך נמנעים מתלות ב-await בתוך executeScript.
// מחזירה גם debug עשיר כדי לאבחן כשאין מחיר.
function extractFarnellOnce(qty) {
  function parsePrice(t) {
    if (!t) return null;
    const m = String(t).match(/\d{1,3}(?:[,\s]\d{3})*(?:\.\d+)?|\d+\.\d+|\d+/);
    if (!m) return null;
    const v = parseFloat(m[0].replace(/[,\s]/g, ""));
    return isNaN(v) ? null : v;
  }

  // מזהה את מטבע המחיר לפי סמל/קוד בטקסט (il.farnell.com לא בהכרח USD). סדר הבדיקות
  // חשוב: ₪/€/£ לפני $, כי "US$" מכיל $.
  function detectCurrency(t) {
    if (!t) return null;
    if (t.indexOf("₪") >= 0 || /ILS|NIS/i.test(t)) return "ILS";
    if (t.indexOf("€") >= 0 || /EUR/i.test(t)) return "EUR";
    if (t.indexOf("£") >= 0 || /GBP/i.test(t)) return "GBP";
    if (/US\$|\$|USD/i.test(t)) return "USD";
    return null;
  }

  const debug = {
    url: location.href,
    title: document.title,
    hasLoginForm: !!document.querySelector("input[name='logonId'], input[name='password'], input[data-testid*='logon-id']"),
    priceTables: document.querySelectorAll("table[class*='PriceTable']").length,
    anyTables: document.querySelectorAll("table").length,
  };

  if (location.href.includes("/auth/login") || debug.hasLoginForm)
    return { status: "not_logged_in", price: null, debug };

  // הסלקטור הרחב 'PriceTable' תופס גם את טבלת ה-Total Price (Packaging/Unit/Total).
  // לכן לא בוחרים טבלה אחת, אלא עוברים על כל הטבלאות התואמות ואוספים רק שורות שיש בהן
  // מחיר חוזה (span.PriceWrapper) או מחיר רשימה (td.strike-through). כך טבלת ה-Total
  // מסוננת אוטומטית (אין בה שורות כאלה), וגם מאוחדים שוברי כמות שמתפרסים על כמה טבלאות.
  const tiers = [];
  document.querySelectorAll("table[class*='PriceTable']").forEach((table) => {
    table.querySelectorAll("tbody tr").forEach((row) => {
      const cells = row.querySelectorAll("td");
      if (cells.length === 0) return; // שורת כותרת (th)

      let contract = null, contractText = "";
      const cw = row.querySelector("[class*='PriceWrapper']");
      if (cw) { contractText = cw.textContent || ""; contract = parsePrice(contractText); }

      let list = null, listText = "";
      const st = row.querySelector("td.strike-through");
      if (st) { listText = st.textContent || ""; list = parsePrice(listText); }

      // רק שורות מחיר אמיתיות — מסנן את טבלת ה-Total ושורות כותרת/סיכום.
      if (contract == null && list == null) return;

      const brk = parsePrice(cells[0].textContent) || 0; // "900+" => 900
      tiers.push({ brk, contract, list, contractText, listText });
    });
  });

  debug.rowCount = tiers.length;
  debug.firstRows = tiers.slice(0, 8);

  if (tiers.length === 0) {
    // אולי טבלאות שוברי הכמות עדיין נטענות (SPA) — נבקש מה-SW לנסות שוב.
    // מצרפים את ה-HTML של הטבלאות לאבחון אם זה נמשך.
    debug.tablesHtml = Array.from(document.querySelectorAll("table[class*='PriceTable']"))
      .map((t) => t.outerHTML.slice(0, 8000));
    return { status: "pending", price: null, debug };
  }

  // שובר הכמות הגבוה ביותר שעדיין <= qty (המחיר הטוב ביותר לכמות המבוקשת).
  tiers.sort((a, b) => a.brk - b.brk);
  let chosen = tiers[0];
  for (const t of tiers) if (t.brk <= qty) chosen = t;

  // עדיפות למחיר החוזה; אם אין — מחיר הרשימה.
  const price = chosen.contract ?? chosen.list;
  const priceText = chosen.contract != null ? chosen.contractText : chosen.listText;
  const currency = detectCurrency(priceText);
  debug.chosen = chosen;
  debug.priceText = priceText;   // הטקסט הגולמי של המחיר שנבחר — לאימות הסמל/המטבע
  debug.currency = currency;
  return { status: price != null ? "ok" : "no_price", price, currency, debug };
}

// ===== הפונקציה שמוזרקת לדף NetComponents (חיפוש) =====
// רצה בקונטקסט הדף. בודקת מצב נוכחי פעם אחת: אם הטבלה/מחירים עוד לא נטענו מחזירה
// status:"pending" וה-service worker יקרא שוב (polling). הסלקטורים תואמים בדיוק את אלה
// שהיו בקוד ה-Selenium (table.searchresultstable, tr[id^='trv_'], a.ncprc[data-pbrk] וכו').
function extractNetComponentsOnce() {
  function parsePbrk(el) {
    try {
      const raw = el.getAttribute("data-pbrk");
      if (!raw) return [];
      const data = JSON.parse(raw);
      const currency = data.currency || "";
      const out = [];
      (data.Prices || []).forEach((p) => {
        const price = typeof p.price === "string" ? parseFloat(p.price) : p.price;
        const minQty = p.minQty == null ? "" : String(p.minQty);
        if (price != null && !isNaN(price)) out.push({ price, minQty, currency });
      });
      return out;
    } catch (e) {
      return [];
    }
  }

  const debug = { url: location.href, title: document.title };

  // לא מחובר: כשמחוברים קישור ה-Login (a.login-link) נעלם; או שהופנינו לדף התחברות.
  const loginLink = document.querySelector("a.login-link");
  if (/\/account\/login|\/login/i.test(location.pathname))
    return { status: "not_logged_in", rows: [], debug };

  const table = document.querySelector("table.searchresultstable");
  const rowEls = document.querySelectorAll("table.searchresultstable tbody tr[id^='trv_']");
  debug.hasTable = !!table;
  debug.rowEls = rowEls.length;

  if (!table) {
    const noResults = document.querySelector(".no-results, .search-no-results, #noResultsMessage");
    if (noResults) return { status: "no_results", rows: [], debug };
    if (loginLink) return { status: "not_logged_in", rows: [], debug };
    return { status: "pending", rows: [], debug }; // עדיין נטען
  }

  // יש טבלה אך המחירים (a.ncprc) נטענים ב-AJAX אחריה — ניתן עוד זמן אם יש שורות בלי מחירים.
  const anyPrice = document.querySelectorAll("a.ncprc").length > 0;
  if (rowEls.length > 0 && !anyPrice) return { status: "pending", rows: [], debug };

  const rows = [];
  const rowsDebug = [];
  rowEls.forEach((row) => {
    try {
      const cols = row.querySelectorAll("td");
      if (cols.length < 16) return;

      const partNumber = (cols[0].textContent || "").trim();
      const manufacturer = (cols[3].textContent || "").trim();

      // אבחון: לכל שורה — האם יש אלמנט מחיר (a.ncprc) ומה ה-data-pbrk הגולמי שלו.
      // עוזר להבחין בין "ל-NC אין מחיר לשורה" לבין "המחיר קיים אך לא חולץ/מטבע חריג".
      try {
        let supDbg = "";
        try { const sd = cols[15].querySelector("a.supname"); if (sd) supDbg = (sd.textContent || "").trim(); } catch (e) {}
        const pe0 = cols[9].querySelector("a.ncprc");
        rowsDebug.push({ part: partNumber, sup: supDbg, hasPrice: !!pe0, pbrk: pe0 ? (pe0.getAttribute("data-pbrk") || "").slice(0, 300) : null });
      } catch (e) {}

      let description = "";
      try {
        const descDiv = cols[5].querySelector("div");
        description = ((descDiv && (descDiv.getAttribute("data-original-title") || descDiv.textContent)) || "").trim();
      } catch (e) {}

      const country = (cols[7].textContent || "").trim();
      const quantity = (cols[8].textContent || "").trim();

      let supplier = "";
      try {
        const s = cols[15].querySelector("a.supname");
        if (s) supplier = (s.textContent || "").trim();
      } catch (e) {}

      let supplierLink = "";
      try {
        const a = cols[1].querySelector("a.nctd");
        if (a) supplierLink = a.getAttribute("data-url") || "";
      } catch (e) {}

      let prices = [];
      try {
        const pe = cols[9].querySelector("a.ncprc");
        if (pe) prices = parsePbrk(pe);
      } catch (e) {}

      // ספק מורשה (Authorized): מסומן ב-NC באייקון i.ncauth ליד שם הספק; לא-מורשה — i.ncnoauth.
      // שמרני-בטוח: מסמנים לא-מורשה רק כשמסומן מפורשות (ncnoauth) — עמיד לשינויי DOM עתידיים.
      const hasAuth = !!row.querySelector("i.ncauth");
      const hasNoAuth = !!row.querySelector("i.ncnoauth");
      const authorized = hasAuth || !hasNoAuth;

      rows.push({ partNumber, manufacturer, description, country, quantity, supplier, supplierLink, prices, authorized });
    } catch (e) {}
  });

  debug.rowCount = rows.length;
  debug.rowsDebug = rowsDebug;
  if (rows.length === 0) return { status: "pending", rows: [], debug };
  return { status: "ok", rows, debug };
}

// ===== הפונקציה שמוזרקת לדף Arrow (חיפוש) =====
// Arrow הוא React SPA עם class-ים אקראיים (css-xxxx) שמשתנים בכל deploy, ומראה מספר הצעות
// לכל מחסן — DOM לא יציב לחילוץ סלקטורים. לכן במקום לפרסר כאן, מושכים את *הטקסט* של אזור
// התוצאות ומחזירים אותו לשרת (שורת __AI_RAW__), שם Claude מחלץ הצעה לכל מחסן. הטקסט כולל
// מק"ט, מחסנים, מלאי ומדרגות מחיר ("400+ $1.5 800+ $1.32") — כל מה שצריך לחילוץ.
function extractArrowOnce(qty) {
  const root = document.querySelector("#spa-root");
  const bodyText = document.body ? (document.body.innerText || document.body.textContent || "") : "";
  const debug = {
    url: location.href,
    title: document.title,
    spaChildren: root ? root.children.length : -1,
    blocked: /Access Denied|Reference #18|Pardon Our Interruption/i.test(bodyText.slice(0, 400)),
  };

  if (debug.blocked) return { status: "blocked", rows: [], debug };
  // ה-SPA עדיין ריק — לתת לו עוד זמן להתרנדר.
  if (!root || root.children.length === 0) return { status: "pending", rows: [], debug };

  // ממתינים שטבלת התוצאות תתרנדר (Pricing/Stock מופיעים רק אחרי טעינת המחירים ב-AJAX).
  const table = document.querySelector("table.MuiTable-root, table");
  const priced = /\$\s*\d/.test(bodyText);
  if (!table || !priced) {
    // אולי "No results" — אם ה-SPA מלא אבל אין טבלה/מחיר, מחזירים ok ריק (נשארים עם NC).
    if (/no results|0 of 0|did not match/i.test(bodyText)) return { status: "ok", rows: [], debug };
    return { status: "pending", rows: [], debug };
  }

  // טקסט אזור התוצאות: מעדיפים את הטבלה (מק"ט+מחסנים+מדרגות), נופלים ל-main/root.
  const src = table || document.querySelector("#main-content, main") || root;
  let text = (src.innerText || src.textContent || "").replace(/[ \t]+/g, " ").replace(/\n{3,}/g, "\n\n").trim();
  if (text.length > 14000) text = text.slice(0, 14000);
  debug.textLen = text.length;

  // שורת סנטינל: השרת יזהה __AI_RAW__ וישלח את description ל-Claude לחילוץ הצעות.
  return {
    status: "ok",
    rows: [{
      partNumber: "",
      manufacturer: "",
      description: text,
      country: "",
      quantity: "",
      supplier: "__AI_RAW__",
      supplierLink: location.href,
      prices: [],
    }],
    debug,
  };
}

// ===== הפונקציה שמוזרקת לדף Master Electronics (חיפוש) =====
// חיפוש מק"ט מדויק ב-keywordsearch מפנה לעמוד המוצר (nopCommerce, מרונדר בשרת — לא SPA).
// מחירי החוזה בטבלת .price-breakdown: כל שורה = span.hdbreak (כמות) + input.hdprice (מחיר, "$1.83").
// המחיר הזה הוא מחיר החשבון של Comvia — מוצג רק כשהמשתמש מחובר.
function extractMasterElectronicsOnce(qty) {
  const bodyText = (document.body ? (document.body.innerText || document.body.textContent || "") : "").slice(0, 400);
  const debug = { url: location.href, title: document.title };

  if (/Access Denied|Pardon Our Interruption|Reference #\d/i.test(bodyText)) {
    return { status: "blocked", rows: [], debug };
  }

  function parsePrice(t) {
    if (t == null) return null;
    const v = parseFloat(String(t).replace(/[^0-9.]/g, ""));
    return isNaN(v) ? null : v;
  }
  function parseQty(t) {
    if (t == null) return null;
    const v = parseInt(String(t).replace(/[^0-9]/g, ""), 10); // "10,000 +" -> 10000
    return isNaN(v) ? null : v;
  }

  // טבלת מחירי החוזה: יש שני .price-breakdown — כותרת (עם h4 "Price (USD)") וטבלת השורות.
  // בוחרים את זה שמכיל את שורות המחיר (span.hdbreak / input.hdprice).
  const breakdown = Array.from(document.querySelectorAll("div.price-breakdown"))
    .find((el) => el.querySelector(".hdbreak, input.hdprice"));

  const isProductPage = !!document.querySelector(".ProductPage, .product-details, #product-details");
  // חיפוש שלא נחת על מוצר יחיד (אין התאמה מדויקת) — עמוד רשימת תוצאות. מזהים כדי לא
  // להיתקע ב-polling עד timeout: אין מחיר חוזה ברשימה, מחזירים ok ריק ונשארים עם NC.
  const isResultsList = !!document.querySelector(".product-grid, .item-grid, .product-item, .item-box, .search-results");

  if (!breakdown) {
    // עמוד מוצר טעון בלי טבלת מחיר (מלאי 0 / מחיר לפי בקשה) או רשימת תוצאות — אין מחיר חוזה.
    if (isProductPage || isResultsList) return { status: "ok", rows: [], debug };
    // אחרת ייתכן שהדף עדיין נטען / הפניה בעיצומה — מבקשים ניסיון נוסף.
    return { status: "pending", rows: [], debug };
  }

  const prices = [];
  breakdown.querySelectorAll(":scope > div.row").forEach((row) => {
    if (row.classList.contains("heading-row")) return; // שורת כותרת Qty / Unit Price
    const qtyEl = row.querySelector(".hdbreak");
    const priceInput = row.querySelector("input.hdprice");
    const priceRight = row.querySelector(".col-6.text-right div");
    const priceText = priceInput ? priceInput.value : (priceRight ? priceRight.textContent : "");
    const q = parseQty(qtyEl ? qtyEl.textContent : "");
    const p = parsePrice(priceText);
    if (q != null && p != null) prices.push({ price: p, minQty: String(q), currency: "USD" });
  });

  debug.tierCount = prices.length;
  if (prices.length === 0) return { status: "ok", rows: [], debug }; // כלום לפרסר — נשארים עם NC

  const h1 = document.querySelector("h1");
  const brand = document.querySelector(".product-brand-New, a.hrefMunfImage img");
  const stockEl = document.querySelector(".stock-qty");
  const descEl = document.querySelector(".short-description");

  const rows = [{
    partNumber: h1 ? (h1.textContent || "").trim() : "",
    manufacturer: brand ? (brand.textContent || brand.getAttribute("alt") || "").trim() : "",
    description: descEl ? (descEl.textContent || "").trim() : "",
    country: "",
    quantity: stockEl ? (stockEl.textContent || "").replace(/[^0-9]/g, "") : "",
    supplier: "Master Electronics",
    supplierLink: location.href,
    prices: prices,
  }];

  return { status: "ok", rows, debug };
}

// ===== עיבוד job בודד =====
function waitForTabLoad(tabId, timeoutMs) {
  return new Promise((resolve) => {
    let done = false;
    const finish = () => {
      if (done) return;
      done = true;
      chrome.tabs.onUpdated.removeListener(listener);
      resolve();
    };
    const listener = (id, info) => {
      if (id === tabId && info.status === "complete") finish();
    };
    chrome.tabs.onUpdated.addListener(listener);
    setTimeout(finish, timeoutMs);
  });
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// מנקה את כל ה-cookies של דומיין (כולל httpOnly כמו _abck/bm_sz של Akamai). דרוש כי
// אחרי גישה אוטומטית Akamai מסמן את _abck כ"bot" ואז כל reload נחסם — בדיוק כמו שחלונית
// אורח (cookies נקיים) כן עוברת. ניקוי + reload נותן challenge חדש שעובר.
async function clearCookiesFor(domain) {
  if (!chrome.cookies) return 0;
  let removed = 0;
  try {
    const cookies = await chrome.cookies.getAll({ domain });
    for (const c of cookies) {
      const host = c.domain.charAt(0) === "." ? c.domain.slice(1) : c.domain;
      const url = (c.secure ? "https://" : "http://") + host + c.path;
      try { await chrome.cookies.remove({ url, name: c.name }); removed++; } catch (_) {}
    }
  } catch (_) {}
  return removed;
}

async function processJob(job, cfg) {
  let tab;
  let out = { status: "error", price: null };
  let leaveOpen = false;
  try {
    console.log("[Farnell] job received:", job.id, job.url, "qty", job.qty);
    tab = await chrome.tabs.create({ url: job.url, active: false });
    console.log("[Farnell] tab created:", tab.id);
    await waitForTabLoad(tab.id, 30000);
    console.log("[Farnell] tab finished loading, polling for price table...");

    // polling מצד ה-SW: קוראים את ה-DOM כל 500ms עד שיש טבלה/מחיר או עד ~25 ניסיונות.
    for (let i = 0; i < 50; i++) {
      const results = await chrome.scripting.executeScript({
        target: { tabId: tab.id },
        func: extractFarnellOnce,
        args: [job.qty],
      });
      out = (results && results[0] && results[0].result) || { status: "error", price: null };
      if (out.status !== "pending") break;
      await sleep(500);
    }

    console.log("[Farnell] extraction result:", JSON.stringify({ ...out, debug: { ...out.debug, tablesHtml: undefined } }, null, 2));

    // הדפסת ה-HTML של הטבלאות בנפרד (קל להעתקה) לצורך דיוק הסלקטורים.
    if (out.debug && out.debug.tablesHtml) {
      out.debug.tablesHtml.forEach((h, i) =>
        console.log(`[Farnell] === PriceTable[${i}] HTML ===\n` + h)
      );
    }

    if (out.status === "not_logged_in") {
      await setSiteState("farnell", { state: "not_logged_in" });
    } else if (out.status === "ok") {
      await setSiteState("farnell", { state: "logged_in" });
    }

    // כישלון? משאירים את הטאב פתוח כדי שאפשר יהיה לבדוק ידנית מה נטען בו.
    leaveOpen = out.status !== "ok";
    if (leaveOpen)
      console.warn("[Farnell] לא נמצא מחיר — הטאב נשאר פתוח לבדיקה. status:", out.status, "debug:", out.debug);

    await postResult(cfg, job.id, out.price, out.status, out.currency);
  } catch (e) {
    console.error("[Farnell] processJob error:", e);
    await postResult(cfg, job.id, null, "error:" + (e && e.message), null);
  } finally {
    if (tab && tab.id && !leaveOpen) {
      try { await chrome.tabs.remove(tab.id); } catch (_) {}
    }
  }
}

async function postResult(cfg, id, price, status, currency) {
  try {
    await fetch(cfg.serverUrl.replace(/\/+$/, "") + "/api/farnell/jobs/result", {
      method: "POST",
      headers: serverHeaders(cfg),
      body: JSON.stringify({ id, price, status, currency }),
    });
  } catch (e) {
    // השרת לא זמין — ה-job יפוג ב-timeout בצד השרת. לא קריטי.
  }
}

// ===== לולאת ה-polling (long-poll) =====
async function pollOnce(cfg) {
  const url = cfg.serverUrl.replace(/\/+$/, "") + "/api/farnell/jobs?wait=" + (cfg.pollWait || 25);
  const resp = await fetch(url, { headers: serverHeaders(cfg) });
  if (resp.status === 401) {
    await recordStatus({ server: "unauthorized" });
    return [];
  }
  if (!resp.ok) {
    await recordStatus({ server: "error_" + resp.status });
    return [];
  }
  await recordStatus({ server: "connected" });
  return await resp.json();
}

async function pollLoop() {
  if (polling) return;
  polling = true;
  try {
    // לולאה מתמשכת: ה-long-poll (fetch שנמשך עד ~25ש') מחזיק את ה-service worker חי,
    // ומיד עם סיומו מתחיל סבב חדש — כך ה-SW נשאר ער ותופס jobs כמעט מיידית.
    // אם הלולאה בכל זאת נהרגת (למשל Chrome סוגר את ה-SW), ה-alarm כל 30ש' מפעיל אותה מחדש.
    while (true) {
      const cfg = await getConfig();
      if (!cfg.serverUrl) { setBadge("off"); break; }
      let jobs = [];
      try {
        jobs = await pollOnce(cfg);
      } catch (e) {
        setBadge("off");
        await recordStatus({ server: "offline" });
        await new Promise((r) => setTimeout(r, 3000));
        continue;
      }
      for (const job of jobs) await processJob(job, cfg);
    }
  } finally {
    polling = false;
  }
}

// ===== לולאת ה-polling של *חיפושים* (NetComponents, ובהמשך ספקים נוספים) =====
// לולאה שנייה, נפרדת מ-Farnell, שמושכת jobs מ-/api/extension/search/jobs ומחזירה שורות טבלה.
// בוחרים extractor לפי שם הספק ב-job, כדי שהרחבה לאתר נוסף = הוספת extractor + ענף כאן.
let searchPolling = false;

function pickExtractor(supplier) {
  const s = (supplier || "").toLowerCase();
  if (s.includes("master")) return extractMasterElectronicsOnce;
  if (s.includes("arrow")) return extractArrowOnce;
  if (s.includes("netcomponents")) return extractNetComponentsOnce;
  return extractNetComponentsOnce; // ברירת מחדל
}

async function processSearchJob(job, cfg) {
  let tab;
  let out = { status: "error", rows: [] };
  let leaveOpen = false;
  try {
    console.log("[Search] job received:", job.id, job.supplier, job.sku, job.url);
    tab = await chrome.tabs.create({ url: job.url, active: false });
    await waitForTabLoad(tab.id, 30000);
    console.log("[Search] tab loaded, polling for results table...");

    const extractor = pickExtractor(job.supplier);
    // polling מצד ה-SW: קוראים את ה-DOM כל 500ms עד סטטוס סופי או ~50 ניסיונות (25ש').
    // אם Akamai חוסם (status "blocked") — הטעינה הראשונה מקבלת challenge אבל מציבה את
    // cookies ה-bot-manager (_abck/bm_sz); reload בד"כ עובר. מנסים עד 2 reloads.
    let blockReloads = 0;
    while (true) {
      for (let i = 0; i < 50; i++) {
        const results = await chrome.scripting.executeScript({
          target: { tabId: tab.id },
          func: extractor,
        });
        out = (results && results[0] && results[0].result) || { status: "error", rows: [] };
        if (out.status !== "pending") break;
        await sleep(500);
      }

      if (out.status === "blocked" && blockReloads < 2) {
        blockReloads++;
        // מנקים את cookies ה-bot-manager המורעלים ואז reload — challenge חדש כמו חלונית אורח.
        let host = "arrow.com";
        try { host = new URL(job.url).hostname.replace(/^www\./, ""); } catch (_) {}
        const removed = await clearCookiesFor(host);
        console.warn("[Search] Akamai block — cleared " + removed + " cookies for " + host + ", reloading (attempt " + blockReloads + ")");
        await chrome.tabs.reload(tab.id);
        await waitForTabLoad(tab.id, 30000);
        await sleep(2500); // זמן ל-sensor JS להריץ את ה-challenge ולהציב cookies תקינים
        continue;
      }
      break;
    }

    console.log("[Search] result:", JSON.stringify({ status: out.status, rows: (out.rows || []).length, blockReloads, debug: out.debug }, null, 2));

    // עדכון סטטוס לפי הספק של ה-job (בעבר נכתב תמיד תחת nc — גם עבור Arrow/Master).
    const jobSiteKey = siteKeyForSupplier(job.supplier);
    if (out.status === "not_logged_in") {
      await setSiteState(jobSiteKey, { state: "not_logged_in" });
    } else if (out.status === "blocked") {
      await setSiteState(jobSiteKey, { state: "blocked" });
    } else if (out.status === "ok") {
      await setSiteState(jobSiteKey, { state: "logged_in" });
    }

    // משאירים את הטאב פתוח כשצריך התחברות או כשנחסם (Akamai) — כדי שאפשר יהיה לבדוק ידנית.
    leaveOpen = out.status === "not_logged_in" || out.status === "blocked";
    await postSearchResult(cfg, job.id, out.rows || [], out.status);
  } catch (e) {
    console.error("[Search] processSearchJob error:", e);
    await postSearchResult(cfg, job.id, [], "error:" + (e && e.message));
  } finally {
    if (tab && tab.id && !leaveOpen) {
      try { await chrome.tabs.remove(tab.id); } catch (_) {}
    }
  }
}

async function postSearchResult(cfg, id, rows, status) {
  try {
    await fetch(cfg.serverUrl.replace(/\/+$/, "") + "/api/extension/search/jobs/result", {
      method: "POST",
      headers: serverHeaders(cfg),
      body: JSON.stringify({ id, rows, status }),
    });
  } catch (e) {
    // השרת לא זמין — ה-job יפוג ב-timeout בצד השרת. לא קריטי.
  }
}

async function pollSearchOnce(cfg) {
  const url = cfg.serverUrl.replace(/\/+$/, "") + "/api/extension/search/jobs?wait=" + (cfg.pollWait || 25);
  const resp = await fetch(url, { headers: serverHeaders(cfg) });
  if (resp.status === 401) { await recordStatus({ server: "unauthorized" }); return []; }
  if (!resp.ok) { await recordStatus({ server: "error_" + resp.status }); return []; }
  await recordStatus({ server: "connected" });
  return await resp.json();
}

async function searchPollLoop() {
  if (searchPolling) return;
  searchPolling = true;
  try {
    while (true) {
      const cfg = await getConfig();
      if (!cfg.serverUrl) break;
      let jobs = [];
      try {
        jobs = await pollSearchOnce(cfg);
      } catch (e) {
        await new Promise((r) => setTimeout(r, 3000));
        continue;
      }
      for (const job of jobs) await processSearchJob(job, cfg);
    }
  } finally {
    searchPolling = false;
  }
}

// ===== הפעלה והשארה-בחיים =====
function startLoops() {
  pollLoop();        // Farnell — מחיר בודד
  searchPollLoop();  // NetComponents (ועוד) — תוצאות חיפוש
}

chrome.runtime.onStartup.addListener(() => startLoops());
chrome.runtime.onInstalled.addListener(() => {
  chrome.alarms.create("keepalive", { periodInMinutes: 0.5 });
  startLoops();
});
chrome.alarms.onAlarm.addListener((a) => {
  if (a.name === "keepalive") startLoops();
});
// ===== רישום האתרים שהתוסף שולף מהם + בדיקת התחברות (נקרא מה-popup) =====
// פונקציות זיהוי שמוזרקות לדף הספק. self-contained — קוראות רק את ה-DOM.
// מחזירות { state, user }: state = logged_in | not_logged_in | pending (SPA עוד נטען) | blocked.

function detectNcLogin() {
  // ב-NetComponents קישור ה-Login (a.login-link) נעלם כשמחוברים.
  return { state: document.querySelector("a.login-link") ? "not_logged_in" : "logged_in", user: "" };
}

function detectFarnellLogin() {
  const hasLoginForm = !!document.querySelector(
    "input[name='logonId'], input[name='password'], input[data-testid*='logon-id']"
  );
  if (location.href.includes("/auth/login") || hasLoginForm) return { state: "not_logged_in", user: "" };
  return { state: "logged_in", user: "" };
}

function detectMasterLogin() {
  // מחובר: בכותרת יש קישור Logout + האימייל של המשתמש (.dd-selection .ellipsis).
  // לא מחובר: /en/customer/account מפנה לדף התחברות (שדה סיסמה).
  const emailEl = document.querySelector(".dd-selection .ellipsis");
  const email = emailEl ? (emailEl.getAttribute("title") || emailEl.textContent || "").trim() : "";
  if (document.querySelector("a[href*='/logout']") || email.includes("@"))
    return { state: "logged_in", user: email.toLowerCase() };
  return { state: "not_logged_in", user: "" };
}

function detectArrowLogin() {
  // Arrow הוא React SPA — אם ה-root עוד ריק מחזירים pending וה-SW ינסה שוב.
  const text = document.body ? (document.body.innerText || document.body.textContent || "") : "";
  if (/Access Denied|Pardon Our Interruption|Reference #\d/i.test(text.slice(0, 400)))
    return { state: "blocked", user: "" };
  const root = document.querySelector("#spa-root");
  if (!root || root.children.length === 0) return { state: "pending", user: "" };
  // מחובר: הכותרת מציגה "Hello Avi Lipnik (#2189356)".
  const m = text.match(/Hello\s+(.+?)\s*\(#\d+\)/);
  if (m) return { state: "logged_in", user: m[1].trim() };
  // הכותרת מתרנדרת מאוחר — אם עדיין אין אזכור חשבון/כניסה, ננסה שוב.
  if (!/Sign In|Log ?in|My Account|Hello/i.test(text)) return { state: "pending", user: "" };
  return { state: "not_logged_in", user: "" };
}

// כל האתרים שהתוסף שולף מהם. checkUrl — דף לבדיקה שקטה ברקע; loginUrl — דף שנפתח
// למשתמש כדי להתחבר. spa=true => הזיהוי דורש polling עד שהדף מתרנדר.
const SITES = {
  nc:      { label: "NetComponents",      needsLogin: true,
             checkUrl: "https://www.netcomponents.com",
             loginUrl: "https://www.netcomponents.com",
             detect: detectNcLogin },
  farnell: { label: "Farnell",            needsLogin: true,
             checkUrl: "https://il.farnell.com/auth/login",
             loginUrl: "https://il.farnell.com/auth/login",
             detect: detectFarnellLogin },
  arrow:   { label: "Arrow",              needsLogin: true, spa: true,
             checkUrl: "https://www.arrow.com/",
             loginUrl: "https://www.arrow.com/",
             detect: detectArrowLogin },
  master:  { label: "Master Electronics", needsLogin: true,
             checkUrl: "https://www.masterelectronics.com/en/customer/account",
             loginUrl: "https://www.masterelectronics.com/en/customer/account",
             detect: detectMasterLogin },
};

// בדיקה שקטה: פותח את checkUrl בטאב רקע, מזריק את פונקציית הזיהוי (עם polling ל-SPA),
// מעדכן את status.sites[key] וסוגר את הטאב. לא מפריע לעבודה של המשתמש.
async function checkSiteLogin(key) {
  const site = SITES[key];
  if (!site) return;
  await setSiteState(key, { state: "checking" });
  let tab;
  try {
    tab = await chrome.tabs.create({ url: site.checkUrl, active: false });
    await waitForTabLoad(tab.id, 30000);
    let out = { state: "pending", user: "" };
    const tries = site.spa ? 16 : 4; // SPA (Arrow) מתרנדר לאט — עד ~8 שניות
    for (let i = 0; i < tries; i++) {
      const results = await chrome.scripting.executeScript({ target: { tabId: tab.id }, func: site.detect });
      out = (results && results[0] && results[0].result) || { state: "unknown", user: "" };
      if (out.state !== "pending") break;
      await sleep(500);
    }
    if (out.state === "pending") out = { state: "unknown", user: "" };
    await setSiteState(key, { state: out.state, user: out.user || "" });
    console.log("[CheckLogin]", key, "=>", out.state, out.user || "");
  } catch (e) {
    console.error("[CheckLogin]", key, "error:", e);
    await setSiteState(key, { state: "unknown" });
  } finally {
    if (tab && tab.id) { try { await chrome.tabs.remove(tab.id); } catch (_) {} }
  }
}

// פותח את דף ההתחברות של האתר בטאב קדמי (המשתמש מתחבר ידנית; הסיסמאות לא בתוסף).
async function openSiteLogin(key) {
  const site = SITES[key];
  if (!site) return;
  await chrome.tabs.create({ url: site.loginUrl, active: true });
}

// טריגרים מה-popup.
chrome.runtime.onMessage.addListener((msg, _sender, sendResponse) => {
  if (msg && msg.type === "kickPoll") {
    startLoops();
    sendResponse({ ok: true });
  } else if (msg && msg.type === "checkLogin") {
    checkSiteLogin(msg.site);
    sendResponse({ ok: true });
  } else if (msg && msg.type === "checkAllLogins") {
    (async () => { for (const k of Object.keys(SITES)) await checkSiteLogin(k); })();
    sendResponse({ ok: true });
  } else if (msg && msg.type === "openLogin") {
    openSiteLogin(msg.site);
    sendResponse({ ok: true });
  } else if (msg && msg.type === "getSites") {
    // ה-popup מציג את הרשימה מהרישום — מקור אמת יחיד.
    sendResponse({
      sites: Object.keys(SITES).map((k) => ({ key: k, label: SITES[k].label, needsLogin: SITES[k].needsLogin })),
    });
  }
  return true;
});

// ודא ש-alarm קיים גם בטעינה רגילה של ה-SW.
chrome.alarms.create("keepalive", { periodInMinutes: 0.5 });
startLoops();

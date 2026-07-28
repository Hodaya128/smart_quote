# Comvia Price Agent (תוסף Chrome)

תוסף שרץ בדפדפן המחובר של המתמחר ושולף מחירים ותוצאות חיפוש מאתרי הספקים
דרך הסשן האמיתי של המשתמש: **NetComponents, Farnell, Arrow, Master Electronics**
(הרשימה מתרחבת — ראה "הוספת ספק" למטה). התוצאות מוחזרות לשרת Comvia.

מדוע תוסף ולא Selenium מהשרת? האתרים חוסמים אוטומציה מ-IP של שרת (Akamai 403),
ומחירי חוזה מוצגים רק כשמחוברים לחשבון. התוסף עוקף את שתי הבעיות: הוא משתמש
בדפדפן ובסשן האמיתיים של המשתמש (IP ביתי/משרדי), בלי שום אוטומציית התחברות.

## איך זה עובד (זרימה)

```
smart_quote_client → שרת (NetComponents + DigiKey כרגיל)
                        │  פריט Farnell? → מכניס job לתור וממתין (עד 15ש')
                        ▼
            [ תור בשרת ] ◄── long-poll ── התוסף (דפדפן המשתמש, מחובר ל-Farnell)
                        ▲                     │ פותח את דף המוצר בטאב רקע,
                        └── POST result ──────┘ קורא את מחיר החוזה, סוגר טאב
```

אם התוסף לא זמין / המשתמש לא מחובר / timeout — השרת נשאר עם מחיר ה-NetComponents
(בדיוק כמו ההתנהגות לפני התוסף). כלומר התוסף רק *משפר*, לא שובר כלום.

## התקנה (טעינה ידנית — load unpacked)

1. ב-Chrome: `chrome://extensions`
2. הפעל **Developer mode** (פינה ימנית עליונה)
3. **Load unpacked** → בחר את התיקייה `comvia-extension`
4. לחץ על אייקון התוסף → מלא:
   - **כתובת השרת** — למשל `https://your-comvia-server` (אותו origin של ה-API)
   - **Token** — הערך מ-`appsettings.json` תחת `Farnell:ExtensionToken`
   - **שמור** (Chrome יבקש הרשאת גישה לכתובת השרת — אשר)

## שגרת בוקר (פעם ביום)

1. לחץ על התוסף → **פתח Farnell להתחברות** → התחבר לחשבון COMVIA (סיסמה שמורה).
2. זהו. כל היום התוסף שולף מחירים אוטומטית מהסשן הזה.
3. אם הסשן פג — באייקון התוסף יופיע **!** אדום, והחיווי יראה "Farnell: לא מחובר".
   התחבר שוב והכל ממשיך.

## חיווי מצב (בלחיצה על האייקון)

ה-popup מציג את **כל אתרי הספקים** שהתוסף שולף מהם (NetComponents, Farnell, Arrow,
Master Electronics — מתוך רישום `SITES` ב-background.js), ולכל אתר:

| רכיב | משמעות |
|---|---|
| 🟢 מחובר (+שם/אימייל) | הסשן פעיל; מוצג המשתמש המחובר כשהאתר חושף אותו |
| 🔴 לא מחובר | נדרשת התחברות — לחץ "התחברות" |
| 🟠 חסום (anti-bot) | ‏Akamai חסם; פתח את האתר ידנית פעם אחת |
| ⚪ לא ידוע | טרם נבדק — לחץ "בדוק" |
| כפתור **בדוק** | בדיקה שקטה ברקע (טאב נפתח ונסגר לבד) ומעדכן את החיווי |
| כפתור **התחברות** | פותח את דף ההתחברות של האתר בטאב — מתחברים ידנית |
| **בדוק את כולם** | מריץ בדיקה שקטה לכל האתרים ברצף |

הסטטוסים מתעדכנים גם אוטומטית מכל חיפוש שרץ (job), ואם אתר כלשהו לא מחובר —
מופיע **!** אדום על אייקון התוסף. שרת: 🟢 מחובר / 🔴 token שגוי / ⚪ לא מחובר.

## בדיקה מהירה (בלי הפרונט)

לאחר שהשרת רץ והתוסף מוגדר, אפשר להזריק job ידנית מ-Swagger/REST:
מבצעים חיפוש רגיל (`POST /api/PriceComparison/search`) עם SKU שיש לו ספק Farnell ב-NetComponents.
בלוג השרת יופיע `Farnell job ... enqueued`, ומיד אחריו `Farnell result ... price=...`.

## הגדרות שרת רלוונטיות (`appsettings.json`)

```json
"Farnell": {
  "ExtensionToken": "comvia-farnell-dev-token-change-me",
  "TimeoutSeconds": 15
}
```

- **ExtensionToken** — חובה להחליף בפרודקשן. אם ריק → ה-API פתוח (לפיתוח בלבד).
- **TimeoutSeconds** — כמה זמן השרת ממתין לתוסף לפני שנשאר עם מחיר ה-NC.

## הוספת ספק (הדפוס)

לכל אתר עם מחיר-חוזה שנשלף בסשן המחובר: (1) בונה URL בשרת (`Model/Services/*Url.cs`),
(2) `SearchXViaExtensionAsync` שמזריק job ל-`BrowserSearchQueue` ומצטרף ל-`DoSearchAsync`,
(3) `extractXOnce(qty)` ב-`background.js` שקורא את ה-DOM, (4) ענף ב-`pickExtractor`,
(5) host ב-`manifest.json`. דוגמה מלאה: **Master Electronics** (keywordsearch → עמוד מוצר,
קריאת `.price-breakdown` → `.hdbreak` + `input.hdprice`; מסומן IsCustomPrice).

## הערות / מגבלות (pilot)

- התור הוא in-memory. למשתמשים מעטים זה מספיק; לריבוי שרתים נצטרך persistence.
- הסלקטורים לקריאת המחיר (`table[class*='PriceTable']`, `PriceWrapper`, `strike-through`)
  נלקחו מה-`FarnellScraper` הקיים. אם Farnell ישנו את ה-DOM — לעדכן ב-`background.js`
  בפונקציה `extractFarnellPrice`. בהמשך אפשר להחליף לקריאת ה-API הפנימי או ל-AI-vision.

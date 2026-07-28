# Selenium Scraping — קוד שהוצא מהפלואו (שמור לשחזור)

הקוד כאן **אינו מקומפל** ואינו רץ. הוא הוצא מהפלואו כי סקרייפינג ה-Selenium
לא עבד על השרת (Chrome headless חסום ע"י anti-bot כמו Akamai). במקומו רץ תוסף
ה-Chrome בסשן המחובר של המשתמש — ראה `ExtensionSearchController` / `FarnellController`
ו-`BrowserSearchQueue` / `FarnellJobQueue`.

ההחרגה מה-build מוגדרת ב-`comviaServer.csproj`:
```xml
<Compile Remove="legacy\**\*.cs" />
```

## מה יש כאן

| קובץ | תיאור |
|---|---|
| `ISupplierLoginScraper.cs` | ממשק ה-scraper (EnsureLoggedIn / GetPrice) |
| `MasterElectronicsScraper.cs` | מימוש scraper ל-Master Electronics (היה רשום ב-DI) |
| `FarnellScraper.cs` | scraper ל-Farnell (לא היה רשום ב-DI — Farnell עבר לתוסף) |
| `PriceComparisonService.pre-removal-backup.cs` | עותק מלא ומדויק של `PriceComparisonService` **לפני** ההסרה — המקור לכל מתודות ה-Selenium שהוסרו |

מה שהוסר מ-`Model/Services/PriceComparisonService.cs`: השדות `_driver`/`_scrapers`/
`_seleniumLock`/`_headless`, פרמטר הבנאי `IEnumerable<ISupplierLoginScraper>`, המתודות
`Normalize`/`FindScraper`/`LoadCredentials`/`LoadNetComponentsCredentials`/
`EnrichWithCustomPrices`/`EnsureDriver`/`CreateChromeDriver`, כל בלוק ה-NC-via-Selenium
(LEGACY), והשורה `EnrichWithCustomPrices(...)` בזרימת `DoSearchAsync`.

## איך להחזיר את זה

1. **חבילות** — להחזיר ל-`comviaServer.csproj`:
   ```xml
   <PackageReference Include="Selenium.Support" Version="4.16.2" />
   <PackageReference Include="Selenium.WebDriver" Version="4.16.2" />
   ```
2. **סקרייפרים** — להעביר את שלושת קבצי ה-`*Scraper*.cs` בחזרה ל-`Model/Scrapers/`
   (namespace `comviaServer.Model`), ולהסיר את החרגת ה-`Compile Remove` אם התיקייה מתרוקנת.
3. **PriceComparisonService** — לשחזר את מתודות/שדות ה-Selenium מתוך
   `PriceComparisonService.pre-removal-backup.cs` (diff מול הקובץ החי), כולל פרמטר הבנאי
   ואת הקריאה `EnrichWithCustomPrices(suppliers, item.Qty);` בזרימה.
4. **DI** — להחזיר ל-`Program.cs`:
   ```csharp
   builder.Services.AddSingleton<ISupplierLoginScraper, MasterElectronicsScraper>();
   ```
5. `dotnet build` ולוודא שהשרת עולה.

> שים לב: גם אם תשוחזר התשתית, סקרייפינג Selenium מהשרת עלול עדיין להיחסם ע"י anti-bot.
> נתיב התוסף הוא הפתרון הפעיל.

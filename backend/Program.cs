using comviaServer.DAL;
using comviaServer.Model;
using comviaServer.Security;
using System.Text;

// רשת ביטחון ברמת התהליך: ה-try/catch למטה תופס רק חריגות ב-thread הראשי (בעליית
// האפליקציה). חריגה שלא טופלה ב-thread רקע (למשל Task.Run של search-background, או
// finalizer של Selenium) *לא* נתפסת שם — היא מפילה את כל התהליך עם קוד 0xe0434352
// בלי שום לוג. ההאזנות הבאות מתעדות גם מקרים כאלה לקובץ crash.log שליד ה-exe.
static void WriteCrashLog(string source, Exception? ex)
{
    try
    {
        var sb = new StringBuilder();
        sb.AppendLine("===== UNHANDLED (" + source + ") =====");
        sb.AppendLine("Time: " + DateTime.Now.ToString("o"));
        for (var e = ex; e != null; e = e.InnerException)
        {
            sb.AppendLine("---");
            sb.AppendLine(e.GetType().FullName + ": " + e.Message);
            sb.AppendLine(e.StackTrace);
        }
        var text = sb.ToString();
        Console.WriteLine(text);
        var path = Path.Combine(AppContext.BaseDirectory, "crash.log");
        File.AppendAllText(path, text);
    }
    catch { /* אם אי אפשר לכתוב — לפחות ניסינו */ }
}

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    WriteCrashLog("AppDomain", e.ExceptionObject as Exception);

// משימות Task שנכשלו ואיש לא עשה להן await — מתועדות, ו-SetObserved מונע הפלה של התהליך.
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    WriteCrashLog("UnobservedTask", e.Exception);
    e.SetObserved();
};

// כל הקוד עטוף ב-try/catch כדי לתפוס גם חריגות *בזמן עליית האפליקציה* (בנייה/רישום שירותים/
// middleware), לא רק חריגות בזמן ריצה. החריגה המלאה (כולל InnerException) נכתבת גם לקונסול
// וגם לקובץ startup-error.log שליד ה-exe — כדי שאפשר יהיה לאבחן גם כשהחלון נסגר.
try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddControllers();

    // Security
    builder.Services.AddSingleton<CredentialProtector>();

    // DAL
    builder.Services.AddScoped<DBServices>();

    // הלוגיקה הדומיינית (users/quotes/customers...) חיה כיום במחלקות המודל עצמן
    // (rich model) — אין יותר שירותי BL נפרדים לרישום. שירותי התשתית שנשארו
    // מחלקות עצמאיות רשומים למטה.
    builder.Services.AddScoped<AlternativeComponent>();

    // סקרייפינג ה-Selenium (MasterElectronics/ChromeDriver) הוצא מהפלואו — לא עבד על השרת.
    // הקוד נשמר תחת legacy/SeleniumScraping/ (מוחרג מ-build) לשחזור עתידי. ראה README שם.
    // Farnell רץ דרך תור jobs שתוסף Chrome בדפדפן המחובר של המשתמש מושך ומחזיר אליו מחירי חוזה.
    builder.Services.AddSingleton<FarnellJobQueue>();
    // NetComponents עבר מ-Selenium לתוסף הדפדפן — חיפושים נכנסים לתור הזה ונשלפים בסשן
    // המחובר של המשתמש (ראה ExtensionSearchController + background.js). מתוכנן להרחבה לספקים נוספים.
    builder.Services.AddSingleton<BrowserSearchQueue>();
    // המרת מטבע ל-USD (שערים מ-API ציבורי) — כדי לייבא גם מחירים שאינם בדולר ולנרמל אותם.
    builder.Services.AddSingleton<CurrencyService>();
    // חילוץ מחירים ב-AI לאתרים מבולגנים (Arrow) — שולח טקסט ל-Claude, מחזיר הצעות מובנות.
    builder.Services.AddSingleton<AiSupplierExtractor>();
    builder.Services.AddSingleton<PriceComparisonService>();

    // Insights (Dashboard AI) — משתמש ב-HttpClient לקריאות ל-Claude API
    builder.Services.AddHttpClient();
    builder.Services.AddScoped<InsightsService>();
    // WebExtract (POC) — לא היה רשום ב-DI וה-Controller שלו נכשל בבנייה; תוקן.
    builder.Services.AddHttpClient<WebExtractService>();

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    var app = builder.Build();

    // Configure the HTTP request pipeline.
    if (true)
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();
    app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
    app.UseAuthorization();
    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    var sb = new StringBuilder();
    sb.AppendLine("===== STARTUP CRASH =====");
    sb.AppendLine("Time: " + DateTime.Now.ToString("o"));
    // משרשרים את כל שרשרת ה-InnerException — שם בד"כ נמצאת הסיבה האמיתית.
    for (var e = ex; e != null; e = e.InnerException)
    {
        sb.AppendLine("---");
        sb.AppendLine(e.GetType().FullName + ": " + e.Message);
        sb.AppendLine(e.StackTrace);
    }
    var text = sb.ToString();

    Console.WriteLine(text);
    try
    {
        var path = Path.Combine(AppContext.BaseDirectory, "startup-error.log");
        File.WriteAllText(path, text);
        Console.WriteLine("נכתב גם לקובץ: " + path);
    }
    catch { /* אם אי אפשר לכתוב קובץ — לפחות הודפס לקונסול */ }

    Console.WriteLine("הקש Enter לסגירה...");
    Console.ReadLine();
}

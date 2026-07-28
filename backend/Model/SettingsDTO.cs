using comviaServer.DAL;

namespace comviaServer.Model;

public class SettingsDTO
{
    public decimal MinProfitPercent { get; set; }

    public decimal MinSavingPercent { get; set; }

    public decimal MaxIncreasePercent { get; set; }

    public decimal MinSavingPercentQuantity { get; set; }

    // ===== לוגיקה עסקית (לשעבר SettingsService) =====

    public static SettingsDTO Load(DBServices dal)
    {
        var settings = dal.GetAllSettings();

        decimal Value(string key) =>
            settings.FirstOrDefault(s => s.SettingKey == key)?.SettingValue ?? 0;

        return new SettingsDTO
        {
            MinProfitPercent = Value("MinProfitPercent"),
            MinSavingPercent = Value("MinSavingPercent"),
            MaxIncreasePercent = Value("MaxIncreasePercent"),
            MinSavingPercentQuantity = Value("MinSavingPercentQuantity")
        };
    }

    public void Save(DBServices dal)
    {
        dal.UpdateSetting("MinProfitPercent", MinProfitPercent);
        dal.UpdateSetting("MinSavingPercent", MinSavingPercent);
        dal.UpdateSetting("MaxIncreasePercent", MaxIncreasePercent);
        dal.UpdateSetting("MinSavingPercentQuantity", MinSavingPercentQuantity);
    }
}

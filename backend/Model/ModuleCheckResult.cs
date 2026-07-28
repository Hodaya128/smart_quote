namespace comviaServer.Model
{
    public class ModuleCheckResult
    {
        public string ModuleName { get; set; } = "";
        public string Message { get; set; } = "";
        public List<Recommendation> Recommendations { get; set; } = new List<Recommendation>();
    }
}

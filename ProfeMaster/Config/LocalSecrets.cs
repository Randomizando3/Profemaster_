namespace ProfeMaster.Config;

public static partial class LocalSecrets
{
    public static string GroqApiKey => GetSecrets().GroqApiKey;
    public static string GroqModel => string.IsNullOrWhiteSpace(GetSecrets().GroqModel)
        ? "llama-3.3-70b-versatile"
        : GetSecrets().GroqModel;
    public static string FirebaseApiKey => GetSecrets().FirebaseApiKey;
    public static string FirebaseRealtimeDbUrl => GetSecrets().FirebaseRealtimeDbUrl.TrimEnd('/');
    public static string FirebaseStorageBucket => GetSecrets().FirebaseStorageBucket;

    private static SecretValues GetSecrets()
    {
        var values = new SecretValues
        {
            GroqApiKey = Environment.GetEnvironmentVariable("PROFEMASTER_GROQ_API_KEY") ?? "",
            GroqModel = Environment.GetEnvironmentVariable("PROFEMASTER_GROQ_MODEL") ?? "llama-3.3-70b-versatile",
            FirebaseApiKey = Environment.GetEnvironmentVariable("PROFEMASTER_FIREBASE_API_KEY") ?? "",
            FirebaseRealtimeDbUrl = Environment.GetEnvironmentVariable("PROFEMASTER_FIREBASE_DB_URL") ?? "",
            FirebaseStorageBucket = Environment.GetEnvironmentVariable("PROFEMASTER_FIREBASE_STORAGE_BUCKET") ?? ""
        };

        ApplyLocalSecrets(values);
        return values;
    }

    static partial void ApplyLocalSecrets(SecretValues values);

    public sealed class SecretValues
    {
        public string GroqApiKey { get; set; } = "";
        public string GroqModel { get; set; } = "llama-3.3-70b-versatile";
        public string FirebaseApiKey { get; set; } = "";
        public string FirebaseRealtimeDbUrl { get; set; } = "";
        public string FirebaseStorageBucket { get; set; } = "";
    }
}

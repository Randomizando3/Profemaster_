namespace ProfeMaster.Config;

public static class FirebaseConfig
{
    public static string ApiKey => LocalSecrets.FirebaseApiKey;
    public static string RealtimeDbUrl => LocalSecrets.FirebaseRealtimeDbUrl;
    public static string StorageBucket => LocalSecrets.FirebaseStorageBucket;
}

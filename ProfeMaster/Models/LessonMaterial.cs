namespace ProfeMaster.Models;

public enum MaterialKind
{
    Link = 0,
    StorageFile = 1
}

public sealed class LessonMaterial
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public MaterialKind Kind { get; set; } = MaterialKind.Link;

    public string Title { get; set; } = "";     // Ex: “Lista de Exercícios”
    public string Url { get; set; } = "";       // Para Link ou para DownloadUrl do Storage
    public string StoragePath { get; set; } = "";// Ex: users/{uid}/plans/{planId}/arquivo.pdf
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; } = 0;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

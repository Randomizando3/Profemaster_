namespace ProfeMaster.Models;

public sealed class ScheduleEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";

    // vínculos (opcional)
    public string InstitutionId { get; set; } = "";
    public string InstitutionName { get; set; } = "";
    public string ClassId { get; set; } = "";
    public string ClassName { get; set; } = "";

    // datas
    public DateTime Start { get; set; } = DateTime.Now;
    public DateTime End { get; set; } = DateTime.Now.AddHours(1);

    // =========
    // Tipo do item na agenda
    // =========
    // Use "Kind" daqui pra frente (é o que seu editor está usando).
    // Valores sugeridos: "Aula", "Plano de aula", "Evento", "Prova"
    private string _kind = "Aula";
    public string Kind
    {
        get => string.IsNullOrWhiteSpace(_kind) ? "Aula" : _kind;
        set
        {
            _kind = string.IsNullOrWhiteSpace(value) ? "Aula" : value;

            // mantém consistência com "Type" caso algum ponto do app ainda use
            _type = _kind;
        }
    }

    // Mantido para compatibilidade com dados antigos/screen antiga.
    // NÃO precisa usar no código novo (use Kind).
    private string _type = "Aula";
    public string Type
    {
        get => string.IsNullOrWhiteSpace(_type) ? Kind : _type;
        set
        {
            _type = string.IsNullOrWhiteSpace(value) ? "Aula" : value;

            // mantém consistência com "Kind"
            _kind = _type;
        }
    }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Vínculo opcional com um item “source”
    // Ex.: Kind="Plano de aula" + LinkedKind="Plano" + LinkedId=<planId>
    public string LinkedKind { get; set; } = ""; // "Plano" | "Evento" | "Prova" | "Aula" | ""
    public string LinkedId { get; set; } = "";
    public string LinkedTitle { get; set; } = "";

    // Thumb do item (opcional)
    public string ThumbLocalPath { get; set; } = "";
    public string ThumbUrl { get; set; } = "";

    // Campos opcionais (se algum ponto ainda usa diretamente)
    public string? LinkedExamId { get; set; }
    public string? LinkedExamTitle { get; set; }

    public string? LinkedEventId { get; set; }
    public string? LinkedEventTitle { get; set; }
}

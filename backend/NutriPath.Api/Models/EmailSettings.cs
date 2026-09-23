namespace NutriPath.Api.Models;

public class EmailSettings
{
    public string SenderAddress { get; set; } = string.Empty;
    public string SenderName { get; set; } = "NutriPath";
    public string AppPassword { get; set; } = string.Empty;
    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
}
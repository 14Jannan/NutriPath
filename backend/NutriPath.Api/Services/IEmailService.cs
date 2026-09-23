namespace NutriPath.Api.Services;

public interface IEmailService
{
    Task SendAsync(string toAddress, string subject, string body);
}
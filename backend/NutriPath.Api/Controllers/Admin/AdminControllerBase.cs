using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NutriPath.Api.Services.Admin;

namespace NutriPath.Api.Controllers.Admin;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public sealed record ReasonRequest(string Reason);

/// <summary>Shared behaviour for admin controllers: no caching, ProblemDetails for rule violations.</summary>
[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[AdminExceptionFilter]
public abstract class AdminControllerBase : ControllerBase
{
    protected static (int Page, int PageSize) Paging(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));

    protected static string Csv(IEnumerable<IEnumerable<object?>> rows)
    {
        static string Cell(object? value)
        {
            var text = value switch
            {
                null => "",
                DateTime d => d.ToString("u"),
                _ => value.ToString() ?? "",
            };
            // Neutralise spreadsheet formulas (CSV injection) and quote every cell.
            if (text.Length > 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        return string.Join("\r\n", rows.Select(r => string.Join(",", r.Select(Cell)))) + "\r\n";
    }
}

/// <summary>Maps expected admin errors to ProblemDetails without leaking internals.</summary>
public sealed class AdminExceptionFilterAttribute : Attribute, IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        (int status, string detail)? mapped = context.Exception switch
        {
            AdminRuleException rule => (rule.Status, rule.Message),
            AdminAuthException auth => (auth.Status, auth.Message),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Not found."),
            Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException =>
                (StatusCodes.Status409Conflict, "This record was changed by someone else. Reload and try again."),
            _ => null,
        };
        if (mapped is not { } m) return;
        context.Result = new ObjectResult(new ProblemDetails { Status = m.status, Title = m.detail, Detail = m.detail })
            { StatusCode = m.status };
        context.ExceptionHandled = true;
    }
}

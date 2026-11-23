using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FoodJS.Api.Common;

/// <summary>
/// Thrown by services to produce the same <c>{ "message": "..." }</c> error body
/// (and status code) the Node API returned.
/// </summary>
public sealed class ApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class ApiExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        // A database relationship can block a legacy parent's physical delete.
        // Translate the integrity error without deciding that feature's delete policy.
        if (context.Exception is DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 19 } sqlite }
            && sqlite.Message.Contains("FOREIGN KEY constraint failed", StringComparison.OrdinalIgnoreCase))
        {
            context.Result = new ConflictObjectResult(new { message = "This record is referenced by retained data and cannot be permanently deleted." });
            context.ExceptionHandled = true;
            return;
        }

        if (context.Exception is not ApiException exception)
        {
            return;
        }

        context.Result = new ObjectResult(new { message = exception.Message })
        {
            StatusCode = exception.StatusCode
        };
        context.ExceptionHandled = true;
    }
}

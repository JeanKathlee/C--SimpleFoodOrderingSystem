using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

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

using FileSharing.Api.Middleware;
using FileSharing.Application.Common.Errors;
using FileSharing.Application.Common.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace FileSharing.Api.Extensions;

public static class ErrorHandlingExtensions
{
    public static IServiceCollection AddErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            // Applies to every ProblemDetails response written through IProblemDetailsService —
            // GlobalExceptionHandler's (both the AppException and the unhandled-exception
            // branches), and the ValidationProblem() helper controllers call directly for
            // FluentValidation failures — so a correlation id and a stable public "code" are
            // available on every single error response from one place, instead of a switch
            // repeated at every call site.
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.GetCorrelationId();

                context.ProblemDetails.Extensions["code"] = context.Exception switch
                {
                    AppException appException => appException.PublicCode,
                    null when context.ProblemDetails is ValidationProblemDetails => ErrorCodeCatalog.Map(ValidationErrorCode.InvalidRequest),
                    _ => ErrorCodeCatalog.Map(SystemErrorCode.UnexpectedError)
                };

                // ASP.NET Core's own ValidationProblem() default Title ("One or more validation
                // errors occurred.") is English and was never meant to be user-facing copy in
                // this app — every other error message here is Portuguese.
                if (context.ProblemDetails is ValidationProblemDetails)
                    context.ProblemDetails.Title = "Um ou mais campos são inválidos.";
            };
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();

        return services;
    }
}

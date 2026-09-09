using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FileSharing.Api.Extensions;

public static class ValidationResultExtensions
{
    public static void AddToModelState(this ValidationResult validationResult, ModelStateDictionary modelState)
    {
        foreach (var error in validationResult.Errors)
        {
            modelState.AddModelError(error.PropertyName, error.ErrorMessage);
        }
    }
}

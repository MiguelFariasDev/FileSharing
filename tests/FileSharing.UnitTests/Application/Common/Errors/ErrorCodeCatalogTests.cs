using FileSharing.Application.Common.Errors;

namespace FileSharing.UnitTests.Application.Common.Errors;

/// <summary>
/// Guards the two properties the whole error-code mechanism depends on: every declared enum
/// value has an explicit mapping (no silent fallback to a default), and no two values — even
/// across different enum families — collapse onto the same public string.
/// </summary>
public class ErrorCodeCatalogTests
{
    [Theory]
    [InlineData(AuthErrorCode.InvalidCredentials, "AUTH_INVALID_CREDENTIALS")]
    [InlineData(AuthErrorCode.EmailAlreadyExists, "AUTH_EMAIL_ALREADY_EXISTS")]
    [InlineData(AuthErrorCode.InvalidEmail, "AUTH_EMAIL_INVALID")]
    [InlineData(AuthErrorCode.AccountDisabled, "AUTH_ACCOUNT_DISABLED")]
    [InlineData(AuthErrorCode.PasswordResetInvalid, "AUTH_PASSWORD_RESET_INVALID")]
    [InlineData(AuthErrorCode.PasswordResetExpired, "AUTH_PASSWORD_RESET_EXPIRED")]
    [InlineData(AuthErrorCode.PasswordResetUsed, "AUTH_PASSWORD_RESET_USED")]
    [InlineData(AuthErrorCode.PasswordResetRateLimited, "AUTH_PASSWORD_RESET_RATE_LIMITED")]
    public void Map_AuthErrorCode_ReturnsExpectedPublicCode(AuthErrorCode code, string expected) =>
        Assert.Equal(expected, ErrorCodeCatalog.Map(code));

    [Theory]
    [InlineData(FileErrorCode.NotFound, "FILE_NOT_FOUND")]
    [InlineData(FileErrorCode.Expired, "FILE_EXPIRED")]
    [InlineData(FileErrorCode.AccessDenied, "FILE_ACCESS_DENIED")]
    [InlineData(FileErrorCode.InvalidType, "FILE_INVALID_TYPE")]
    [InlineData(FileErrorCode.InvalidUploadState, "FILE_UPLOAD_INVALID_STATE")]
    [InlineData(FileErrorCode.InvalidFileName, "FILE_INVALID_NAME")]
    [InlineData(FileErrorCode.UploadNotCompleted, "FILE_UPLOAD_NOT_COMPLETED")]
    public void Map_FileErrorCode_ReturnsExpectedPublicCode(FileErrorCode code, string expected) =>
        Assert.Equal(expected, ErrorCodeCatalog.Map(code));

    [Fact]
    public void Map_AuthorizationErrorCode_ReturnsExpectedPublicCode() =>
        Assert.Equal("AUTH_FORBIDDEN", ErrorCodeCatalog.Map(AuthorizationErrorCode.Forbidden));

    [Fact]
    public void Map_ValidationErrorCode_ReturnsExpectedPublicCode() =>
        Assert.Equal("VALIDATION_ERROR", ErrorCodeCatalog.Map(ValidationErrorCode.InvalidRequest));

    [Theory]
    [InlineData(ResourceErrorCode.NotFound, "RESOURCE_NOT_FOUND")]
    [InlineData(ResourceErrorCode.Conflict, "RESOURCE_CONFLICT")]
    public void Map_ResourceErrorCode_ReturnsExpectedPublicCode(ResourceErrorCode code, string expected) =>
        Assert.Equal(expected, ErrorCodeCatalog.Map(code));

    [Theory]
    [InlineData(SystemErrorCode.UnexpectedError, "INTERNAL_ERROR")]
    [InlineData(SystemErrorCode.ServiceUnavailable, "SERVICE_UNAVAILABLE")]
    public void Map_SystemErrorCode_ReturnsExpectedPublicCode(SystemErrorCode code, string expected) =>
        Assert.Equal(expected, ErrorCodeCatalog.Map(code));

    [Fact]
    public void Map_RateLimitErrorCode_ReturnsExpectedPublicCode() =>
        Assert.Equal("RATE_LIMITED", ErrorCodeCatalog.Map(RateLimitErrorCode.TooManyRequests));

    [Fact]
    public void EveryDeclaredAuthErrorCodeValue_HasAMapping()
    {
        foreach (AuthErrorCode code in Enum.GetValues<AuthErrorCode>())
            ErrorCodeCatalog.Map(code); // throws ArgumentOutOfRangeException if unmapped
    }

    [Fact]
    public void EveryDeclaredFileErrorCodeValue_HasAMapping()
    {
        foreach (FileErrorCode code in Enum.GetValues<FileErrorCode>())
            ErrorCodeCatalog.Map(code);
    }

    [Fact]
    public void NoTwoPublicCodes_AreDuplicated_AcrossAnyEnumFamily()
    {
        var allCodes = Enum.GetValues<AuthErrorCode>().Select(c => ErrorCodeCatalog.Map(c))
            .Concat(Enum.GetValues<FileErrorCode>().Select(c => ErrorCodeCatalog.Map(c)))
            .Concat(Enum.GetValues<AuthorizationErrorCode>().Select(c => ErrorCodeCatalog.Map(c)))
            .Concat(Enum.GetValues<ValidationErrorCode>().Select(c => ErrorCodeCatalog.Map(c)))
            .Concat(Enum.GetValues<ResourceErrorCode>().Select(c => ErrorCodeCatalog.Map(c)))
            .Concat(Enum.GetValues<SystemErrorCode>().Select(c => ErrorCodeCatalog.Map(c)))
            .Concat(Enum.GetValues<RateLimitErrorCode>().Select(c => ErrorCodeCatalog.Map(c)))
            .ToList();

        Assert.Equal(allCodes.Count, allCodes.Distinct().Count());
    }
}

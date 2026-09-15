using FileSharing.Application.Common.Errors;
using FileSharing.Application.Common.Exceptions;

namespace FileSharing.UnitTests.Application.Common.Exceptions;

/// <summary>Each exception type must carry exactly the status/code/message/enum it was constructed with — GlobalExceptionHandler trusts these blindly.</summary>
public class AppExceptionTests
{
    [Fact]
    public void AuthenticationException_DefaultsTo401_AndCarriesTheAuthErrorCode()
    {
        var exception = new AuthenticationException(AuthErrorCode.InvalidCredentials, "E-mail ou senha inválidos.");

        Assert.Equal(401, exception.HttpStatusCode);
        Assert.Equal("AUTH_INVALID_CREDENTIALS", exception.PublicCode);
        Assert.Equal("E-mail ou senha inválidos.", exception.PublicMessage);
        Assert.Equal(AuthErrorCode.InvalidCredentials, exception.Code);
    }

    [Fact]
    public void ForbiddenException_IsAlways403()
    {
        var exception = new ForbiddenException(AuthorizationErrorCode.Forbidden, "Você não tem permissão para acessar este recurso.");

        Assert.Equal(403, exception.HttpStatusCode);
        Assert.Equal("AUTH_FORBIDDEN", exception.PublicCode);
    }

    [Fact]
    public void ResourceNotFoundException_IsAlways404_ForResourceErrorCode()
    {
        var exception = new ResourceNotFoundException(ResourceErrorCode.NotFound, "Recurso não encontrado.");

        Assert.Equal(404, exception.HttpStatusCode);
        Assert.Equal("RESOURCE_NOT_FOUND", exception.PublicCode);
    }

    [Fact]
    public void ResourceNotFoundException_IsAlways404_ForFileErrorCode()
    {
        var exception = new ResourceNotFoundException(FileErrorCode.NotFound, "Arquivo não encontrado.");

        Assert.Equal(404, exception.HttpStatusCode);
        Assert.Equal("FILE_NOT_FOUND", exception.PublicCode);
    }

    [Fact]
    public void ConflictException_IsAlways409_ForAuthErrorCode()
    {
        var exception = new ConflictException(AuthErrorCode.EmailAlreadyExists, "Não foi possível concluir o cadastro.");

        Assert.Equal(409, exception.HttpStatusCode);
        Assert.Equal("AUTH_EMAIL_ALREADY_EXISTS", exception.PublicCode);
    }

    [Theory]
    [InlineData(AuthErrorCode.PasswordResetInvalid, "AUTH_PASSWORD_RESET_INVALID", 404)]
    [InlineData(AuthErrorCode.PasswordResetExpired, "AUTH_PASSWORD_RESET_EXPIRED", 410)]
    [InlineData(AuthErrorCode.PasswordResetUsed, "AUTH_PASSWORD_RESET_USED", 410)]
    public void DomainException_UsesTheExplicitlyPassedHttpStatus(AuthErrorCode code, string expectedPublicCode, int httpStatusCode)
    {
        var exception = new DomainException(code, "some message", httpStatusCode);

        Assert.Equal(httpStatusCode, exception.HttpStatusCode);
        Assert.Equal(expectedPublicCode, exception.PublicCode);
    }
}

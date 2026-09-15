namespace FileSharing.Web.Services;

/// <summary>
/// An otherwise-plain HttpClient, given its own type purely so dependency injection can tell it
/// apart from the HttpClient FileSharingApiClient uses — registering two unrelated `HttpClient`
/// instances in the same container is ambiguous. This client must never carry the Api's base
/// address or an Authorization header: the presigned URL already carries its own authorization
/// and points straight at S3, never at this Api (same discipline as
/// FileSharing.Mobile.Core.Services.Upload.S3UploadHttpClient — duplicated here rather than
/// shared, since Web and Mobile.Core intentionally do not reference each other).
/// </summary>
public class S3UploadHttpClient : HttpClient
{
    public S3UploadHttpClient()
    {
    }

    // Lets tests substitute a fake handler for the PUT-to-S3 step without a real network call —
    // see FileSharing.Web.Tests.Components.WebComponentTestContext.
    public S3UploadHttpClient(HttpMessageHandler handler) : base(handler)
    {
    }
}

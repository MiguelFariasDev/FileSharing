namespace FileSharing.Mobile.Core.Services.Upload;

/// <summary>
/// An otherwise-plain HttpClient, given its own type purely so dependency injection can tell it
/// apart from the separate HttpClient FileSharingApiClient uses — registering two unrelated
/// `HttpClient` instances in the same container is ambiguous (the container has no way to know
/// which constructor parameter should get which one). This client must never carry the API's
/// base address or an Authorization header (see FileUploadService's own remarks: the presigned
/// URL already carries its own authorization and points straight at S3, never at this API).
/// </summary>
public class S3UploadHttpClient : HttpClient
{
    public S3UploadHttpClient()
    {
    }

    public S3UploadHttpClient(HttpMessageHandler handler) : base(handler)
    {
    }
}

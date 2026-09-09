namespace FileSharing.Domain.Entities;

public class Download
{
    public Guid Id { get; private set; }
    public Guid FileId { get; private set; }
    public DateTimeOffset DownloadedAt { get; private set; }
    public string IpAddress { get; private set; } = string.Empty;
    public string UserAgent { get; private set; } = string.Empty;

    public File File { get; private set; } = null!;

    private Download()
    {
    }

    public Download(Guid fileId, string ipAddress, string userAgent)
    {
        if (fileId == Guid.Empty)
            throw new ArgumentException("FileId is required.", nameof(fileId));

        if (string.IsNullOrWhiteSpace(ipAddress))
            throw new ArgumentException("IpAddress is required.", nameof(ipAddress));

        if (string.IsNullOrWhiteSpace(userAgent))
            throw new ArgumentException("UserAgent is required.", nameof(userAgent));

        Id = Guid.NewGuid();
        FileId = fileId;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        DownloadedAt = DateTimeOffset.UtcNow;
    }
}

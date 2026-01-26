using System;

namespace ACT.Models;

public class UploadedFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; }
    public string ContentType { get; set; }
    public long Size { get; set; }
    public string S3BucketName { get; set; }
    public string S3Key { get; set; }
    public DateTime UploadDate { get; set; } = DateTime.UtcNow;
}

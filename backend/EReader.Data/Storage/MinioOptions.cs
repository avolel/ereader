namespace EReader.Data.Storage;

public sealed class MinioOptions
{
    public const string SectionName = "Minio";
    public string Endpoint { get; set; } = "localhost:9000"; // host:port, NO scheme
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Bucket { get; set; } = "ereader-media";
    public bool UseSSL { get; set; } = false;
}

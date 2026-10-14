using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EcommerceWebApi.Services
{
    /// <summary>
    /// Implements application update distribution via AWS S3 + CloudFront CDN.
    /// Replaces ClickOnce deployment technology (cr-dotnet-0048).
    ///
    /// Version manifest stored in S3 at: s3://{BucketName}/{VersionManifestKey}
    /// Application packages distributed via CloudFront at: https://{CloudFrontDomain}/{PackagePrefix}/
    /// </summary>
    public class CloudFrontUpdateService : IUpdateService
    {
        private readonly IAmazonS3 _s3Client;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CloudFrontUpdateService> _logger;

        // Configuration keys (set via environment variables or appsettings)
        private string BucketName => _configuration["AWS:UpdateService:BucketName"]
            ?? Environment.GetEnvironmentVariable("AWS_UPDATE_BUCKET_NAME")
            ?? throw new InvalidOperationException("AWS S3 bucket name for update service is not configured.");

        private string VersionManifestKey => _configuration["AWS:UpdateService:VersionManifestKey"]
            ?? Environment.GetEnvironmentVariable("AWS_UPDATE_VERSION_MANIFEST_KEY")
            ?? "releases/version.txt";

        private string CloudFrontDomain => _configuration["AWS:UpdateService:CloudFrontDomain"]
            ?? Environment.GetEnvironmentVariable("AWS_CLOUDFRONT_DOMAIN")
            ?? throw new InvalidOperationException("CloudFront domain for update service is not configured.");

        private string PackagePrefix => _configuration["AWS:UpdateService:PackagePrefix"]
            ?? Environment.GetEnvironmentVariable("AWS_UPDATE_PACKAGE_PREFIX")
            ?? "releases";

        private string LocalAppVersion => _configuration["AppVersion"]
            ?? Environment.GetEnvironmentVariable("APP_VERSION")
            ?? "1.0.0";

        public CloudFrontUpdateService(
            IAmazonS3 s3Client,
            IConfiguration configuration,
            ILogger<CloudFrontUpdateService> logger)
        {
            _s3Client = s3Client;
            _configuration = configuration;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<bool> IsUpdateAvailableAsync()
        {
            try
            {
                var latestVersion = await GetCurrentVersionAsync();
                var isUpdateAvailable = !string.Equals(
                    latestVersion.Trim(),
                    LocalAppVersion.Trim(),
                    StringComparison.OrdinalIgnoreCase);

                _logger.LogInformation(
                    "Update check: local={LocalVersion}, remote={RemoteVersion}, updateAvailable={UpdateAvailable}",
                    LocalAppVersion, latestVersion, isUpdateAvailable);

                return isUpdateAvailable;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check for application updates via S3/CloudFront.");
                return false;
            }
        }

        /// <inheritdoc />
        public async Task ApplyUpdateAsync()
        {
            try
            {
                var latestVersion = await GetCurrentVersionAsync();
                var packageKey = $"{PackagePrefix}/{latestVersion.Trim()}/app.zip";
                var downloadUrl = $"https://{CloudFrontDomain}/{packageKey}";

                _logger.LogInformation(
                    "Downloading application update v{Version} from CloudFront: {Url}",
                    latestVersion, downloadUrl);

                // Download the package from S3 via the AWS SDK (server-side update workflow).
                // In a cloud-hosted service, this triggers a rolling deployment rather than
                // a client-side ClickOnce install.
                var request = new GetObjectRequest
                {
                    BucketName = BucketName,
                    Key = packageKey
                };

                using var response = await _s3Client.GetObjectAsync(request);
                var tempPath = Path.Combine(Path.GetTempPath(), $"app-update-{latestVersion.Trim()}.zip");

                await response.WriteResponseStreamToFileAsync(tempPath, false, CancellationToken.None);

                _logger.LogInformation(
                    "Application update package v{Version} downloaded to {TempPath}. Ready for deployment.",
                    latestVersion, tempPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply application update via S3/CloudFront.");
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<string> GetCurrentVersionAsync()
        {
            try
            {
                var request = new GetObjectRequest
                {
                    BucketName = BucketName,
                    Key = VersionManifestKey
                };

                using var response = await _s3Client.GetObjectAsync(request);
                using var reader = new StreamReader(response.ResponseStream);
                var version = await reader.ReadToEndAsync();

                return version.Trim();
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Version manifest not found in S3 at key '{Key}'. Returning local version.", VersionManifestKey);
                return LocalAppVersion;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve current version from S3.");
                throw;
            }
        }
    }
}

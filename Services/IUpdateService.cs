namespace EcommerceWebApi.Services
{
    /// <summary>
    /// Abstraction for application update checks and distribution.
    /// Replaces ClickOnce deployment with S3 + CloudFront distribution (cr-dotnet-0048).
    /// </summary>
    public interface IUpdateService
    {
        /// <summary>
        /// Checks whether a newer version of the application package is available via CloudFront CDN.
        /// </summary>
        Task<bool> IsUpdateAvailableAsync();

        /// <summary>
        /// Downloads the latest application package from S3 via CloudFront and applies the update.
        /// </summary>
        Task ApplyUpdateAsync();

        /// <summary>
        /// Returns the current deployed version string stored in S3.
        /// </summary>
        Task<string> GetCurrentVersionAsync();
    }
}

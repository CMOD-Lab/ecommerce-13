using EcommerceWebApi.Services;

namespace EcommerceWebApi.Notification
{
    public class NotificationSubject
    {
        private readonly List<INotificationObserver> _observers = new();
        // cr-dotnet-0048: Replaced ClickOnce deployment with S3 + CloudFront distribution.
        // Application update checks are now performed via CloudFrontUpdateService using AWS SDK.
        private readonly IUpdateService _updateService;

        public NotificationSubject(IUpdateService updateService)
        {
            _updateService = updateService;
        }

        public void Attach(INotificationObserver observer)
        {
            _observers.Add(observer);
        }

        public void Detach(INotificationObserver observer)
        {
            _observers.Remove(observer);
        }

        public async Task NotifyAsync(string userId, string message)
        {
            foreach (var observer in _observers)
            {
                await observer.UpdateAsync(userId, message);
            }
        }

        /// <summary>
        /// Checks for application updates via CloudFront CDN (replaces ClickOnce update check).
        /// </summary>
        public async Task<bool> CheckForUpdateAsync()
        {
            return await _updateService.IsUpdateAvailableAsync();
        }

        /// <summary>
        /// Downloads and applies the latest application package from S3 via CloudFront (replaces ClickOnce update).
        /// </summary>
        public async Task ApplyUpdateAsync()
        {
            await _updateService.ApplyUpdateAsync();
        }
    }
}

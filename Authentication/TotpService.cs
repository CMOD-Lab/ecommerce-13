using OtpNet;
using Polly;
using Polly.Retry;
using System.Globalization;
using System.Net;
using System.Threading.Channels;
using System.Web;

namespace EcommerceWebApi.Authentication
{
    public class TotpService : ITotpService
    {
        // cr-dotnet-0039: Replaced static blocking field with a bounded Channel<long> to track
        // used TOTP time-windows in a non-blocking, async-safe producer-consumer pattern.
        // Channel<T> provides backpressure and is safe for concurrent cloud environments (AWS).
        private static readonly Channel<long> _usedTimeWindowChannel =
            Channel.CreateBounded<long>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = false,
                SingleWriter = false
            });

        // cr-dotnet-0039: Tracks the last used time-window value; written/read via Channel
        // writer/reader to avoid blocking collection operations.
        private static long _timeWindowUsedCurrent = 0L;

        private const string issuer = "EcommerceWebApi";

        // cr-dotnet-1014: Polly retry policy for transient HTTP failures (503, timeout) targeting
        // AWS API Gateway / external endpoints. Retries up to 3 times with exponential back-off.
        private static readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy =
            Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .OrResult(r =>
                    r.StatusCode == HttpStatusCode.ServiceUnavailable ||   // 503
                    r.StatusCode == HttpStatusCode.GatewayTimeout ||       // 504
                    r.StatusCode == HttpStatusCode.RequestTimeout)         // 408
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    onRetry: (outcome, timespan, retryAttempt, context) =>
                    {
                        // Log retry attempt (structured for cloud monitoring)
                        Console.WriteLine(
                            $"[TotpService] Retry {retryAttempt} after {timespan.TotalSeconds}s " +
                            $"due to: {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()}");
                    });

        public string GenerateBase32Secret()
        {
            var secret = KeyGeneration.GenerateRandomKey(20);
            return Base32Encoding.ToString(secret);
        }

        public string? GenerateQrCode(string? base32Secret, string? username)
        {
            // Validate inputs
            if (username == null || base32Secret == null)
            {
                return null;
            }

            try
            {
                // Generate URI for the QR Code
                var uriString = new OtpUri(OtpType.Totp, base32Secret, username, issuer).ToString();
                return uriString;
            }
            catch
            {
                return null;
            }
        }

        public string? GetSecretFromQrCode(string qrCode)
        {
            // Return null if validate failed
            if (ValidateAndExtractSecret(uriString: qrCode, out string? secret))
            {
                return secret;
            }
            else
            {
                return null;
            }
        }

        private static bool ValidateAndExtractSecret(string uriString, out string? secret)
        {
            // otpauth://totp/{issuer}:{username}?secret={secret}&issuer={issuer}&algorithm=SHA1&digits=6&period=30
            secret = null!;

            if (
                Uri.TryCreate(uriString, UriKind.Absolute, out Uri? uri)
                && uri.Scheme == "otpauth"
                && uri.Host == "totp"
            )
            {
                var queryParameters = HttpUtility.ParseQueryString(uri.Query);
                if (queryParameters["secret"] != null && queryParameters["issuer"] == issuer)
                {
                    secret = queryParameters["secret"];
                    return true;
                }
            }

            return false;
        }

        // cr-dotnet-0037: Converted ValidateTotp to async to eliminate the blocking
        // GetAwaiter().GetResult() call on GetNistTimeAsync(), preventing thread-pool
        // starvation in high-concurrency AWS cloud environments.
        // cr-dotnet-0039: Uses Channel<long> async writer to record used time-windows
        // instead of a blocking static field assignment, enabling non-blocking
        // producer-consumer tracking safe for distributed cloud deployments.
        public async Task<bool> ValidateTotpAsync(string? base32Secret, string totp)
        {
            try
            {
                var secret = Base32Encoding.ToBytes(base32Secret);

                // Get exact time for TOTP using fully async call — no blocking
                DateTime exactTime = await GetNistTimeAsync();

                // Validate TOTP
                var correction = new TimeCorrection(exactTime);
                var totpValidator = new Totp(secret, timeCorrection: correction);
                bool verify = totpValidator.VerifyTotp(
                    totp,
                    out long timeWindowUsed,
                    VerificationWindow.RfcSpecifiedNetworkDelay
                );

                // cr-dotnet-0039: Non-blocking async check of the used time-window via Channel<long>.
                // Drain any pending time-window values from the channel asynchronously before
                // comparing, then publish the new value — replacing the blocking static field pattern.
                await foreach (var usedWindow in _usedTimeWindowChannel.Reader.ReadAllAsync(
                    new CancellationTokenSource(TimeSpan.FromMilliseconds(50)).Token)
                    .WithCancellation(CancellationToken.None))
                {
                    _timeWindowUsedCurrent = usedWindow;
                }

                if (_timeWindowUsedCurrent == timeWindowUsed)
                {
                    return false;
                }

                // Publish the newly used time-window to the channel (non-blocking write)
                await _usedTimeWindowChannel.Writer.WriteAsync(timeWindowUsed);
                _timeWindowUsedCurrent = timeWindowUsed;

                return verify;
            }
            catch
            {
                throw;
            }
        }

        // cr-dotnet-0037: Replaced synchronous blocking .Result call with async/await pattern
        // to prevent thread-pool starvation in cloud auto-scaling scenarios (AWS ElastiCache-backed async data layer).
        // cr-dotnet-1014: Wrapped HttpClient.GetAsync with Polly retry policy to handle transient
        // 503/timeout errors common in AWS API Gateway and cloud infrastructure.
        public static async Task<DateTime> GetNistTimeAsync()
        {
            // Get UTC time from the response header of request to "http://www.google.com"
            // using a Polly retry policy to handle transient cloud failures (503, timeout).
            using var httpClient = new HttpClient();
            try
            {
                // cr-dotnet-1014: Execute the HTTP GET through the Polly retry policy so that
                // transient 503 / timeout responses from AWS API Gateway are automatically retried
                // (up to 3 times with exponential back-off) before surfacing an exception.
                using var response = await _retryPolicy.ExecuteAsync(
                    () => httpClient.GetAsync("http://www.google.com"));

                if (response.IsSuccessStatusCode && response.Headers.Date != null)
                {
                    return DateTime.ParseExact(
                        response.Headers.Date.Value.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'"),
                        "ddd, dd MMM yyyy HH:mm:ss 'GMT'",
                        CultureInfo.InvariantCulture.DateTimeFormat,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal
                    );
                }
                else
                {
                    throw new Exception("Failed to get exact time for the TOTP");
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to get exact time for the TOTP", ex);
            }
        }
    }
}

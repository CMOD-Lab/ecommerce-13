namespace EcommerceWebApi.Authentication
{
    public interface ITotpService
    {
        string GenerateBase32Secret();
        string? GenerateQrCode(string? base32Secret, string? username);
        string? GetSecretFromQrCode(string qrCode);
        // cr-dotnet-0037: Replaced synchronous ValidateTotp with async variant to eliminate
        // blocking .GetAwaiter().GetResult() calls and improve throughput in AWS cloud environments.
        Task<bool> ValidateTotpAsync(string? base32Secret, string totp);
    }
}

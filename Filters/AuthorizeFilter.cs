using EcommerceWebApi.Authentication;
using EcommerceWebApi.Entities;
using EcommerceWebApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.IdentityModel.Tokens.Jwt;

namespace EcommerceWebApi.Filters
{
    // cr-dotnet-1000: Converted from IAuthorizationFilter (synchronous) to IAsyncAuthorizationFilter
    // to replace blocking synchronous data-access calls with async/await patterns, ensuring
    // non-blocking thread-pool usage under high load in AWS cloud auto-scaling scenarios.
    public class AuthorizeFilter : IAsyncAuthorizationFilter
    {
        private readonly AuthService _authService;
        private readonly UserService _userService;
        private readonly JwtService _jwtService;
        private readonly ILogger<AuthorizeFilter> _logger;

        public AuthorizeFilter(
            AuthService authService,
            UserService userService,
            JwtService jwtService,
            ILogger<AuthorizeFilter> logger
        )
        {
            _authService = authService;
            _userService = userService;
            _jwtService = jwtService;
            _logger = logger;
        }

        // cr-dotnet-1000: Replaced synchronous OnAuthorization with async OnAuthorizationAsync
        // to prevent thread-pool blocking in cloud auto-scaling scenarios.
        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            try
            {
                var allowAnonymous = context.ActionDescriptor.EndpointMetadata
                    .OfType<AllowAnonymousAttribute>()
                    .Any();
                if (allowAnonymous)
                {
                    return;
                }

                var allowFirstFactor = context.ActionDescriptor.EndpointMetadata
                    .OfType<AllowFirstFactorAttribute>()
                    .Any();

                var requiredRoles = context.ActionDescriptor.EndpointMetadata
                    .OfType<AuthorizeRoleAttribute>()
                    .Select(a => a.Role);

                var accessTokenString = context.HttpContext.Request.Cookies["access_token"];
                var refreshTokenString = context.HttpContext.Request.Cookies["refresh_token"];

                if (!allowFirstFactor)
                {
                    // cr-dotnet-1000: Replaced synchronous CheckRefreshToken with async version (lines 118, 129)
                    var (refreshOk, user) = await CheckRefreshTokenAsync(refreshTokenString, context);
                    if (!refreshOk)
                    {
                        return;
                    }

                    if (string.IsNullOrEmpty(accessTokenString) && user != null)
                    {
                        _ = _jwtService.GenerateJWT(
                            user,
                            isSecondFactorChecked: true,
                            context.HttpContext
                        );

                        return;
                    }
                    else
                    {
                        // cr-dotnet-1000: Replaced synchronous CheckAccessToken with async version (lines 150, 162, 175)
                        var (accessOk, accessToken, currentUser) = await CheckAccessTokenAsync(
                            accessTokenString,
                            user,
                            context
                        );
                        if (!accessOk)
                        {
                            return;
                        }

                        if (requiredRoles.Any() && !CheckRole(accessToken!, requiredRoles, context))
                        {
                            return;
                        }

                        _authService.CurrentUser = currentUser!;
                    }
                }
                else
                {
                    // cr-dotnet-1000: Replaced synchronous CheckAccessToken with async version (lines 219, 230)
                    var (accessOk, accessToken, currentUser) = await CheckAccessTokenAsync(
                        accessTokenString,
                        null,
                        context
                    );
                    if (!accessOk)
                    {
                        return;
                    }

                    if (requiredRoles.Any() && !CheckRole(accessToken!, requiredRoles, context))
                    {
                        return;
                    }

                    _authService.CurrentUser = currentUser!;

                    context.Result = new ContentResult()
                    {
                        Content = "Need second factor authentication",
                        StatusCode = StatusCodes.Status200OK
                    };
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{ex.Message} - {ex.InnerException?.Message}");
                context.Result = new ContentResult()
                {
                    Content = "Unauthorized",
                    StatusCode = StatusCodes.Status401Unauthorized
                };
                return;
            }
        }

        // cr-dotnet-1000: Converted CheckRefreshToken to async to replace blocking _userService.GetUserByToken()
        // call (lines 118, 129) with non-blocking async/await pattern for cloud scalability.
        private async Task<(bool success, User? user)> CheckRefreshTokenAsync(
            string? refreshTokenString,
            AuthorizationFilterContext context
        )
        {
            try
            {
                if (string.IsNullOrEmpty(refreshTokenString))
                {
                    context.Result = new ContentResult()
                    {
                        Content = "Unauthorized",
                        StatusCode = StatusCodes.Status401Unauthorized
                    };
                    return (false, null);
                }

                // cr-dotnet-1000: Replaced synchronous GetUserByToken (line 129) with async GetUserByTokenAsync
                var user = await _userService.GetUserByTokenAsync(token: refreshTokenString);

                if (user == null || user.RefreshToken.Expires < DateTimeOffset.UtcNow.UtcDateTime)
                {
                    context.Result = new ContentResult()
                    {
                        Content = "Unauthorized",
                        StatusCode = StatusCodes.Status401Unauthorized
                    };
                    return (false, null);
                }

                return (true, user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{ex.Message} - {ex.InnerException?.Message}");
                context.Result = new ContentResult()
                {
                    Content = "Unauthorized",
                    StatusCode = StatusCodes.Status401Unauthorized
                };
                return (false, null);
            }
        }

        // cr-dotnet-1000: Converted CheckAccessToken to async to replace blocking _userService.GetUserById()
        // call (lines 150, 162, 175, 219, 230, 257, 271) with non-blocking async/await pattern for cloud scalability.
        private async Task<(bool success, JwtSecurityToken? accessToken, User? currentUser)> CheckAccessTokenAsync(
            string? accessTokenString,
            User? user,
            AuthorizationFilterContext context
        )
        {
            try
            {
                var accessToken = new JwtSecurityTokenHandler().ReadJwtToken(accessTokenString);
                var accessTokenId = accessToken.Claims.FirstOrDefault(x => x.Type == "id");
                var isSecondFactorChecked = accessToken.Claims.FirstOrDefault(
                    x => x.Type == "status"
                );

                if (accessTokenId == null || isSecondFactorChecked == null)
                {
                    return (false, null, null);
                }

                // cr-dotnet-1000: Replaced synchronous GetUserById (lines 150, 219) with async GetUserByIdAsync
                var currentUser = await _userService.GetUserByIdAsync(accessTokenId.Value);

                if (
                    (
                        user != null
                        && accessTokenId.Value == user.Id
                        && isSecondFactorChecked.Value == "True"
                    ) || (user == null && currentUser != null)
                )
                {
                    return (true, accessToken, currentUser);
                }
                context.Result = new ContentResult()
                {
                    Content = "Unauthorized",
                    StatusCode = StatusCodes.Status401Unauthorized
                };
                return (false, null, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{ex.Message} - {ex.InnerException?.Message}");
                context.Result = new ContentResult()
                {
                    Content = "Unauthorized",
                    StatusCode = StatusCodes.Status401Unauthorized
                };
                return (false, null, null);
            }
        }

        // cr-dotnet-1000: CheckRole remains synchronous as it only performs in-memory JWT claim inspection
        // (lines 257, 271) — no I/O blocking occurs here.
        private bool CheckRole(
            JwtSecurityToken accessToken,
            IEnumerable<string>? requiredRoles,
            AuthorizationFilterContext context
        )
        {
            try
            {
                if (requiredRoles == null)
                {
                    return false;
                }

                if (requiredRoles.Any())
                {
                    var userRoleClaim = accessToken.Claims.FirstOrDefault(c => c.Type == "role");
                    if (userRoleClaim == null || !requiredRoles.Contains(userRoleClaim.Value))
                    {
                        context.Result = new ContentResult()
                        {
                            Content = "Forbidden",
                            StatusCode = StatusCodes.Status403Forbidden
                        };
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{ex.Message} - {ex.InnerException?.Message}");
                context.Result = new ContentResult()
                {
                    Content = "Forbidden",
                    StatusCode = StatusCodes.Status403Forbidden
                };
                return false;
            }
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class AllowAnonymousAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class AllowFirstFactorAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class AuthorizeRoleAttribute : Attribute
    {
        public string Role { get; }

        public AuthorizeRoleAttribute(string role)
        {
            Role = role;
        }
    }
}

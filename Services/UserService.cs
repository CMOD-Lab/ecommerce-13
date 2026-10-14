using EcommerceWebApi.Entities;
using EcommerceWebApi.Utilities;
using System.Reflection;

namespace EcommerceWebApi.Services
{
    public class UserService : IUserService
    {
        private readonly IUnitOfWork _unitOfWork;
        // cr-dotnet-0048: Replaced ClickOnce deployment with S3 + CloudFront distribution.
        // Application update checks are now performed via CloudFrontUpdateService using AWS SDK.
        private readonly IUpdateService _updateService;

        public UserService(UnitOfWork unitOfWork, IUpdateService updateService)
        {
            _unitOfWork = unitOfWork;
            _updateService = updateService;
        }

        public List<User> GetAllUsers()
        {
            try
            {
                return _unitOfWork.Users.GetAll().AsQueryable().ToList();
            }
            catch
            {
                throw;
            }
        }

        public List<User> GetPaginationUsers(PaginationFilter paginationFilter)
        {
            return GetAllUsers()
                .Skip((paginationFilter.PageNumber - 1) * paginationFilter.PageSize)
                .Take(paginationFilter.PageSize)
                .ToList();
        }

        public User? GetUserById(string id)
        {
            return _unitOfWork.Users.GetById(id);
        }

        // cr-dotnet-1000: Async overload for non-blocking cloud-ready data retrieval (AWS ElastiCache-backed async data layer)
        public Task<User?> GetUserByIdAsync(string id)
        {
            return _unitOfWork.Users.GetByIdAsync(id);
        }

        public User? GetUserByToken(string token)
        {
            return _unitOfWork.Users.GetByToken(token);
        }

        // cr-dotnet-1000: Async overload for non-blocking cloud-ready data retrieval (AWS ElastiCache-backed async data layer)
        public Task<User?> GetUserByTokenAsync(string token)
        {
            return _unitOfWork.Users.GetByTokenAsync(token);
        }

        public User? GetUserByName(string name)
        {
            return _unitOfWork.Users.GetByName(name);
        }

        public async Task<bool> InsertUserAsync(User user)
        {
            try
            {
                var result = await _unitOfWork.Users.InsertAsync(user);
                return result;
            }
            catch
            {
                throw;
            }
        }

        public async Task<bool> UpdateUserAsync(User user)
        {
            try
            {
                var result = await _unitOfWork.Users.UpdateAsync(user);
                return result;
            }
            catch
            {
                throw;
            }
        }

        public async Task<bool> UpdateUserTokenAsync(User user, RefreshToken? refreshToken)
        {
            if (refreshToken != null)
            {
                user.RefreshToken = refreshToken;
            }
            else
            {
                user.RefreshToken.Token = null!;
            }
            return await UpdateUserAsync(user);
        }

        public async Task<bool> UpdateUserPropertyAsync<T>(User user, string property, T value)
        {
            var propertyInfo = typeof(User).GetProperty(
                property,
                BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance
            );

            if (propertyInfo == null)
            {
                return false;
            }
            try
            {
                propertyInfo.SetValue(user, value, null);
                return await UpdateUserAsync(user);
            }
            catch
            {
                throw;
            }
        }

        public async Task<bool> DeleteUserAsync(string id)
        {
            try
            {
                var result = await _unitOfWork.Users.DeleteAsync(id);
                return result;
            }
            catch
            {
                throw;
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

        public void Dispose()
        {
            _unitOfWork.Dispose();
        }
    }
}

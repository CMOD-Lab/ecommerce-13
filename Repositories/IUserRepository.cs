using EcommerceWebApi.Entities;
using JsonFlatFileDataStore;

namespace EcommerceWebApi.Repositories
{
    public interface IUserRepository
    {
        Task<bool> DeleteAsync(string id);
        IDocumentCollection<User> GetAll();
        User? GetById(string id);
        // cr-dotnet-1000: Added async overloads for non-blocking cloud-ready data retrieval
        Task<User?> GetByIdAsync(string id);
        User? GetByName(string name);
        User? GetByToken(string token);
        // cr-dotnet-1000: Added async overload for non-blocking cloud-ready data retrieval
        Task<User?> GetByTokenAsync(string token);
        Task<bool> InsertAsync(User user);
        Task<bool> UpdateAsync(User user);
    }
}

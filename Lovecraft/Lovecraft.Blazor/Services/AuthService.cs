using System;
using System.Threading.Tasks;
using Lovecraft.Common.DataContracts;
using Lovecraft.Common.Interfaces;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace Lovecraft.Blazor.Services
{
    public class AuthService
    {
        private readonly ProtectedLocalStorage _storage;
        private readonly ILovecraftApiClient _api;
        private const string StorageKey = "lovecraft_user_id";

        public event Action? OnChange;

        public User? CurrentUser { get; private set; }

        public bool IsAuthenticated => CurrentUser != null;

        public AuthService(ProtectedLocalStorage storage, ILovecraftApiClient api)
        {
            _storage = storage;
            _api = api;
        }

        public async Task InitializeAsync()
        {
            try
            {
                var res = await _storage.GetAsync<string>(StorageKey);
                if (res.Success && !string.IsNullOrWhiteSpace(res.Value))
                {
                    if (Guid.TryParse(res.Value, out var id))
                    {
                        CurrentUser = await _api.GetUserByIdAsync(id);
                        NotifyStateChanged();
                    }
                }
            }
            catch
            {
                // ignore initialization errors
            }
        }

        public async Task<bool> SignInAsync(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return false;

            var user = await _api.AuthenticateAsync(username.Trim().ToLowerInvariant(), password);
            if (user != null)
            {
                CurrentUser = user;
                await _storage.SetAsync(StorageKey, user.Id.ToString());
                NotifyStateChanged();
                return true;
            }

            return false;
        }

        public async Task<User?> RegisterAsync(string username, string password, string name, string avatarUri)
        {
            var req = new CreateUserRequest
            {
                Username = string.IsNullOrWhiteSpace(username) ? null : username.Trim(),
                Password = password,
                Name = name,
                AvatarUri = avatarUri
            };

            var created = await _api.CreateUserAsync(req);
            if (created != null)
            {
                CurrentUser = created;
                await _storage.SetAsync(StorageKey, created.Id.ToString());
                NotifyStateChanged();
            }
            return created;
        }

        public async Task SignOutAsync()
        {
            CurrentUser = null;
            try
            {
                await _storage.DeleteAsync(StorageKey);
            }
            catch
            {
                // ignore
            }
            NotifyStateChanged();
        }

        private void NotifyStateChanged() => OnChange?.Invoke();
    }
}

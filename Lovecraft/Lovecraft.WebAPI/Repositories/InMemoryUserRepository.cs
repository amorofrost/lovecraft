using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Lovecraft.Common.DataContracts;

namespace Lovecraft.WebAPI.Repositories
{
    public class InMemoryUserRepository : IUserRepository
    {
        private readonly ConcurrentDictionary<Guid, User> _store = new();
    // Index from TelegramUserId -> internal Guid
    private readonly ConcurrentDictionary<long, Guid> _telegramIndex = new();
    // Index for username (case-insensitive) -> internal Guid
    private readonly ConcurrentDictionary<string, Guid> _usernameIndex = new(StringComparer.OrdinalIgnoreCase);

        public Task<User> CreateAsync(User user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            // Validate lengths
            User.ValidateLengthsOrThrow(user);

            if (user.TelegramUserId.HasValue)
            {
                var tgId = user.TelegramUserId.Value;
                // Quick pre-check — exact check performed atomically below
                if (_telegramIndex.ContainsKey(tgId))
                    throw new DuplicateTelegramUserIdException(tgId);
            }

            if (!string.IsNullOrWhiteSpace(user.TelegramUsername))
            {
                var uname = user.TelegramUsername!;
                if (_usernameIndex.ContainsKey(uname))
                    throw new DuplicateTelegramUsernameException(uname);
            }

            if (user.Id == Guid.Empty)
                user.Id = Guid.NewGuid();
            if (user.CreatedAt == default)
                user.CreatedAt = DateTime.UtcNow;
            user.Version = Guid.NewGuid().ToString();

            // We need to atomically reserve both indexes (if provided). Reserving means adding to index map(s),
            // then storing in _store. If reserving one index succeeds and another fails, rollback the successful one.

            bool addedTelegram = false;
            bool addedUsername = false;
            try
            {
                if (user.TelegramUserId.HasValue)
                {
                    var tgId = user.TelegramUserId.Value;
                    if (!_telegramIndex.TryAdd(tgId, user.Id))
                        throw new DuplicateTelegramUserIdException(tgId);
                    addedTelegram = true;
                }

                if (!string.IsNullOrWhiteSpace(user.TelegramUsername))
                {
                    var uname = user.TelegramUsername!;
                    if (!_usernameIndex.TryAdd(uname, user.Id))
                        throw new DuplicateTelegramUsernameException(uname);
                    addedUsername = true;
                }

                // Store user by internal id
                _store[user.Id] = user;
                return Task.FromResult(user);
            }
            catch
            {
                // Rollback any index adds
                if (addedTelegram && user.TelegramUserId.HasValue)
                {
                    _telegramIndex.TryRemove(user.TelegramUserId.Value, out _);
                }
                if (addedUsername && !string.IsNullOrWhiteSpace(user.TelegramUsername))
                {
                    _usernameIndex.TryRemove(user.TelegramUsername!, out _);
                }

                throw;
            }
        }

        public Task<User?> GetByIdAsync(Guid id)
        {
            _store.TryGetValue(id, out var user);
            return Task.FromResult(user);
        }

        public Task<User?> GetByTelegramUserIdAsync(long telegramUserId)
        {
            if (_telegramIndex.TryGetValue(telegramUserId, out var id))
            {
                _store.TryGetValue(id, out var user);
                return Task.FromResult(user);
            }

            return Task.FromResult<User?>(null);
        }

        public Task<User?> GetByTelegramUsernameAsync(string telegramUsername)
        {
            if (string.IsNullOrWhiteSpace(telegramUsername))
                return Task.FromResult<User?>(null);

            if (_usernameIndex.TryGetValue(telegramUsername, out var id))
            {
                _store.TryGetValue(id, out var user);
                return Task.FromResult(user);
            }

            return Task.FromResult<User?>(null);
        }
    }
}

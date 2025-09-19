using Microsoft.VisualStudio.TestTools.UnitTesting;
using Lovecraft.WebAPI.Repositories;
using Lovecraft.Common.DataContracts;
using System;
using System.Threading.Tasks;

namespace Lovecraft.UnitTests
{
    [TestClass]
    public class InMemoryUserRepositoryTests
    {
        [TestMethod]
        public async Task CreateSetsIdCreatedAtAndVersion()
        {
            var repo = new InMemoryUserRepository();
            var user = new User { Name = "Bob", AvatarUri = "https://x" };

            var created = await repo.CreateAsync(user);

            Assert.AreNotEqual(Guid.Empty, created.Id);
            Assert.AreNotEqual(default(DateTime), created.CreatedAt);
            Assert.IsFalse(string.IsNullOrWhiteSpace(created.Version));
            Assert.AreEqual("Bob", created.Name);
        }

        [TestMethod]
        public async Task GetByIdReturnsCreatedUser()
        {
            var repo = new InMemoryUserRepository();
            var user = new User { Name = "Bob", AvatarUri = "https://x" };
            var created = await repo.CreateAsync(user);

            var fetched = await repo.GetByIdAsync(created.Id);
            Assert.IsNotNull(fetched);
            Assert.AreEqual(created.Id, fetched!.Id);
        }

        [TestMethod]
        public async Task GetByTelegramIdAndUsernameWork()
        {
            var repo = new InMemoryUserRepository();
            var user = new User { Name = "Carl", AvatarUri = "https://x", TelegramUserId = 999, TelegramUsername = "TeStUser" };
            var created = await repo.CreateAsync(user);

            var byId = await repo.GetByTelegramUserIdAsync(999);
            Assert.IsNotNull(byId);
            Assert.AreEqual(created.Id, byId!.Id);

            var byUsername = await repo.GetByTelegramUsernameAsync("testuser");
            Assert.IsNotNull(byUsername);
            Assert.AreEqual(created.Id, byUsername!.Id);
        }

        [TestMethod]
        public async Task GetRandomAsync_ReturnsOneOfCreatedUsers()
        {
            var repo = new InMemoryUserRepository();
            var u1 = new User { Name = "Alice", AvatarUri = "https://a" };
            var u2 = new User { Name = "Bob", AvatarUri = "https://b" };

            var c1 = await repo.CreateAsync(u1);
            var c2 = await repo.CreateAsync(u2);

            var random = await repo.GetRandomAsync();
            Assert.IsNotNull(random);
            Assert.IsTrue(random!.Id == c1.Id || random.Id == c2.Id);
        }

        [TestMethod]
        public async Task AuthenticateAsync_SucceedsWithCorrectCredentials()
        {
            var repo = new InMemoryUserRepository();
            // Create a user with precomputed password hash using the same format as controller's HashPassword
            var user = new User { Name = "AuthUser", AvatarUri = "https://x", Username = "authuser" };

            // Use the same helper as in UsersController: produce PBKDF2 hash
            var password = "P@ssw0rd!";
            user.PasswordHash = CreatePbkdf2Hash(password, 120_000);

            var created = await repo.CreateAsync(user);

            var auth = await repo.AuthenticateAsync("authuser", password);
            Assert.IsNotNull(auth);
            Assert.AreEqual(created.Id, auth!.Id);
        }

        [TestMethod]
        public async Task AuthenticateAsync_FailsWithWrongPassword()
        {
            var repo = new InMemoryUserRepository();
            var user = new User { Name = "AuthUser2", AvatarUri = "https://x", Username = "authuser2" };
            var password = "CorrectPassword";
            user.PasswordHash = CreatePbkdf2Hash(password, 120_000);
            var created = await repo.CreateAsync(user);

            var auth = await repo.AuthenticateAsync("authuser2", "WrongPassword");
            Assert.IsNull(auth);
        }

        [TestMethod]
        public async Task AuthenticateAsync_FailsWithMalformedStoredHash()
        {
            var repo = new InMemoryUserRepository();
            var user = new User { Name = "BadHash", AvatarUri = "https://x", Username = "badhash" };
            user.PasswordHash = "not-a-valid-hash";
            var created = await repo.CreateAsync(user);

            var auth = await repo.AuthenticateAsync("badhash", "anything");
            Assert.IsNull(auth);
        }

        // Helper to create PBKDF2 hash string in format {iterations}.{saltBase64}.{hashBase64}
        private static string CreatePbkdf2Hash(string password, int iterations)
        {
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            var salt = new byte[16];
            rng.GetBytes(salt);

            using var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(password, salt, iterations, System.Security.Cryptography.HashAlgorithmName.SHA256);
            var hash = pbkdf2.GetBytes(32);
            return $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
        }
    }
}

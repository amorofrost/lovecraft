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
    }
}

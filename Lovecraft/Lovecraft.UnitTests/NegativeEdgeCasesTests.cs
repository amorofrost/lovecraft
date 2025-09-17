using Microsoft.VisualStudio.TestTools.UnitTesting;
using Lovecraft.WebAPI.Repositories;
using Lovecraft.Common.DataContracts;
using System.Threading.Tasks;
using System;

namespace Lovecraft.UnitTests
{
    [TestClass]
    public class NegativeEdgeCasesTests
    {
        [TestMethod]
        public async Task GetByTelegramUsername_WithEmptyString_ReturnsNull()
        {
            var repo = new InMemoryUserRepository();
            var res = await repo.GetByTelegramUsernameAsync("");
            Assert.IsNull(res);
        }

        [TestMethod]
        public async Task Create_DuplicateTelegramIds_Throws()
        {
            var repo = new InMemoryUserRepository();
            var u1 = new User { Name = "A", AvatarUri = "https://a", TelegramUserId = 42 };
            var u2 = new User { Name = "B", AvatarUri = "https://b", TelegramUserId = 42 };

            var c1 = await repo.CreateAsync(u1);
            await Assert.ThrowsExceptionAsync<DuplicateTelegramUserIdException>(async () => await repo.CreateAsync(u2));
        }

        [TestMethod]
        public async Task Create_LongStrings_AreStored()
        {
            var repo = new InMemoryUserRepository();
            var validName = new string('x', Lovecraft.Common.DataContracts.User.MaxNameLength);
            var u = new User { Name = validName, AvatarUri = "https://a" };
            var created = await repo.CreateAsync(u);
            Assert.AreEqual(validName, created.Name);
        }

        [TestMethod]
        public async Task Create_TooLongName_ThrowsArgument()
        {
            var repo = new InMemoryUserRepository();
            var longName = new string('x', Lovecraft.Common.DataContracts.User.MaxNameLength + 1);
            var u = new User { Name = longName, AvatarUri = "https://a" };
            await Assert.ThrowsExceptionAsync<ArgumentException>(async () => await repo.CreateAsync(u));
        }

        [TestMethod]
        public async Task Create_DuplicateUsernames_Throws()
        {
            var repo = new InMemoryUserRepository();
            var u1 = new User { Name = "A", AvatarUri = "https://a", TelegramUsername = "dupuser" };
            var u2 = new User { Name = "B", AvatarUri = "https://b", TelegramUsername = "DupUser" };

            var c1 = await repo.CreateAsync(u1);
            await Assert.ThrowsExceptionAsync<DuplicateTelegramUsernameException>(async () => await repo.CreateAsync(u2));
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Lovecraft.WebAPI.Controllers;
using Lovecraft.WebAPI.Repositories;
using Lovecraft.Common.DataContracts;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System;

namespace Lovecraft.UnitTests
{
    [TestClass]
    public class UsersControllerTests
    {
        [TestMethod]
        public async Task Create_ReturnsCreatedAndCanGetById()
        {
            var repo = new InMemoryUserRepository();
            var controller = new UsersController(repo);

            var req = new CreateUserRequest { Name = "Zoe", AvatarUri = "https://avatar" };
            var result = await controller.Create(req) as CreatedAtActionResult;
            Assert.IsNotNull(result);

            var created = result!.Value as User;
            Assert.IsNotNull(created);
            Assert.AreEqual("Zoe", created!.Name);

            var getResult = await controller.GetById(created.Id) as OkObjectResult;
            Assert.IsNotNull(getResult);
            var fetched = getResult!.Value as User;
            Assert.IsNotNull(fetched);
            Assert.AreEqual(created.Id, fetched!.Id);
        }

        [TestMethod]
        public async Task Create_ValidatesRequiredFields()
        {
            var repo = new InMemoryUserRepository();
            var controller = new UsersController(repo);

            // Missing name
            var bad1 = await controller.Create(new CreateUserRequest { Name = "", AvatarUri = "https://a" });
            Assert.IsInstanceOfType(bad1, typeof(BadRequestObjectResult));

            // Missing avatar
            var bad2 = await controller.Create(new CreateUserRequest { Name = "Anna", AvatarUri = "" });
            Assert.IsInstanceOfType(bad2, typeof(BadRequestObjectResult));
        }
    }
}

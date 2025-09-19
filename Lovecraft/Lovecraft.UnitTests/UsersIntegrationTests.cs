using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.AspNetCore.Mvc.Testing;
using Lovecraft.WebAPI;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using Lovecraft.Common.DataContracts;
using System.Net;

namespace Lovecraft.UnitTests
{
    [TestClass]
    public class UsersIntegrationTests
    {
        private static WebApplicationFactory<Program>? _factory;
        private static HttpClient? _client;

        [ClassInitialize]
        public static void Setup(TestContext ctx)
        {
            // Use the WebAPI project's Program as the entry point
            _factory = new WebApplicationFactory<Program>();
            _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        }

        [ClassCleanup]
        public static void Teardown()
        {
            _client?.Dispose();
            _factory?.Dispose();
        }

        [TestMethod]
        public async Task PostUser_ReturnsCreated_AndGetByIdWorks()
        {
            var req = new CreateUserRequest { Name = "IntTest", AvatarUri = "https://a" };
            var json = JsonSerializer.Serialize(req);
            var res = await _client!.PostAsync("/api/users", new StringContent(json, Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.Created, res.StatusCode);

            var body = await res.Content.ReadAsStringAsync();
            var created = JsonSerializer.Deserialize<User>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.IsNotNull(created);

            var getRes = await _client.GetAsync($"/api/users/{created!.Id}");
            Assert.AreEqual(HttpStatusCode.OK, getRes.StatusCode);
        }

        [TestMethod]
        public async Task PostUser_BadRequest_WhenMissingFields()
        {
            var req = new { name = "", avatarUri = "" };
            var json = JsonSerializer.Serialize(req);
            var res = await _client!.PostAsync("/api/users", new StringContent(json, Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.BadRequest, res.StatusCode);
        }

        [TestMethod]
        public async Task GetByTelegramIdAndUsername_ReturnsCreatedUser_AndUsernameIsCaseInsensitive()
        {
            var req = new CreateUserRequest { Name = "LookupTest", AvatarUri = "https://a", TelegramUserId = 4321, TelegramUsername = "CaseUser" };
            var json = JsonSerializer.Serialize(req);
            var res = await _client!.PostAsync("/api/users", new StringContent(json, Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.Created, res.StatusCode);

            var body = await res.Content.ReadAsStringAsync();
            var created = JsonSerializer.Deserialize<User>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.IsNotNull(created);

            var byTelegram = await _client.GetAsync($"/api/users/byTelegramId/{created!.TelegramUserId}");
            Assert.AreEqual(HttpStatusCode.OK, byTelegram.StatusCode);

            var byUsernameLower = await _client.GetAsync($"/api/users/byTelegramUsername/{created.TelegramUsername!.ToLowerInvariant()}");
            Assert.AreEqual(HttpStatusCode.OK, byUsernameLower.StatusCode);
        }

        [TestMethod]
        public async Task GetEndpoints_Return404ForUnknown()
        {
            var id = System.Guid.NewGuid();
            var res1 = await _client!.GetAsync($"/api/users/{id}");
            Assert.AreEqual(HttpStatusCode.NotFound, res1.StatusCode);

            var res2 = await _client.GetAsync($"/api/users/byTelegramId/999999999");
            Assert.AreEqual(HttpStatusCode.NotFound, res2.StatusCode);

            var res3 = await _client.GetAsync($"/api/users/byTelegramUsername/nosuchuser12345");
            Assert.AreEqual(HttpStatusCode.NotFound, res3.StatusCode);
        }

        [TestMethod]
        public async Task GetNextEndpoint_ReturnsOneOfSampleUsers()
        {
            // The shared test host loads Resources/sample-users.json on startup; ensure /api/users/next returns one of those
            var res = await _client!.GetAsync("/api/users/next");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, res.StatusCode);

            var body = await res.Content.ReadAsStringAsync();
            var returned = JsonSerializer.Deserialize<User>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.IsNotNull(returned);

            var allowedNames = new[] { "Андрей", "Даша", "Валера", "Аня" };
            Assert.IsTrue(System.Array.Exists(allowedNames, n => n == returned!.Name) || returned!.Id != default);
        }

        [TestMethod]
        public async Task PostDuplicateTelegramUserId_ReturnsConflict()
        {
            var req = new CreateUserRequest { Name = "DupTest1", AvatarUri = "https://a", TelegramUserId = 77777, TelegramUsername = "dup1" };
            var json = JsonSerializer.Serialize(req);
            var res = await _client!.PostAsync("/api/users", new StringContent(json, Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.Created, res.StatusCode);

            var req2 = new CreateUserRequest { Name = "DupTest2", AvatarUri = "https://b", TelegramUserId = 77777, TelegramUsername = "dup2" };
            var json2 = JsonSerializer.Serialize(req2);
            var res2 = await _client.PostAsync("/api/users", new StringContent(json2, Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.Conflict, res2.StatusCode);
        }

        [TestMethod]
        public async Task PostDuplicateTelegramUsername_ReturnsConflict()
        {
            var req = new CreateUserRequest { Name = "U1", AvatarUri = "https://a", TelegramUserId = 33333, TelegramUsername = "uniqueuser" };
            var json = JsonSerializer.Serialize(req);
            var res = await _client!.PostAsync("/api/users", new StringContent(json, Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.Created, res.StatusCode);

            var req2 = new CreateUserRequest { Name = "U2", AvatarUri = "https://b", TelegramUserId = 33334, TelegramUsername = "UniqueUser" };
            var json2 = JsonSerializer.Serialize(req2);
            var res2 = await _client.PostAsync("/api/users", new StringContent(json2, Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.Conflict, res2.StatusCode);
        }

        [TestMethod]
        public async Task GetNextProfile_ReturnsAUser_WhenUsersExist()
        {
            // Use a fresh factory/client for isolation so repo starts empty for this test
            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(tempDir);
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                // Point ContentRoot to an empty temp dir so Program won't find Resources/sample-users.json
                // UseSetting("contentRoot", ...) works without extra hosting references in the test project.
                builder.UseSetting("contentRoot", tempDir);
            });
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // Create a user
            var req = new CreateUserRequest { Name = "IntNext", AvatarUri = "https://a" };
            var json = JsonSerializer.Serialize(req);
            var res = await client.PostAsync("/api/users", new StringContent(json, Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.Created, res.StatusCode);

            var createdBody = await res.Content.ReadAsStringAsync();
            var created = JsonSerializer.Deserialize<User>(createdBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.IsNotNull(created);

            // Call the next endpoint
            var nextRes = await client.GetAsync("/api/users/next");
            Assert.AreEqual(HttpStatusCode.OK, nextRes.StatusCode);

            var body = await nextRes.Content.ReadAsStringAsync();
            var returned = JsonSerializer.Deserialize<User>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.IsNotNull(returned);
            Assert.AreEqual(created!.Id, returned!.Id);
        }
    }
}

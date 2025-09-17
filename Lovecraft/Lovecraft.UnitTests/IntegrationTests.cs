using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using Lovecraft.WebAPI;
using System.Net.Http;
using Lovecraft.TelegramBot;
using System.Threading;
using Telegram.Bot.Types;

namespace Lovecraft.UnitTests
{
    [TestClass]
    public class IntegrationTests
    {
        [TestMethod]
        public async Task WeatherEndpoint_ReturnsData_And_BotHandlerCanUseIt()
        {
            await using var factory = new WebApplicationFactory<Program>();

            var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new System.Uri("https://localhost:5001")
            });

            // direct call to WebAPI
            var res = await client.GetAsync("/WeatherForecast");
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync();
            Assert.IsTrue(json.Length > 10);

            // Now use ApiClient with the factory client and BotMessageHandler
            var apiClient = new Lovecraft.Common.LovecraftApiClient(client);
            var fakeSender = new FakeSender();
            var handler = new BotMessageHandler(fakeSender, apiClient);

            var msg = new Message
            {
                Chat = new Telegram.Bot.Types.Chat { Id = 9999 },
                From = new Telegram.Bot.Types.User { Id = 2, Username = "inttest" },
                Text = "/start"
            };

            await handler.HandleMessageAsync(msg, CancellationToken.None);

            // expect the fake sender to have captured messages
            Assert.AreEqual(9999, fakeSender.LastChatId);
            Assert.IsTrue(fakeSender.LastText.Length > 0);
        }
    }
}

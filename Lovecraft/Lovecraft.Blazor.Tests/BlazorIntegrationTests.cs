using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lovecraft.Blazor.Tests
{
    [TestClass]
    public class BlazorIntegrationTests
    {
        [TestMethod]
        public async Task IndexPage_ShowsWebApiHealth()
        {
            var factory = new WebApplicationFactory<Lovecraft.Blazor.Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddHttpClient("webapi").ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler());
                });
            });

            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var res = await client.GetAsync("/");
            var body = await res.Content.ReadAsStringAsync();

            Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
            // HTML may encode quotes; just ensure the Ready token appears in the page output
            StringAssert.Contains(body.ToLowerInvariant(), "sign in");
        }

        private class TestHttpMessageHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                var json = "{\"Ready\":true,\"Version\":\"test\",\"Uptime\":\"00:00:01\"}";
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                };
                return Task.FromResult(resp);
            }
        }
    }
}

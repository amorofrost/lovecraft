using Microsoft.VisualStudio.TestTools.UnitTesting;
using Lovecraft.WebAPI.Controllers;
using System.Linq;

namespace Lovecraft.UnitTests
{
    [TestClass]
    public class WeatherForecastControllerTests
    {
        [TestMethod]
        public void Get_ReturnsFiveItems()
        {
            var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<WeatherForecastController>();
            var controller = new WeatherForecastController(logger);

            var result = controller.Get().ToArray();

            Assert.IsNotNull(result);
            Assert.AreEqual(5, result.Length);
            Assert.IsTrue(result.All(r => r.TemperatureC >= -50 && r.TemperatureC <= 100));
        }
    }
}

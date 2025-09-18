using Microsoft.VisualStudio.TestTools.UnitTesting;
using Lovecraft.WebAPI.Controllers;

namespace Lovecraft.UnitTests
{
    [TestClass]
    public class HealthControllerTests
    {
        [TestMethod]
        public void Get_ReturnsTrue()
        {
            var controller = new HealthController();
            var result = controller.Get();
            Assert.IsNotNull(result.Value);
            Assert.IsTrue(result.Value.Ready);
        }
    }
}

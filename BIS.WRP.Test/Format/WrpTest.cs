using Xunit;
using BIS.WRP;

namespace BIS.WRP.Test
{
    public class WrpTest
    {
        [Fact]
        public void Wrp_CanInitialize()
        {
            var wrp = new AnyWrp();
            Assert.NotNull(wrp);
        }
    }
}

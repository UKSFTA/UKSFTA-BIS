using System;
using System.IO;
using Xunit;
using BIS.PBO;

namespace BIS.PBO.Test.Format
{
    public class PboTest
    {
        [Fact]
        public void Pbo_CanInitialize()
        {
            // Simple structure test
            var pbo = new PBO();
            Assert.NotNull(pbo);
        }
    }
}

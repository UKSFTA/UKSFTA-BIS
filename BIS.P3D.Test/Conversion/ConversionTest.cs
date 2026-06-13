using System;
using Xunit;
using BIS.P3D.Conversion;

namespace BIS.P3D.Test.Conversion
{
    public class ConversionTest
    {
        [Fact]
        public void ODOL2MLOD_NullOdol_ThrowsNullReferenceException()
        {
            var ex = Record.Exception(() => ODOL2MLOD.Convert(null));
            Assert.NotNull(ex);
            Assert.IsType<NullReferenceException>(ex);
        }

        [Fact]
        public void ODOL2MLOD_NoLods_ThrowsNullReferenceException()
        {
            var odol = new BIS.P3D.ODOL.ODOL();
            var ex = Record.Exception(() => ODOL2MLOD.Convert(odol));
            Assert.NotNull(ex);
            Assert.IsType<NullReferenceException>(ex);
        }

        [Fact]
        public void ODOL2MLOD_StaticClass_Exists()
        {
            Assert.NotNull(typeof(ODOL2MLOD));
        }
    }
}

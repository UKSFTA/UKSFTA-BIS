using Xunit;
using BIS.PAA;

namespace BIS.PAA.Test.Format
{
    public class PaaTest
    {
        [Fact]
        public void ARGB16ToARGB32_ShouldConvertCorrectly()
        {
            // Sample ARGB16: 0xFFFF (Full white, 0xF alpha, 0xF red, 0xF green, 0xF blue)
            // Expect ARGB32: 255, 255, 255, 255
            byte[] src = new byte[] { 0xFF, 0xFF }; 
            
            var result = PixelFormatConversion.ARGB16ToARGB32(src);
            
            Assert.Equal(4, result.Length);
            Assert.Equal(255, result[0]); // B
            Assert.Equal(255, result[1]); // G
            Assert.Equal(255, result[2]); // R
            Assert.Equal(255, result[3]); // A
        }
    }
}

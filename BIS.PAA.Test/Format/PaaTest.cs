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
        [Fact]
        public void ARGB1555ToARGB32_ShouldConvertCorrectly()
        {
            // Sample ARGB1555: 0xFFFF (1 bit alpha, 5 bits each for R, G, B)
            // Expect ARGB32: 255, 255, 255, 255
            byte[] src = new byte[] { 0xFF, 0xFF };

            var result = PixelFormatConversion.ARGB1555ToARGB32(src);

            Assert.Equal(4, result.Length);
            Assert.Equal(255, result[0]); // B
            Assert.Equal(255, result[1]); // G
            Assert.Equal(255, result[2]); // R
            Assert.Equal(255, result[3]); // A
        }

        [Fact]
        public void AI88ToARGB32_ShouldConvertCorrectly()
        {
            // Sample AI88: 0x80 Grey, 0xFF Alpha
            byte[] src = new byte[] { 0x80, 0xFF };

            var result = PixelFormatConversion.AI88ToARGB32(src);

            Assert.Equal(4, result.Length);
            Assert.Equal(128, result[0]); // B (Grey)
            Assert.Equal(128, result[1]); // G (Grey)
            Assert.Equal(128, result[2]); // R (Grey)
            Assert.Equal(255, result[3]); // A (Alpha)
        }
    }
}

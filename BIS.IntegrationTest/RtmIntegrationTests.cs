using Xunit;

namespace BIS.IntegrationTest;

public class RtmIntegrationTests
{
    [Fact]
    public void Read_ValidRtm_Succeeds()
    {
        if (!TestData.CheckAvailable("rtm", "Install Arma 3 Samples on Steam.")) return;

        var files = TestData.GetFiles("rtm", "*.rtm");
        foreach (var file in files)
        {
            var rtm = new global::BIS.RTM.RTM(file);
            Assert.NotNull(rtm);
        }
    }

    [Fact]
    public void Read_NonExistentFile_Throws()
    {
        Assert.Throws<System.IO.FileNotFoundException>(() =>
            new global::BIS.RTM.RTM("/tmp/__nonexistent_test_file__.rtm"));
    }

    [Fact]
    public void Read_MultipleRtm_Succeed()
    {
        var files = TestData.GetFiles("rtm", "*.rtm");
        if (files.Length == 0) return;

        foreach (var file in files)
        {
            var rtm = new global::BIS.RTM.RTM(file);
            Assert.NotNull(rtm);
        }
    }
}

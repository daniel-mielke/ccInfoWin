using CCInfoWindows.Helpers;

namespace CCInfoWindows.Tests.Helpers;

public class RegistryHelperTests
{
    [Fact]
    public void SetAutostart_True_ThenGetAutostart_ReturnsTrue()
    {
        try
        {
            RegistryHelper.SetAutostart(true);

            Assert.True(RegistryHelper.GetAutostart());
        }
        finally
        {
            RegistryHelper.SetAutostart(false);
        }
    }

    [Fact]
    public void SetAutostart_False_ThenGetAutostart_ReturnsFalse()
    {
        RegistryHelper.SetAutostart(true);
        try
        {
            RegistryHelper.SetAutostart(false);

            Assert.False(RegistryHelper.GetAutostart());
        }
        finally
        {
            RegistryHelper.SetAutostart(false);
        }
    }
}

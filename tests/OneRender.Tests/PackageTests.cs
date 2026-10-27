using OneRender.TestSupport;

namespace OneRender.Tests;

public class PackageTests
{
    [Fact]
    public void EveryPackageStubIsReachable()
    {
        Assert.Equal("OneRender", EngineInfo.Name);
        Assert.Equal("OneRender.Image", Image.ImageBackendInfo.Name);
        Assert.Equal("OneRender.Accessibility", Accessibility.AccessibilityInfo.Name);
        Assert.Equal("OneRender.Pdf", Pdf.PdfBackendInfo.Name);
        Assert.Equal("OneRender.Fonts", Fonts.FontsInfo.Name);
        Assert.Equal("OneRender.Razor", Razor.RazorInfo.Name);
    }

    [Fact]
    public void AllocationHelperSeesAllocationAndNoAllocation()
    {
        int[] sink = new int[1];
        Action quiet = () => sink[0]++;
        Action noisy = () => sink = new int[64];

        Allocation.AssertZero(quiet, "increment");
        Assert.True(Allocation.Measure(noisy, iterations: 4) >= 4 * 64 * sizeof(int));
        Assert.Throws<InvalidOperationException>(() => Allocation.AssertZero(noisy, "array"));
    }
}

using FluentAssertions;
using PDFAgent.Core.Configuration;
using Xunit;

namespace PDFAgent.Core.Tests.Configuration;

public class AppConfigurationTests
{
    [Fact]
    public void Default_ShouldHaveSaneValues()
    {
        var config = new AppConfiguration();
        config.PdfEngine.MaxCachedPages.Should().Be(50);
        config.PdfEngine.RenderThreads.Should().Be(4);
        config.Ocr.DefaultLanguage.Should().Be("eng");
        config.Ui.Theme.Should().Be("FluentDark");
        config.Privacy.LocalProcessingOnly.Should().BeTrue();
        config.Cloud.EnableCloudFeatures.Should().BeFalse();
        config.Scripting.EnableScripting.Should().BeTrue();
    }

    [Fact]
    public void Load_WhenFileMissing_ShouldReturnDefaults()
    {
        var config = AppConfiguration.Load("/nonexistent/config.json");
        config.Should().NotBeNull();
        config.PdfEngine.DefaultDpi.Should().Be(150);
    }

    [Fact]
    public void RoundTrip_ShouldPreserveValues()
    {
        var path = Path.GetTempFileName();
        try
        {
            var original = new AppConfiguration
            {
                PdfEngine = new PdfEngineConfig { DefaultDpi = 200, MaxCachedPages = 100 },
                Ocr = new OcrConfig { DefaultLanguage = "fra" },
                Privacy = new PrivacyConfig { LocalProcessingOnly = true },
            };

            original.Save(path);
            var loaded = AppConfiguration.Load(path);

            loaded.PdfEngine.DefaultDpi.Should().Be(200);
            loaded.PdfEngine.MaxCachedPages.Should().Be(100);
            loaded.Ocr.DefaultLanguage.Should().Be("fra");
            loaded.Privacy.LocalProcessingOnly.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

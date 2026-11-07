using GosTek.Services;
using Xunit;

namespace GosTek.Tests;

public class ExportServiceTests
{
    [Theory]
    [InlineData("../evil.docx", "fallback", "evil")]
    [InlineData("../../evil.docx", "fallback", "evil")]
    [InlineData("..\\..\\evil.docx", "fallback", "evil")]
    [InlineData("..", "fallback", "fallback")]
    [InlineData("../..", "fallback", "fallback")]
    [InlineData("....", "fallback", "fallback")]
    [InlineData("folder/sub/doc.docx", "fallback", "doc")]
    [InlineData("normal_name.docx", "fallback", "normal_name")]
    [InlineData("normal_name", "fallback", "normal_name")]
    [InlineData("   ", "fallback", "fallback")]
    [InlineData(null, "fallback", "fallback")]
    public void SafeFileName_PreventsPathTraversal(string? input, string fallback, string expected)
    {
        var result = ExportService.SafeFileName(input, fallback);
        Assert.Equal(expected, result);
        Assert.DoesNotContain("..", result);
        Assert.DoesNotContain("/", result);
        Assert.DoesNotContain("\\", result);
    }
}

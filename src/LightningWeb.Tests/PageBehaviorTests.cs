using System.Text.RegularExpressions;
using Jint;

public class PageBehaviorTests : IClassFixture<LightningWebFactory>
{
    private readonly LightningWebFactory _factory;

    public PageBehaviorTests(LightningWebFactory factory) => _factory = factory;

    [Fact]
    public async Task Served_script_polls_every_10_seconds_without_reloading()
    {
        var script = await _factory.CreateClient().GetStringAsync("/js/app.js");
        Assert.Contains("10000", script);
        Assert.Contains("/api/strikes", script);
        Assert.Contains("setInterval", script);
        Assert.DoesNotContain("location.reload", script);
    }

    [Fact]
    public async Task Page_script_keeps_strikes_on_failure_pauses_the_timer_and_labels_markers()
    {
        var engine = new Engine();
        engine.Execute(await _factory.CreateClient().GetStringAsync("/js/app.js"));
        var report = engine.Evaluate(Scenario).AsString();
        var lines = report.Split("~~~");

        Assert.Equal("1", lines[0]);
        Assert.Equal("1", lines[1]);
        Assert.Equal("2", lines[2]);
        Assert.Equal("Resume updates", lines[3]);
        Assert.Equal("Pause updates", lines[4]);
        Assert.Contains("7", lines[5]);
        Assert.Contains("51.5074", lines[5]);
        Assert.Contains("-0.1278", lines[5]);
        Assert.Contains("10", lines[5]);
        Assert.Contains("Strike updates are unavailable", lines[6]);
        Assert.Contains("51.5074", lines[7]);
        Assert.Equal("2", lines[8]);
        Assert.Contains("a", lines[9]);
        Assert.Equal("No current strikes", lines[10]);
        Assert.Equal("", lines[11]);
        Assert.Contains("a:7:51.5074:-0.1278:12", lines[12]);
        Assert.Contains("b:10:1:2:12", lines[12]);
        Assert.Equal("No current strikes", engine.Evaluate("renderStrikeListText([])").AsString());
    }

    [Fact]
    public async Task Served_css_uses_flexbox_meets_contrast_and_sizes_controls()
    {
        var css = await _factory.CreateClient().GetStringAsync("/css/site.css");
        Assert.Contains("display: flex", css);
        Assert.DoesNotMatch(new Regex(@"display\s*:\s*grid"), css);
        Assert.Matches(new Regex(@"@media\s*\(\s*min-width:\s*768px\s*\)"), css);
        Assert.Contains("flex: 1 1 auto", css);
        Assert.Contains("min-height: 0", css);
        Assert.Contains("min-height: 100dvh", css);
        Assert.Contains("margin: 0", css);
        Assert.Contains("width: 20rem", css);
        Assert.Contains("min-width: 44px", css);
        Assert.Contains("min-height: 44px", css);
        Assert.Contains(":focus-visible", css);
        Assert.Contains("prefers-reduced-motion", css);

        var colors = CssColors(css);
        AssertContrast(colors, "--color-text", "--color-page-bg", 4.5);
        AssertContrast(colors, "--color-heading", "--color-page-bg", 4.5);
        AssertContrast(colors, "--color-error", "--color-page-bg", 4.5);
        AssertContrast(colors, "--color-button-text", "--color-button-bg", 4.5);
        AssertContrast(colors, "--color-skip-text", "--color-skip-bg", 4.5);
    }

    private static void AssertContrast(IReadOnlyDictionary<string, string> colors, string foreground, string background, double minimum)
    {
        var ratio = Contrast(colors[foreground], colors[background]);
        Assert.True(ratio >= minimum, $"{foreground} on {background} is {ratio:F2}:1");
    }

    private static Dictionary<string, string> CssColors(string css)
    {
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(css, @"(--color-[a-z0-9-]+):\s*(#[0-9a-fA-F]{6})"))
        {
            colors[match.Groups[1].Value] = match.Groups[2].Value;
        }

        return colors;
    }

    private static double Contrast(string foreground, string background)
    {
        var left = Luminance(foreground);
        var right = Luminance(background);
        var lighter = Math.Max(left, right);
        var darker = Math.Min(left, right);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(string hex)
    {
        var color = hex.TrimStart('#');
        double Channel(int value)
        {
            var scaled = value / 255d;
            return scaled <= 0.04045 ? scaled / 12.92 : Math.Pow((scaled + 0.055) / 1.055, 2.4);
        }

        var red = Channel(Convert.ToInt32(color[..2], 16));
        var green = Channel(Convert.ToInt32(color.Substring(2, 2), 16));
        var blue = Channel(Convert.ToInt32(color.Substring(4, 2), 16));
        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    private const string Scenario = """
        (function () {
          var fetches = 0;
          var removed = [];
          var added = [];
          var rendered = "";
          var error = "";
          var controller = createController({
            fetchStrikes: function () {
              fetches = fetches + 1;
              return [];
            },
            createMarker: function (spec) {
              added.push(spec.id + ":" + spec.label + ":" + spec.latitude + ":" + spec.longitude + ":" + spec.scale);
              return {
                setMap: function (value) {
                  if (value === null) {
                    removed.push(spec.id);
                  }
                }
              };
            },
            render: function (state) {
              rendered = renderStrikeListText(state.strikes);
              error = state.error;
            }
          });
          controller.onTimer();
          var fetchesAfterRun = fetches;
          controller.pause();
          var pausedName = controller.buttonName();
          controller.onTimer();
          var fetchesWhilePaused = fetches;
          controller.resume();
          var resumedName = controller.buttonName();
          controller.onTimer();
          var fetchesAfterResume = fetches;
          controller.onSuccess([
            { id: "a", latitude: 51.5074, longitude: -0.1278, intensity: 7 },
            { id: "b", latitude: 1, longitude: 2, intensity: 10 }
          ]);
          var textWithStrikes = rendered;
          controller.onFailure();
          var errorText = error;
          var textAfterFailure = rendered;
          var markerCountAfterFailure = controller.getMarkers().length;
          controller.onSuccess([{ id: "b", latitude: 1, longitude: 2, intensity: 10 }]);
          var removedIds = removed.join(",");
          controller.onSuccess([]);
          var emptyText = rendered;
          var errorAfterSuccess = error;
          return [
            fetchesAfterRun,
            fetchesWhilePaused,
            fetchesAfterResume,
            pausedName,
            resumedName,
            textWithStrikes,
            errorText,
            textAfterFailure,
            markerCountAfterFailure,
            removedIds,
            emptyText,
            errorAfterSuccess,
            added.join("|")
          ].join("~~~");
        })()
        """;
}

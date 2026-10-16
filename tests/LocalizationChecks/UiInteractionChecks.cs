using System.Numerics;
using PvpStats.Interface;
using PvpStats.Localization;
using PvpStats.Settings;

internal static class UiInteractionChecks {
    internal static void Run(Action<bool,string> check) {
        for(var i = 0; i < ThemeConfiguration.PresetNames.Length; i++) {
            var p = ThemeConfiguration.Preset(i);
            bool accessible = true;
            for(var hover = 0; hover <= 10; hover++) for(var pressed = 0; pressed <= 10; pressed++) {
                foreach(var normal in new[] { p.Button, p.Selection, p.Panel }) {
                    var bg = Vector4.Lerp(Vector4.Lerp(normal, p.ButtonHovered, hover / 10f), p.ButtonPressed, pressed / 10f);
                    accessible &= ThemeContrast.Ratio(bg, ThemeContrast.Foreground(bg, p.Text)) >= 4.5;
                }
            }
            check(accessible, "all animated button/selection states meet 4.5:1 text contrast in " + ThemeConfiguration.PresetNames[i]);
            check(new[] { p.Button, p.ButtonHovered, p.ButtonPressed }.All(bg => ThemeContrast.Ratio(ThemeContrast.BackgroundForText(bg,p.Text),p.Text) >= 4.5), "native combo-arrow button colors retain readable text in " + ThemeConfiguration.PresetNames[i]);
        }
        float Run(int fps) { var value = 0f; for(int i=0;i<fps;i++) value = ThemeContrast.Ease(value,1,1f/fps,true); return value; }
        check(Math.Abs(Run(30)-Run(144)) < .0001, "animation timing is independent of refresh rate");
        check(ThemeContrast.Ease(.3f,1,.016f,false) == 1 && ThemeContrast.Ease(.8f,0,.016f,false) == 0, "reduced motion reaches hover and idle states immediately");
        check(ThemeContrast.Ease(0,1,0,true) == 0 && ThemeContrast.Ease(.5f,1,5,true) <= 1, "animation does not overshoot after a long frame");
        var custom = new ThemeConfiguration { EnableAnimations = false, ButtonHovered = new(.2f,.8f,.5f,1) };
        var copy = custom.Copy(); custom.EnableAnimations = true;
        check(!copy.EnableAnimations && copy.ButtonHovered != new ThemeConfiguration().ButtonHovered, "saving/reverting preserves motion preference and custom interaction colors");
        foreach(var key in new[] { "Combine related actions", "Min. matches", "Search for players and actions...", "Status Success Rate", "Highest team kill advantage", "Median Damage Dealt Contrib.", "Total MP Gain", "Action timing", "Spectated Matches" })
            check(ChineseText.Text(key) != key, "secondary UI translation exists: " + key);
        var label = ChineseText.Label("Select All##stable-filter-id");
        check(label.Contains("全选") && label.Contains("Select All##stable-filter-id"), "newly translated controls retain original stable ImGui IDs");
    }
}

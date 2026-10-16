using PvpStats.Leaderboards;

// Excerpts captured from public official pages on 2026-10-16; sources.json records their URLs.
static class OfficialMarkupChecks {
    public static void Run(Action<bool,string> check) {
        string Read(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", file));
        var jp = LodestoneParser.Parse(Read("lodestone-jp.html"), LodestoneParser.Root(2), DateTimeOffset.UtcNow);
        check(jp.OfficialDate == new DateOnly(2026,10,16) && jp.Scope == "Elemental", "real JP page exposes summarytime and selected physical ranking scope");
        check(jp.Season == "SEASON21" && !jp.Headings.Any(x => x.Contains("Rein Asshe")), "season metadata comes from image alt and excludes player names");
        check(jp.Entries.Length == 2 && jp.Entries[0].CharacterId == "17170082" && jp.Entries[0].CharacterName == "Rein Asshe", "real data-href rows preserve complete names and stable IDs");
        check(jp.Entries[0].World == "Ultima [Gaia]" && jp.Entries[0].Tier == "アルテマ", "real world data and image-only tier are parsed separately from names");
        check(jp.Entries[0].Rank == "1" && jp.Entries[0].PreviousRank == "2" && jp.Entries[0].RankChange == "+1", "movement is computed from previous rank rather than mistaking it for a delta");
        check(jp.Entries[0].Rating == "5362" && jp.Entries[0].RatingChange == "+ 57" && jp.Entries[0].Victories == "248" && jp.Entries[0].VictoriesChange == "+ 9", "real points and wins are separated from their day-over-day changes");
        check(jp.Entries[0].PortraitUrl.Contains("img2.finalfantasyxiv.com") && jp.Entries[0].ImageUrls.Length >= 3, "real portraits, tier images and movement images are retained");
        check(jp.Scopes.Order().SequenceEqual(new[]{"Elemental","Light","Materia","Primal"}) && jp.NextPages.Any(u=>u.Contains("page=2")), "physical-region options and scroll loader are discovered from real markup");
        var fragmentUrl = LodestoneParser.PageUrl(LodestoneParser.ScopeUrl(LodestoneParser.Root(2), "Elemental"),2);
        var p2 = LodestoneParser.Parse(Read("lodestone-jp-page2.html"), fragmentUrl, DateTimeOffset.UtcNow);
        check(p2.IsFragment && p2.OfficialDate == null && p2.Entries[0].Rank == "51" && p2.Entries[0].CharacterName == "Ame Ame", "real scroll responses are HTML fragments with ranks 51 onward and no date header");
        check(p2.NextPages.Any(u=>u.Contains("page=3")), "fragment pagination continues despite the absence of ordinary next links");
        var p6 = LodestoneParser.Parse(Read("lodestone-jp-page6.html"), LodestoneParser.PageUrl(fragmentUrl,6), DateTimeOffset.UtcNow);
        check(p6.Entries[0].Rank == "251" && p6.NextPages.Any(u=>u.Contains("page=7")), "last full batch is followed by a terminal-page probe");
        var empty = LodestoneParser.Parse(Read("lodestone-jp-page7.html"), LodestoneParser.PageUrl(fragmentUrl,7), DateTimeOffset.UtcNow);
        check(empty.ExplicitEmpty && empty.IsFragment && empty.Entries.Length==0 && empty.NextPages.Length==0, "empty official scroll response ends pagination");
        bool blankIndexRejected=false;
        try{LodestoneParser.Parse("",LodestoneParser.Root(2),DateTimeOffset.UtcNow);}catch(FormatException){blankIndexRejected=true;}
        check(blankIndexRejected, "an empty index cannot be accepted as a valid ranking");
        var na=LodestoneParser.Parse(Read("lodestone-na.html"),LodestoneParser.Root(0),DateTimeOffset.UtcNow);
        var eu=LodestoneParser.Parse(Read("lodestone-eu.html"),LodestoneParser.Root(1),DateTimeOffset.UtcNow);
        check(na.OfficialDate==new DateOnly(2026,10,15) && eu.OfficialDate==new DateOnly(2026,10,16), "real English sites retain different official dates caused by local display zones");
        check(na.Scope=="Primal" && na.Entries[0].CharacterName=="Miyu Moon" && eu.Scope=="Light" && eu.Entries[0].CharacterName=="Cliffe Zanza", "real NA and EU scopes and names parse correctly");
        var oc=LodestoneParser.Parse(Read("lodestone-oc.html"),LodestoneParser.ScopeUrl(LodestoneParser.Root(0),"Materia"),DateTimeOffset.UtcNow);
        check(oc.Scope=="Materia" && oc.Entries[0].CharacterName=="Ash Kitamura" && oc.Entries[0].World=="Ravana [Materia]", "real Oceania board does not contain North American default rows");
        check(oc.Entries[0].Tier=="Diamond" && oc.Entries[0].Division=="5" && oc.Entries[0].Rating=="★☆☆" && oc.Entries[0].RatingChange=="", "lower-tier division and stars are preserved without treating stars as point changes");
        check(LodestoneParser.CanonicalTier("アルテマ")=="Ultima" && LodestoneParser.CanonicalTier("オメガ")=="Omega", "JP tier labels map to previously approved translation keys");
    }
}

using System.Net;
using System.Net.Http;
using PvpStats.Leaderboards;

static class ScrollCacheChecks {
    public static async Task Run(Action<bool,string> check) {
        var fixtures=Path.Combine(AppContext.BaseDirectory,"Fixtures");
        var index=File.ReadAllText(Path.Combine(fixtures,"lodestone-jp.html"));
        var fragment=File.ReadAllText(Path.Combine(fixtures,"lodestone-jp-page2.html"));
        var dir=Path.Combine(Path.GetTempPath(),"ccstats-scroll-replay-"+Guid.NewGuid().ToString("N"));
        try {
            var normal=new Handler(url=>LodestoneParser.Query(url).GetValueOrDefault("page") switch {"2"=>fragment,"3"=>"",_=>index});
            using(var service=new LeaderboardService(dir,normal,TimeSpan.Zero)) {
                await service.CheckDateAsync(RankingRegion.All[0]);
                check(service.State.Snapshot?.Entries.Length==4 && normal.Requests==4,"real scroll structure is crawled through an empty terminator and its dated index is rechecked before saving");
                check(service.State.Snapshot!.Scopes.SequenceEqual(new[]{"Elemental"}),"one physical-region board is saved rather than crawling other server choices");
            }
            var indexReads=0;
            using(var rollover=new LeaderboardService(dir,new Handler(url=>{
                var page=LodestoneParser.Query(url).GetValueOrDefault("page");
                if(page=="2")return fragment;
                if(page=="3")return "";
                return index.Replace("2026/10/16",++indexReads==1?"2026/10/17":"2026/10/18");
            }),TimeSpan.Zero)) {
                await rollover.Ready;var before=rollover.State.Snapshot;
                await rollover.CheckDateAsync(RankingRegion.All[0]);
                check(ReferenceEquals(before,rollover.State.Snapshot)&&rollover.State.Message.Contains("日期或赛季发生变化"),"undated scroll fragments cannot overwrite cache if the official date changes during the crawl");
            }
        }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
    sealed class Handler(Func<Uri,string> body):HttpMessageHandler{
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Requests++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body(request.RequestUri!),System.Text.Encoding.UTF8,"text/html")});}
    }
}

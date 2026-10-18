namespace CCStats.Core.Library;
public sealed class PlayerLinks {
    public Dictionary<string,string> Aliases {get;set;}=new(StringComparer.OrdinalIgnoreCase);
    public string Canonical(string name) {
        var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var current=name;
        while(Aliases.TryGetValue(current,out var next)&&visited.Add(current))current=next;
        return current;
    }
    public void Link(string alias,string main) {
        alias=alias.Trim();main=main.Trim();if(alias.Length==0||main.Length==0||alias.Equals(main,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("请输入不同的完整角色名。");
        if(Canonical(main).Equals(alias,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("角色关联不能构成循环。");
        Aliases[alias]=main;
    }
}

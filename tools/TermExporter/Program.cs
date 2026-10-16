using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json;
var root = args[0];
var rows = new List<object>();
var enums = new List<object>();
int errors = 0;
foreach(var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).Where(x=>!x.Contains("/obj/")&&!x.Contains("/bin/"))) {
 var text = File.ReadAllText(file);
 foreach(var symbols in new[]{Array.Empty<string>(),new[]{"DEBUG"}}) {
  var tree = CSharpSyntaxTree.ParseText(text,new CSharpParseOptions(preprocessorSymbols:symbols));
  foreach(var d in tree.GetDiagnostics().Where(x=>x.Severity==DiagnosticSeverity.Error)) { Console.Error.WriteLine($"{file}: {d}"); errors++; }
 }
 var syntax = CSharpSyntaxTree.ParseText(text,new CSharpParseOptions(preprocessorSymbols:new[]{"DEBUG"})).GetRoot();
 foreach(var node in syntax.DescendantNodes()) {
  if(node is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression) || node is InterpolatedStringExpressionSyntax) {
   var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
   var member = node.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault();
   var parent = node.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault()?.Expression.ToString() ?? member?.Kind().ToString() ?? "";
   var value = node is LiteralExpressionSyntax lit ? lit.Token.ValueText : node.ToString();
   rows.Add(new {file=Path.GetRelativePath(root,file).Replace('\\','/'),line,value,raw=node.ToString(),context=parent,member=member is MethodDeclarationSyntax m ? m.Identifier.ValueText : "",interpolated=node is InterpolatedStringExpressionSyntax});
  }
  if(node is EnumDeclarationSyntax en) {
   foreach(var value in en.Members) enums.Add(new {file=Path.GetRelativePath(root,file).Replace('\\','/'),line=value.GetLocation().GetLineSpan().StartLinePosition.Line+1,type=en.Identifier.ValueText,name=value.Identifier.ValueText,assigned=value.EqualsValue?.Value.ToString()});
  }
 }
}
File.WriteAllText(args[1],JsonSerializer.Serialize(new{strings=rows,enums},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Exported {rows.Count} strings and {enums.Count} enum members; syntax errors: {errors}");
return errors == 0 ? 0 : 1;

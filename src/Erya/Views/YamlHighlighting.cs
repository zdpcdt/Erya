using System.IO;
using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace Erya.Views;

/// <summary>
/// YAML 语法高亮（AvaloniaEdit 无内置 YAML 定义，这里按 xshd 手写）。
/// 明暗各一套（取 VS Code Light+/Dark+ 配色），由页面按主题变体切换。
/// </summary>
public static class YamlHighlighting
{
    public static IHighlightingDefinition Light { get; } = Load("""
        <SyntaxDefinition xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008" name="Yaml" extensions=".yaml;.yml">
            <Color name="Comment" foreground="#FF008000" />
            <Color name="Key" foreground="#FF0451A5" />
            <Color name="String" foreground="#FFA31515" />
            <Color name="Literal" foreground="#FF0000FF" />
            <Color name="Number" foreground="#FF098658" />
            <Color name="Anchor" foreground="#FF795E26" />
            <Color name="Marker" foreground="#FF808080" />
            <RuleSet>
                <Rule color="Comment">\#.*</Rule>
                <Rule color="String">"[^"]*"</Rule>
                <Rule color="String">'[^']*'</Rule>
                <Rule color="Literal">\b(true|false|null|True|False|Null|yes|no|on|off)\b</Rule>
                <Rule color="Number">[-+]?[0-9]+(\.[0-9]+)?([eE][-+]?[0-9]+)?\b</Rule>
                <Rule color="Anchor">[&amp;*][A-Za-z0-9_.-]+</Rule>
                <Rule color="Marker">^(-{3}|\.{3})[ \t]*$</Rule>
                <Rule color="Key">^[ \t]*(?:-[ \t]+)*[^:#]+:(?=[ \t]|$)</Rule>
                <Rule color="Marker">^[ \t]*-(?=[ \t])</Rule>
            </RuleSet>
        </SyntaxDefinition>
        """);

    public static IHighlightingDefinition Dark { get; } = Load("""
        <SyntaxDefinition xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008" name="Yaml" extensions=".yaml;.yml">
            <Color name="Comment" foreground="#FF6A9955" />
            <Color name="Key" foreground="#FF9CDCFE" />
            <Color name="String" foreground="#FFCE9178" />
            <Color name="Literal" foreground="#FF569CD6" />
            <Color name="Number" foreground="#FFB5CEA8" />
            <Color name="Anchor" foreground="#FFDCDCAA" />
            <Color name="Marker" foreground="#FF808080" />
            <RuleSet>
                <Rule color="Comment">\#.*</Rule>
                <Rule color="String">"[^"]*"</Rule>
                <Rule color="String">'[^']*'</Rule>
                <Rule color="Literal">\b(true|false|null|True|False|Null|yes|no|on|off)\b</Rule>
                <Rule color="Number">[-+]?[0-9]+(\.[0-9]+)?([eE][-+]?[0-9]+)?\b</Rule>
                <Rule color="Anchor">[&amp;*][A-Za-z0-9_.-]+</Rule>
                <Rule color="Marker">^(-{3}|\.{3})[ \t]*$</Rule>
                <Rule color="Key">^[ \t]*(?:-[ \t]+)*[^:#]+:(?=[ \t]|$)</Rule>
                <Rule color="Marker">^[ \t]*-(?=[ \t])</Rule>
            </RuleSet>
        </SyntaxDefinition>
        """);

    private static IHighlightingDefinition Load(string xshd) =>
        HighlightingLoader.Load(XmlReader.Create(new StringReader(xshd)), HighlightingManager.Instance);
}

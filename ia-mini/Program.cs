using Statiq.App;
using Statiq.Web;
using Statiq.Markdown;
using Markdig;

await Bootstrapper
  .Factory.CreateWeb(args)
  .AddSetting(MarkdownKeys.MarkdownExtensions, MarkdownExtensions.UseAdvancedExtensions)
  // 또는 특정 확장만:
  //.AddSetting(MarkdownKeys.MarkdownExtensions, new[] { "PipeTables", "GridTables" })
  .RunAsync();
using Statiq.App;
using Statiq.Web;
using Statiq.Markdown;

await Bootstrapper.Factory.CreateWeb(args)
  .ModifyTemplate(
      MediaTypes.Markdown,
      x => ((RenderMarkdown)x).UseExtensions())
  // .ModifyTemplate(
  //     MediaTypes.Markdown,
  //     x => ((RenderMarkdown)x).UseExtensions("pipetables+gridtables"))
  .RunAsync();
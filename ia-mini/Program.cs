using Statiq.App;
using Statiq.Web;
using Statiq.Markdown;

await Bootstrapper.Factory.CreateWeb(args)
  // 확장 추가
  .ModifyTemplate(
      MediaTypes.Markdown,
      x => ((RenderMarkdown)x).UseExtensions())
  // 확장 개별 지정 필요 시
  // .ModifyTemplate(
  //     MediaTypes.Markdown,
  //     x => ((RenderMarkdown)x).UseExtensions("pipetables+gridtables"))
  .RunAsync();
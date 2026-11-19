using System.Runtime.CompilerServices;

// TvdbV4Client（以及各 provider 内部类型）是 internal —— 只对本仓库的测试程序集开放。
// csproj 里的 <InternalsVisibleTo> 项在这个 SDK 组合下没有被生成到程序集里，
// 直接写特性最可靠。
[assembly: InternalsVisibleTo("MediaBrowser.Tests")]

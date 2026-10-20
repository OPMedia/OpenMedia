using System.Runtime.CompilerServices;

// 观测点（P8 查询门 / 连接池计数器）需要被 MediaBrowser.Tests 读取做断言；
// 仓储公共 API 不为此加宽，仅开放给测试程序集。
[assembly: InternalsVisibleTo("MediaBrowser.Tests")]
# C09 前置：ServiceStack.Text → System.Text.Json 迁移地图

> 负责人：json-migration ｜ 基线：master @ 1d7c2ab4bf（Emby 3.5.3）｜ 对应计划：`docs/upgrade-net10-plan.md` §6 组件表（ServiceStack.Text 5.1.0 → System.Text.Json 内置）、§7 C09
> 证据来源：仓库源码行号引用 + ServiceStack.Text 5.1.0 DLL 反编译（ilspycmd，`packages/ServiceStack.Text.5.1.0/lib/net45/ServiceStack.Text.dll`）+ System.Text.Json .NET 10 实测（D:\tmp\stj-test 控制台程序，SDK 10.0.401）+ dashboard-ui 前端源码抽查 + 历史参考分支 `backup-migration-20261013-210711` 交叉验证。

---

## 0. 执行摘要

**迁移面比预期小得多**：全库只有 **2 个文件有真实 ServiceStack.Text API 调用**（IJsonSerializer 包装类 + ApplicationHost 的 1 处 JsvReader 查询参数解析），另有 3 处纯文本引用（日志字符串/注释）和 3 个 csproj 工程引用。其余 **100 个文件**全部通过 `MediaBrowser.Model.Serialization.IJsonSerializer` 抽象间接消费 JSON 序列化——**一行都不用改**。

**核心结论**：

1. **字段命名必须保持 PascalCase 原名**（`PropertyNamingPolicy = null`）。ServiceStack.Text 默认 `EmitCamelCaseNames=false` 按属性原名输出；dashboard-ui 前端有 106 处 `item.Type` 这类 PascalCase 访问。⚠️ 历史参考分支用了 `CamelCase`，与前端契约**冲突**（其前端未同步修改，属实际 bug），本设计予以纠正。
2. **Guid 是最大的隐性破坏点**：旧输出为 32 位无连字符十六进制（"N" 格式），且 `Guid.Empty` 属性整个省略；STJ 默认输出带连字符（"D" 格式），且**读取 "N" 格式字符串直接抛 JsonException**（.NET 10 实测）。必须自定义 GuidConverter。
3. **旧反序列化是"吞错"语义**（`ThrowOnDeserializationError=false`）：`null`/`""` 进 int 属性 → 静默默认值；STJ 严格模式直接抛异常。外部 API（TMDb 等会返回 `"runtime": null`）会当场炸掉 provider，需要 Lenient 容错转换器。

---

## 1. 使用点全量盘点

grep 范围：全仓库 `*.cs`，排除 `bin/ obj/ packages/ .git/ dashboard-ui/bower_components/`。

### 1.1 直接 API 调用点（C09 必须改，共 2 文件 17 行）

#### 文件 A：`Emby.Server.Implementations/Serialization/JsonSerializer.cs`（275 行）——唯一实现类

`IJsonSerializer` 接口定义在 `src/MediaBrowser.Model/MediaBrowser.Model.Serialization/IJsonSerializer.cs:7`（11 个方法：SerializeToStream/SerializeToFile/DeserializeFromFile×2/DeserializeFromStream×2/DeserializeFromStreamAsync×2/DeserializeFromString×2/SerializeToString）。本文件是它在全库的唯一实现（DI 注册点：`ApplicationHost.cs:788-790` `CreateJsonSerializer()`）。

| 行号 | 用法 | 类别 | 替换难度 |
|------|------|------|----------|
| 43 | `ServiceStack.Text.JsonSerializer.SerializeToStream(obj, obj.GetType(), stream)` | 序列化 API | 易 |
| 136 | `ServiceStack.Text.JsonSerializer.DeserializeFromStream<T>(stream)` | 反序列化 API | 易 |
| 150 | `ServiceStack.Text.JsonSerializer.DeserializeFromString<T>(json)`（async 路径内） | 反序列化 API | 易 |
| 168 | `ServiceStack.Text.JsonSerializer.DeserializeFromString<T>(text)` | 反序列化 API | 易 |
| 190 | `ServiceStack.Text.JsonSerializer.DeserializeFromStream(type, stream)` | 反序列化 API | 易 |
| 209 | `ServiceStack.Text.JsonSerializer.DeserializeFromString(json, type)`（async 路径内） | 反序列化 API | 易 |
| 218 | `JsConfig.DateHandler = DateHandler.ISO8601` | 全局配置 | 中（语义映射） |
| 219 | `JsConfig.ExcludeTypeInfo = true` | 全局配置 | 易（STJ 默认即无 __type） |
| 220 | `JsConfig.IncludeNullValues = false` | 全局配置 | 易（WhenWritingNull） |
| 221 | `JsConfig.AlwaysUseUtc = true` | 全局配置 | 中（需 DateTime 转换器） |
| 222 | `JsConfig.AssumeUtc = true` | 全局配置 | 中（需 DateTime 转换器） |
| 224 | `JsConfig<Guid>.SerializeFn = SerializeGuid` | 全局配置 | 中（需 Guid 转换器） |
| 227-235 | `SerializeGuid`：`Guid.Empty → null`，否则 `guid.ToString("N")` | 自定义序列化 | 中（并入 GuidConverter） |
| 256 | `ServiceStack.Text.JsonSerializer.DeserializeFromString(json, type)` | 反序列化 API | 易 |
| 272 | `ServiceStack.Text.JsonSerializer.SerializeToString(obj, obj.GetType())` | 序列化 API | 易 |

其余方法（`SerializeToFile` L52-68、`DeserializeFromFile` ×2 L83-120、`OpenFile` L70-74、参数空检查）不触碰 ServiceStack.Text，原样保留。

#### 文件 B：`Emby.Server.Implementations/ApplicationHost.cs`

| 行号 | 用法 | 类别 | 替换难度 |
|------|------|------|----------|
| 104 | `using ServiceStack;` | using | 易（删除） |
| 122 | `using ServiceStack.Text.Jsv;` | using | 易（删除） |
| 1081-1084 | `GetParseFn(Type)` → `JsvReader.GetParseFn(propertyType)(s)` | **非 JSON**：JSV 格式的字符串→对象解析，供查询参数绑定 | **难**（需自写解析器，见 §3.3） |

调用链：`ApplicationHost.cs:978` 把 `GetParseFn` 传给 `HttpListenerHost` 构造函数 → `HttpListenerHost.cs:831` 暴露为 `GetParseFn` → `ServiceController.cs:78` 传入 `RestPath` → `StringMapTypeDeserializer.cs:50` 用它把 URL 查询串/路径参数填充到请求 DTO 属性。**这是 REST 路由绑定的一部分，不是 JSON**，ServiceStack.Text 在这里被当作"字符串→任意类型"解析器使用。

### 1.2 纯文本引用（无需修改，共 3 处）

| 文件:行 | 内容 |
|---------|------|
| `Emby.Server.Implementations/HttpServer/HttpListenerHost.cs:786` | 日志字符串 `"Calling ServiceStack AppHost.Init"`（可顺手改为中性文案，无功能影响） |
| `MediaBrowser.Api/PluginService.cs:301` | 注释里的 code.google.com 源码链接 |
| `MediaBrowser.Api/ScheduledTasks/ScheduledTaskService.cs:238` | 同上注释链接 |

### 1.3 工程引用（C09 删除）

| 文件:行 | 内容 |
|---------|------|
| `Emby.Server.Implementations/Emby.Server.Implementations.csproj:626-628` | `<Reference Include="ServiceStack.Text, ...">` + HintPath `..\packages\ServiceStack.Text.5.1.0\lib\net45\ServiceStack.Text.dll` |
| `MediaBrowser.Server.Mono/MediaBrowser.Server.Mono.csproj:59-61` | 同上 |
| `MediaBrowser.ServerApplication/MediaBrowser.ServerApplication.csproj:82-84` | 同上 |
| `MediaBrowser.Server.Mono/packages.config` | `<package id="ServiceStack.Text" version="5.1.0" .../>` |
| `MediaBrowser.ServerApplication/packages.config` | 同上 |
| `packages/ServiceStack.Text.5.1.0/` 目录 | 整目录（C02 的 .gitignore 已覆盖 packages/，C09 只需保证不再被引用） |

注：`MediaBrowser.Server.Mono` 与 `MediaBrowser.ServerApplication` 两个项目**没有任何代码使用** ServiceStack.Text（grep 验证），引用仅为把 DLL 复制到输出目录（运行时 `Emby.Server.Implementations` 需要）。C08 合并入口后这两个引用自然消失。

### 1.4 间接消费点（不改，共 100 文件）

全部通过 `IJsonSerializer` 抽象消费（含接口定义与实现类本身共 102 个文件引用该名），接口签名不变 → **零改动**。按项目分布（前 12）：

| 项目/目录 | 文件数 | 典型用途 |
|-----------|--------|----------|
| MediaBrowser.Providers（Music/TV/Movies/MediaInfo/Omdb/People 等） | ~40 | TMDb/AudioDb/Omdb 外部 API JSON 反序列化 + 元数据缓存文件读写 |
| Emby.Server.Implementations（LiveTv/Session/Data/ScheduledTasks/HttpServer 等） | ~35 | API 响应序列化、WebSocket 消息、SQLite `data` 列水合、定时器/配置 JSON 持久化 |
| src/Emby.Server.MediaEncoding（Encoder/Api/Subtitles） | ~17 | 转码参数、probe 结果、字幕 JSON |
| MediaBrowser.Api | 4 | 响应构造（HttpResultFactory） |
| Emby.Dlna / Emby.Drawing / MediaBrowser.WebDashboard / src/MediaBrowser.Common / src/MediaBrowser.Controller / src/MediaBrowser.Model | 6 | DLNA profile JSON 往返、图片缓存索引、GitHub 更新检查、CollectionFolder |

关键消费路径（C09 后行为由新实现决定）：
- **API 响应**：`HttpListenerHost.cs:838` `SerializeToJson` → `_jsonSerializer.SerializeToStream(o, stream)`；`HttpResultFactory.cs:289` `_jsonSerializer.SerializeToString(dto)` —— dashboard-ui 拿到的所有 JSON 都出自这里。
- **请求体**：`HttpListenerHost.cs:859` `DeserializeJson` → `DeserializeFromStreamAsync(stream, type)`。
- **WebSocket**：`WebSocketConnection.cs`（构造注入 IJsonSerializer）。
- **持久化 JSON 文件**（升级兼容场景，旧格式文件必须能被新实现读回）：`EmbyTV/ItemDataProvider.cs:52,79`（LiveTv 定时器）、`MovieDbProvider.cs:77,211`（TMDb 缓存）、`SqliteItemRepository` 的 `data` 列、DisplayPreferences `data` 列。

### 1.5 盘点统计

| 指标 | 数量 |
|------|------|
| 直接 API 调用文件 | 2 |
| ServiceStack.Text API 调用行 | 17（10 处 JsonSerializer.* + 6 处 JsConfig + 1 处 JsvReader） |
| 纯文本引用 | 3 |
| 工程引用 | 3 csproj + 2 packages.config |
| 间接消费（不改） | 100 文件 |
| `.ToJson()` / `.FromJson<T>()` 扩展方法使用 | **0**（grep 全库无匹配） |
| DTO 上 ServiceStack 特有序列化特性（DataContract/DataMember/JsonObject） | **0**（全库纯 POCO；`[Route]` 等是仓库自研的 `MediaBrowser.Model.Services.RouteAttribute`，与序列化无关） |

---

## 2. 序列化语义分析

### 2.1 旧 ServiceStack.Text 行为（反编译证据）

Emby 在 `JsonSerializer.cs:218-224`（Configure 方法）设置的全局配置及其默认值语义：

| JsConfig 项 | Emby 设置 | SST 5.1.0 默认 | 反编译证据 |
|-------------|-----------|----------------|------------|
| DateHandler | ISO8601 | TimestampOffset | `DateTimeSerializer.cs:605-606`：ISO8601 → `dateTime.ToString("o", InvariantCulture)`（.NET round-trip 格式，7 位小数秒，Utc 加 Z） |
| ExcludeTypeInfo | true | false | 无 `__type` 发射 |
| IncludeNullValues | false | false（`JsConfig.cs:290`） | null 属性省略 |
| IncludeNullValuesInDictionaries | （未设） | false（`JsConfig.cs:305`） | **字典 null 值也省略** |
| AlwaysUseUtc | true | false | 读路径：`DateTimeSerializer.cs:67-79` `Prepare()` 把非 Utc 转 `ToStableUniversalTime()` |
| AssumeUtc | true | false | 写路径：`DateTimeSerializer.cs:591-598` `UseConfigSpecifiedSetting()` 把 Kind=Unspecified `SpecifyKind(Utc)`（不改数值） |
| JsConfig\<Guid\>.SerializeFn | Empty→null，否则 "N" | — | 见下 |
| PropertyConvention | （未设） | Strict（`JsConfig.cs:532`） | `DefaultPropertyNameResolver.cs`：精确哈希查找，**大小写敏感** |
| EmitCamelCaseNames | （未设） | false（`JsConfig.cs:498`） | **属性名按 C# 原名（PascalCase）输出** |
| TreatEnumAsInteger | （未设） | false（`JsConfig.cs:335`） | 枚举序列化为**字符串名** |
| IncludeDefaultEnums | （未设） | true（`JsConfig.cs:320`） | 枚举值 0 也输出（输出名字，如 `"Ok"`） |
| ThrowOnDeserializationError | （未设） | false（`JsConfig.cs:547`） | **属性级反序列化错误被吞掉**（`DeserializeTypeRefJson.cs:130-160`：catch 后仅 Tracer 警告，属性保持默认值） |

逐项展开（每项都有代码级证据）：

**① 字段命名 = PascalCase 原名**
`EmitCamelCaseNames=false` + `DefaultPropertyNameResolver` 精确匹配。Emby API 输出形如 `{"Name":"...","ServerName":"...","Id":"..."}`。

**② DateTime = "o" round-trip 格式 + UTC 归一**
- 写：`ToString("o")` → `2016-05-12T15:27:00.0000000Z`（Utc）/ `...+08:00`（Local）/ 无后缀（Unspecified，但 AssumeUtc 会先转 Utc）。
- 读：`DateTimeSerializer.cs:146-173` 极宽松——接受 `yyyy-MM-ddTHH:mm:ssZ`、带 3-7 位小数、`yyyy-MM-dd` 纯日期、`yyyyMMdd`、WCF `/Date(...)/` 等；`Prepare()` + AlwaysUseUtc 把结果统一转 UTC。

**③ null 省略**（含字典 null 值省略）

**④ 枚举 = 字符串名**
- 写：`JsonTypeSerializer.cs:364` `WriteRawString(writer, enumValue.ToString())`；`[Flags]` 枚举例外 → 数字（`JsWriter.cs:135` WriteEnumFlags）。
- 读：`ParseUtils.cs:78` `Enum.Parse(enumType, str, ignoreCase: true)` —— 大小写不敏感，且接受数字字符串（"2"）。
- 全库 JSON 路径上的 [Flags] 枚举：**无**（`DlnaFlags` 只在 `DlnaMaps.cs`/`ContentFeatureBuilder.cs` 手工转字符串，不进 JSON；`FileOpenOptions` 不序列化；`DlnaManager.cs:497-499` 的 DeviceProfile JSON 往返不含 Flags 字段——已验证）。

**⑤ Guid = "N" 格式 + Empty 省略**
- 写：`JsonTypeSerializer.cs:167` `WriteGuid` → `((Guid)oValue).ToString("N")`（32 位无连字符，这是 SST 默认，Emby 又加了 Empty→null）。
- Empty 省略机制：`WriteType.cs:224` `propertySuppressDefaultConfig`（属性类型有 SerializeFn 的值类型）+ `WriteType.cs:485`（默认值相等且 !IncludeNullValues → 跳过）→ **Guid 属性 = Guid.Empty 时整个键不输出**。
- 读：`StringSegmentExtensions.cs:786` `ParseGuid` 宽松解析（N/D/B 均可）。
- `Guid?`：Model DTO 无此类型属性；Controller 实体有（`LinkedChild.ItemId`、`UserView.UserId` 等，会进 `data` 列 JSON）。旧写法：null → 省略；Empty → 输出 32 个 0。

**⑥ 反序列化容错 = 吞错**
`ThrowOnDeserializationError=false`：`{"N":null}` 进 `int N` → 警告 + N=0；`{"N":""}` → 同样默认值；类型不匹配 → 属性跳过。未知字段 → 忽略。**这是外部 provider JSON（TMDb 返回 `"runtime": null` 之类）能工作的原因。**

**⑦ 无 __type、无循环引用处理**（DTO 实际无环，双方都会炸，等价）

### 2.2 dashboard-ui 前端契约（抽查证据）

前端解析服务端 JSON 的三个硬约束：

**① PascalCase 字段名**（大量直接属性访问）：
- `scripts/itemdetailpage.js`：`item.Type` ×106、`item.Id` ×24、`item.UserData` ×5、`item.MediaSources`、`chapters[0].ImageTag`、`stream.Type`（`"Data" != stream.Type`、`"Video" == stream.Type`）、`version.Protocol`（`"Http" != version.Protocol`）
- `components/activitylog.js`：`entry.Severity`（`"Error"==entry.Severity`）、`entry.Date`、`entry.Name`
- `scripts/livetvcomponents.js`：`item.StartDate`、`t.Type="Timer"`
- `scripts/videoosd.js`：`program.EndDate`、`state.PlayState.IsPaused`、`state.MediaSource.SupportsTranscoding`

**② 枚举 = 字符串**：上列 `"Movie"`/`"BoxSet"`/`"Series"`/`"Episode"`/`"TvChannel"`/`"Program"`/`"Recording"`/`"Error"`/`"Fatal"`/`"Warn"`/`"Director"`/`"Data"`/`"Video"`/`"Http"` 全是字符串比较。

**③ 日期 = ISO8601（带 Z 或偏移）**：`bower_components/emby-webcomponents/datetime.js` 的 `parseISO8601Date` 正则：
```js
/(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(\.\d+)?(Z|([+-])(\d{2}):(\d{2}))?/
```
接受任意位数小数秒 + `Z` 或 `±HH:mm` 偏移。**SST 的 `.0000000Z` 和 STJ 的 `Z`（零小数省略）都匹配**。前端还用 `minDate.toISOString()` 回传日期（`components/activitylog.js`），服务端查询参数解析要能吃 ISO 字符串。

**④ Guid = 任意格式字符串**：前端把 Id 当不透明字符串拼接 URL（`apiClient.getUrl("Videos/"+params.id+"/...")`），不解析格式；但**空 Guid 语义**：前端大量 `if (item.SeriesId)` 真值判断——旧输出省略键（undefined，falsy）；若新输出 32 个 0 的字符串（truthy）会**破坏**这些判断。见 §3.2-G 的处理。

**结论：兼容层必须保持——PascalCase 键名、枚举字符串、ISO8601 日期、null 省略、Guid "N" 格式 + Empty 不产生 truthy 值。**

### 2.3 System.Text.Json（.NET 10）实测行为

实测程序：`D:\tmp\stj-test`（SDK 10.0.401，Release）。关键结果：

| # | 场景 | STJ .NET 10 实测 | 旧 SST 行为 | 差异 |
|---|------|------------------|-------------|------|
| 1 | `Dictionary<string,string>` 含 null 值 + `WhenWritingNull` | `{"a":"1","b":null}` —— **null 字典值仍输出** | 省略 | ⚠️ 差异 |
| 2 | 枚举 + `JsonStringEnumConverter` | 写 `"Movie"`；读 `"movie"`（小写）✅、数字 `1` ✅、`"1"` ✅；值 0 → `"Zero"` | 同（读同样大小写不敏感+数字） | ✅ 一致 |
| 2b | `[Flags]` 枚举 + StringEnumConverter | 写 `"A, B"`（逗号拼接名） | 写数字 | ⚠️ 差异（已验证 JSON 路径无 Flags 枚举，无实际影响） |
| 3a | DateTime Kind=Utc 写 | `"2016-05-12T15:27:00Z"`（零小数**省略**） | `"...00.0000000Z"` | 外观差异，前端均接受 |
| 3b | DateTime Kind=Unspecified 写 | `"2016-05-12T15:27:00"`（**无 Z**） | AssumeUtc → 有 Z | ⚠️ 差异，需转换器 |
| 3c | DateTime Kind=Local 写 | `"2016-05-12T15:27:00+08:00"` | 同（"o" 带偏移） | ✅ 一致 |
| 3e | 读 `+02:00` 偏移 | **Kind=Local，且换算成本机时区**（+08 机器上变 21:27 Local） | AlwaysUseUtc → UTC | ⚠️ 差异，需转换器 |
| 3f | 读 `Z` | Kind=Utc | 同 | ✅ |
| 3g | 读 `"2016-05-12"`（纯日期） | **成功**（.NET 10 支持）→ Kind=Unspecified | 成功 | ✅ |
| 4a | Guid 写（默认） | 带连字符 `"12345678-90ab-..."` | "N" 无连字符 | ⚠️ 差异，需转换器 |
| 4b | **读 "N" 格式 32 hex** | **抛 JsonException**（"not in a supported Guid format"） | 成功 | 🔴 **破坏性**，需转换器 |
| 4c | 读 `""` 空 Guid | 抛 JsonException | 吞错→默认 | 🔴 需容错 |
| 5a | `{"N":null}` → `int N` | **抛 JsonException** | 吞错→0 | 🔴 需容错 |
| 5b | `{"N":""}` → int | 抛 JsonException | 吞错→0 | 🔴 需容错 |
| 5c | `{"N":"42"}` → int（`NumberHandling=AllowReadingFromString`） | 42 ✅ | 42 | ✅ |
| 5d | `{"S":123}` → string | 抛 JsonException | 吞错/转字符串 | ⚠️ 低风险（外部 API 无此形态） |
| 6 | 属性名大小写 | 默认**大小写敏感**（`{"n":5}` 不绑定）；`PropertyNameCaseInsensitive=true` 可开 | 大小写敏感（Strict） | ✅ 默认一致 |
| 7 | `object` 类型属性 | 按运行时类型序列化（`{"Box":123}`） | 同 | ✅ |
| 8 | 嵌套深度 >64 | 抛 JsonException（默认 MaxDepth=64） | 无限制 | ⚠️ 设 MaxDepth=128 |
| 10 | `{"B":"true"}` → bool | 抛（`NumberHandling` 也救不了 bool） | 吞错/解析 | ⚠️ 需容错 |
| 11 | `null` → `MyEnum?` | null ✅ | 同 | ✅ |
| 13 | 非 ASCII | 默认转义 `\u7535\u5F71`；`UnsafeRelaxedJsonEscaping` 原样输出 | 原样输出 | ⚠️ 建议用 relaxed（JSON 语义等价，仅体积/外观） |
| 15 | null List / 空 List | `null` / `[]`（WhenWritingNull 时 null 省略） | 同 | ✅ |

### 2.4 差异矩阵汇总（兼容层必须覆盖的点）

| 优先级 | 差异点 | 影响 | 缓解 |
|--------|--------|------|------|
| 🔴 P0 | Guid 读 "N" 格式抛异常 | 旧持久化文件（LiveTv 定时器、TMDb 缓存、`data` 列）全部读不回 | `EmbyGuidConverter`（§3.2-G） |
| 🔴 P0 | Guid 写带连字符 + Empty 变 truthy | 前端 `if (item.SeriesId)` 判断被破坏；API 输出格式变化 | 同上（写 "N"；Empty → null） |
| 🔴 P0 | null/"" 进值类型抛异常 | TMDb 等外部 API 返回 null 数值字段 → provider 崩 | `LenientPrimitiveConverterFactory`（§3.2-L） |
| 🟡 P1 | DateTime Unspecified 无 Z / 偏移读转本机时区 | DTO 日期（多来自元数据，Kind=Unspecified）输出无时区；读偏移日期不再是 UTC | `EmbyDateTimeConverter`（§3.2-D） |
| 🟡 P1 | 枚举默认数字 | API 输出 `"Type":2` → 前端全挂 | `JsonStringEnumConverter` |
| 🟡 P1 | null 属性默认输出 | 契约变化 | `DefaultIgnoreCondition=WhenWritingNull` |
| 🟡 P1 | 命名策略 | 前端 PascalCase 契约 | `PropertyNamingPolicy=null`（**不要用 CamelCase**，参考分支的教训） |
| 🟢 P2 | 字典 null 值仍输出 | `Dictionary<string,string>` 含 null 的场景罕见 | 接受差异（前端 `x || fallback` 模式两者等价）；如需严格可后续加字典转换器 |
| 🟢 P2 | MaxDepth 64 | DTO 实际深度 <10 | `MaxDepth=128` |
| 🟢 P2 | 非 ASCII 转义 | 体积增大，语义等价 | `UnsafeRelaxedJsonEscaping` |
| 🟢 P2 | 零小数秒省略 `.0000000` | 外观差异，前端正则均匹配 | 接受（或转换器用 "O" 格式，见 §3.2-D） |
| ✅ | 枚举读大小写/数字、未知字段忽略、`object` 运行时类型、无 __type、空/ null 集合 | — | 无需处理 |

---

## 3. System.Text.Json 兼容层设计

### 3.1 JsonSerializerOptions 总配置（逐项对照 §2 语义）

```csharp
// Emby.Server.Implementations/Serialization/JsonSerializer.cs 内部
private static readonly JsonSerializerOptions Options = CreateOptions();

private static JsonSerializerOptions CreateOptions()
{
    var options = new JsonSerializerOptions
    {
        // ── 命名 ─────────────────────────────────────────────
        // SST: EmitCamelCaseNames=false → 属性原名（PascalCase）。
        // 前端契约：item.Type 等 106 处 PascalCase 访问（§2.2-①）。
        // ⚠️ 参考分支 backup-migration-20261013-210711 用了 CamelCase，是 bug。
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null,          // 字典键原样（SST 同）

        // ── null 处理 ───────────────────────────────────────
        // SST: IncludeNullValues=false → null 属性省略（§2.1-③）。
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // ── 大小写匹配（读） ────────────────────────────────
        // SST 默认 Strict（大小写敏感）；这里放宽为超集：多绑定不误伤，
        // 兼容外部 API 大小写漂移。写侧不受影响。
        PropertyNameCaseInsensitive = true,

        // ── 数字容错（读） ──────────────────────────────────
        // SST 吞错语义接受 "42" → int（§2.3-5c）。
        NumberHandling = JsonNumberHandling.AllowReadingFromString,

        // ── 转义 ────────────────────────────────────────────
        // SST 原样输出非 ASCII；STJ 默认全转义（语义等价但体积大）。
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

        // ── 其他 ────────────────────────────────────────────
        WriteIndented = false,               // SST 默认紧凑
        MaxDepth = 128,                      // SST 无限制；DTO 实际 <10，STJ 默认 64 偏紧
    };

    // ── 转换器（顺序无关，类型各归其位） ──────────────────
    options.Converters.Add(new EmbyGuidConverter());            // §3.2-G
    options.Converters.Add(new EmbyDateTimeConverter());        // §3.2-D
    options.Converters.Add(new JsonStringEnumConverter());      // 枚举字符串（§2.1-④）
    options.Converters.Add(new LenientPrimitiveConverterFactory()); // §3.2-L
    return options;
}
```

**不配置的项及理由**：
- `ReferenceHandler`：SST 无循环引用处理（DTO 无环），保持默认 `null`（遇环抛异常，与旧等价）。
- `RespectNullableAnnotations` / `RespectRequiredConstructorParameters`：.NET 8+ 的严格化开关，方向与旧的宽松语义相反，**不开**。
- `UnmappedMemberHandling`：默认忽略未知字段，与 SST 一致，不开。

### 3.2 自定义 Converter 设计

#### G：EmbyGuidConverter（P0，必做）

```csharp
public sealed class EmbyGuidConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // 容错：null / "" → Guid.Empty（复刻 SST 吞错语义；Guid? 的 null 由 STJ 框架先行处理）
        if (reader.TokenType == JsonTokenType.Null) return Guid.Empty;
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (string.IsNullOrEmpty(s)) return Guid.Empty;
            return Guid.Parse(s);            // 接受 N/D/B/P —— 修复 STJ 只认 D 的破坏性差异（§2.3-4b）
        }
        throw new JsonException($"Unexpected token {reader.TokenType} for Guid");
    }

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
    {
        // SST: JsonTypeSerializer.cs:167 → ToString("N")；Emby SerializeFn: Empty → null
        // Empty 写 null（而非省略键）：converter 无法省略属性，但 null 在前端与 undefined
        // 同为 falsy，保住 if (item.SeriesId) 判断（§2.2-④）；读回时 null → Empty，往返无损。
        if (value == Guid.Empty) writer.WriteNullValue();
        else writer.WriteStringValue(value.ToString("N"));
    }
}
```
- STJ 自动用 `JsonConverter<Guid>` 处理 `Guid?`（Nullable 包装）：`Guid? = null` → 属性级 `WhenWritingNull` 省略（= 旧行为）；`Guid? = Empty` → 写 null（旧写 32 个 0，新更前端友好，读回等价）。
- 旧行为"Empty 属性整个省略" vs 新"写 null"的差异：JSON 消费方（前端/PG JSONB）均等价处理，接受。

#### D：EmbyDateTimeConverter（P1，必做）

```csharp
public sealed class EmbyDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return default;    // 容错（SST 吞错）
        if (reader.TokenType != JsonTokenType.String) throw new JsonException(...);
        var s = reader.GetString();
        if (string.IsNullOrEmpty(s)) return default;                    // 容错
        var dt = reader.GetDateTime();                                  // .NET 10：支持 Z/偏移/无后缀/纯日期（§2.3-3g）
        return dt.Kind switch
        {
            DateTimeKind.Utc      => dt,
            DateTimeKind.Local    => dt.ToUniversalTime(),              // AlwaysUseUtc（修复 §2.3-3e：STJ 会转本机时区）
            _                     => DateTime.SpecifyKind(dt, DateTimeKind.Utc) // AssumeUtc
        };
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var dt = value.Kind switch
        {
            DateTimeKind.Utc   => value,
            DateTimeKind.Local => value.ToUniversalTime(),               // 归一 UTC（旧写本机偏移，同一时刻；前端两者均接受）
            _                  => DateTime.SpecifyKind(value, DateTimeKind.Utc) // AssumeUtc（修复 §2.3-3b：Unspecified 无 Z）
        };
        // "O" = 与旧 SST ISO8601 完全同格式（含 .fffffff），前端正则匹配（§2.2-③）
        writer.WriteStringValue(dt.ToString("O", CultureInfo.InvariantCulture));
    }
}
```
- `DateTime?`：STJ 自动套用；null → 属性省略（= 旧）。
- `DateTimeOffset` 属性：全库 DTO 未使用（grep 验证），不配转换器，STJ 默认 `+00:00` 格式即可。

#### L：LenientPrimitiveConverterFactory（P0，必做）

复刻 SST `ThrowOnDeserializationError=false` 对值类型的吞错语义（§2.1-⑥），否则 TMDb 的 `"runtime": null` 直接炸 provider：

```csharp
public sealed class LenientPrimitiveConverterFactory : JsonConverterFactory
{
    private static readonly HashSet<Type> Supported = new()
    {
        typeof(bool), typeof(int), typeof(long), typeof(short), typeof(byte),
        typeof(float), typeof(double), typeof(decimal)
        // Nullable<T> 由 STJ 自动解包到 T 再进工厂
    };

    public override bool CanConvert(Type typeToConvert)
        => Supported.Contains(Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var underlying = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
        // 反射 MakeGenericType 构造 LenientConverter<T>（T = underlying），缓存实例
        ...
    }
}

internal sealed class LenientConverter<T> : JsonConverter<T> where T : struct
{
    public override T Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:      return default;               // {"N":null} → 0（旧语义）
            case JsonTokenType.String:                                      // "" → default；"42" → 42；"true" → true
                var s = reader.GetString();
                if (string.IsNullOrEmpty(s)) return default;
                return (T)Convert.ChangeType(s, typeof(T), CultureInfo.InvariantCulture);
            case JsonTokenType.True:
            case JsonTokenType.False:
                if (typeof(T) == typeof(bool)) return (T)(object)(reader.TokenType == JsonTokenType.True);
                break;
            case JsonTokenType.Number:   // 正常数/带引号数字（NumberHandling 之外的自保）
                return JsonSerializer.Deserialize<T>(ref reader, o with { Converters = ... }); // 或直接 reader.GetInt32/GetDouble + Convert
        }
        throw new JsonException(...);
    }
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions o)
        => JsonSerializer.Serialize(writer, value, o);   // 写路径走 STJ 原生（无行为差异）
}
```
> 实现注意：写路径委托回 STJ 原生以避免重复实现；`Convert.ChangeType` 覆盖 bool（"true"/"false"/"1"/"0"）与数值字符串。`Guid`/`DateTime`/枚举的容错已由各自转换器承担，不进本工厂。

#### 枚举：直接用内置 `JsonStringEnumConverter`

实测（§2.3-2）已证明读写行为与 SST 对齐（写名字、读大小写不敏感 + 数字 + 引号数字、值 0 写名字）。JSON 路径无 [Flags] 枚举（§2.1-④已验证），无需自定义。

### 3.3 JsvReader.GetParseFn 替换（查询参数解析器）

`ApplicationHost.cs:1081-1084` 的 `JsvReader.GetParseFn` 是 REST 查询串 → 请求 DTO 属性的解析入口（调用链见 §1.1-文件B）。请求 DTO 实际属性类型盘点（grep MediaBrowser.Api + src/Emby.Server.MediaEncoding）：`string` ×386、`bool?` ×115、`Guid` ×88、`int?` ×83、`bool` ×67、`int` ×13、`long?` ×10、`double?` ×4、`DateTime` ×2、枚举（ImageType/SortOrder/MetadataRefreshMode/PackageVersionClass 等）少量；列表参数走 `string Ids` + 服务内手工 split（如 `ItemsService`），**无 List\<T\> 直接绑定**。

新文件 `Emby.Server.Implementations/Serialization/QueryStringParser.cs`：

```csharp
public static class QueryStringParser
{
    public static Func<string, object> GetParseFn(Type propertyType)
    {
        var underlying = Nullable.GetUnderlyingType(propertyType);
        var targetType = underlying ?? propertyType;

        Func<string, object> core = targetType switch
        {
            _ when targetType == typeof(string)  => s => s,
            _ when targetType == typeof(bool)    => s => bool.TryParse(s, out var b) ? b : Convert.ToInt32(s) != 0, // "1"/"0" 容错
            _ when targetType == typeof(Guid)    => s => Guid.Parse(s),          // N/D 均可
            _ when targetType == typeof(DateTime)=> s => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            _ when targetType.IsEnum             => s => Enum.Parse(targetType, s, ignoreCase: true), // SST ParseUtils.cs:78 同款
            _ when targetType == typeof(string[])=> s => s.Split(','),
            _ => s => Convert.ChangeType(s, targetType, CultureInfo.InvariantCulture) // int/long/double/float/short/byte/decimal
        };

        if (underlying != null)
            return s => string.IsNullOrEmpty(s) ? null : core(s);
        return core;
    }
}
```
- 与 SST `JsvReader` 的差异：不支持 JSV 复合语法（`[{...}]`）——查询串绑定从未用到（DTO 无 List 属性直接绑定，已盘点验证）。
- 参考分支同款方案已验证可行（`backup-migration-20261013-210711:ApplicationHost.cs:1108-1163`，其覆盖面与本设计一致）。
- `DateTimeStyles.RoundtripKind`：前端 `toISOString()` 传来的 `Z` 字符串保留 Utc Kind。

### 3.4 IJsonSerializer 包装类重写（完整映射表）

`Emby.Server.Implementations/Serialization/JsonSerializer.cs` —— 类名、命名空间（`Emby.Common.Implementations.Serialization`）、构造函数签名 `(IFileSystem, ILogger)` 全部保持（DI 注册点 `ApplicationHost.cs:788-790` 零改动）：

| 旧（SST） | 新（STJ） | 备注 |
|-----------|-----------|------|
| `ServiceStack.Text.JsonSerializer.SerializeToStream(obj, obj.GetType(), stream)` (L43) | `System.Text.Json.JsonSerializer.Serialize(stream, obj, obj.GetType(), Options)` | 运行时类型序列化，语义同 |
| `ServiceStack.Text.JsonSerializer.SerializeToString(obj, obj.GetType())` (L272) | `System.Text.Json.JsonSerializer.Serialize(obj, obj.GetType(), Options)` | |
| `DeserializeFromString<T>(text)` (L168) | `System.Text.Json.JsonSerializer.Deserialize<T>(text, Options)` | 保留原 null/empty → ArgumentNullException 守卫 |
| `DeserializeFromString(json, type)` (L256) | `System.Text.Json.JsonSerializer.Deserialize(json, type, Options)` | |
| `DeserializeFromStream<T>(stream)` (L136) | `System.Text.Json.JsonSerializer.Deserialize<T>(stream, Options)` | |
| `DeserializeFromStream(stream, type)` (L190) | `System.Text.Json.JsonSerializer.Deserialize(stream, type, Options)` | |
| `DeserializeFromStreamAsync<T>`：StreamReader 全读 + `DeserializeFromString<T>` (L139-152) | `await System.Text.Json.JsonSerializer.DeserializeAsync<T>(stream, Options)` | 真异步，去中间字符串 |
| `DeserializeFromStreamAsync(stream, type)` (L193-211) | `await System.Text.Json.JsonSerializer.DeserializeAsync(stream, type, Options)` | 同上 |
| `Configure()`：JsConfig ×6 (L216-225) | 删除 → `Options` 静态只读（§3.1） | 全局可变静态 → 实例不可变，线程安全更优 |
| `SerializeGuid` (L227-235) | 删除 → 并入 `EmbyGuidConverter` | |
| `SerializeToFile` / `DeserializeFromFile` ×2 / `OpenFile` / 参数守卫 | 原样保留（内部转调上表方法） | |

### 3.5 已验证无需处理的风险

| 项 | 结论 | 证据 |
|----|------|------|
| [Flags] 枚举进 JSON | 无 | DlnaFlags 只在 DlnaMaps/ContentFeatureBuilder 手工转字符串；FileOpenOptions 不序列化；DeviceProfile JSON 往返不含 Flags 字段 |
| DTO 特性（DataContract/DataMember/JsonObject） | 全库 0 处 | grep 全库无匹配；外部 API DTO（TMDb）用 snake_case C# 属性名直接对齐 JSON 键 |
| `Guid?` DTO 属性 | Controller 实体 4 处，STJ 自动套用 Guid 转换器 | LinkedChild/UserView/CollectionCreationOptions/IntroInfo |
| `DateTimeOffset` DTO 属性 | 未使用 | grep 全库 |
| JsonObject/JsonValue（SST 动态类型） | 未使用 | grep 全库 |
| 循环引用 | DTO 无环，双方均抛异常，等价 | — |
| MediaBrowser.Model.Services（Route/IReturn/IRequest…） | 仓库自研接口克隆，与 ServiceStack.Text 无关 | `src/MediaBrowser.Model/MediaBrowser.Model.Services/`（RouteAttribute.cs 等 20 文件） |
| SocketHttpListener / MediaBrowser.Tests | 无 ServiceStack.Text 依赖 | packages.config 与代码 grep 均无 |

---

## 4. 逐文件修改清单（C09 执行地图）

| # | 文件 | 行号 | 改动 | 难度 |
|---|------|------|------|------|
| 1 | `Emby.Server.Implementations/Serialization/JsonSerializer.cs` | 全文件 | 按 §3.4 重写：删 6 处 JsConfig + 10 处 SST 调用 + SerializeGuid；新增 `Options` 静态字段与 `CreateOptions()`（§3.1）；方法体逐一替换为 STJ 调用；保留类名/命名空间/构造签名/文件与守卫逻辑 | 中 |
| 2 | `Emby.Server.Implementations/Serialization/EmbyGuidConverter.cs` | 新增 | §3.2-G 全文（~40 行） | 易 |
| 3 | `Emby.Server.Implementations/Serialization/EmbyDateTimeConverter.cs` | 新增 | §3.2-D 全文（~45 行） | 易 |
| 4 | `Emby.Server.Implementations/Serialization/LenientPrimitiveConverterFactory.cs` | 新增 | §3.2-L（~80 行，含 LenientConverter\<T\>） | 中 |
| 5 | `Emby.Server.Implementations/Serialization/QueryStringParser.cs` | 新增 | §3.3 全文（~40 行） | 易 |
| 6 | `Emby.Server.Implementations/ApplicationHost.cs` | 104 | 删 `using ServiceStack;` | 易 |
| 7 | 同上 | 122 | 删 `using ServiceStack.Text.Jsv;` | 易 |
| 8 | 同上 | 1081-1084 | `GetParseFn` 方法体改为 `return QueryStringParser.GetParseFn(propertyType);`（方法本身保留，签名是 HttpListenerHost 构造委托） | 易 |
| 9 | `Emby.Server.Implementations/Emby.Server.Implementations.csproj` | 626-628 | 删 `<Reference Include="ServiceStack.Text...">` 节点（届时若已转 SDK 格式，对应删 PackageReference/hint 路径） | 易 |
| 10 | `MediaBrowser.Server.Mono/MediaBrowser.Server.Mono.csproj` | 59-61 | 删同上 Reference 节点 | 易 |
| 11 | `MediaBrowser.ServerApplication/MediaBrowser.ServerApplication.csproj` | 82-84 | 删同上 Reference 节点 | 易 |
| 12 | `MediaBrowser.Server.Mono/packages.config` | ServiceStack.Text 行 | 删除该行 | 易 |
| 13 | `MediaBrowser.ServerApplication/packages.config` | ServiceStack.Text 行 | 删除该行 | 易 |
| 14 | `Emby.Server.Implementations/HttpServer/HttpListenerHost.cs` | 786 | （可选）日志文案 `"Calling ServiceStack AppHost.Init"` → `"Initializing HTTP host"` | 易 |
| 15 | `MediaBrowser.Api/PluginService.cs` / `MediaBrowser.Api/ScheduledTasks/ScheduledTaskService.cs` | 301 / 238 | （可选）注释里的失效 google code 链接清理 | 易 |

**不需要动的**：100 个 IJsonSerializer 消费文件、`src/MediaBrowser.Model/MediaBrowser.Model.Serialization/IJsonSerializer.cs`（接口不变）、`XmlSerializer.cs`（IXmlSerializer 独立实现，不依赖 ServiceStack.Text）、DI 注册（`ApplicationHost.cs:788-790`）、dashboard-ui 全部。

**C09 验收**：`dotnet build MediaBrowser.sln` 0 error；全库 grep `ServiceStack` 仅剩注释/日志（或为 0）；`packages/ServiceStack.Text.5.1.0` 不再被任何工程引用。

---

## 5. 验证清单（C09 提交前 + P5 冒烟）

1. **编译**：`dotnet build -c Debug` 0 error 0 warning（新增代码）。
2. **API 输出契约**（curl 冒烟）：
   - `GET /emby/System/Info` → 键名 PascalCase；`Id` 为 32 位无连字符 hex；无 null 字段。
   - `GET /emby/Items?...` → `"Type":"Movie"`（枚举字符串）、`"LocationType":"FileSystem"`、`PremiereDate` 形如 `2016-05-12T15:27:00.0000000Z`。
   - 空 Guid 字段（如无剧集的 `SeriesId`）→ 输出 `null` 或省略，**不得是 32 个 0 的 truthy 字符串**。
3. **前端冒烟**：dashboard-ui 首页/详情页加载，`item.Type` 等非 undefined（P5 一起做）。
4. **旧数据读回**（升级兼容）：用旧版生成的 LiveTv timers JSON（N 格式 Guid + "o" 日期）+ TMDb 缓存文件，新实现 `DeserializeFromFile` 成功。
5. **容错回归**：`{"runtime":null}`、`{"N":""}`、`{"B":"true"}` 样例 JSON 反序列化不抛异常且得默认值。
6. **查询参数绑定**：`?HasThemeSong=true&MinIndexNumber=5&SortOrder=Ascending&UserId=<guid>&MinDateCreated=2026-01-01T00:00:00.0000000Z` 各类型正确落 DTO。
7. **（可选）round-trip diff**：对典型 DTO（BaseItemDto/TimerInfo/DeviceProfile）用旧 DLL 序列化 → 新实现反序列化 → 再序列化，比对语义等价（键集合、值）。

---

## 6. 证据附录

**仓库源码**：
- `Emby.Server.Implementations/Serialization/JsonSerializer.cs`（L43/136/150/168/190/209/218-224/227-235/256/272）
- `src/MediaBrowser.Model/MediaBrowser.Model.Serialization/IJsonSerializer.cs:7`
- `Emby.Server.Implementations/ApplicationHost.cs`（L104/122/978/1081-1084；DI：L391/788-790/805/864）
- `Emby.Server.Implementations/HttpServer/HttpListenerHost.cs`（L52/831-838/859；L786 日志）
- `Emby.Server.Implementations/HttpServer/HttpResultFactory.cs:289`
- `Emby.Server.Implementations/Services/StringMapTypeDeserializer.cs`（L29-35/46-54/97）
- `Emby.Server.Implementations/Services/ServiceController.cs:78`
- `Emby.Server.Implementations/LiveTv/EmbyTV/ItemDataProvider.cs:52,79`
- `MediaBrowser.Providers/Movies/MovieDbProvider.cs`（L77/159/211/358/403；DTO L594-640 snake_case）
- `MediaBrowser.Providers/Movies/TmdbSettings.cs`（snake_case 属性）
- `Emby.Dlna/DlnaManager.cs:497-499`
- `src/MediaBrowser.Model/MediaBrowser.Model.Services/RouteAttribute.cs:6`（自研路由特性）
- 工程引用：3 个 csproj + 2 个 packages.config（§1.3）

**ServiceStack.Text 5.1.0 反编译**（ilspycmd，临时目录）：
- `JsConfig.cs`：L290 IncludeNullValues=false 默认、L305 IncludeNullValuesInDictionaries=false、L320 IncludeDefaultEnums=true、L335 TreatEnumAsInteger=false、L498 EmitCamelCaseNames=false、L532 PropertyConvention=Strict、L547 ThrowOnDeserializationError=false
- `JsonTypeSerializer.cs`：L167 WriteGuid→"N"、L364 WriteEnum→ToString()
- `JsWriter.cs`：L135 WriteEnumFlags→数字
- `ParseUtils.cs`：L78 TryParseEnum→Enum.Parse(ignoreCase:true)
- `DateTimeSerializer.cs`：L606 ISO8601→"o"、L591-598 AssumeUtc、L67-79 AlwaysUseUtc(Prepare)、L146-173 宽松读
- `WriteType.cs`：L85/L485 null/默认值省略、L224 propertySuppressDefaultConfig（Guid Empty 省略机制）
- `DefaultPropertyNameResolver.cs`：精确哈希（大小写敏感）
- `DeserializeTypeRefJson.cs`：L43 Strict 解析器选择、L130-160 属性级错误吞掉

**STJ .NET 10 实测**：`D:\tmp\stj-test/Program.cs`（17 组用例，结果见 §2.3 表）。

**dashboard-ui 前端**：
- `bower_components/emby-webcomponents/datetime.js`（parseISO8601Date 正则）
- `scripts/itemdetailpage.js`（item.Type ×106 等）、`components/activitylog.js`、`scripts/livetvcomponents.js`、`scripts/videoosd.js`、`scripts/serversecurity.js`

**历史参考分支**（backup-migration-20261013-210711）：
- `Emby.Server.Implementations/Serialization/JsonSerializer.cs`：已换 STJ 但 `PropertyNamingPolicy=CamelCase` —— 与前端 PascalCase 契约冲突（其 dashboard-ui 未改，`item.Type` 会全 undefined），本设计纠正为 `null`。
- `Emby.Server.Implementations/ApplicationHost.cs:1108-1163`：手写 GetParseFn（方向正确，本设计 §3.3 采纳并补 bool "1"/"0" 容错与 RoundtripKind）。

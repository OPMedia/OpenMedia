using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Persistence;

/// <summary>
/// 显示偏好仓储。异步方法为真实实现，同步方法为遗留门面桥接。
/// </summary>
public interface IDisplayPreferencesRepository : IRepository, IDisposable
{
	Task SaveDisplayPreferencesAsync(DisplayPreferences displayPreferences, Guid userId, string client, CancellationToken cancellationToken);

	Task SaveAllDisplayPreferencesAsync(IEnumerable<DisplayPreferences> displayPreferences, Guid userId, CancellationToken cancellationToken);

	Task<DisplayPreferences> GetDisplayPreferencesAsync(string displayPreferencesId, Guid userId, string client, CancellationToken cancellationToken = default);

	Task<List<DisplayPreferences>> GetAllDisplayPreferencesAsync(Guid userId, CancellationToken cancellationToken = default);

	// ---- 兼容桥接（默认实现 → 异步）----
	// 分析器豁免：这是有意为之的同步适配边界，供未迁移的第三方插件调用；
	// 实现即「阻塞等待异步」，新代码一律使用 *Async 版本。
#pragma warning disable VSTHRD002
	void SaveDisplayPreferences(DisplayPreferences displayPreferences, string userId, string client, CancellationToken cancellationToken)
		=> SaveDisplayPreferencesAsync(displayPreferences, new Guid(userId), client, cancellationToken).GetAwaiter().GetResult();

	void SaveAllDisplayPreferences(IEnumerable<DisplayPreferences> displayPreferences, Guid userId, CancellationToken cancellationToken)
		=> SaveAllDisplayPreferencesAsync(displayPreferences, userId, cancellationToken).GetAwaiter().GetResult();

	DisplayPreferences GetDisplayPreferences(string displayPreferencesId, string userId, string client)
		=> GetDisplayPreferencesAsync(displayPreferencesId, new Guid(userId), client).GetAwaiter().GetResult();

	IEnumerable<DisplayPreferences> GetAllDisplayPreferences(Guid userId)
		=> GetAllDisplayPreferencesAsync(userId).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
}
